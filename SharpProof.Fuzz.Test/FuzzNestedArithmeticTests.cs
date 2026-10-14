using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzNestedArithmeticTests
{
    [Test]
    public async Task NestedTotalDivisionRemainsInsideFiniteIntegerDomain()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var x = factory.Variable(factory.CreateVariable("x", factory.IntegerType));
        var y = factory.Variable(factory.CreateVariable("y", factory.IntegerType));
        IrTerm nested = x;
        for (var index = 0; index < 6; index++)
        {
            nested = factory.Binary(IrBinaryOperator.Divide,
                index % 2 == 0 ? y : x, nested);
        }

        // Signed Total division of two values in -2..2 stays in -2..2,
        // including the intentionally totalized zero-divisor result.
        var outsideDomain = factory.Binary(IrBinaryOperator.Equal, nested, factory.Integer(3));
        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(
            factory, outsideDomain), Is.True);
        var result = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, outsideDomain);

        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Agreement), result.Detail);
        Assert.That(result.Expected, Is.EqualTo(FiniteDomainSatisfiability.Unsatisfiable));
        Assert.That(result.Actual, Is.EqualTo(FiniteDomainSatisfiability.Unsatisfiable));
        Assert.That(result.FiniteDomainAssumptions, Is.EqualTo(2));
    }
}
