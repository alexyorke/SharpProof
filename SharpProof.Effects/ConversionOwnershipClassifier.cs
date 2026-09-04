namespace SharpProof.Effects;

internal sealed class ConversionOwnershipClassifier
{
    private readonly CoalesceAssignmentFlowCaptures _coalesceCaptures;
    private readonly ConditionalTruthOperatorFlowCaptures _conditionalTruthCaptures;
    private readonly Compilation _compilation;
    private readonly CreationFlowCaptures _creationCaptures;
    private readonly Dictionary<ISymbol, EffectRegionSet> _localRegions =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ISymbol, EffectRegionSet> _refLocalStorageRegions =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, RefAliasAnalysis> _refAliasAnalyses =
        new(SymbolEqualityComparer.Default);
    private readonly IMethodSymbol _method;

    private readonly record struct RefAliasAnalysis(
        bool MayIntroduceUnknownAlias,
        bool IsContextIndependent);

    internal ConversionOwnershipClassifier(
        IMethodSymbol method,
        Compilation compilation,
        CoalesceAssignmentFlowCaptures coalesceCaptures,
        ConditionalTruthOperatorFlowCaptures conditionalTruthCaptures,
        CreationFlowCaptures creationCaptures)
    {
        _method = method;
        _compilation = compilation;
        _coalesceCaptures = coalesceCaptures;
        _conditionalTruthCaptures = conditionalTruthCaptures;
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
            IFlowCaptureReferenceOperation capture
                when _conditionalTruthCaptures.TryResolve(
                    capture,
                    out var truthOperand) =>
                ClassifyRegion(truthOperand, aliasSource),
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

    /// <summary>
    /// Classifies state reachable through a call argument, including reference
    /// fields copied as part of a managed value type.
    /// </summary>
    /// <remarks>
    /// <see cref="ClassifyRegion"/> describes ownership of the value itself.
    /// That distinction keeps writes to an ordinary by-value struct copy local.
    /// A call boundary can also write objects referenced by fields of that copy,
    /// so managed value arguments need a separate reachability classification.
    /// </remarks>
    internal EffectRegionSet ClassifyCallArgumentRegion(
        IOperation? operation)
    {
        return operation?.Type is
        {
            IsValueType: true,
            IsUnmanagedType: false,
            IsRefLikeType: false
        }
            ? ClassifyManagedValueReachability(operation)
            : ClassifyRegion(operation);
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

        return (parameter.Type.IsReferenceType ||
                parameter.Type.IsRefLikeType) &&
            _localRegions.TryGetValue(parameter, out var learnedRegions)
                ? declaredRegion.Union(learnedRegions)
                : declaredRegion;
    }

    internal EffectRegionSet ClassifyRefLocalStorage(ILocalSymbol local)
    {
        if (SymbolEqualityComparer.Default.Equals(
                local.ContainingSymbol?.OriginalDefinition,
                _method.OriginalDefinition))
        {
            return _refLocalStorageRegions.TryGetValue(local, out var regions)
                ? regions
                : EffectRegionSet.Unknown;
        }

        return ClassifyCapturedLocal(local);
    }

    private EffectRegionSet ClassifyManagedValueReachability(
        IOperation operation)
    {
        return operation switch
        {
            IConversionOperation { OperatorMethod: null } conversion =>
                ClassifyCallArgumentRegion(conversion.Operand),
            IParenthesizedOperation parenthesized =>
                ClassifyCallArgumentRegion(parenthesized.Operand),
            IParameterReferenceOperation parameter =>
                ReachableParameterRegion(parameter.Parameter),
            IInstanceReferenceOperation =>
                EffectRegionSet.Create(EffectRegionId.Receiver),
            IFieldReferenceOperation { Field.IsStatic: true } =>
                EffectRegionSet.Create(EffectRegionId.Static()),
            IFieldReferenceOperation { Instance: { } instance } =>
                ClassifyCallArgumentRegion(instance),
            IConditionalOperation conditional =>
                ClassifyCallArgumentRegion(conditional.WhenTrue).Union(
                    ClassifyCallArgumentRegion(conditional.WhenFalse)),
            ICoalesceOperation coalesce =>
                ClassifyCallArgumentRegion(coalesce.Value).Union(
                    ClassifyCallArgumentRegion(coalesce.WhenNull)),
            ITupleOperation tuple =>
                tuple.Elements.Aggregate(
                    EffectRegionSet.Empty,
                    (regions, element) => regions.Union(
                        ClassifyCallArgumentRegion(element))),
            IFlowCaptureReferenceOperation capture
                when _coalesceCaptures.TryResolve(
                    capture,
                    out var captured) =>
                ClassifyCallArgumentRegion(captured),
            ILiteralOperation or IDefaultValueOperation =>
                EffectRegionSet.Empty,
            _ => EffectRegionSet.Unknown
        };
    }

    private EffectRegionSet ReachableParameterRegion(
        IParameterSymbol parameter)
    {
        var declaredRegion = ClassifyParameter(parameter);
        return !declaredRegion.IsEmpty ||
            !SymbolEqualityComparer.Default.Equals(
                parameter.ContainingSymbol?.OriginalDefinition,
                _method.OriginalDefinition)
                ? declaredRegion
                : EffectRegionSet.Create(
                    EffectRegionId.Parameter(parameter.Ordinal));
    }

    internal void BuildLocalRegions(
        IOperation root,
        Func<IOperation, bool> isReachable,
        ImmutableArray<IOperation> operations)
    {
        var relevant = operations
            .Where(operation => !IsInsideNestedCallable(operation, root))
            .ToImmutableArray();
        foreach (var declarator in relevant.OfType<IVariableDeclaratorOperation>())
        {
            if (!_localRegions.ContainsKey(declarator.Symbol))
            {
                _localRegions.Add(declarator.Symbol, EffectRegionSet.Empty);
            }
            if (declarator.Symbol.RefKind != RefKind.None &&
                !_refLocalStorageRegions.ContainsKey(declarator.Symbol))
            {
                _refLocalStorageRegions.Add(
                    declarator.Symbol,
                    EffectRegionSet.Empty);
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

                if (TryGetRefLocalAliasSource(
                        operation,
                        out var refLocal,
                        out var refSource))
                {
                    var discoveredStorage = ClassifyRefAliasSource(refSource);
                    var previousStorage = _refLocalStorageRegions.TryGetValue(
                        refLocal,
                        out var existingStorage)
                            ? existingStorage
                            : EffectRegionSet.Empty;
                    var joinedStorage = previousStorage.Union(discoveredStorage);
                    if (joinedStorage != previousStorage)
                    {
                        _refLocalStorageRegions[refLocal] = joinedStorage;
                        changed = true;
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

    private EffectRegionSet ClassifyRefAliasSource(IOperation? operation)
    {
        if (operation == null)
        {
            return EffectRegionSet.Unknown;
        }

        operation = DefiniteOperationFacts.UnwrapHarmlessValue(operation);
        return operation switch
        {
            ILocalReferenceOperation local
                when local.Local.RefKind != RefKind.None =>
                ClassifyRefLocalStorage(local.Local),
            ILocalReferenceOperation local =>
                ClassifyLocalStorage(local.Local),
            IParameterReferenceOperation parameter
                when parameter.Parameter.RefKind == RefKind.None &&
                    SymbolEqualityComparer.Default.Equals(
                        parameter.Parameter.ContainingSymbol?.OriginalDefinition,
                        _method.OriginalDefinition) =>
                EffectRegionSet.Empty,
            IConditionalOperation conditional =>
                ClassifyRefAliasSource(conditional.WhenTrue).Union(
                    ClassifyRefAliasSource(conditional.WhenFalse)),
            ICoalesceOperation coalesce =>
                ClassifyRefAliasSource(coalesce.Value).Union(
                    ClassifyRefAliasSource(coalesce.WhenNull)),
            _ => ClassifyRegion(operation, aliasSource: true)
        };
    }

    private EffectRegionSet ClassifyLocalStorage(ILocalSymbol local)
    {
        if (SymbolEqualityComparer.Default.Equals(
                local.ContainingSymbol?.OriginalDefinition,
                _method.OriginalDefinition))
        {
            return EffectRegionSet.Empty;
        }

        return ClassifyCapturedLocal(local);
    }

    private static bool TryGetRefLocalAliasSource(
        IOperation operation,
        out ILocalSymbol local,
        out IOperation source)
    {
        switch (operation)
        {
            case IVariableDeclaratorOperation
            {
                Symbol.RefKind: not RefKind.None,
                Initializer.Value: { } initializer
            } declarator:
                local = declarator.Symbol;
                source = initializer;
                return true;
            case ISimpleAssignmentOperation
            {
                IsRef: true,
                Target: ILocalReferenceOperation target
            } assignment:
                local = target.Local;
                source = assignment.Value;
                return true;
            default:
                local = null!;
                source = null!;
                return false;
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
        method = (method.ReducedFrom ?? method).OriginalDefinition;
        if (_refAliasAnalyses.TryGetValue(method, out var cached))
        {
            return cached.MayIntroduceUnknownAlias;
        }

        var analysis = AnalyzeMethodMayIntroduceUnknownRefAlias(
            method,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
        if (analysis.IsContextIndependent)
        {
            _refAliasAnalyses[method] = analysis;
        }
        return analysis.MayIntroduceUnknownAlias;
    }

    private RefAliasAnalysis AnalyzeMethodMayIntroduceUnknownRefAlias(
        IMethodSymbol method,
        HashSet<IMethodSymbol> activeMethods)
    {
        method = (method.ReducedFrom ?? method).OriginalDefinition;
        if (method.DeclaringSyntaxReferences.Length != 1)
        {
            return new(true, true);
        }
        if (activeMethods.Count >= EffectCallGraph.MaximumCallGraphDepth)
        {
            return new(true, false);
        }
        if (_refAliasAnalyses.TryGetValue(method, out var cached))
        {
            return cached;
        }
        if (!activeMethods.Add(method))
        {
            return new(false, false);
        }

        try
        {
            var declaration = method.DeclaringSyntaxReferences[0].GetSyntax();
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, declaration.SyntaxTree);
            var root = model.GetOperation(declaration);
            if (root == null)
            {
                return new(true, true);
            }

            var refLikeInvocations = new List<IInvocationOperation>();
            foreach (var operation in root.DescendantsAndSelf())
            {
                if (operation is ISimpleAssignmentOperation
                    {
                        IsRef: true,
                        Value: { } value
                    } && !IsCallMappedRefSource(value, method))
                {
                    return new(true, true);
                }

                if (operation is IInvocationOperation invocation &&
                    CanRebindRefLikeStorage(invocation))
                {
                    refLikeInvocations.Add(invocation);
                }
            }

            var isContextIndependent = true;
            foreach (var invocation in refLikeInvocations)
            {
                var nested = AnalyzeMethodMayIntroduceUnknownRefAlias(
                        invocation.TargetMethod,
                        activeMethods);
                isContextIndependent &= nested.IsContextIndependent;
                if (nested.MayIntroduceUnknownAlias)
                {
                    return new(true, isContextIndependent);
                }
            }

            return new(false, isContextIndependent);
        }
        finally
        {
            activeMethods.Remove(method);
        }
    }

    private static bool CanRebindRefLikeStorage(
        IInvocationOperation invocation)
    {
        if (invocation.Instance?.Type?.IsRefLikeType == true ||
            !invocation.TargetMethod.IsStatic &&
            invocation.TargetMethod.ContainingType?.IsRefLikeType == true)
        {
            return true;
        }

        return invocation.Arguments.Any(static argument =>
            argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
            argument.Value.Type?.IsRefLikeType == true);
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

        // Concrete value-type boxing creates a locally owned copy. Roslyn also
        // classifies a type-parameter-to-interface conversion as boxing when the
        // type parameter permits both value and reference instantiations; retain
        // the operand ownership for the reference-instantiation path.
        if (conversion.IsBoxing)
        {
            var fresh = EffectRegionSet.Create(
                EffectRegionId.Fresh(operation.Syntax.SpanStart));
            if (operation.Operand.Type is ITypeParameterSymbol typeParameter)
            {
                if (typeParameter.IsReferenceType)
                {
                    return ClassifyRegion(operation.Operand, aliasSource);
                }
                if (!typeParameter.IsValueType)
                {
                    return fresh.Union(
                        ClassifyRegion(operation.Operand, aliasSource));
                }
            }
            return fresh;
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

        return ClassifyCapturedLocal(local);
    }

    private static EffectRegionSet ClassifyCapturedLocal(ILocalSymbol local)
    {
        var ordinal = local.DeclaringSyntaxReferences.FirstOrDefault()?.Span.Start ?? 0;
        return EffectRegionSet.Create(EffectRegionId.Captured(ordinal));
    }
}
