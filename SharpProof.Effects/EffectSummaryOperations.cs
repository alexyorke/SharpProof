namespace SharpProof.Effects;

internal static class EffectSummaryOperations
{
    private static readonly EffectSummaryDomain Domain = EffectSummaryDomain.Instance;

    internal static EffectSummary Join(params EffectSummary[] summaries)
    {
        return JoinFrom(EffectSummary.Bottom, summaries);
    }

    internal static EffectSummary JoinFrom(EffectSummary result, IEnumerable<EffectSummary> summaries)
    {
        foreach (var summary in summaries)
        {
            result = Domain.Join(result, summary);
        }

        return result;
    }

    internal static EffectSummary Read(EffectRegionSet regions)
    {
        return Create(reads: regions);
    }

    internal static EffectSummary Write(EffectRegionSet regions)
    {
        return Create(writes: regions);
    }

    internal static EffectSummary Allocate(EffectAllocationKind allocation)
    {
        return Create(allocation: allocation);
    }

    internal static EffectSummary Throw(EffectThrowSet exceptions)
    {
        return Create(throws: exceptions, completeness: exceptions.IncludesUnknown
            ? EffectCompleteness.Incomplete : EffectCompleteness.Complete);
    }

    internal static EffectSummary WithThrows(EffectSummary summary, EffectThrowSet exceptions)
    {
        if (summary.IsBottom)
        {
            return summary;
        }

        return new EffectSummary(
            summary.Reads, summary.Writes, summary.Allocation, summary.Capabilities,
            exceptions, summary.Termination, summary.Completeness, summary.Uncertainty,
            summary.AnalysisIncompleteReason);
    }

    internal static EffectSummary ExceptionConstructionThrow(
        EffectSummary construction,
        EffectThrowSet exceptions)
    {
        return new EffectSummary(
            EffectRegionSet.Empty,
            EffectRegionSet.Empty,
            EffectAllocationKind.None,
            construction.Capabilities,
            exceptions,
            EffectTermination.Unknown,
            construction.Completeness,
            construction.Uncertainty,
            construction.AnalysisIncompleteReason);
    }

    internal static EffectSummary Capability(EffectCapabilityKind capabilities)
    {
        return Create(capabilities: new EffectCapabilitySet(capabilities));
    }

    internal static EffectSummary DirectCall()
    {
        return Create(uncertainty: EffectUncertainty.DirectCall);
    }

    internal static EffectSummary UnknownBoundary(EffectUncertainty uncertainty)
    {
        return new(
            EffectRegionSet.Unknown, EffectRegionSet.Unknown,
            EffectAllocationKind.Unknown, EffectCapabilitySet.Unknown,
            EffectThrowSet.Unknown, EffectTermination.Unknown,
            EffectCompleteness.Incomplete, uncertainty);
    }

    internal static EffectSummary TypeInitializationBoundary()
    {
        return new(
            EffectRegionSet.Empty, EffectRegionSet.Empty,
            EffectAllocationKind.Unknown, EffectCapabilitySet.Unknown,
            EffectThrowSet.Unknown, EffectTermination.Unknown,
            EffectCompleteness.Incomplete, EffectUncertainty.UnmodeledCall);
    }

    internal static EffectSummary IncompleteAnalysis(EffectAnalysisIncompleteReason reason)
    {
        return new(
            EffectRegionSet.Empty, EffectRegionSet.Empty,
            EffectAllocationKind.None, EffectCapabilitySet.Empty,
            EffectThrowSet.Empty,
            reason == EffectAnalysisIncompleteReason.CyclicControlFlow
                ? EffectTermination.MayDiverge
                : EffectTermination.Unknown,
            EffectCompleteness.Incomplete, EffectUncertainty.None, reason);
    }

    internal static EffectSummary Unsupported()
    {
        return UnknownBoundary(EffectUncertainty.UnsupportedOperation);
    }

    internal static EffectSummary MayDiverge()
    {
        return Create(termination: EffectTermination.MayDiverge);
    }

    internal static EffectSummary Remap(
        EffectSummary summary,
        EffectRegionSet receiver,
        ImmutableArray<EffectRegionSet> arguments)
    {
        return Remap(summary, receiver, receiver, arguments);
    }

    internal static EffectSummary Remap(
        EffectSummary summary,
        EffectRegionSet receiver,
        EffectRegionSet writeReceiver,
        ImmutableArray<EffectRegionSet> arguments)
    {
        if (summary.IsBottom)
        {
            return summary;
        }

        return new EffectSummary(
            RemapRegions(summary.Reads, receiver, arguments),
            RemapRegions(summary.Writes, writeReceiver, arguments),
            summary.Allocation, summary.Capabilities,
            summary.Throws, summary.Termination,
            summary.Completeness, summary.Uncertainty, summary.AnalysisIncompleteReason);
    }

    private static EffectRegionSet RemapRegions(
        EffectRegionSet regions, EffectRegionSet receiver, ImmutableArray<EffectRegionSet> arguments)
    {
        if (regions.IsUnknown)
        {
            return EffectRegionSet.Unknown;
        }

        var result = EffectRegionSet.Empty;
        foreach (var region in regions.Regions)
        {
            var mapped = region.Kind switch
            {
                EffectRegionKind.Receiver => receiver,
                EffectRegionKind.Parameter when region.Ordinal < arguments.Length => arguments[region.Ordinal],
                EffectRegionKind.Parameter => EffectRegionSet.Unknown,
                _ => EffectRegionSet.Create(region)
            };
            result = result.Union(mapped);
        }
        return result;
    }

    private static EffectSummary Create(
        EffectRegionSet reads = default,
        EffectRegionSet writes = default,
        EffectAllocationKind allocation = EffectAllocationKind.None,
        EffectCapabilitySet capabilities = default,
        EffectThrowSet throws = default,
        EffectTermination termination = EffectTermination.Terminates,
        EffectCompleteness completeness = EffectCompleteness.Complete,
        EffectUncertainty uncertainty = EffectUncertainty.None)
    {
        return new(
            reads, writes, allocation, capabilities,
            throws, termination, completeness, uncertainty);
    }
}

/// <summary>
/// The effects observed while evaluating one source-order step.
/// </summary>
/// <remarks>
/// A may-effect summary alone cannot say whether a following expression can
/// execute.  Keeping normal completion beside the summary lets callers retain
/// effects from a definitely throwing step while suppressing effects that are
/// only reachable after it.
/// </remarks>
internal readonly record struct EffectStep(
    EffectSummary Summary,
    bool CompletesNormally)
{
    internal static EffectStep Empty => new(EffectSummary.Empty, true);

    internal EffectStep Then(EffectStep next)
    {
        return new(
            CompletesNormally
                ? EffectSummaryDomain.Instance.Join(Summary, next.Summary)
                : Summary,
            CompletesNormally && next.CompletesNormally);
    }

    internal EffectStep WithSummary(EffectSummary summary)
    {
        return new(summary, CompletesNormally);
    }
}
