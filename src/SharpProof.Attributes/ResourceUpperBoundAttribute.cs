namespace SharpProof.Attributes;

/// <summary>Identifies a cumulative resource measured by a static bound.</summary>
public enum ResourceMetric
{
    /// <summary>No resource metric; this value cannot declare a bound.</summary>
    Unspecified = 0,
    /// <summary>Total body entries across modeled loops, including admitted calls.</summary>
    LoopBodyEntries = 1,
    /// <summary>Number of managed primitive-array allocations.</summary>
    ArrayAllocations = 2,
    /// <summary>Total primitive-array elements allocated, independent of element width.</summary>
    AllocatedElements = 3
}

/// <summary>Requires a native proof of a cumulative resource upper bound.</summary>
/// <remarks>
/// The mathematical bound is constant plus coefficient times the nonnegative entry size.
/// A size is an Int32/Int64 parameter name or an array parameter followed by .Length.
/// Negative scalars and null arrays have entry size zero. Exceptional execution prefixes are included.
/// This is a static contract; it does not execute the method or insert runtime checks.
/// Unsupported operations produce Unknown. These metrics do not measure elapsed time,
/// allocated bytes, peak live memory, retained output, or stack memory.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ResourceUpperBoundAttribute : Attribute
{
    /// <summary>Creates a constant or linear mathematical resource bound.</summary>
    /// <param name="metric">The cumulative resource to bound.</param>
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

    /// <summary>Gets the cumulative resource metric.</summary>
    public ResourceMetric Metric { get; }
    /// <summary>Gets the nonnegative constant term.</summary>
    public long Constant { get; }
    /// <summary>Gets the entry size name, or null for a constant bound.</summary>
    public string? SizeParameter { get; }
    /// <summary>Gets the nonnegative size coefficient.</summary>
    public long Coefficient { get; }
}
