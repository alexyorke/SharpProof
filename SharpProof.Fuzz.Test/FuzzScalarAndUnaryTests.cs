using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzScalarAndUnaryTests
{
    [Test]
    public void SemanticEdgesPreserveBooleanStringAndSharedSequenceInputs()
    {
        long[] shared = [4, 7];
        var cases = new[]
        {
            new FrontendSemanticEdgeCase("bool", "bool value", "!value",
                [true], FrontendSubsetDecision.Exact, FrontendAbstention.None),
            new FrontendSemanticEdgeCase("string", "string value", "value",
                ["proof"], FrontendSubsetDecision.Exact, FrontendAbstention.None),
            new FrontendSemanticEdgeCase("long[]", "long[] first, long[] second", "first",
                [shared, shared], FrontendSubsetDecision.Exact, FrontendAbstention.None)
        };

        var results = FrontendDifferentialOracle.CompareSemanticEdges(cases);
        Assert.That(results, Has.Length.EqualTo(cases.Length));
        for (var index = 0; index < results.Length; index++)
        {
            Assert.That(results[index].Status, Is.EqualTo(FuzzOracleStatus.Agreement),
                cases[index].Expression + ": " + results[index].Detail);
            Assert.That(results[index].ActualDecision, Is.EqualTo(cases[index].ExpectedDecision));
            Assert.That(results[index].ActualAbstention, Is.EqualTo(FrontendAbstention.None));
        }
    }

    [Test]
    public void IrUnaryCandidatesRetainTypeAndReduceNestedOperand()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.Variable(factory.CreateVariable("value", factory.IntegerType));
        var parent = factory.Unary(IrUnaryOperator.Negate,
            factory.Binary(IrBinaryOperator.Add, variable, factory.Integer(2)));
        var candidates = IrStructuralShrinker.GetCandidates(factory, parent);

        Assert.That(candidates, Is.Not.Empty);
        Assert.That(candidates.Any(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate is IrUnaryTerm)), Is.True);
        foreach (var candidate in candidates)
        {
            Assert.That(candidate.Type, Is.EqualTo(parent.Type));
            Assert.That(IrStructuralShrinker.StructuralSize(candidate),
                Is.LessThan(IrStructuralShrinker.StructuralSize(parent)));
        }
    }

    [Test]
    public void FrontendUnaryAndConditionalCandidatesMatchCompiledEvaluation()
    {
        var integer = GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Add,
            GeneratedCSharpExpression.Left(), GeneratedCSharpExpression.Integer(2));
        var parents = new[]
        {
            GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Negate, integer),
            GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Not,
                GeneratedCSharpExpression.Binary(GeneratedExpressionKind.AndAlso,
                    GeneratedCSharpExpression.Condition(), GeneratedCSharpExpression.Boolean(true))),
            GeneratedCSharpExpression.Conditional(GeneratedCSharpExpression.Condition(),
                integer, GeneratedCSharpExpression.Right())
        };
        var cases = new List<GeneratedCSharpCase>();
        foreach (var parent in parents)
        {
            var candidates = CSharpStructuralShrinker.GetCandidates(parent);
            Assert.That(candidates, Is.Not.Empty);
            foreach (var candidate in candidates)
            {
                Assert.That(candidate.Type, Is.EqualTo(parent.Type));
                Assert.That(candidate.NodeCount, Is.LessThan(parent.NodeCount));
                cases.Add(new(candidate, 3, -1, true));
            }
        }

        var results = FrontendDifferentialOracle.CompareBatch(cases);
        Assert.That(results, Has.Length.EqualTo(cases.Count));
        for (var index = 0; index < results.Length; index++)
        {
            Assert.That(results[index].Status, Is.EqualTo(FuzzOracleStatus.Agreement),
                cases[index].Expression.Render() + ": " + results[index].Detail);
        }
    }
}
