namespace SharpProof.Effects;

public sealed record EffectSummary
{
    private EffectSummary(bool isBottom)
    {
        (IsBottom, Reads, Writes, Allocation) =
            (isBottom, EffectRegionSet.Empty, EffectRegionSet.Empty, EffectAllocationKind.None);
        (Capabilities, Throws, Termination, Completeness, Uncertainty, AnalysisIncompleteReason) =
            (EffectCapabilitySet.Empty, EffectThrowSet.Empty, EffectTermination.Bottom,
                EffectCompleteness.Complete, EffectUncertainty.None, EffectAnalysisIncompleteReason.None);
    }

    internal EffectSummary(
        EffectRegionSet reads,
        EffectRegionSet writes,
        EffectAllocationKind allocation,
        EffectCapabilitySet capabilities,
        EffectThrowSet throws,
        EffectTermination termination,
        EffectCompleteness completeness,
        EffectUncertainty uncertainty = EffectUncertainty.None,
        EffectAnalysisIncompleteReason analysisIncompleteReason = EffectAnalysisIncompleteReason.None)
    {
        ValidateAllocation(allocation);
        if (!Enum.IsDefined(typeof(EffectTermination), termination) ||
            termination == EffectTermination.Bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(termination));
        }

        if (!Enum.IsDefined(typeof(EffectCompleteness), completeness))
        {
            throw new ArgumentOutOfRangeException(nameof(completeness));
        }

        if ((uncertainty & ~EffectUncertainty.Unknown) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(uncertainty));
        }

        var uncertaintyMarker = (EffectUncertainty)(1 << 6);
        if ((uncertainty & uncertaintyMarker) != 0 &&
            uncertainty != EffectUncertainty.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(uncertainty));
        }

        if ((analysisIncompleteReason &
             ~(EffectAnalysisIncompleteReason.BlockBudgetExceeded |
               EffectAnalysisIncompleteReason.OperationBudgetExceeded |
               EffectAnalysisIncompleteReason.CyclicControlFlow)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(analysisIncompleteReason));
        }

        if (completeness == EffectCompleteness.Complete &&
            analysisIncompleteReason != EffectAnalysisIncompleteReason.None)
        {
            throw new ArgumentException(
                "A complete effect summary cannot carry an incomplete-analysis reason.",
                nameof(analysisIncompleteReason));
        }

        (Reads, Writes, Allocation, Capabilities) =
            (reads, writes, allocation, capabilities);
        (Throws, Termination, Completeness, Uncertainty, AnalysisIncompleteReason) =
            (throws, termination, completeness, uncertainty, analysisIncompleteReason);
    }

    public static EffectSummary Bottom { get; } = new(true);

    public static EffectSummary Empty
    {
        get;
    } = new(
        EffectRegionSet.Empty, EffectRegionSet.Empty,
        EffectAllocationKind.None, EffectCapabilitySet.Empty,
        EffectThrowSet.Empty, EffectTermination.Terminates,
        EffectCompleteness.Complete);

    public static EffectSummary Top
    {
        get;
    } = new(
        EffectRegionSet.Unknown, EffectRegionSet.Unknown,
        EffectAllocationKind.Unknown, EffectCapabilitySet.Unknown,
        EffectThrowSet.Unknown, EffectTermination.Unknown,
        EffectCompleteness.Incomplete, EffectUncertainty.Unknown);

    public bool IsBottom
    {
        get;
    }
    public EffectRegionSet Reads
    {
        get;
    }
    public EffectRegionSet Writes
    {
        get;
    }
    public EffectAllocationKind Allocation
    {
        get;
    }
    public EffectCapabilitySet Capabilities
    {
        get;
    }
    public EffectThrowSet Throws
    {
        get;
    }
    public EffectTermination Termination
    {
        get;
    }
    public EffectCompleteness Completeness
    {
        get;
    }
    public EffectUncertainty Uncertainty
    {
        get;
    }
    internal EffectAnalysisIncompleteReason AnalysisIncompleteReason
    {
        get;
    }

    private static void ValidateAllocation(EffectAllocationKind allocation)
    {
        if ((allocation & ~EffectAllocationKind.Unknown) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(allocation));
        }

        var unknownMarker = (EffectAllocationKind)(1 << 2);
        if ((allocation & unknownMarker) != 0 &&
            allocation != EffectAllocationKind.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(allocation));
        }
    }
}

public sealed class EffectSummaryDomain : IAbstractDomain<EffectSummary>
{
    public static EffectSummaryDomain Instance { get; } = new();

    private EffectSummaryDomain()
    {
    }

    public EffectSummary Bottom => EffectSummary.Bottom;
    public EffectSummary Top => EffectSummary.Top;

    public bool LessThanOrEqual(EffectSummary left, EffectSummary right)
    {
        if (left == null)
        {
            throw new ArgumentNullException(nameof(left));
        }

        if (right == null)
        {
            throw new ArgumentNullException(nameof(right));
        }

        if (left.IsBottom)
        {
            return true;
        }

        if (right.IsBottom)
        {
            return false;
        }

        return left.Reads.IsSubsetOf(right.Reads) &&
               left.Writes.IsSubsetOf(right.Writes) &&
               AllocationLessThanOrEqual(left.Allocation, right.Allocation) &&
               left.Capabilities.IsSubsetOf(right.Capabilities) &&
               left.Throws.IsSubsetOf(right.Throws) &&
               TerminationLessThanOrEqual(left.Termination, right.Termination) &&
               left.Completeness <= right.Completeness &&
               (left.Uncertainty & ~right.Uncertainty) == 0;
    }

    public bool AreEquivalent(EffectSummary left, EffectSummary right)
    {
        return LessThanOrEqual(left, right) &&
        LessThanOrEqual(right, left);
    }

    public EffectSummary Join(EffectSummary left, EffectSummary right)
    {
        if (left == null)
        {
            throw new ArgumentNullException(nameof(left));
        }

        if (right == null)
        {
            throw new ArgumentNullException(nameof(right));
        }

        if (left.IsBottom)
        {
            return right;
        }

        if (right.IsBottom)
        {
            return left;
        }

        return new EffectSummary(
            left.Reads.Union(right.Reads), left.Writes.Union(right.Writes),
            left.Allocation | right.Allocation, left.Capabilities.Union(right.Capabilities),
            left.Throws.Union(right.Throws),
            JoinTermination(left.Termination, right.Termination),
            left.Completeness > right.Completeness
                ? left.Completeness
                : right.Completeness,
            left.Uncertainty | right.Uncertainty,
            left.AnalysisIncompleteReason | right.AnalysisIncompleteReason);
    }

    public EffectSummary Widen(EffectSummary previous, EffectSummary next)
    {
        return Join(previous, next);
    }

    public EffectSummary Havoc(EffectSummary value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return value.IsBottom ? Bottom : Top;
    }

    private static bool AllocationLessThanOrEqual(
        EffectAllocationKind left, EffectAllocationKind right)
    {
        return (left & ~right) == 0;
    }

    private static bool TerminationLessThanOrEqual(
        EffectTermination left, EffectTermination right)
    {
        return left <= right;
    }

    private static EffectTermination JoinTermination(
        EffectTermination left, EffectTermination right)
    {
        return left > right ? left : right;
    }
}
