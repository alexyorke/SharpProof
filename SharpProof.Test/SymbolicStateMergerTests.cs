using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicStateMergerTests {
    [Test]
    public void ConditionalMerge_UsesStructuralLimitAndRecordsTruncation() {
        var source = SyntaxFactory.ParseExpression("select");
        var select = new SymbolicVariableTerm("select", SmtValueKind.Bool);
        var first = new SymbolicVariableTerm("first", SmtValueKind.Int);
        var second = new SymbolicVariableTerm("second", SmtValueKind.Int);
        var trueState = new SymbolicState(pathConditions: new[] {
            Truth(select, source),
            Equal(first, 1, source),
            Equal(second, 2, source)
        });
        var falseState = new SymbolicState(pathConditions: new SymbolicCondition[] {
            new SymbolicNotCondition(Truth(select, source)),
            Equal(first, 3, source),
            Equal(second, 4, source)
        });
        using var scope = SymbolicAnalysisLimitContext.Push(
            SharpProofAnalysisBudget.Default with {
                MaxMergedPathConditions = 1,
                MaxGuardFactsPerTargetPerState = 1
            });

        Assert.That(trueState.PathConditions, Has.Length.EqualTo(3));
        Assert.That(falseState.PathConditions, Has.Length.EqualTo(3));
        var merged = SymbolicStateMerger.MergePathConditionsAcrossAll(new[] { trueState, falseState });
        var truncation = scope.Snapshot();

        Assert.That(merged, Has.Length.EqualTo(1));
        Assert.That(truncation.IsTruncated, Is.True);
        Assert.That(
            truncation.Events,
            Has.Some.Matches<SymbolicAnalysisTruncationEvent>(item =>
                item.Kind == SymbolicAnalysisLimitKind.MergedPathConditions &&
                item.Limit == 1 &&
                item.Observed == 2));
    }
    private static SymbolicCondition Truth(SymbolicTerm value, Microsoft.CodeAnalysis.SyntaxNode source)
        => new SymbolicFactCondition(SymbolicFact.Exact(new SymbolicTruthAtom(value), source, "test.truth"));
    private static SymbolicCondition Equal(SymbolicTerm value, long constant, Microsoft.CodeAnalysis.SyntaxNode source)
        => new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(SymbolicRelationOperator.Equal, value, new SymbolicIntegerConstantTerm(constant)),
            source,
            "test.equal"));
}
