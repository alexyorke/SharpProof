namespace SharpProof.Effects;

internal sealed class EffectCallSiteResolver(
    EffectAnalysisSession session,
    IMethodSymbol caller,
    List<EffectCallSite> sourceCalls,
    ManagedFlowResult? flow)
{
    private readonly IMethodSymbol _caller =
        ArgumentNullGuard.NotNull(caller, nameof(caller));
    private readonly ManagedFlowResult? _flow = flow;
    private readonly EffectAnalysisSession _session =
        ArgumentNullGuard.NotNull(session, nameof(session));
    private readonly List<EffectCallSite> _sourceCalls =
        ArgumentNullGuard.NotNull(sourceCalls, nameof(sourceCalls));

    internal EffectSummary Resolve(
        IMethodSymbol target,
        EffectRegionSet receiver,
        ImmutableArray<EffectRegionSet> arguments,
        ImmutableArray<IOperation?> actualArguments,
        bool dispatchUncertain,
        IOperation origin,
        IOperation? instance,
        IEnumerable<IArgumentOperation>? callArguments = null)
    {
        var summary = _session.ResolveCall(
            _caller,
            target,
            receiver,
            arguments,
            dispatchUncertain,
            _sourceCalls,
            origin,
            instance,
            actualArguments,
            _flow);
        return callArguments == null
            ? summary
            : EffectSummaryOperations.Join(
                ExpandedParamsEvidence(callArguments),
                summary);
    }

    internal EffectSummary ResolveOperator(
        IMethodSymbol? target,
        EffectRegionSet receiver,
        ImmutableArray<EffectRegionSet> arguments,
        ImmutableArray<IOperation?> actualArguments,
        IOperation origin)
    {
        return target == null
            ? EffectSummary.Empty
            : Resolve(
                target,
                receiver,
                arguments,
                actualArguments,
                dispatchUncertain: false,
                origin,
                instance: null);
    }

    internal EffectSummary ResolveConstruction(
        IObjectCreationOperation creation,
        EffectRegionSet receiver,
        ImmutableArray<EffectRegionSet> arguments)
    {
        var constructor = creation.Constructor;
        if (constructor == null)
        {
            return EffectSummaryOperations.Unsupported();
        }

        var implicitLayers = EffectSummary.Empty;
        var implicitDepth = 0;
        while (EffectMethodNodeBuilder.IsProvablyEmptyImplicitConstructorLayer(
                   constructor,
                   _session.ApiSpecs))
        {
            if (implicitDepth++ >= 256)
            {
                return EffectSummaryOperations.Join(
                    implicitLayers,
                    EffectSummaryOperations.Unsupported());
            }

            implicitLayers = EffectSummaryOperations.Join(
                implicitLayers,
                EffectSummaryOperations.DirectCall());
            if (constructor.ContainingType.IsValueType)
            {
                return implicitLayers;
            }

            constructor = EffectMethodNodeBuilder
                .GetUniqueParameterlessBaseConstructor(constructor);
            if (constructor == null)
            {
                return EffectSummaryOperations.Join(
                    implicitLayers,
                    EffectSummaryOperations.Unsupported());
            }

            arguments = [];
        }

        return EffectSummaryOperations.Join(
            implicitLayers,
            Resolve(
                constructor,
                receiver,
                arguments,
                AlignActualArguments(
                    creation.Arguments,
                    constructor.Parameters.Length),
                dispatchUncertain: false,
                creation,
                instance: null,
                creation.Arguments));
    }

    internal static ImmutableArray<IOperation?> AlignActualArguments(
        ImmutableArray<IArgumentOperation> arguments,
        int parameterCount)
    {
        var result = Enumerable.Repeat<IOperation?>(
            null,
            parameterCount).ToImmutableArray();
        foreach (var argument in arguments)
        {
            if (argument.ArgumentKind ==
                    ArgumentKind.ParamArray ||
                argument.Parameter is not
                {
                    Ordinal: var ordinal
                } ||
                ordinal < 0 ||
                ordinal >= result.Length)
            {
                continue;
            }

            result = result.SetItem(
                ordinal,
                argument.Value);
        }

        return result;
    }

    internal static EffectSummary ExpandedParamsEvidence(
        IEnumerable<IArgumentOperation> arguments)
    {
        return arguments.Any(static argument =>
            argument.ArgumentKind ==
            ArgumentKind.ParamArray)
                ? EffectSummaryOperations.Unsupported()
                : EffectSummary.Empty;
    }
}
