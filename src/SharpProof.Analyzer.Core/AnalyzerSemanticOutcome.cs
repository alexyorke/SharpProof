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
        return outcome switch
        {
            AnalyzerSemanticOutcome.NotApplicable => 0,
            AnalyzerSemanticOutcome.Proven => 1,
            AnalyzerSemanticOutcome.Suppressed => 2,
            AnalyzerSemanticOutcome.Abstained => 3,
            AnalyzerSemanticOutcome.Unknown => 4,
            AnalyzerSemanticOutcome.Refuted => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
    }
}
