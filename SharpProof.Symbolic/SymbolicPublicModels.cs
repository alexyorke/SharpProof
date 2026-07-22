namespace SharpProof.Symbolic;

internal enum SymbolicProofStatus {
    Unknown,
    Reachable,
    Unreachable,
    ProvenTrue,
    ProvenFalse
}
internal enum SymbolicUnknownReason {
    None,
    UnsupportedIrEncoding,
    SmtDisabled,
    SmtUnavailable,
    Timeout,
    MethodBudgetExceeded,
    PathConditionBudgetExceeded,
    ExpressionBudgetExceeded,
    CancellationRequested,
    EncodingFailure,
    Unknown
}
internal sealed record SymbolicBudgetInfo(
    int MaxPathConditions,
    int MaxExpressionNodes,
    int TimeoutMilliseconds,
    int MethodBudgetMilliseconds,
    int ExecutedQueryCount,
    int CacheEntryCount,
    SymbolicCacheInfo? Cache = null);

internal sealed record SymbolicCacheInfo(long Hits, long Misses, int Entries, long Evictions);

internal sealed record SymbolicProofInfo(
    SymbolicProofStatus Status,
    SymbolicUnknownReason UnknownReason,
    string Reason,
    bool CacheHit,
    SymbolicBudgetInfo? Budget) {
    public SymbolicProofStatus Status { get; init; } = Status;
    public SymbolicUnknownReason UnknownReason { get; init; } = UnknownReason;
    public string Reason { get; init; } = Reason ?? string.Empty;

    public SymbolicUnknownReasonInfo UnknownReasonInfo =>
        SymbolicUnknownReasonTaxonomy.ForProof(UnknownReason, Reason);

    public bool CacheHit { get; init; } = CacheHit;
    public SymbolicBudgetInfo? Budget { get; init; } = Budget;

    internal AnalysisProofResult? RawResult { get; init; }

    internal static SymbolicProofInfo Unknown(SymbolicUnknownReason reason, string? detail = null) => new(
        SymbolicProofStatus.Unknown,
        reason,
        detail ?? reason.ToString(),
        false,
        null);

    internal static SymbolicProofInfo Syntactic(SymbolicProofStatus status, string reason) => new(
        status,
        SymbolicUnknownReason.None,
        reason,
        false,
        null);

    internal SymbolicProofInfo WithCacheHit(SymbolicBudgetInfo? budget) => this with {
        CacheHit = true,
        Budget = budget ?? Budget
    };

    internal SymbolicProofInfo WithStatus(SymbolicProofStatus status, string? reason = null) => this with {
        Status = status,
        UnknownReason = status == SymbolicProofStatus.Unknown && UnknownReason == SymbolicUnknownReason.None
            ? SymbolicUnknownReason.Unknown
            : UnknownReason,
        Reason = reason ?? Reason
    };

    internal static SymbolicProofInfo FromReachability(AnalysisProofResult result, SymbolicBudgetInfo? budget) =>
        FromResult(
            result,
            result.PathCheck.Feasibility switch {
                Feasibility.Satisfiable => SymbolicProofStatus.Reachable,
                Feasibility.Unsatisfiable => SymbolicProofStatus.Unreachable,
                _ => SymbolicProofStatus.Unknown
            },
            budget);

    internal static SymbolicProofInfo FromImplication(AnalysisProofResult result, SymbolicBudgetInfo? budget) =>
        FromResult(
            result,
            result.Outcome switch {
                AnalysisProofOutcome.Proven => SymbolicProofStatus.ProvenTrue,
                AnalysisProofOutcome.Disproven => SymbolicProofStatus.ProvenFalse,
                _ => SymbolicProofStatus.Unknown
            },
            budget);

    internal static SymbolicProofInfo FromConditionTruth(AnalysisProofResult result, SymbolicProofStatus status,
        SymbolicBudgetInfo? budget) => FromResult(result, status, budget);

    private static SymbolicProofInfo FromResult(AnalysisProofResult result, SymbolicProofStatus status, SymbolicBudgetInfo? budget) => new(
        status,
        status == SymbolicProofStatus.Unknown
            ? SymbolicUnknownReasonClassifier.Classify(result.Reason)
            : SymbolicUnknownReason.None,
        result.Reason,
        false,
        budget) { RawResult = result };
}
