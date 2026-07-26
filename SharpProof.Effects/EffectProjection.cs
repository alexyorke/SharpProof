namespace SharpProof.Effects;

public readonly struct EffectProjection(
    SharpProofEffect effects,
    SharpProofCapability capabilities,
    bool isComplete)
    : IEquatable<EffectProjection> {
    public SharpProofEffect Effects { get; } = effects;
    public SharpProofCapability Capabilities { get; } = capabilities;
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
                SharpProofEffect.None, SharpProofCapability.None, isComplete: true);

        var effects = ProjectRegions(summary.Reads, isWrite: false) |
                      ProjectRegions(summary.Writes, isWrite: true);
        var allocationUnknown =
            (summary.Allocation & (EffectAllocationKind)(1 << 2)) != 0;
        if (summary.Allocation != EffectAllocationKind.None && !allocationUnknown)
            effects |= SharpProofEffect.Allocates;
        if (!summary.Throws.Types.IsDefaultOrEmpty)
            effects |= SharpProofEffect.Throws;
        if (!summary.Capabilities.IsUnknown) {
            if (summary.Capabilities.Contains(EffectCapabilityKind.Synchronization))
                effects |= SharpProofEffect.Synchronizes;
            if (summary.Capabilities.Contains(EffectCapabilityKind.Randomness) ||
                summary.Capabilities.Contains(EffectCapabilityKind.Clock))
                effects |= SharpProofEffect.UsesNondeterminism;
            if (summary.Capabilities.Contains(EffectCapabilityKind.Reflection))
                effects |= SharpProofEffect.UsesReflection;
            if (summary.Capabilities.Contains(EffectCapabilityKind.NativeInterop))
                effects |= SharpProofEffect.UsesNativeCode;
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
            ? SharpProofCapability.None
            : (SharpProofCapability)(
                summary.Capabilities.Kinds & EffectCapabilityKind.AllKnown);
        return new EffectProjection(effects, capabilities, isComplete);
    }

    private static SharpProofEffect ProjectRegions(
        EffectRegionSet regions, bool isWrite) {
        if (regions.IsUnknown)
            return SharpProofEffect.None;
        var result = SharpProofEffect.None;
        foreach (var region in regions.Regions)
            result |= ProjectRegion(region.Kind, isWrite);
        return result;
    }

    private static SharpProofEffect ProjectRegion(
        EffectRegionKind kind, bool isWrite) =>
        (kind, isWrite) switch {
            (EffectRegionKind.Receiver, false) => SharpProofEffect.ReadsReceiverState,
            (EffectRegionKind.Parameter, false) => SharpProofEffect.ReadsArgumentState,
            (EffectRegionKind.Captured, false) => SharpProofEffect.ReadsCapturedState,
            (EffectRegionKind.Static, false) => SharpProofEffect.ReadsStaticState,
            (EffectRegionKind.Ambient, false) => SharpProofEffect.ReadsAmbientState,
            (EffectRegionKind.Receiver, true) => SharpProofEffect.WritesReceiverState,
            (EffectRegionKind.Parameter, true) => SharpProofEffect.WritesArgumentState,
            (EffectRegionKind.Captured, true) => SharpProofEffect.WritesCapturedState,
            (EffectRegionKind.Static, true) => SharpProofEffect.WritesStaticState,
            (EffectRegionKind.Fresh, true) => SharpProofEffect.None,
            (EffectRegionKind.Ambient, true) => SharpProofEffect.WritesAmbientState,
            (EffectRegionKind.Unknown, _) => SharpProofEffect.None,
            _ => SharpProofEffect.None
        };
}
