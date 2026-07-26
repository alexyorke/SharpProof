namespace SharpProof.Effects;

internal static class EffectSummaryOperations {
    private static readonly EffectSummaryDomain Domain = EffectSummaryDomain.Instance;

    internal static EffectSummary Join(params EffectSummary[] summaries) {
        var result = EffectSummary.Bottom;
        foreach (var summary in summaries)
            result = Domain.Join(result, summary);
        return result;
    }

    internal static EffectSummary Read(EffectRegionSet regions) =>
        new(
            regions,
            EffectRegionSet.Empty,
            EffectAllocationKind.None,
            EffectCapabilitySet.Empty,
            EffectThrowSet.Empty,
            EffectTermination.Terminates,
            EffectCompleteness.Complete);

    internal static EffectSummary Write(EffectRegionSet regions) =>
        new(
            EffectRegionSet.Empty,
            regions,
            EffectAllocationKind.None,
            EffectCapabilitySet.Empty,
            EffectThrowSet.Empty,
            EffectTermination.Terminates,
            EffectCompleteness.Complete);

    internal static EffectSummary Allocate(EffectAllocationKind allocation) =>
        new(
            EffectRegionSet.Empty,
            EffectRegionSet.Empty,
            allocation,
            EffectCapabilitySet.Empty,
            EffectThrowSet.Empty,
            EffectTermination.Terminates,
            EffectCompleteness.Complete);

    internal static EffectSummary Throw(EffectThrowSet exceptions) =>
        new(
            EffectRegionSet.Empty,
            EffectRegionSet.Empty,
            EffectAllocationKind.None,
            EffectCapabilitySet.Empty,
            exceptions,
            EffectTermination.Terminates,
            exceptions.IncludesUnknown
                ? EffectCompleteness.Incomplete
                : EffectCompleteness.Complete);

    internal static EffectSummary Capability(EffectCapabilityKind capabilities) =>
        new(
            EffectRegionSet.Empty,
            EffectRegionSet.Empty,
            EffectAllocationKind.None,
            new EffectCapabilitySet(capabilities),
            EffectThrowSet.Empty,
            EffectTermination.Terminates,
            EffectCompleteness.Complete);

    internal static EffectSummary DirectCall() =>
        new(
            EffectRegionSet.Empty,
            EffectRegionSet.Empty,
            EffectAllocationKind.None,
            EffectCapabilitySet.Empty,
            EffectThrowSet.Empty,
            EffectTermination.Terminates,
            EffectCompleteness.Complete,
            EffectUncertainty.DirectCall);

    internal static EffectSummary UnknownBoundary(EffectUncertainty uncertainty) =>
        new(
            EffectRegionSet.Unknown,
            EffectRegionSet.Unknown,
            EffectAllocationKind.Unknown,
            EffectCapabilitySet.Unknown,
            EffectThrowSet.Unknown,
            EffectTermination.Unknown,
            EffectCompleteness.Incomplete,
            uncertainty);

    internal static EffectSummary Unsupported() =>
        UnknownBoundary(EffectUncertainty.UnsupportedOperation);

    internal static EffectSummary MayDiverge() =>
        new(
            EffectRegionSet.Empty,
            EffectRegionSet.Empty,
            EffectAllocationKind.None,
            EffectCapabilitySet.Empty,
            EffectThrowSet.Empty,
            EffectTermination.MayDiverge,
            EffectCompleteness.Complete);

    internal static EffectSummary Remap(
        EffectSummary summary,
        EffectRegionSet receiver,
        ImmutableArray<EffectRegionSet> arguments) {
        if (summary.IsBottom) return summary;
        return new EffectSummary(
            RemapRegions(summary.Reads, receiver, arguments),
            RemapRegions(summary.Writes, receiver, arguments),
            summary.Allocation,
            summary.Capabilities,
            summary.Throws,
            summary.Termination,
            summary.Completeness,
            summary.Uncertainty);
    }

    private static EffectRegionSet RemapRegions(
        EffectRegionSet regions,
        EffectRegionSet receiver,
        ImmutableArray<EffectRegionSet> arguments) {
        if (regions.IsUnknown) return EffectRegionSet.Unknown;
        var result = EffectRegionSet.Empty;
        foreach (var region in regions.Regions) {
            var mapped = region.Kind switch {
                EffectRegionKind.Receiver => receiver,
                EffectRegionKind.Parameter when region.Ordinal < arguments.Length =>
                    arguments[region.Ordinal],
                EffectRegionKind.Parameter => EffectRegionSet.Unknown,
                _ => EffectRegionSet.Create(region)
            };
            result = result.Union(mapped);
        }
        return result;
    }
}
