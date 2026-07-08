using System;

namespace SharpProof.Symbolic
{
    internal static class SymbolicUnknownReasonClassifier
    {
        internal static SymbolicUnknownReason Classify(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return SymbolicUnknownReason.Unknown;
            }

            if (ContainsReason(reason, "timeout") ||
                ContainsReason(reason, "timed_out"))
            {
                return SymbolicUnknownReason.Timeout;
            }

            if (ContainsReason(reason, "method_budget"))
            {
                return SymbolicUnknownReason.MethodBudgetExceeded;
            }

            if (ContainsReason(reason, "path_condition") ||
                ContainsReason(reason, "max_path_conditions") ||
                ContainsReason(reason, "too_many_path_conditions"))
            {
                return SymbolicUnknownReason.PathConditionBudgetExceeded;
            }

            if (ContainsReason(reason, "expression_budget") ||
                ContainsReason(reason, "max_expression"))
            {
                return SymbolicUnknownReason.ExpressionBudgetExceeded;
            }

            if (ContainsReason(reason, "cancellation") ||
                ContainsReason(reason, "cancelled") ||
                ContainsReason(reason, "canceled"))
            {
                return SymbolicUnknownReason.CancellationRequested;
            }

            if (ContainsReason(reason, "encoding"))
            {
                return SymbolicUnknownReason.EncodingFailure;
            }

            if (ContainsReason(reason, "unsupported"))
            {
                return SymbolicUnknownReason.UnsupportedIrEncoding;
            }

            if (ContainsReason(reason, "smt_required") ||
                ContainsReason(reason, "smt_disabled") ||
                ContainsReason(reason, "smt_off"))
            {
                return SymbolicUnknownReason.SmtDisabled;
            }

            if (ContainsReason(reason, "z3") ||
                ContainsReason(reason, "native") ||
                ContainsReason(reason, "unavailable") ||
                ContainsReason(reason, "load"))
            {
                return SymbolicUnknownReason.SmtUnavailable;
            }

            return SymbolicUnknownReason.Unknown;
        }

        private static bool ContainsReason(string reason, string value)
        {
            return reason.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
