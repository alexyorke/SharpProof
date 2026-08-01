namespace SharpProof.Analyzer;

internal enum AnalyzerSemanticOutcome
{
    NotApplicable,
    Proven,
    Suppressed,
    Abstained,
    Unknown,
    Refuted
}

internal static class AnalyzerSemanticOutcomes
{
    internal static AnalyzerSemanticOutcome Combine(
        AnalyzerSemanticOutcome left,
        AnalyzerSemanticOutcome right)
    {
        return Rank(left) >= Rank(right) ? left : right;
    }

    private static int Rank(AnalyzerSemanticOutcome outcome)
    {
        return (int)ArgumentNullGuard.RequireDefined(
            outcome,
            nameof(outcome));
    }
}
