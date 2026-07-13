using NUnit.Framework;
using SearchLib.Purity;
using SharpProof.Test.Smt;

namespace SharpProof.Test;

[TestFixture]
public class SearchLibRoslynLoweringTests
{
    [TestCase(
        "int x",
        "x > 0 && x < 0",
        "true",
        AnalyzerPurityHazardKind.ImpureCallReachability,
        PurityProofOutcome.ProvablyPure,
        "path_unsatisfiable",
        TestName = "Lowering_ContradictoryImpureCallPath_ProvesPure")]
    [TestCase(
        "string s",
        "s == null",
        "s == null",
        AnalyzerPurityHazardKind.NullDereference,
        PurityProofOutcome.ProvablyImpure,
        "null_dereference_reachable",
        TestName = "Lowering_NullReceiverGuard_ProvesNullDereference")]
    [TestCase(
        "int divisor",
        "divisor != 0",
        "divisor == 0",
        AnalyzerPurityHazardKind.DivideByZero,
        PurityProofOutcome.ProvablyPure,
        "divide_by_zero_unreachable",
        TestName = "Lowering_NonZeroGuard_ProvesDivideByZeroUnreachable")]
    [TestCase(
        "int x",
        "x + 1 <= 0 && x >= 0",
        "true",
        AnalyzerPurityHazardKind.ImpureCallReachability,
        PurityProofOutcome.ProvablyPure,
        "path_unsatisfiable",
        TestName = "Lowering_AffineContradictoryImpureCallPath_ProvesPure")]
    [TestCase(
        "int divisor",
        "divisor + 1 != 1",
        "divisor == 0",
        AnalyzerPurityHazardKind.DivideByZero,
        PurityProofOutcome.ProvablyPure,
        "divide_by_zero_unreachable",
        TestName = "Lowering_AffineGuard_ProvesDivideByZeroUnreachable")]
    public void Lowering_ClassifiesRoslynEvidence(
        string parameters,
        string pathCondition,
        string conclusion,
        int hazardKind,
        PurityProofOutcome expectedOutcome,
        string expectedReason)
    {
        var context = AnalyzerTestHost.CreateConditionImplicationContext(
            parameters,
            pathCondition,
            conclusion);
        var evidence = new AnalyzerPurityEvidence(
            (AnalyzerPurityHazardKind)hazardKind,
            new[] { context.PathCondition },
            context.Conclusion);

        var lowered = AnalyzerEvidenceToSearchLibLowering.TryLower(
            evidence,
            context.SemanticModel,
            CancellationToken.None,
            out var query);

        Assert.That(lowered, Is.True);
        using var search = new PurityProofSearch();
        var result = search.Classify(query!, TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(expectedOutcome));
        Assert.That(result.Reason, Is.EqualTo(expectedReason));
    }
}
