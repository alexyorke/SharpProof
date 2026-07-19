namespace SharpProof.Symbolic;

internal readonly struct SymbolicProofProjection(
    SymbolicProofStatus status,
    SymbolicProofBackend backend,
    SymbolicUnknownReason unknownReason)
{
    internal static SymbolicProofStatus MapStatus(SymbolicTruthValue truthValue) => truthValue switch
    {
        SymbolicTruthValue.ProvenTrue => SymbolicProofStatus.ProvenTrue,
        SymbolicTruthValue.ProvenFalse => SymbolicProofStatus.ProvenFalse,
        SymbolicTruthValue.Unreachable => SymbolicProofStatus.Unreachable,
        _ => SymbolicProofStatus.Unknown
    };

    internal static SymbolicProofStatus MapStatus(SymbolicConditionProofSummaryStatus status) => status switch
    {
        SymbolicConditionProofSummaryStatus.AlwaysTrue => SymbolicProofStatus.ProvenTrue,
        SymbolicConditionProofSummaryStatus.AlwaysFalse => SymbolicProofStatus.ProvenFalse,
        SymbolicConditionProofSummaryStatus.UnreachableOnly => SymbolicProofStatus.Unreachable,
        _ => SymbolicProofStatus.Unknown
    };

    internal static SymbolicProofStatus MapStatus(SymbolicRuntimeHazardStatus status) => status switch
    {
        SymbolicRuntimeHazardStatus.Proven => SymbolicProofStatus.ProvenTrue,
        SymbolicRuntimeHazardStatus.Unreachable => SymbolicProofStatus.Unreachable,
        _ => SymbolicProofStatus.Unknown
    };

    private SymbolicProofStatus Status { get; } = status;

    private SymbolicProofBackend Backend { get; } = backend;

    private SymbolicUnknownReason UnknownReason { get; } = unknownReason;

    internal static SymbolicProofProjection FromSolverBackedResult(
        SymbolicProofStatus status,
        bool isSolverBacked,
        string? rawUnknownReason = null)
    {
        return new SymbolicProofProjection(
            status,
            ResolveBackend(status, isSolverBacked),
            status == SymbolicProofStatus.Unknown && rawUnknownReason != null
                ? SymbolicUnknownReasonClassifier.Classify(rawUnknownReason)
                : SymbolicUnknownReason.None);
    }

    internal static SymbolicProofProjection FromExisting(
        SymbolicProofStatus status,
        SymbolicProofInfo proof)
    {
        return new SymbolicProofProjection(
            status,
            proof.Backend,
            status == SymbolicProofStatus.Unknown
                ? proof.UnknownReason
                : SymbolicUnknownReason.None);
    }

    internal SymbolicProofInfo CreateInfo(
        string reason,
        bool cacheHit,
        SymbolicBudgetInfo? budget,
        string? target = null,
        string? conditionText = null,
        string? displayKind = null)
    {
        return new SymbolicProofInfo(
            Status,
            Backend,
            UnknownReason,
            reason,
            cacheHit,
            budget,
            target,
            conditionText,
            displayKind);
    }

    private static SymbolicProofBackend ResolveBackend(
        SymbolicProofStatus status,
        bool isSolverBacked)
    {
        if (isSolverBacked) return SymbolicProofBackend.Smt;

        return status == SymbolicProofStatus.Unknown
            ? SymbolicProofBackend.None
            : SymbolicProofBackend.Syntactic;
    }
}
