using SharpProof.Symbolic;

internal sealed class SymbolicCliInvariantResultAdapter
{
    private readonly Func<SymbolicCompactQueryOptions, object> _createCompactResult;
    private readonly Func<SymbolicCompactQueryOptions, SymbolicInvariantQueryResult> _createInvariantResult;
    private readonly Func<SymbolicCompactQueryOptions, bool> _isCompactTruncated;

    private SymbolicCliInvariantResultAdapter(
        int programPointCount,
        int conservativeUnknownCount,
        SymbolicProofOutcomeSummary proofOutcomes,
        int reachabilityUnknownCount,
        Func<SymbolicCompactQueryOptions, object> createCompactResult,
        Func<SymbolicCompactQueryOptions, SymbolicInvariantQueryResult> createInvariantResult,
        Func<SymbolicCompactQueryOptions, bool> isCompactTruncated)
    {
        ProgramPointCount = programPointCount;
        ConservativeUnknownCount = conservativeUnknownCount;
        ProofOutcomes = proofOutcomes;
        ReachabilityUnknownCount = reachabilityUnknownCount;
        _createCompactResult = createCompactResult;
        _createInvariantResult = createInvariantResult;
        _isCompactTruncated = isCompactTruncated;
    }

    public int ProgramPointCount { get; }

    public int ConservativeUnknownCount { get; }

    public SymbolicProofOutcomeSummary ProofOutcomes { get; }

    public int ReachabilityUnknownCount { get; }

    public static SymbolicCliInvariantResultAdapter Create(object result)
    {
        if (TryCreate(result, out var adapter)) return adapter;

        throw new InvalidOperationException("Unexpected invariant query result type.");
    }

    public static bool TryCreate(object result, out SymbolicCliInvariantResultAdapter adapter)
    {
        switch (result)
        {
            case SymbolicSourceQueryResult point:
                adapter = new SymbolicCliInvariantResultAdapter(
                    1,
                    point.InvariantQuery.UnknownFactCount,
                    point.ProofOutcomes,
                    point.Reachability == SymbolicReachability.Unknown ? 1 : 0,
                    options => point.ToCompactResult(options),
                    options => point.ToInvariantQueryResult(options),
                    options => point.ToCompactResult(options).Truncation.IsTruncated);
                return true;
            case SymbolicLineQueryResult line:
                adapter = new SymbolicCliInvariantResultAdapter(
                    line.ProgramPoints.Count,
                    line.InvariantQuery.UnknownFactCount,
                    line.ProgramPointSummary.ProofOutcomes,
                    line.Reachability.UnknownCount,
                    options => line.ToCompactResult(options),
                    options => line.ToInvariantQueryResult(options),
                    options => line.ToCompactResult(options).Truncation.IsTruncated);
                return true;
            case SymbolicSpanQueryResult span:
                adapter = new SymbolicCliInvariantResultAdapter(
                    span.ProgramPointCount,
                    span.InvariantQuery.UnknownFactCount,
                    span.ProgramPointSummary.ProofOutcomes,
                    span.Reachability.UnknownCount,
                    options => span.ToCompactResult(options),
                    options => span.ToInvariantQueryResult(options),
                    options => span.ToCompactResult(options).Truncation.IsTruncated);
                return true;
            case SymbolicFileQueryResult file:
                adapter = new SymbolicCliInvariantResultAdapter(
                    file.ProgramPointCount,
                    file.InvariantQuery.UnknownFactCount,
                    file.ProgramPointSummary.ProofOutcomes,
                    file.Reachability.UnknownCount,
                    options => file.ToCompactResult(options),
                    options => file.ToInvariantQueryResult(options),
                    options => file.ToCompactResult(options).Truncation.IsTruncated);
                return true;
            default:
                adapter = null!;
                return false;
        }
    }

    public object ToCompactResult(SymbolicCompactQueryOptions options)
    {
        return _createCompactResult(options);
    }

    public SymbolicInvariantQueryResult ToInvariantQueryResult(SymbolicCompactQueryOptions options)
    {
        return _createInvariantResult(options);
    }

    public bool IsCompactTruncated(SymbolicCompactQueryOptions options)
    {
        return _isCompactTruncated(options);
    }
}
