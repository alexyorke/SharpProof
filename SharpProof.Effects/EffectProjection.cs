namespace SharpProof.Effects;

public readonly record struct EffectProjection {
    public EffectProjection(
        EffectContractKind effects,
        EffectContractCapabilityKind capabilities,
        bool isComplete) =>
        (Effects, Capabilities, IsComplete) =
            (effects, capabilities, isComplete);

    public EffectContractKind Effects { get; }
    public EffectContractCapabilityKind Capabilities { get; }
    public bool IsComplete { get; }
}

public static class EffectSummaryProjector {
    public static EffectProjection Project(EffectSummary summary) {
        if (summary == null) throw new ArgumentNullException(nameof(summary));
        if (summary.IsBottom)
            return new EffectProjection(
                EffectContractKind.None, EffectContractCapabilityKind.None, isComplete: true);

        var effects = ProjectRegions(summary.Reads, isWrite: false) |
                      ProjectRegions(summary.Writes, isWrite: true);
        var allocationUnknown =
            (summary.Allocation & (EffectAllocationKind)(1 << 2)) != 0;
        if (summary.Allocation != EffectAllocationKind.None && !allocationUnknown)
            effects |= EffectContractKind.Allocates;
        if (!summary.Throws.Types.IsDefaultOrEmpty)
            effects |= EffectContractKind.Throws;
        if (!summary.Capabilities.IsUnknown) {
            if (summary.Capabilities.Contains(EffectCapabilityKind.Synchronization))
                effects |= EffectContractKind.Synchronizes;
            if (summary.Capabilities.Contains(EffectCapabilityKind.Randomness) ||
                summary.Capabilities.Contains(EffectCapabilityKind.Clock))
                effects |= EffectContractKind.UsesNondeterminism;
            if (summary.Capabilities.Contains(EffectCapabilityKind.Reflection))
                effects |= EffectContractKind.UsesReflection;
            if (summary.Capabilities.Contains(EffectCapabilityKind.NativeInterop))
                effects |= EffectContractKind.UsesNativeCode;
        }
        var isComplete =
            summary.Completeness == EffectCompleteness.Complete &&
            !allocationUnknown &&
            !summary.Throws.IncludesUnknown &&
            !summary.Reads.IsUnknown &&
            !summary.Writes.IsUnknown &&
            !summary.Capabilities.IsUnknown &&
            summary.Uncertainty is EffectUncertainty.None or EffectUncertainty.DirectCall;

        var capabilities = summary.Capabilities.IsUnknown
            ? EffectContractCapabilityKind.None
            : (EffectContractCapabilityKind)(
                summary.Capabilities.Kinds & EffectCapabilityKind.AllKnown);
        return new EffectProjection(effects, capabilities, isComplete);
    }

    private static EffectContractKind ProjectRegions(
        EffectRegionSet regions, bool isWrite) {
        if (regions.IsUnknown)
            return EffectContractKind.None;
        var result = EffectContractKind.None;
        foreach (var region in regions.Regions)
            result |= ProjectRegion(region.Kind, isWrite);
        return result;
    }

    private static EffectContractKind ProjectRegion(
        EffectRegionKind kind, bool isWrite) {
        var offset = kind switch {
            EffectRegionKind.Receiver => 0,
            EffectRegionKind.Parameter => 1,
            EffectRegionKind.Captured => 2,
            EffectRegionKind.Static => 3,
            EffectRegionKind.Ambient => 4,
            _ => -1
        };
        return offset < 0
            ? EffectContractKind.None
            : (EffectContractKind)(1L << (offset + (isWrite ? 5 : 0)));
    }
}
