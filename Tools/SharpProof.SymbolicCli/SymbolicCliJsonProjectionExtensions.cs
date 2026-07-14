namespace SharpProof.Symbolic;

public static class SymbolicCliJsonProjectionExtensions
{
    public static SymbolicCompactCapabilityResult ToCompactResult(this SymbolicCapabilityResult result)
    {
        return SymbolicCompactCapabilityResult.FromResult(result);
    }

    public static SymbolicCompactComplexityResult ToCompactResult(this SymbolicComplexityResult result)
    {
        return SymbolicCompactComplexityResult.FromResult(result);
    }

    public static SymbolicCompactRuntimeHazardQueryResult ToCompactResult(
        this SymbolicRuntimeHazardQueryResult result,
        SymbolicCompactRuntimeHazardQueryOptions? options = null)
    {
        return SymbolicCompactRuntimeHazardQueryResult.FromResult(result, options);
    }

    public static SymbolicCompactQueryResult ToCompactResult(
        this SymbolicProgramPointResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        return SymbolicCompactQueryResult.FromPoint(result, options);
    }

    public static SymbolicInvariantQueryResult ToInvariantQueryResult(
        this SymbolicProgramPointResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        return SymbolicInvariantQueryResult.FromPoint(result, options);
    }

    public static SymbolicCompactQueryResult ToCompactResult(
        this SymbolicQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        return result.SelectScope(
            file => SymbolicCompactQueryResult.FromFile(file, options),
            line => SymbolicCompactQueryResult.FromLine(line, options),
            span => SymbolicCompactQueryResult.FromSpan(span, options),
            point => SymbolicCompactQueryResult.FromPoint(point, options));
    }

    public static SymbolicInvariantQueryResult ToInvariantQueryResult(
        this SymbolicQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        return result.SelectScope(
            file => SymbolicInvariantQueryResult.FromFile(file, options),
            line => SymbolicInvariantQueryResult.FromLine(line, options),
            span => SymbolicInvariantQueryResult.FromSpan(span, options),
            point => SymbolicInvariantQueryResult.FromPoint(point, options));
    }

    internal static SymbolicCompactQueryResult ToCompactResult(
        this SymbolicLineQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        return SymbolicCompactQueryResult.FromLine(result, options);
    }

    internal static SymbolicInvariantQueryResult ToInvariantQueryResult(
        this SymbolicLineQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        return SymbolicInvariantQueryResult.FromLine(result, options);
    }

    internal static SymbolicCompactQueryResult ToCompactResult(
        this SymbolicSpanQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        return SymbolicCompactQueryResult.FromSpan(result, options);
    }

    internal static SymbolicInvariantQueryResult ToInvariantQueryResult(
        this SymbolicSpanQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        return SymbolicInvariantQueryResult.FromSpan(result, options);
    }

    internal static SymbolicCompactQueryResult ToCompactResult(
        this SymbolicFileQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        return SymbolicCompactQueryResult.FromFile(result, options);
    }

    internal static SymbolicInvariantQueryResult ToInvariantQueryResult(
        this SymbolicFileQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        return SymbolicInvariantQueryResult.FromFile(result, options);
    }
}
