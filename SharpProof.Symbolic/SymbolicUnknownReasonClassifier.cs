namespace SharpProof.Symbolic;

internal static class SymbolicUnknownReasonClassifier
{
    internal static SymbolicUnknownReason Classify(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return SymbolicUnknownReason.Unknown;

        if (ContainsReason(reason, "timeout") ||
            ContainsReason(reason, "timed_out"))
            return SymbolicUnknownReason.Timeout;

        if (ContainsReason(reason, "method_budget")) return SymbolicUnknownReason.MethodBudgetExceeded;

        if (ContainsReason(reason, "path_condition") ||
            ContainsReason(reason, "max_path_conditions") ||
            ContainsReason(reason, "too_many_path_conditions"))
            return SymbolicUnknownReason.PathConditionBudgetExceeded;

        if (ContainsReason(reason, "expression_budget") ||
            ContainsReason(reason, "max_expression"))
            return SymbolicUnknownReason.ExpressionBudgetExceeded;

        if (ContainsReason(reason, "cancellation") ||
            ContainsReason(reason, "cancelled") ||
            ContainsReason(reason, "canceled"))
            return SymbolicUnknownReason.CancellationRequested;

        if (ContainsReason(reason, "encoding")) return SymbolicUnknownReason.EncodingFailure;

        if (ContainsReason(reason, "unsupported")) return SymbolicUnknownReason.UnsupportedIrEncoding;

        if (ContainsReason(reason, "smt_required") ||
            ContainsReason(reason, "smt_disabled") ||
            ContainsReason(reason, "smt_off"))
            return SymbolicUnknownReason.SmtDisabled;

        if (ContainsReason(reason, "transient_failure")) return SymbolicUnknownReason.SmtUnavailable;

        if (ContainsReason(reason, "z3") ||
            ContainsReason(reason, "native") ||
            ContainsReason(reason, "unavailable") ||
            ContainsReason(reason, "load"))
            return SymbolicUnknownReason.SmtUnavailable;

        return SymbolicUnknownReason.Unknown;
    }

    private static bool ContainsReason(string reason, string value)
    {
        return reason.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

internal static class SymbolicReasonDisplay
{
    internal static string Format(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return reason ?? string.Empty;

        return reason switch
        {
            "smt_disabled" => "SMT disabled",
            "smt_disposed" => "SMT solver disposed",
            "smt_timeout" => "SMT solver timed out",
            "smt_unavailable" => "SMT solver unavailable",
            "smt_transient_failure" => "SMT solver remained unavailable after transient-failure retries",
            "smt_encoding_failure" => "SMT formula encoding failed",
            "smt_expression_budget_exceeded" => "SMT expression node budget exceeded",
            "smt_path_condition_budget_exceeded" => "SMT path condition budget exceeded",
            "smt_method_budget_exceeded" => "SMT method-level budget exceeded",
            "unsupported_formula_fallback" =>
                "unsupported formula fallback; legacy translated trigger was not trusted as proof",
            "unsupported_typed_projection" =>
                "runtime-hazard trigger could not be projected to typed symbolic IR",
            "trigger_always_true" => "trigger condition is always true",
            "trigger_always_false" => "trigger condition is always false",
            "path_unsatisfiable" => "path condition is unsatisfiable",
            "condition_parse_failure" => "condition could not be parsed",
            "not_common_to_all_candidate_program_points" => "not common to all candidate program points",
            _ => reason!
        };
    }
}
