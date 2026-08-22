namespace SharpProof.Effects;

internal sealed class ConversionOwnershipClassifier
{
    private readonly CoalesceAssignmentFlowCaptures _coalesceCaptures;
    private readonly CreationFlowCaptures _creationCaptures;
    private readonly Dictionary<ISymbol, EffectRegionSet> _localRegions =
        new(SymbolEqualityComparer.Default);
    private readonly IMethodSymbol _method;

    internal ConversionOwnershipClassifier(
        IMethodSymbol method,
        CoalesceAssignmentFlowCaptures coalesceCaptures,
        CreationFlowCaptures creationCaptures)
    {
        _method = method;
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
        if (PrimaryConstructorParameterOwnership.IsReceiverBacked(
                parameter,
                _method))
        {
            return EffectRegionSet.Create(EffectRegionId.Receiver);
        }

        if (SymbolEqualityComparer.Default.Equals(
                parameter.ContainingSymbol?.OriginalDefinition,
                _method.OriginalDefinition))
        {
            if (parameter.Type.IsValueType &&
                !parameter.Type.IsRefLikeType &&
                parameter.RefKind == RefKind.None)
            {
                return EffectRegionSet.Empty;
            }

            return EffectRegionSet.Create(EffectRegionId.Parameter(parameter.Ordinal));
        }

        return EffectRegionSet.Create(EffectRegionId.Captured(parameter.Ordinal));
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

                if (operation is IInvocationOperation invocation)
                {
                    var argumentRegions = EffectRegionSet.Empty;
                    var refLikeLocals = new List<ILocalSymbol>();
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
                            if (canRebind &&
                                DefiniteOperationFacts.UnwrapHarmlessValue(
                                    argument.Value) is
                                ILocalReferenceOperation argumentLocal &&
                                argumentLocal.Local.Type.IsRefLikeType)
                            {
                                refLikeLocals.Add(argumentLocal.Local);
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
                        DefiniteOperationFacts.UnwrapHarmlessValue(
                            invocationInstanceLocal) is
                            ILocalReferenceOperation receiver &&
                        receiver.Local.Type.IsRefLikeType)
                    {
                        refLikeLocals.Add(receiver.Local);
                    }

                    foreach (var refLikeLocal in refLikeLocals)
                    {
                        var previousReceiverRegions =
                            _localRegions.TryGetValue(
                                refLikeLocal,
                                out var receiverRegions)
                                ? receiverRegions
                                : EffectRegionSet.Empty;
                        var joinedReceiverRegions =
                            previousReceiverRegions.Union(argumentRegions);
                        if (joinedReceiverRegions != previousReceiverRegions)
                        {
                            _localRegions[refLikeLocal] =
                                joinedReceiverRegions;
                            changed = true;
                        }
                    }
                }

                (ILocalSymbol? Target, IOperation? Value) source = operation switch
                {
                    IVariableDeclaratorOperation declarator =>
                        (declarator.Symbol, declarator.Initializer?.Value),
                    IAssignmentOperation { Target: ILocalReferenceOperation local } assignment =>
                        (local.Local, assignment.Value),
                    ISimpleAssignmentOperation
                    {
                        IsRef: true,
                        Target: IFieldReferenceOperation
                        {
                            Field.RefKind: not RefKind.None,
                            Instance: { } instance
                        }
                    } assignment
                        when DefiniteOperationFacts.UnwrapHarmlessValue(
                            instance) is ILocalReferenceOperation local =>
                        (local.Local, assignment.Value),
                    _ => default
                };
                if (source.Value == null || source.Target == null)
                {
                    continue;
                }

                var discovered = source.Target.Type.IsValueType &&
                    !source.Target.Type.IsRefLikeType &&
                    source.Target.RefKind == RefKind.None
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
