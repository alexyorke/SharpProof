using NUnit.Framework;
using SharpProof.ProofCore.Analysis;
using SharpProof.Test.Smt;

namespace SharpProof.Test;

[TestFixture]
internal class ProofCoreRoslynLoweringTests {
    [TestCase(
        "int x",
        "x > 0 && x < 0",
        "true",
        AnalyzerAnalysisHazardKind.EffectViolationReachability,
        AnalysisProofOutcome.Proven,
        "path_unsatisfiable",
        TestName = "Lowering_ContradictoryImpureCallPath_ProvesPure")]
    [TestCase(
        "string s",
        "s == null",
        "s == null",
        AnalyzerAnalysisHazardKind.NullDereference,
        AnalysisProofOutcome.Disproven,
        "null_dereference_reachable",
        TestName = "Lowering_NullReceiverGuard_ProvesNullDereference")]
    [TestCase(
        "int divisor",
        "divisor != 0",
        "divisor == 0",
        AnalyzerAnalysisHazardKind.DivideByZero,
        AnalysisProofOutcome.Proven,
        "divide_by_zero_unreachable",
        TestName = "Lowering_NonZeroGuard_ProvesDivideByZeroUnreachable")]
    [TestCase(
        "int x",
        "x + 1 <= 0 && x >= 0",
        "true",
        AnalyzerAnalysisHazardKind.EffectViolationReachability,
        AnalysisProofOutcome.Proven,
        "path_unsatisfiable",
        TestName = "Lowering_AffineContradictoryImpureCallPath_ProvesPure")]
    [TestCase(
        "int divisor",
        "divisor + 1 != 1",
        "divisor == 0",
        AnalyzerAnalysisHazardKind.DivideByZero,
        AnalysisProofOutcome.Proven,
        "divide_by_zero_unreachable",
        TestName = "Lowering_AffineGuard_ProvesDivideByZeroUnreachable")]
    public void Lowering_ClassifiesRoslynEvidence(
        string parameters,
        string pathCondition,
        string conclusion,
        int hazardKind,
        AnalysisProofOutcome expectedOutcome,
        string expectedReason) {
        var context = AnalyzerTestHost.CreateConditionImplicationContext( parameters, pathCondition, conclusion);
        var evidence = new AnalyzerPurityEvidence( (AnalyzerAnalysisHazardKind)hazardKind, new[] { context.PathCondition }, context.Conclusion);

        var lowered = AnalyzerEvidenceToProofCoreLowering.TryLower( evidence, context.SemanticModel, CancellationToken.None, out var query);

        Assert.That(lowered, Is.True);
        using var search = new AnalysisProofSearch();
        var result = search.Classify(query!, TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(expectedOutcome));
        Assert.That(result.Reason, Is.EqualTo(expectedReason));
    }
}
