using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class FiniteDomainResourceLimitTests
{
    // Nightly seed 20956982, case 8560 (issue #453): the symbolic query asks
    // Z3 to show that no 32-bit divisor of the prime 2^31 - 1 lies in the
    // domain, which exhausts its resource limit.
    [Test]
    public async Task RemainderOfAPrimeByADomainVariableAgrees()
    {
        var factory = new IrFactory();
        var v0 = factory.CreateVariable("v0", factory.IntegerType);
        var variable = factory.Variable(v0);
        var divisor = factory.Conditional(
            factory.Binary(IrBinaryOperator.Equal, variable, factory.Integer(-2147483648)),
            factory.Integer(-2), variable);
        var formula = factory.Binary(IrBinaryOperator.Equal,
            factory.Binary(IrBinaryOperator.Remainder, factory.Integer(-2147483647), divisor), factory.Integer(0));

        var result = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, formula,
            cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Agreement), result.Detail);
    }
}
