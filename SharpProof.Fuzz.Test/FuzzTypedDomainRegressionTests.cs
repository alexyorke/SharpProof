using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzTypedDomainRegressionTests
{
    [TestCase(32)]
    [TestCase(64)]
    public async Task SignedIntegerDomainUsesTheVariablesActualType(int width)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, signed: true);
        var variable = factory.CreateVariable("value", type);
        var formula = factory.Binary(IrBinaryOperator.Equal, factory.Variable(variable),
            factory.Integer(type, 1));

        // The concrete domain [-2,-1,0,1,2] contains 1 for both signed widths.
        var result = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, formula, cancellationToken: CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Agreement));
        Assert.That(result.Expected, Is.EqualTo(FiniteDomainSatisfiability.Satisfiable));
        Assert.That(result.Actual, Is.EqualTo(FiniteDomainSatisfiability.Satisfiable));
        Assert.That(result.FiniteDomainAssumptions, Is.EqualTo(1));
        Assert.That(result.Detail, Is.Empty);
        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory,
            formula, cancellationToken: CancellationToken.None), Is.True);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory,
            formula, CancellationToken.None, out var prepared), Is.True);
        var cached = await FiniteDomainSmtDifferentialOracle.ComparePreparedAsync(factory,
            prepared!, cancellationToken: CancellationToken.None);
        Assert.That(cached, Is.EqualTo(result));
    }
}
