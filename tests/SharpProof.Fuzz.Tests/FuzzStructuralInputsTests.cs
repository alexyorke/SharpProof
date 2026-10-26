using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzStructuralInputsTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void IrCandidatesRebuildNestedCastLengthSequenceAndConditionalInputs(int index)
    {
        if (index < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(index)); }

        var factory = new IrFactory(IrExecutionSemantics.Total);
        var condition = factory.Variable(factory.CreateVariable("condition", factory.BooleanType));
        var otherCondition = factory.Variable(factory.CreateVariable("other", factory.BooleanType));
        var firstObject = factory.Variable(factory.CreateVariable("first", factory.ObjectType));
        var secondObject = factory.Variable(factory.CreateVariable("second", factory.ObjectType));
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var firstSequence = factory.Variable(factory.CreateVariable("firstSequence", sequenceType));
        var secondSequence = factory.Variable(factory.CreateVariable("secondSequence", sequenceType));
        var integer = factory.Variable(factory.CreateVariable("integer", factory.IntegerType));
        var arithmetic = factory.Binary(IrBinaryOperator.Add, integer, factory.Integer(1));
        var cast = factory.Cast(factory.StringType,
            factory.Conditional(condition, firstObject, secondObject));
        ImmutableArray<IrTerm> parents =
        [
            cast,
            factory.Length(cast),
            factory.SequenceAccess(factory.Conditional(condition, firstSequence, secondSequence),
                arithmetic),
            factory.Conditional(factory.Binary(IrBinaryOperator.AndAlso, condition, otherCondition),
                arithmetic, factory.Integer(2))
        ];
        var parent = parents[index];
        var candidates = IrStructuralShrinker.GetCandidates(factory, parent);

        Assert.That(candidates, Is.Not.Empty);
        Assert.That(IrStructuralShrinker.GetCandidates(factory, parent), Is.EqualTo(candidates));
        Assert.That(candidates.Select(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Id)), Is.Unique);
        foreach (var candidate in candidates)
        {
            Assert.That(candidate.Type, Is.EqualTo(parent.Type));
            Assert.That(IrStructuralShrinker.StructuralSize(candidate),
                Is.LessThan(IrStructuralShrinker.StructuralSize(parent)));
        }
    }

    [Test]
    public void RebuiltFrontendCandidatesAgreeWithCompiledNestedInputs()
    {
        var condition = GeneratedCSharpExpression.Condition();
        var text = GeneratedCSharpExpression.Text();
        var reference = GeneratedCSharpExpression.Reference();
        var values = GeneratedCSharpExpression.Values();
        ImmutableArray<GeneratedCSharpExpression> parents =
        [
            GeneratedCSharpExpression.Length(GeneratedCSharpExpression.Binary(
                GeneratedExpressionKind.StringConcat, text, GeneratedCSharpExpression.String("xy"))),
            GeneratedCSharpExpression.CastToString(GeneratedCSharpExpression.Conditional(
                condition, reference, GeneratedCSharpExpression.NullReference())),
            GeneratedCSharpExpression.ArrayIndex(GeneratedCSharpExpression.Conditional(
                condition, values, values), GeneratedCSharpExpression.Binary(
                GeneratedExpressionKind.Add, GeneratedCSharpExpression.Left(),
                GeneratedCSharpExpression.Right()))
        ];
        var caseBuilder = ImmutableArray.CreateBuilder<GeneratedCSharpCase>();
        foreach (var parent in parents)
        {
            var candidates = CSharpStructuralShrinker.GetCandidates(parent);
            Assert.That(candidates, Is.Not.Empty);
            foreach (var candidate in candidates)
            {
                Assert.That(candidate.Type, Is.EqualTo(parent.Type));
                Assert.That(candidate.NodeCount, Is.LessThan(parent.NodeCount));
                caseBuilder.Add(new(candidate, 1, 1, true)
                {
                    Text = "abcd",
                    Values = [8L, 9L, 10L],
                    Reference = "wxyz"
                });
            }
        }

        var cases = caseBuilder.ToImmutable();
        var results = FrontendDifferentialOracle.CompareBatch(cases, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(results, Has.Length.EqualTo(cases.Length));
        for (var index = 0; index < results.Length; index++)
        {
            Assert.That(results[index].Status, Is.EqualTo(FuzzOracleStatus.Agreement),
                cases[index].Expression.Render() + ": " + results[index].Detail);
        }
    }
}
