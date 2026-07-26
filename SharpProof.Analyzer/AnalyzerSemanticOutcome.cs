namespace SharpProof.Analyzer;

internal enum AnalyzerSemanticOutcome {
    NotApplicable,
    Proven,
    Refuted,
    Unknown,
    Abstained,
    Suppressed
}

internal static class AnalyzerSemanticOutcomes {
    internal static AnalyzerSemanticOutcome Combine(
        AnalyzerSemanticOutcome left,
        AnalyzerSemanticOutcome right) {
        if (left == AnalyzerSemanticOutcome.NotApplicable) return right;
        if (right == AnalyzerSemanticOutcome.NotApplicable) return left;
        if (left == AnalyzerSemanticOutcome.Refuted ||
            right == AnalyzerSemanticOutcome.Refuted)
            return AnalyzerSemanticOutcome.Refuted;
        if (left == AnalyzerSemanticOutcome.Unknown ||
            right == AnalyzerSemanticOutcome.Unknown)
            return AnalyzerSemanticOutcome.Unknown;
        if (left == AnalyzerSemanticOutcome.Abstained ||
            right == AnalyzerSemanticOutcome.Abstained)
            return AnalyzerSemanticOutcome.Abstained;
        if (left == AnalyzerSemanticOutcome.Suppressed ||
            right == AnalyzerSemanticOutcome.Suppressed)
            return AnalyzerSemanticOutcome.Suppressed;
        return AnalyzerSemanticOutcome.Proven;
    }
}
