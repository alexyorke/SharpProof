using NUnit.Framework;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using SearchLib.Smt;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public class CfgBranchAssumptionTests
{
    [Test]
    public void SymbolicOnlyContradictoryBranch_PrunesSuccessor()
    {
        var value = new SymbolicVariableTerm("value", SmtValueKind.Int);
        ExpressionSyntax source = SyntaxFactory.ParseExpression("value > 0");
        var positive = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                value,
                new SymbolicIntegerConstantTerm(0)),
            source,
            "test.incoming");
        var nonPositive = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThanOrEqual,
                value,
                new SymbolicIntegerConstantTerm(0)),
            source,
            "test.branch");
        var branchState = new SymbolicState(new[] { positive, nonPositive });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var hasSuccessor = PurityAnalysisEngine.TryFinalizeUntranslatedSuccessorState(
            PurityAnalysisEngine.PurityAnalysisState.Pure,
            System.Collections.Immutable.ImmutableArray<SmtFormula>.Empty,
            branchState,
            addedBranchAssumptions: false,
            addedSymbolicBranchAssumption: true,
            smtAnalysis,
            sourceNode: null,
            out _);

        Assert.That(hasSuccessor, Is.False);
    }
}
