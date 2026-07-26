namespace SharpProof.Effects;

public sealed class EffectSummary : IEquatable<EffectSummary> {
    private EffectSummary(bool isBottom) {
        IsBottom = isBottom;
        Reads = EffectRegionSet.Empty;
        Writes = EffectRegionSet.Empty;
        Allocation = EffectAllocationKind.None;
        Capabilities = EffectCapabilitySet.Empty;
        Throws = EffectThrowSet.Empty;
        Termination = EffectTermination.Bottom;
        Completeness = EffectCompleteness.Complete;
        Uncertainty = EffectUncertainty.None;
    }

    internal EffectSummary(
        EffectRegionSet reads,
        EffectRegionSet writes,
        EffectAllocationKind allocation,
        EffectCapabilitySet capabilities,
        EffectThrowSet throws,
        EffectTermination termination,
        EffectCompleteness completeness,
        EffectUncertainty uncertainty = EffectUncertainty.None) {
        ValidateAllocation(allocation);
        if (!Enum.IsDefined(typeof(EffectTermination), termination) ||
            termination == EffectTermination.Bottom)
            throw new ArgumentOutOfRangeException(nameof(termination));
        if (!Enum.IsDefined(typeof(EffectCompleteness), completeness))
            throw new ArgumentOutOfRangeException(nameof(completeness));
        if ((uncertainty & ~EffectUncertainty.Unknown) != 0)
            throw new ArgumentOutOfRangeException(nameof(uncertainty));
        var uncertaintyMarker = (EffectUncertainty)(1 << 6);
        if ((uncertainty & uncertaintyMarker) != 0 &&
            uncertainty != EffectUncertainty.Unknown)
            throw new ArgumentOutOfRangeException(nameof(uncertainty));
        Reads = reads;
        Writes = writes;
        Allocation = allocation;
        Capabilities = capabilities;
        Throws = throws;
        Termination = termination;
        Completeness = completeness;
        Uncertainty = uncertainty;
    }

    public static EffectSummary Bottom { get; } = new(true);

    public static EffectSummary Empty { get; } = new(
        EffectRegionSet.Empty,
        EffectRegionSet.Empty,
        EffectAllocationKind.None,
        EffectCapabilitySet.Empty,
        EffectThrowSet.Empty,
        EffectTermination.Terminates,
        EffectCompleteness.Complete);

    public static EffectSummary Top { get; } = new(
        EffectRegionSet.Unknown,
        EffectRegionSet.Unknown,
        EffectAllocationKind.Unknown,
        EffectCapabilitySet.Unknown,
        EffectThrowSet.Unknown,
        EffectTermination.Unknown,
        EffectCompleteness.Incomplete,
        EffectUncertainty.Unknown);

    public bool IsBottom { get; }
    public EffectRegionSet Reads { get; }
    public EffectRegionSet Writes { get; }
    public EffectAllocationKind Allocation { get; }
    public EffectCapabilitySet Capabilities { get; }
    public EffectThrowSet Throws { get; }
    public EffectTermination Termination { get; }
    public EffectCompleteness Completeness { get; }
    public EffectUncertainty Uncertainty { get; }

    public bool Equals(EffectSummary? other) {
        if (ReferenceEquals(this, other)) return true;
        if (other == null || IsBottom != other.IsBottom) return false;
        return IsBottom ||
               Reads == other.Reads &&
               Writes == other.Writes &&
               Allocation == other.Allocation &&
               Capabilities == other.Capabilities &&
               Throws == other.Throws &&
               Termination == other.Termination &&
               Completeness == other.Completeness &&
               Uncertainty == other.Uncertainty;
    }

    public override bool Equals(object? obj) => Equals(obj as EffectSummary);

    public override int GetHashCode() {
        if (IsBottom) return 0;
        unchecked {
            var hash = 17;
            hash = hash * 31 + Reads.GetHashCode();
            hash = hash * 31 + Writes.GetHashCode();
            hash = hash * 31 + (int)Allocation;
            hash = hash * 31 + Capabilities.GetHashCode();
            hash = hash * 31 + Throws.GetHashCode();
            hash = hash * 31 + (int)Termination;
            hash = hash * 31 + (int)Completeness;
            hash = hash * 31 + (int)Uncertainty;
            return hash;
        }
    }

    private static void ValidateAllocation(EffectAllocationKind allocation) {
        if ((allocation & ~EffectAllocationKind.Unknown) != 0)
            throw new ArgumentOutOfRangeException(nameof(allocation));
        var unknownMarker = (EffectAllocationKind)(1 << 2);
        if ((allocation & unknownMarker) != 0 &&
            allocation != EffectAllocationKind.Unknown)
            throw new ArgumentOutOfRangeException(nameof(allocation));
    }
}

public sealed class EffectSummaryDomain : IAbstractDomain<EffectSummary> {
    public static EffectSummaryDomain Instance { get; } = new();

    private EffectSummaryDomain() {
    }

    public EffectSummary Bottom => EffectSummary.Bottom;
    public EffectSummary Top => EffectSummary.Top;

    public bool LessThanOrEqual(EffectSummary left, EffectSummary right) {
        if (left == null) throw new ArgumentNullException(nameof(left));
        if (right == null) throw new ArgumentNullException(nameof(right));
        if (left.IsBottom) return true;
        if (right.IsBottom) return false;
        return left.Reads.IsSubsetOf(right.Reads) &&
               left.Writes.IsSubsetOf(right.Writes) &&
               AllocationLessThanOrEqual(left.Allocation, right.Allocation) &&
               left.Capabilities.IsSubsetOf(right.Capabilities) &&
               left.Throws.IsSubsetOf(right.Throws) &&
               TerminationLessThanOrEqual(left.Termination, right.Termination) &&
               left.Completeness <= right.Completeness &&
               (left.Uncertainty & ~right.Uncertainty) == 0;
    }

    public bool AreEquivalent(EffectSummary left, EffectSummary right) =>
        LessThanOrEqual(left, right) &&
        LessThanOrEqual(right, left);

    public EffectSummary Join(EffectSummary left, EffectSummary right) {
        if (left == null) throw new ArgumentNullException(nameof(left));
        if (right == null) throw new ArgumentNullException(nameof(right));
        if (left.IsBottom) return right;
        if (right.IsBottom) return left;
        return new EffectSummary(
            left.Reads.Union(right.Reads),
            left.Writes.Union(right.Writes),
            left.Allocation | right.Allocation,
            left.Capabilities.Union(right.Capabilities),
            left.Throws.Union(right.Throws),
            JoinTermination(left.Termination, right.Termination),
            left.Completeness > right.Completeness
                ? left.Completeness
                : right.Completeness,
            left.Uncertainty | right.Uncertainty);
    }

    public EffectSummary Widen(EffectSummary previous, EffectSummary next) =>
        Join(previous, next);

    public EffectSummary Havoc(EffectSummary value) {
        if (value == null) throw new ArgumentNullException(nameof(value));
        return value.IsBottom ? Bottom : Top;
    }

    private static bool AllocationLessThanOrEqual(
        EffectAllocationKind left,
        EffectAllocationKind right) =>
        (left & ~right) == 0;

    private static bool TerminationLessThanOrEqual(
        EffectTermination left,
        EffectTermination right) =>
        left <= right;

    private static EffectTermination JoinTermination(
        EffectTermination left,
        EffectTermination right) =>
        left > right ? left : right;
}
