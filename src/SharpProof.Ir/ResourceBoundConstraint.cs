namespace SharpProof.Ir;

internal enum ResourceCostMetric { LoopBodyEntries = 1, ArrayAllocations = 2, AllocatedElements = 3, AllocatedBytes = 4 }

internal sealed class ResourceBoundConstraint
{
    internal ResourceCostMetric Metric { get; }
    internal ResourceExpression Upper { get; }

    internal ResourceBoundConstraint(ResourceCostMetric metric, ResourceExpression upper)
    {
        Upper = ArgumentNullGuard.NotNull(upper, nameof(upper));
        if (!Enum.IsDefined(typeof(ResourceCostMetric), metric))
        { throw new ArgumentOutOfRangeException(nameof(metric)); }
        Metric = metric;
    }
}
