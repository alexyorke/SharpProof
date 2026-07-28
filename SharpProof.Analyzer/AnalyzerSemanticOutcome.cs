namespace SharpProof.Analyzer;

internal enum AnalyzerSemanticOutcome {
    NotApplicable,
    Proven,
    Suppressed,
    Abstained,
    Unknown,
    Refuted
}

internal static class AnalyzerSemanticOutcomes {
    internal static AnalyzerSemanticOutcome Combine(
        AnalyzerSemanticOutcome left,
        AnalyzerSemanticOutcome right) =>
        (AnalyzerSemanticOutcome)Math.Max((int)left, (int)right);
}
