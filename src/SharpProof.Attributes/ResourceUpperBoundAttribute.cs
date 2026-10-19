namespace SharpProof.Attributes;

/// <summary>Identifies a resource measured by a static bound.</summary>
public enum ResourceMetric
{
    /// <summary>No resource metric; this value cannot declare a bound.</summary>
    Unspecified = 0,
    /// <summary>Total body entries across modeled loops, including admitted calls.</summary>
    LoopBodyEntries = 1,
    /// <summary>Number of managed primitive-array allocations.</summary>
    ArrayAllocations = 2,
    /// <summary>Total primitive-array elements allocated, independent of element width.</summary>
    AllocatedElements = 3,
    /// <summary>Fresh managed instance bytes under the reviewed CoreCLR 8.0.32 x64 source storage model.</summary>
    /// <remarks>Requires a captured x64 target and an explicit trusted layout dependency in proof evidence.</remarks>
    AllocatedBytes = 4,
    /// <summary>Peak logically live owned bytes excluding the final returned graph, on normal completion.</summary>
    /// <remarks>Uses the finite strong-reachability and reviewed runtime-layout models, not GC collection times.</remarks>
    PeakAuxiliaryBytes = 5,
    /// <summary>Owned bytes reachable from the returned value on normal completion.</summary>
    /// <remarks>Borrowed input storage is outside the owned-storage model.</remarks>
    RetainedBytes = 6
}

/// <summary>Requires a native proof of a resource upper bound.</summary>
/// <remarks>
/// The mathematical bound is constant plus coefficient times the nonnegative entry size.
/// A size is an Int32/Int64 parameter name or an array parameter followed by .Length.
/// Negative scalars and null arrays have entry size zero. Cumulative metrics include exceptional execution prefixes.
/// This is a static contract; it does not execute the method or insert runtime checks.
/// Unsupported operations produce Unknown. These metrics do not measure elapsed time,
/// physical heap residency or stack memory. Logical peak/retained metrics describe normal completion under their declared model.
/// Byte bounds are conditional on the published runtime layout model.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ResourceUpperBoundAttribute : Attribute
{
    /// <summary>Creates a constant or linear mathematical resource bound.</summary>
    /// <param name="metric">The resource to bound.</param>
    /// <param name="constant">The nonnegative constant term.</param>
    /// <param name="sizeParameter">The entry parameter name or array length, or null for a constant bound.</param>
    /// <param name="coefficient">The positive size coefficient, or zero for a constant bound.</param>
    /// <exception cref="ArgumentOutOfRangeException">A metric or numeric term is invalid.</exception>
    /// <exception cref="ArgumentException">The size name and coefficient do not form a linear bound.</exception>
    public ResourceUpperBoundAttribute(ResourceMetric metric, long constant, string? sizeParameter = null, long coefficient = 0)
    {
        if (metric == ResourceMetric.Unspecified || !Enum.IsDefined(typeof(ResourceMetric), metric))
        { throw new ArgumentOutOfRangeException(nameof(metric)); }
        if (constant < 0)
        { throw new ArgumentOutOfRangeException(nameof(constant)); }
        if (coefficient < 0)
        { throw new ArgumentOutOfRangeException(nameof(coefficient)); }
        if (sizeParameter is null ? coefficient != 0 : string.IsNullOrWhiteSpace(sizeParameter) || coefficient == 0)
        { throw new ArgumentException("A linear bound requires a size name and a positive coefficient.", nameof(sizeParameter)); }
        Metric = metric;
        Constant = constant;
        SizeParameter = sizeParameter;
        Coefficient = coefficient;
    }

    /// <summary>Gets the resource metric.</summary>
    /// <value>The resource measured by this contract.</value>
    public ResourceMetric Metric { get; }
    /// <summary>Gets the nonnegative constant term.</summary>
    /// <value>The nonnegative constant term in the mathematical bound.</value>
    public long Constant { get; }
    /// <summary>Gets the entry size name, or null for a constant bound.</summary>
    /// <value>The scalar parameter name or array length, or null for a constant bound.</value>
    public string? SizeParameter { get; }
    /// <summary>Gets the nonnegative size coefficient.</summary>
    /// <value>The positive size multiplier, or zero for a constant bound.</value>
    public long Coefficient { get; }
}
