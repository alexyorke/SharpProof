namespace SharpProof.Effects;

internal sealed class ConversionOwnershipClassifier
{
    private readonly CoalesceAssignmentFlowCaptures _coalesceCaptures;
    private readonly Compilation _compilation;
    private readonly CreationFlowCaptures _creationCaptures;
    private readonly Dictionary<ISymbol, EffectRegionSet> _localRegions =
        new(SymbolEqualityComparer.Default);
    private readonly IMethodSymbol _method;

    internal ConversionOwnershipClassifier(
        IMethodSymbol method,
        Compilation compilation,
        CoalesceAssignmentFlowCaptures coalesceCaptures,
        CreationFlowCaptures creationCaptures)
    {
        _method = method;
        _compilation = compilation;
        _coalesceCaptures = coalesceCaptures;
        _creationCaptures = creationCaptures;
    }

    internal EffectRegionSet ClassifyRegion(
        IOperation? operation, bool aliasSource = false)
    {
        if (operation is IConversionOperation conversion &&
            conversion.OperatorMethod == null)
        {
            return ClassifyConversionRegion(conversion, aliasSource);
        }

        return operation switch
        {
            null => aliasSource ? EffectRegionSet.Unknown : EffectRegionSet.Empty,
            ILiteralOperation or IDefaultValueOperation when aliasSource => EffectRegionSet.Empty,
            IInstanceReferenceOperation => EffectRegionSet.Create(EffectRegionId.Receiver),
            IParameterReferenceOperation parameter => ClassifyParameter(parameter.Parameter),
            ILocalReferenceOperation local => ClassifyLocal(local.Local),
            IFlowCaptureReferenceOperation capture
                when _creationCaptures.TryResolve(
                    capture,
                    out var creationRegion) =>
                creationRegion,
            IFlowCaptureReferenceOperation capture
                when _coalesceCaptures.TryResolve(
                    capture,
                    out var captured) =>
                ClassifyRegion(captured, aliasSource),
            IFlowCaptureReferenceOperation => EffectRegionSet.Unknown,
            IFieldReferenceOperation { Field.IsStatic: true } => EffectRegionSet.Create(EffectRegionId.Static()),
            IFieldReferenceOperation
            {
                Field.RefKind: not RefKind.None,
                Instance: { } fieldInstance
            } when aliasSource =>
                ClassifyRegion(fieldInstance, aliasSource),
            IFieldReferenceOperation or IArrayElementReferenceOperation =>
                EffectRegionSet.Unknown,
            IObjectCreationOperation creation
                when creation.Type?.IsRefLikeType == true =>
                EffectRegionSet.Unknown,
            IOperation creation when creation is
                IObjectCreationOperation or IArrayCreationOperation =>
                EffectRegionSet.Create(EffectRegionId.Fresh(creation.Syntax.SpanStart)),
            IConditionalOperation conditional when aliasSource =>
                ClassifyRegion(conditional.WhenTrue, true).Union(
                    ClassifyRegion(conditional.WhenFalse, true)),
            ICoalesceOperation coalesce when aliasSource =>
                ClassifyRegion(coalesce.Value, true).Union(
                    ClassifyRegion(coalesce.WhenNull, true)),
            IParenthesizedOperation parenthesized when aliasSource => ClassifyRegion(parenthesized.Operand, true),
            _ => EffectRegionSet.Unknown
        };
    }

    internal EffectRegionSet ClassifyParameter(IParameterSymbol parameter)
    {
        EffectRegionSet declaredRegion;
        if (PrimaryConstructorParameterOwnership.IsReceiverBacked(
                parameter,
                _method))
        {
            declaredRegion = EffectRegionSet.Create(EffectRegionId.Receiver);
        }
        else if (SymbolEqualityComparer.Default.Equals(
                parameter.ContainingSymbol?.OriginalDefinition,
                _method.OriginalDefinition))
        {
            if (parameter.Type.IsValueType &&
                !parameter.Type.IsRefLikeType &&
                parameter.RefKind == RefKind.None)
            {
                declaredRegion = EffectRegionSet.Empty;
            }
            else
            {
                declaredRegion = EffectRegionSet.Create(
                    EffectRegionId.Parameter(parameter.Ordinal));
            }
        }
        else
        {
            declaredRegion = EffectRegionSet.Create(
                EffectRegionId.Captured(parameter.Ordinal));
        }

        return parameter.Type.IsRefLikeType &&
            _localRegions.TryGetValue(parameter, out var learnedRegions)
                ? declaredRegion.Union(learnedRegions)
                : declaredRegion;
    }

    internal void BuildLocalRegions(
        IOperation root,
        Func<IOperation, bool> isReachable)
    {
        var relevant = root.DescendantsAndSelf()
            .Where(operation => !IsInsideNestedCallable(operation, root))
            .ToImmutableArray();
        foreach (var declarator in relevant.OfType<IVariableDeclaratorOperation>())
        {
            if (!_localRegions.ContainsKey(declarator.Symbol))
            {
                _localRegions.Add(declarator.Symbol, EffectRegionSet.Empty);
            }
        }
        foreach (var @catch in relevant.OfType<ICatchClauseOperation>())
        {
            foreach (var declarator in @catch.ExceptionDeclarationOrExpression?
                         .DescendantsAndSelf()
                         .OfType<IVariableDeclaratorOperation>() ?? [])
            {
                _localRegions[declarator.Symbol] = EffectRegionSet.Unknown;
            }
        }
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var operation in relevant)
            {
                if (!isReachable(operation))
                {
                    continue;
                }

                if (operation is IForEachLoopOperation forEach)
                {
                    foreach (var loopVariable in forEach.LoopControlVariable
                                 .DescendantsAndSelf()
                                 .OfType<IVariableDeclaratorOperation>())
                    {
                        var previousLoopRegions = _localRegions.TryGetValue(
                            loopVariable.Symbol,
                            out var loopRegions)
                                ? loopRegions
                                : EffectRegionSet.Empty;
                        var discoveredLoopRegions = loopVariable.Symbol.Type.IsValueType &&
                            !loopVariable.Symbol.Type.IsRefLikeType &&
                            loopVariable.Symbol.RefKind == RefKind.None
                                ? EffectRegionSet.Empty
                                : ClassifyRegion(
                                    forEach.Collection,
                                    aliasSource: true);
                        var joinedLoopRegions = previousLoopRegions.Union(
                            discoveredLoopRegions);
                        if (joinedLoopRegions != previousLoopRegions)
                        {
                            _localRegions[loopVariable.Symbol] = joinedLoopRegions;
                            changed = true;
                        }
                    }
                }

                if (operation is IInvocationOperation invocation)
                {
                    var argumentRegions = EffectRegionSet.Empty;
                    var refLikeTargets = new List<ISymbol>();
                    foreach (var argument in invocation.Arguments)
                    {
                        var canRebind = argument.Parameter?.RefKind is
                            RefKind.Ref or RefKind.Out;
                        if (canRebind ||
                            argument.Value.Type?.IsRefLikeType == true)
                        {
                            argumentRegions = argumentRegions.Union(
                                ClassifyRegion(
                                    argument.Value,
                                    aliasSource: true));
                            if (canRebind && TryGetRefLikeStorageSymbol(
                                    argument.Value,
                                    out var argumentTarget))
                            {
                                refLikeTargets.Add(argumentTarget);
                            }
                        }
                    }

                    if (invocation.Instance is { } invocationInstance &&
                        invocationInstance.Type?.IsRefLikeType == true)
                    {
                        argumentRegions = argumentRegions.Union(
                            ClassifyRegion(
                                invocationInstance,
                                aliasSource: true));
                    }

                    if (invocation.Instance is { } invocationInstanceLocal &&
                        TryGetRefLikeStorageSymbol(
                            invocationInstanceLocal,
                            out var receiver))
                    {
                        refLikeTargets.Add(receiver);
                    }

                    if (refLikeTargets.Count != 0 &&
                        MethodMayIntroduceUnknownRefAlias(
                            invocation.TargetMethod))
                    {
                        argumentRegions = argumentRegions.Union(
                            EffectRegionSet.Unknown);
                    }

                    foreach (var refLikeTarget in refLikeTargets)
                    {
                        var previousReceiverRegions =
                            _localRegions.TryGetValue(
                                refLikeTarget,
                                out var receiverRegions)
                                ? receiverRegions
                                : EffectRegionSet.Empty;
                        var joinedReceiverRegions =
                            previousReceiverRegions.Union(argumentRegions);
                        if (joinedReceiverRegions != previousReceiverRegions)
                        {
                            _localRegions[refLikeTarget] =
                                joinedReceiverRegions;
                            changed = true;
                        }
                    }
                }

                if (TryGetPropertySetter(
                        operation,
                        out var property,
                        out var storedValue,
                        out var valueIsStoredDirectly) &&
                    property is
                    {
                        Instance: { } propertyInstance,
                        Property.SetMethod: { } setter
                    } &&
                    TryGetRefLikeStorageSymbol(
                        propertyInstance,
                        out var propertyReceiver))
                {
                    var setterRegions = ClassifyRegion(
                        propertyInstance,
                        aliasSource: true);
                    if (storedValue?.Type?.IsRefLikeType == true)
                    {
                        setterRegions = setterRegions.Union(ClassifyRegion(
                            storedValue,
                            aliasSource: true));
                    }
                    if (!valueIsStoredDirectly &&
                        property.Type?.IsRefLikeType == true)
                    {
                        setterRegions = setterRegions.Union(
                            EffectRegionSet.Unknown);
                    }
                    foreach (var argument in property.Arguments)
                    {
                        if (argument.Parameter?.RefKind is
                                RefKind.Ref or RefKind.Out ||
                            argument.Value.Type?.IsRefLikeType == true)
                        {
                            setterRegions = setterRegions.Union(ClassifyRegion(
                                argument.Value,
                                aliasSource: true));
                        }
                    }
                    if (MethodMayIntroduceUnknownRefAlias(setter))
                    {
                        setterRegions = setterRegions.Union(
                            EffectRegionSet.Unknown);
                    }

                    var previousRegions = _localRegions.TryGetValue(
                        propertyReceiver,
                        out var existingRegions)
                            ? existingRegions
                            : EffectRegionSet.Empty;
                    var joinedRegions = previousRegions.Union(setterRegions);
                    if (joinedRegions != previousRegions)
                    {
                        _localRegions[propertyReceiver] = joinedRegions;
                        changed = true;
                    }
                }

                if (operation is IPropertyReferenceOperation
                    {
                        Instance: { } getterInstance,
                        Property.GetMethod: { } getter
                    } propertyAccess &&
                    !IsSimpleSetterTarget(propertyAccess) &&
                    TryGetRefLikeStorageSymbol(
                        getterInstance,
                        out var getterReceiver))
                {
                    var getterRegions = ClassifyRegion(
                        getterInstance,
                        aliasSource: true);
                    foreach (var argument in propertyAccess.Arguments)
                    {
                        if (argument.Parameter?.RefKind is
                                RefKind.Ref or RefKind.Out ||
                            argument.Value.Type?.IsRefLikeType == true)
                        {
                            getterRegions = getterRegions.Union(ClassifyRegion(
                                argument.Value,
                                aliasSource: true));
                        }
                    }
                    if (MethodMayIntroduceUnknownRefAlias(getter))
                    {
                        getterRegions = getterRegions.Union(
                            EffectRegionSet.Unknown);
                    }

                    var previousRegions = _localRegions.TryGetValue(
                        getterReceiver,
                        out var existingRegions)
                            ? existingRegions
                            : EffectRegionSet.Empty;
                    var joinedRegions = previousRegions.Union(getterRegions);
                    if (joinedRegions != previousRegions)
                    {
                        _localRegions[getterReceiver] = joinedRegions;
                        changed = true;
                    }
                }

                (ISymbol? Target, IOperation? Value) source = operation switch
                {
                    IVariableDeclaratorOperation declarator =>
                        (declarator.Symbol, declarator.Initializer?.Value),
                    ISimpleAssignmentOperation
                    { Target: ILocalReferenceOperation local } assignment =>
                        (local.Local, assignment.Value),
                    ISimpleAssignmentOperation
                    { Target: IParameterReferenceOperation parameter } assignment =>
                        (parameter.Parameter, assignment.Value),
                    ICompoundAssignmentOperation
                    { Target: { } target } assignment
                        when TryGetRefLikeStorageSymbol(
                            target,
                            out var compoundTarget) =>
                        (compoundTarget, assignment),
                    IIncrementOrDecrementOperation
                    { Target: { } target } increment
                        when TryGetRefLikeStorageSymbol(
                            target,
                            out var incrementTarget) =>
                        (incrementTarget, increment),
                    ISimpleAssignmentOperation
                    {
                        IsRef: true,
                        Target: IFieldReferenceOperation
                        {
                            Field.RefKind: not RefKind.None,
                            Instance: { } instance
                        }
                    } assignment
                        when TryGetRefLikeStorageSymbol(
                            instance,
                            out var target) =>
                        (target, assignment.Value),
                    _ => default
                };
                if (source.Value == null || source.Target == null)
                {
                    continue;
                }

                var targetType = source.Target switch
                {
                    ILocalSymbol local => local.Type,
                    IParameterSymbol parameter => parameter.Type,
                    _ => null
                };
                var targetRefKind = source.Target switch
                {
                    ILocalSymbol local => local.RefKind,
                    IParameterSymbol parameter => parameter.RefKind,
                    _ => RefKind.None
                };
                var discovered = targetType?.IsValueType == true &&
                    !targetType.IsRefLikeType &&
                    targetRefKind == RefKind.None
                    ? EffectRegionSet.Empty
                    : ClassifyRegion(source.Value, aliasSource: true);
                var previous = _localRegions.TryGetValue(source.Target, out var existing)
                    ? existing
                    : EffectRegionSet.Empty;
                var joined = previous.Union(discovered);
                if (joined == previous)
                {
                    continue;
                }

                _localRegions[source.Target] = joined;
                changed = true;
            }
        }
    }

    private static bool IsSimpleSetterTarget(
        IPropertyReferenceOperation property)
    {
        return property.Parent is ISimpleAssignmentOperation assignment &&
            ReferenceEquals(assignment.Target, property);
    }

    private static bool TryGetRefLikeStorageSymbol(
        IOperation operation,
        out ISymbol symbol)
    {
        operation = DefiniteOperationFacts.UnwrapHarmlessValue(operation);
        switch (operation)
        {
            case ILocalReferenceOperation local
                when local.Local.Type.IsRefLikeType:
                symbol = local.Local;
                return true;
            case IParameterReferenceOperation parameter
                when parameter.Parameter.Type.IsRefLikeType:
                symbol = parameter.Parameter;
                return true;
            default:
                symbol = null!;
                return false;
        }
    }

    private static bool TryGetPropertySetter(
        IOperation operation,
        out IPropertyReferenceOperation? property,
        out IOperation? storedValue,
        out bool valueIsStoredDirectly)
    {
        switch (operation)
        {
            case ISimpleAssignmentOperation
            { Target: IPropertyReferenceOperation target } assignment:
                property = target;
                storedValue = assignment.Value;
                valueIsStoredDirectly = true;
                break;
            case ICoalesceAssignmentOperation
            { Target: IPropertyReferenceOperation target } assignment:
                property = target;
                storedValue = assignment.Value;
                valueIsStoredDirectly = true;
                break;
            case ICompoundAssignmentOperation
            { Target: IPropertyReferenceOperation target } assignment:
                property = target;
                storedValue = assignment.Value;
                valueIsStoredDirectly = false;
                break;
            case IIncrementOrDecrementOperation
            { Target: IPropertyReferenceOperation target }:
                property = target;
                storedValue = null;
                valueIsStoredDirectly = false;
                break;
            default:
                property = null;
                storedValue = null;
                valueIsStoredDirectly = false;
                break;
        }
        return property?.Property.SetMethod != null;
    }

    internal static bool IsInsideNestedCallable(IOperation operation, IOperation root)
    {
        for (var parent = operation.Parent; parent != null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return true;
            }
        }

        return false;
    }

    private bool MethodMayIntroduceUnknownRefAlias(IMethodSymbol method)
    {
        method = method.ReducedFrom ?? method;
        if (method.DeclaringSyntaxReferences.Length != 1)
        {
            return true;
        }

        var declaration = method.DeclaringSyntaxReferences[0].GetSyntax();
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(_compilation, declaration.SyntaxTree);
        var root = model.GetOperation(declaration);
        if (root == null)
        {
            return true;
        }

        foreach (var assignment in root.DescendantsAndSelf()
                     .OfType<ISimpleAssignmentOperation>()
                     .Where(static assignment => assignment.IsRef))
        {
            if (!IsCallMappedRefSource(assignment.Value, method))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCallMappedRefSource(
        IOperation operation,
        IMethodSymbol method)
    {
        operation = DefiniteOperationFacts.UnwrapHarmlessValue(operation);
        return operation switch
        {
            IInstanceReferenceOperation => true,
            IParameterReferenceOperation parameter =>
                SymbolEqualityComparer.Default.Equals(
                    parameter.Parameter.ContainingSymbol.OriginalDefinition,
                    method.OriginalDefinition),
            IFieldReferenceOperation { Field.IsStatic: false, Instance: { } instance } =>
                IsCallMappedRefSource(instance, method),
            IConditionalOperation
            {
                WhenTrue: { } whenTrue,
                WhenFalse: { } whenFalse
            } =>
                IsCallMappedRefSource(whenTrue, method) &&
                IsCallMappedRefSource(whenFalse, method),
            _ => false
        };
    }

    private EffectRegionSet ClassifyConversionRegion(
        IConversionOperation operation, bool aliasSource)
    {
        if (!string.Equals(
                operation.Syntax.Language,
                LanguageNames.CSharp,
                StringComparison.Ordinal))
        {
            return EffectRegionSet.Unknown;
        }

        var conversion = Microsoft.CodeAnalysis.CSharp.CSharpExtensions
            .GetConversion(operation);
        if (!conversion.Exists || conversion.IsDynamic)
        {
            return EffectRegionSet.Unknown;
        }

        // Reference conversions preserve the object identity of their operand.
        // Keep walking so a chain such as (Box)(object)value still maps back to
        // the caller-owned parameter (or receiver).
        if (conversion.IsReference &&
            operation.Operand.Type?.IsReferenceType == true &&
            operation.Type?.IsReferenceType == true)
        {
            return ClassifyRegion(operation.Operand, aliasSource);
        }

        // Boxing creates a new object containing a copy of the value. The box is
        // locally owned, even when the source is a ref parameter, so mutations
        // through an interface/object view must not be attributed to the caller.
        if (conversion.IsBoxing)
        {
            return EffectRegionSet.Create(
                EffectRegionId.Fresh(operation.Syntax.SpanStart));
        }

        // Unboxing, nullable, numeric, enum, and identity conversions whose
        // result is a value type all produce a by-value copy. A mutation of that
        // copy is local state and therefore has no caller-owned region.
        if (operation.Type?.IsValueType == true)
        {
            return EffectRegionSet.Empty;
        }

        // Null/default literals have no object identity to preserve. Keep the
        // previous empty-region behavior for these harmless built-in forms.
        if (conversion.IsNullLiteral || conversion.IsDefaultLiteral)
        {
            return EffectRegionSet.Empty;
        }

        return EffectRegionSet.Unknown;
    }

    private EffectRegionSet ClassifyLocal(ILocalSymbol local)
    {
        if (SymbolEqualityComparer.Default.Equals(
                local.ContainingSymbol?.OriginalDefinition,
                _method.OriginalDefinition))
        {
            return _localRegions.TryGetValue(local, out var regions)
                ? regions
                : EffectRegionSet.Unknown;
        }

        var ordinal = local.DeclaringSyntaxReferences.FirstOrDefault()?.Span.Start ?? 0;
        return EffectRegionSet.Create(EffectRegionId.Captured(ordinal));
    }
}
