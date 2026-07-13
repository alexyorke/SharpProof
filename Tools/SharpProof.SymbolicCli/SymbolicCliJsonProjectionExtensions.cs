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
        if (result.FileResult != null) return SymbolicCompactQueryResult.FromFile(result.FileResult, options);
        if (result.LineResult != null) return SymbolicCompactQueryResult.FromLine(result.LineResult, options);
        if (result.SpanResult != null) return SymbolicCompactQueryResult.FromSpan(result.SpanResult, options);
        if (result.PointResult != null) return SymbolicCompactQueryResult.FromPoint(result.PointResult, options);

        throw new InvalidOperationException("Symbolic query result has no typed scope result.");
    }

    public static SymbolicInvariantQueryResult ToInvariantQueryResult(
        this SymbolicQueryResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        if (result.FileResult != null) return SymbolicInvariantQueryResult.FromFile(result.FileResult, options);
        if (result.LineResult != null) return SymbolicInvariantQueryResult.FromLine(result.LineResult, options);
        if (result.SpanResult != null) return SymbolicInvariantQueryResult.FromSpan(result.SpanResult, options);
        if (result.PointResult != null) return SymbolicInvariantQueryResult.FromPoint(result.PointResult, options);

        throw new InvalidOperationException("Symbolic query result has no typed scope result.");
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
