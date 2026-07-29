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
            return new EffectProjection(EffectContractKind.None, EffectContractCapabilityKind.None, isComplete: true);

        var effects = ProjectRegions(summary.Reads, isWrite: false) |
                      ProjectRegions(summary.Writes, isWrite: true);
        var allocationUnknown =
            (summary.Allocation & (EffectAllocationKind)(1 << 2)) != 0;
        if (summary.Allocation != EffectAllocationKind.None && !allocationUnknown)
            effects |= EffectContractKind.Allocates;
        if (!summary.Throws.Types.IsDefaultOrEmpty)
            effects |= EffectContractKind.Throws;
        if (!summary.Capabilities.IsUnknown)
            effects |= EffectContractMappings.ToContractEffects(summary.Capabilities.Kinds);
        var isComplete = summary.Completeness == EffectCompleteness.Complete &&
            !allocationUnknown &&
            !summary.Throws.IncludesUnknown &&
            !summary.Reads.IsUnknown &&
            !summary.Writes.IsUnknown &&
            !summary.Capabilities.IsUnknown &&
            summary.Uncertainty is EffectUncertainty.None or EffectUncertainty.DirectCall;

        var capabilities = summary.Capabilities.IsUnknown
            ? EffectContractCapabilityKind.None
            : EffectContractMappings.ToContractCapabilities(summary.Capabilities.Kinds);
        return new EffectProjection(effects, capabilities, isComplete);
    }

    private static EffectContractKind ProjectRegions(EffectRegionSet regions, bool isWrite) {
        if (regions.IsUnknown) return EffectContractKind.None;
        var result = EffectContractKind.None;
        foreach (var region in regions.Regions)
            result |= EffectContractMappings.ToContractRegion(region.Kind, isWrite);
        return result;
    }
}
