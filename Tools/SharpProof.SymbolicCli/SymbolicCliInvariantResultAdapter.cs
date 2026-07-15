using SharpProof.Symbolic;

internal sealed class SymbolicCliInvariantResultAdapter
{
    private readonly SymbolicQueryResult _result;

    private SymbolicCliInvariantResultAdapter(SymbolicQueryResult result)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public int ProgramPointCount => _result.ProgramPointCount;

    public int ConservativeUnknownCount => _result.InvariantQuery.UnknownFactCount;

    public SymbolicProofOutcomeSummary ProofOutcomes => _result.ProgramPointSummary.ProofOutcomes;

    public int ReachabilityUnknownCount => _result.Reachability.UnknownCount;

    public static SymbolicCliInvariantResultAdapter Create(object result)
    {
        if (TryCreate(result, out var adapter)) return adapter;

        throw new InvalidOperationException("Unexpected invariant query result type.");
    }

    public static bool TryCreate(object result, out SymbolicCliInvariantResultAdapter adapter)
    {
        if (result is SymbolicQueryResult queryResult)
        {
            adapter = new SymbolicCliInvariantResultAdapter(queryResult);
            return true;
        }

        adapter = null!;
        return false;
    }

    public object ToCompactResult(SymbolicCompactQueryOptions options)
    {
        return _result.ToCompactResult(options);
    }

    public SymbolicInvariantQueryResult ToInvariantQueryResult(SymbolicCompactQueryOptions options)
    {
        return _result.ToInvariantQueryResult(options);
    }

    public bool IsCompactTruncated(SymbolicCompactQueryOptions options)
    {
        return _result.ToCompactResult(options).Truncation.IsTruncated;
    }
}
