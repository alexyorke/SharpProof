using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class Pr275OwnedIrStructuralCandidateTests
{
    [TestCaseSource(nameof(NestedShapes))]
    public void OwnedNestedCandidatesRemainDeterministicTypedAndReplayable(string shape, bool flagValue)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var number = factory.CreateVariable("number", factory.IntegerType);
        var index = factory.CreateVariable("index", factory.IntegerType);
        var text = factory.CreateVariable("text", factory.StringType);
        var other = factory.CreateVariable("other", factory.StringType);
        var bindings = new Dictionary<IrVarId, IrValue>
        {
            [flag] = factory.CreateBooleanValue(flagValue),
            [number] = factory.CreateIntegerValue(3),
            [index] = factory.CreateIntegerValue(0),
            [text] = factory.CreateStringValue("abc"),
            [other] = factory.CreateStringValue("defg")
        };
        var condition = factory.Variable(flag);
        var integer = factory.Variable(number);
        var sum = factory.Binary(IrBinaryOperator.Add, integer, factory.Integer(1));
        var selection = factory.Conditional(condition, factory.Variable(text), factory.Variable(other));
        var nestedCondition = factory.Unary(IrUnaryOperator.Not, condition);
        var term = shape switch
        {
            "negate" => factory.Unary(IrUnaryOperator.Negate, sum),
            "not" => factory.Unary(IrUnaryOperator.Not,
                factory.Binary(IrBinaryOperator.AndAlso, condition, nestedCondition)),
            "add" => factory.Binary(IrBinaryOperator.Add, sum, factory.Integer(2)),
            "comparison" => factory.Binary(IrBinaryOperator.GreaterThan, sum, factory.Integer(0)),
            "integer-conditional" => factory.Conditional(nestedCondition, sum, integer),
            "boolean-conditional" => factory.Conditional(nestedCondition, condition, factory.Boolean(false)),
            "string-conditional" => factory.Conditional(nestedCondition, selection, factory.Variable(other)),
            "cast" => factory.Cast(factory.ObjectType, selection),
            "length" => factory.Length(selection),
            "sequence" => factory.SequenceAccess(selection,
                factory.Binary(IrBinaryOperator.Add, factory.Variable(index), factory.Integer(1))),
            "concat" => factory.Binary(IrBinaryOperator.StringConcat, selection, factory.Variable(text)),
            "shared-dag" => factory.Binary(IrBinaryOperator.Add, sum, sum),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var candidates = IrStructuralShrinker.GetCandidates(factory, term);
        var repeated = IrStructuralShrinker.GetCandidates(factory, term);
        var originalSize = IrStructuralShrinker.StructuralSize(term);
        var interpreter = new IrInterpreter(factory);

        Assert.That(interpreter.Evaluate(term, bindings).Status, Is.EqualTo(IrEvaluationStatus.Value), shape);
        Assert.That(candidates, Is.Not.Empty, shape);
        Assert.That(repeated.Select(static candidate => candidate.Id),
            Is.EqualTo(candidates.Select(static candidate => candidate.Id)), shape);
        Assert.That(candidates.Select(static candidate => candidate.Id).Distinct().Count(), Is.EqualTo(candidates.Length), shape);
        foreach (var candidate in candidates)
        {
            Assert.That(candidate.Type, Is.EqualTo(term.Type), shape);
            Assert.That(candidate.Id, Is.Not.EqualTo(term.Id), shape);
            Assert.That(IrStructuralShrinker.StructuralSize(candidate), Is.LessThan(originalSize), shape);
            Assert.That(interpreter.Evaluate(candidate, bindings).Status, Is.EqualTo(IrEvaluationStatus.Value), shape);
        }
        if (shape == "shared-dag")
        {
            Assert.That(originalSize, Is.EqualTo(4));
        }
    }

    private static IEnumerable<TestCaseData> NestedShapes()
    {
        foreach (var shape in new[]
                 {
                     "negate", "not", "add", "comparison", "integer-conditional", "boolean-conditional",
                     "string-conditional", "cast", "length", "sequence", "concat", "shared-dag"
                 })
        {
            yield return new TestCaseData(shape, false);
            yield return new TestCaseData(shape, true);
        }
    }
}
