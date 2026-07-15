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
        SymbolicCompactQueryOptions? options = null) => SymbolicCompactQueryResult.FromResult(result, options);

    public static SymbolicInvariantQueryResult ToInvariantQueryResult(
        this SymbolicQueryResult result,
        SymbolicCompactQueryOptions? options = null) => SymbolicInvariantQueryResult.FromResult(result, options);

    internal static SymbolicCompactQueryResult ToCompactResult(
        this SymbolicLineQueryResult result,
        SymbolicCompactQueryOptions? options = null) =>
        SymbolicCompactQueryResult.FromResult(SymbolicQueryResult.From(result), options);

    internal static SymbolicInvariantQueryResult ToInvariantQueryResult(
        this SymbolicLineQueryResult result,
        SymbolicCompactQueryOptions? options = null) =>
        SymbolicInvariantQueryResult.FromResult(SymbolicQueryResult.From(result), options);

    internal static SymbolicCompactQueryResult ToCompactResult(
        this SymbolicSpanQueryResult result,
        SymbolicCompactQueryOptions? options = null) =>
        SymbolicCompactQueryResult.FromResult(SymbolicQueryResult.From(result), options);

    internal static SymbolicInvariantQueryResult ToInvariantQueryResult(
        this SymbolicSpanQueryResult result,
        SymbolicCompactQueryOptions? options = null) =>
        SymbolicInvariantQueryResult.FromResult(SymbolicQueryResult.From(result), options);

    internal static SymbolicCompactQueryResult ToCompactResult(
        this SymbolicFileQueryResult result,
        SymbolicCompactQueryOptions? options = null) =>
        SymbolicCompactQueryResult.FromResult(SymbolicQueryResult.From(result), options);

    internal static SymbolicInvariantQueryResult ToInvariantQueryResult(
        this SymbolicFileQueryResult result,
        SymbolicCompactQueryOptions? options = null) =>
        SymbolicInvariantQueryResult.FromResult(SymbolicQueryResult.From(result), options);
}
