using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzBoundedDivisionValueTests
{
    [Test]
    public async Task Signed64NestedDivisionAgreesOnItsUnchangedBoundedDomain()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var integer = factory.GetOrCreateIntegerType(64, signed: true);
        var first = factory.CreateVariable("first", integer);
        var second = factory.CreateVariable("second", integer);
        var third = factory.CreateVariable("third", integer);
        IrTerm quotient = factory.Integer(integer, long.MaxValue);
        foreach (var variable in new[] { first, second, third })
        {
            var value = factory.Variable(variable);
            var denominator = factory.Binary(IrBinaryOperator.Add,
                factory.Binary(IrBinaryOperator.Multiply, value, value),
                factory.Integer(integer, 1));
            quotient = factory.Binary(IrBinaryOperator.Divide, quotient, denominator);
        }
        const long target = 73_786_976_294_838_206;
        var formula = factory.Binary(IrBinaryOperator.Equal, quotient,
            factory.Integer(integer, target));

        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory, formula),
            Is.True);
        Assert.That(ReferenceValue(-2, -2, -2), Is.EqualTo(target));
        var result = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, formula);
        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Agreement), result.Detail);
        Assert.That(result.Expected, Is.EqualTo(FiniteDomainSatisfiability.Satisfiable));
        Assert.That(result.Actual, Is.EqualTo(FiniteDomainSatisfiability.Satisfiable));
        Assert.That(result.FiniteDomainAssumptions, Is.EqualTo(3));
        Assert.That(result.Detail, Is.Empty);
    }

    private static long ReferenceValue(long first, long second, long third)
    {
        if (first is < -3037000499L or > 3037000499L)
        {
            throw new ArgumentOutOfRangeException(nameof(first));
        }

        if (second is < -3037000499L or > 3037000499L)
        {
            throw new ArgumentOutOfRangeException(nameof(second));
        }

        if (third is < -3037000499L or > 3037000499L)
        {
            throw new ArgumentOutOfRangeException(nameof(third));
        }

        return long.MaxValue / checked(first * first + 1) /
            checked(second * second + 1) / checked(third * third + 1);
    }
}
