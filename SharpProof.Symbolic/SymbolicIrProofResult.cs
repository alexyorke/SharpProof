namespace SharpProof.Symbolic;

internal sealed class SymbolicIrProofResult(PurityProofResult? rawResult, SymbolicProofInfo info)
{
    public PurityProofResult? RawResult { get; } = rawResult;

    public SymbolicProofInfo Info { get; } = info;

    public static SymbolicIrProofResult Unknown(
        SymbolicUnknownReason reason,
        SymbolicProofStage stage = SymbolicProofStage.Lowering,
        SymbolicProofSupport support = SymbolicProofSupport.Unsupported,
        string? detail = null)
    {
        return new SymbolicIrProofResult(
            null,
            new SymbolicProofInfo(
                SymbolicProofStatus.Unknown,
                SymbolicProofBackend.None,
                reason,
                detail ?? reason.ToString(),
                false,
                null,
                stage,
                support));
    }

    public static SymbolicIrProofResult Syntactic(
        SymbolicProofStatus status,
        string reason)
    {
        return new SymbolicIrProofResult(
            null,
            new SymbolicProofInfo(
                status,
                SymbolicProofBackend.Syntactic,
                SymbolicUnknownReason.None,
                reason,
                false,
                null,
                SymbolicProofStage.SyntacticClassification,
                SymbolicProofSupport.Exact));
    }

    internal SymbolicIrProofResult WithCacheHit(SymbolicBudgetInfo? budget)
    {
        return new SymbolicIrProofResult(
            RawResult,
            new SymbolicProofInfo(
                Info.Status,
                Info.Backend,
                Info.UnknownReason,
                Info.Reason,
                true,
                budget ?? Info.Budget,
                Info.Stage,
                Info.Support,
                Info.Target,
                Info.ConditionText,
                Info.DisplayKind));
    }

    internal SymbolicIrProofResult WithStatus(SymbolicProofStatus status, string? reason = null)
    {
        return new SymbolicIrProofResult(
            RawResult,
            new SymbolicProofInfo(
                status,
                Info.Backend,
                status == SymbolicProofStatus.Unknown && Info.UnknownReason == SymbolicUnknownReason.None
                    ? SymbolicUnknownReason.Unknown
                    : Info.UnknownReason,
                reason ?? Info.Reason,
                Info.CacheHit,
                Info.Budget,
                Info.Stage,
                Info.Support,
                Info.Target,
                Info.ConditionText,
                Info.DisplayKind));
    }

    public static SymbolicIrProofResult FromReachability(
        PurityProofResult result,
        SymbolicBudgetInfo? budget,
        SymbolicProofSupport support = SymbolicProofSupport.Exact)
    {
        var status = result.PathCheck.Feasibility switch
        {
            Feasibility.Satisfiable => SymbolicProofStatus.Reachable,
            Feasibility.Unsatisfiable => SymbolicProofStatus.Unreachable,
            _ => SymbolicProofStatus.Unknown
        };

        return FromResult(result, status, budget, support);
    }

    public static SymbolicIrProofResult FromImplication(
        PurityProofResult result,
        SymbolicBudgetInfo? budget,
        SymbolicProofSupport support = SymbolicProofSupport.Exact)
    {
        var status = result.Outcome switch
        {
            PurityProofOutcome.ProvablyPure => SymbolicProofStatus.ProvenTrue,
            PurityProofOutcome.ProvablyImpure => SymbolicProofStatus.ProvenFalse,
            _ => SymbolicProofStatus.Unknown
        };

        return FromResult(result, status, budget, support);
    }

    public static SymbolicIrProofResult FromConditionTruth(
        PurityProofResult result,
        SymbolicProofStatus status,
        SymbolicBudgetInfo? budget,
        SymbolicProofSupport support = SymbolicProofSupport.Exact)
    {
        if (status is not SymbolicProofStatus.ProvenTrue and
            not SymbolicProofStatus.ProvenFalse and
            not SymbolicProofStatus.Unreachable and
            not SymbolicProofStatus.Unknown)
            throw new ArgumentOutOfRangeException(nameof(status), status,
                "Condition truth proofs must be proven true, proven false, unreachable, or unknown.");

        return FromResult(result, status, budget, support);
    }

    private static SymbolicIrProofResult FromResult(
        PurityProofResult result,
        SymbolicProofStatus status,
        SymbolicBudgetInfo? budget,
        SymbolicProofSupport support)
    {
        return new SymbolicIrProofResult(
            result,
            new SymbolicProofInfo(
                status,
                SymbolicProofBackend.Smt,
                MapUnknownReason(result.Reason),
                result.Reason,
                false,
                budget,
                MapStage(result.Reason, status),
                support));
    }

    private static SymbolicProofStage MapStage(string reason, SymbolicProofStatus status)
    {
        if (status != SymbolicProofStatus.Unknown) return SymbolicProofStage.ResultMapping;

        return reason switch
        {
            "smt_method_budget_exceeded" or
                "smt_path_condition_budget_exceeded" or
                "smt_expression_budget_exceeded" => SymbolicProofStage.Budgeting,
            "smt_disabled" => SymbolicProofStage.Budgeting,
            _ => SymbolicProofStage.SmtExecution
        };
    }

    private static SymbolicUnknownReason MapUnknownReason(string reason)
    {
        return reason switch
        {
            "smt_disabled" => SymbolicUnknownReason.SmtDisabled,
            "smt_unavailable" => SymbolicUnknownReason.SmtUnavailable,
            "smt_transient_failure" => SymbolicUnknownReason.SmtUnavailable,
            "smt_timeout" => SymbolicUnknownReason.Timeout,
            "smt_method_budget_exceeded" => SymbolicUnknownReason.MethodBudgetExceeded,
            "smt_path_condition_budget_exceeded" => SymbolicUnknownReason.PathConditionBudgetExceeded,
            "smt_expression_budget_exceeded" => SymbolicUnknownReason.ExpressionBudgetExceeded,
            "smt_encoding_failure" => SymbolicUnknownReason.EncodingFailure,
            _ => SymbolicUnknownReason.None
        };
    }
}
