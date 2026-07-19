namespace SharpProof.Symbolic;

internal static class SymbolicUnknownReasonClassifier
{
    private static readonly (SymbolicUnknownReason Reason, string[] Fragments)[] Rules =
    [
        (SymbolicUnknownReason.Timeout, ["timeout", "timed_out"]),
        (SymbolicUnknownReason.MethodBudgetExceeded, ["method_budget"]),
        (SymbolicUnknownReason.PathConditionBudgetExceeded,
            ["path_condition", "max_path_conditions", "too_many_path_conditions"]),
        (SymbolicUnknownReason.ExpressionBudgetExceeded, ["expression_budget", "max_expression"]),
        (SymbolicUnknownReason.CancellationRequested, ["cancellation", "cancelled", "canceled"]),
        (SymbolicUnknownReason.EncodingFailure, ["encoding"]),
        (SymbolicUnknownReason.UnsupportedIrEncoding, ["unsupported"]),
        (SymbolicUnknownReason.SmtDisabled, ["smt_required", "smt_disabled", "smt_off"]),
        (SymbolicUnknownReason.SmtUnavailable, ["transient_failure", "z3", "native", "unavailable", "load"])
    ];

    internal static SymbolicUnknownReason Classify(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return SymbolicUnknownReason.Unknown;

        foreach (var rule in Rules)
            if (rule.Fragments.Any(fragment => reason.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0))
                return rule.Reason;

        return SymbolicUnknownReason.Unknown;
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
