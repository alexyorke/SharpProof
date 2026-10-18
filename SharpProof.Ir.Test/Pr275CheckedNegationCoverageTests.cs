using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class Pr275CheckedNegationCoverageTests
{
    [TestCase(long.MinValue, true, 0L)]
    [TestCase(long.MinValue + 1, false, long.MaxValue)]
    [TestCase(long.MaxValue, false, -long.MaxValue)]
    [TestCase(0L, false, 0L)]
    [TestCase(-42L, false, 42L)]
    public void LegacyIntegerNegationChecksItsSignedBoundary(long input, bool overflows, long expected)
    {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var expression = factory.Unary(IrUnaryOperator.Negate, factory.Variable(variable));
        var result = new IrInterpreter(factory).Evaluate(expression,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(variable, factory.CreateIntegerValue(input)));

        Assert.That(result.Status, Is.EqualTo(overflows ? IrEvaluationStatus.Exception : IrEvaluationStatus.Value));
        if (overflows)
        {
            Assert.That(result.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
        }
        else
        {
            Assert.That(result.Value!.Integer, Is.EqualTo(expected));
        }
    }
}
