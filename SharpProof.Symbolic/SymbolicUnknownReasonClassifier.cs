namespace SharpProof.Symbolic;
internal static class SymbolicUnknownReasonClassifier {
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
    internal static SymbolicUnknownReason Classify(string reason) {
        if (string.IsNullOrWhiteSpace(reason)) return SymbolicUnknownReason.Unknown;
        foreach (var rule in Rules)
            if (rule.Fragments.Any(fragment => reason.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0))
                return rule.Reason;
        return SymbolicUnknownReason.Unknown;
    }
}
