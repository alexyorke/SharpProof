using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicStateDifferentialHarnessTests {
    [Test]
    public void Capture_NormalizesEquivalentStatesAndTruncationOrder() {
        var source = SyntaxFactory.ParseExpression("value");
        var value = new SymbolicVariableTerm("value", SmtValueKind.Int);
        var lower = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThanOrEqual,
                value,
                new SymbolicIntegerConstantTerm(0)),
            source,
            "test.lower");
        var upper = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                value,
                new SymbolicIntegerConstantTerm(10)),
            source,
            "test.upper");
        var first = new SymbolicState(
            new[] { lower, upper },
            symbolVersions: new[] {
                new KeyValuePair<string, int>("value", 2),
                new KeyValuePair<string, int>("other", 1)
            });
        var second = new SymbolicState(
            new[] { upper, lower },
            symbolVersions: new[] {
                new KeyValuePair<string, int>("other", 1),
                new KeyValuePair<string, int>("value", 2)
            });
        var ifLimit = new SymbolicAnalysisTruncationEvent(
            SymbolicAnalysisLimitKind.IfElseFactMerge,
            2,
            3,
            "test.if",
            7);
        var switchLimit = new SymbolicAnalysisTruncationEvent(
            SymbolicAnalysisLimitKind.SwitchFactMerge,
            4,
            5,
            "test.switch",
            11);
        var firstSnapshot = SymbolicStateDifferentialHarness.Capture(
            first,
            SymbolicLoweringSupport.Exact,
            SymbolicUnknownReason.None,
            truncation: new SymbolicAnalysisTruncationInfo(new[] { ifLimit, switchLimit }));
        var secondSnapshot = SymbolicStateDifferentialHarness.Capture(
            second,
            SymbolicLoweringSupport.Exact,
            SymbolicUnknownReason.None,
            truncation: new SymbolicAnalysisTruncationInfo(new[] { switchLimit, ifLimit }));

        SymbolicStateDifferentialHarness.AssertEquivalent(firstSnapshot, secondSnapshot, "equivalent states");
    }

    [Test]
    public void Capture_PreservesSupportUnknownReasonProvenanceAndTruncation() {
        var provenance = new SymbolicLoweringProvenance(
            "operation-transfer",
            new TextSpan(12, 4),
            "unsupported-shape");
        var result = SymbolicLoweringResult<SymbolicState>.Unsupported(provenance);
        var truncation = new SymbolicAnalysisTruncationInfo(new[] {
            new SymbolicAnalysisTruncationEvent(
                SymbolicAnalysisLimitKind.MergedPathConditions,
                8,
                9,
                "test.merge",
                12)
        });

        var snapshot = SymbolicStateDifferentialHarness.Capture(result, truncation);

        Assert.Multiple(() => {
            Assert.That(snapshot.NormalizedStateKey, Is.Null);
            Assert.That(snapshot.Support, Is.EqualTo(SymbolicLoweringSupport.Unsupported));
            Assert.That(snapshot.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
            Assert.That(snapshot.ProvenanceKey, Does.Contain("operation-transfer"));
            Assert.That(snapshot.ProvenanceKey, Does.Contain("unsupported-shape"));
            Assert.That(snapshot.TruncationKey, Does.Contain("MergedPathConditions"));
            Assert.That(snapshot.TruncationKey, Does.Contain("test.merge"));
        });
    }
}
