namespace SharpProof.Effects;

public readonly struct EffectProjection(
    EffectContractKind effects,
    EffectContractCapabilityKind capabilities,
    bool isComplete)
    : IEquatable<EffectProjection> {
    public EffectContractKind Effects { get; } = effects;
    public EffectContractCapabilityKind Capabilities { get; } = capabilities;
    public bool IsComplete { get; } = isComplete;

    public bool Equals(EffectProjection other) =>
        Effects == other.Effects &&
        Capabilities == other.Capabilities &&
        IsComplete == other.IsComplete;
    public override bool Equals(object? obj) =>
        obj is EffectProjection other && Equals(other);
    public override int GetHashCode() =>
        unchecked(
            (((int)Effects * 397) ^ (int)Capabilities) * 397 ^
            (IsComplete ? 1 : 0));
    public static bool operator ==(EffectProjection left, EffectProjection right) =>
        left.Equals(right);
    public static bool operator !=(EffectProjection left, EffectProjection right) =>
        !left.Equals(right);
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
        EffectRegionKind kind, bool isWrite) =>
        (kind, isWrite) switch {
            (EffectRegionKind.Receiver, false) => EffectContractKind.ReadsReceiverState,
            (EffectRegionKind.Parameter, false) => EffectContractKind.ReadsArgumentState,
            (EffectRegionKind.Captured, false) => EffectContractKind.ReadsCapturedState,
            (EffectRegionKind.Static, false) => EffectContractKind.ReadsStaticState,
            (EffectRegionKind.Ambient, false) => EffectContractKind.ReadsAmbientState,
            (EffectRegionKind.Receiver, true) => EffectContractKind.WritesReceiverState,
            (EffectRegionKind.Parameter, true) => EffectContractKind.WritesArgumentState,
            (EffectRegionKind.Captured, true) => EffectContractKind.WritesCapturedState,
            (EffectRegionKind.Static, true) => EffectContractKind.WritesStaticState,
            (EffectRegionKind.Fresh, true) => EffectContractKind.None,
            (EffectRegionKind.Ambient, true) => EffectContractKind.WritesAmbientState,
            (EffectRegionKind.Unknown, _) => EffectContractKind.None,
            _ => EffectContractKind.None
        };
}
