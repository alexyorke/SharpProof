using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzOracleCollectionBoundaryTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task FailureSelectionRequiresEqualActualOracleCohortLengths(int absentOracle)
    {
        var frontend = FrontendDifferentialOracle.Compare(
            new SmallCSharpCaseGenerator(12345).Next());
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var finite = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, factory.Boolean(true));
        var partial = await PartialTermSmtDifferentialOracle.CompareAsync(factory,
            PartialTermSmtCaseGenerator.Create(factory, 12345));
        Assert.That(frontend.Status, Is.EqualTo(FuzzOracleStatus.Agreement), frontend.Detail);
        Assert.That(finite.Status, Is.EqualTo(FuzzOracleStatus.Agreement), finite.Detail);
        Assert.That(partial.Status, Is.EqualTo(FuzzOracleStatus.Agreement), partial.Detail);
        Assert.That(FuzzRunner.SelectFailureKeys([frontend.Status], [finite.Status], [partial.Status]),
            Is.Empty);

        FuzzOracleStatus[] frontendStatuses = absentOracle == 0 ? [] : [frontend.Status];
        FuzzOracleStatus[] finiteStatuses = absentOracle == 1 ? [] : [finite.Status];
        FuzzOracleStatus[] partialStatuses = absentOracle == 2 ? [] : [partial.Status];
        await TestContext.Out.WriteLineAsync(
            $"Actual frontend(seed 12345), finite(Total true), partial(seed 12345) outputs: " +
            $"{frontend.Status}/{finite.Status}/{partial.Status}; matched lengths 1/1/1; " +
            $"rejected lengths {frontendStatuses.Length}/{finiteStatuses.Length}/{partialStatuses.Length}.");
        Action select = () => FuzzRunner.SelectFailureKeys(frontendStatuses, finiteStatuses, partialStatuses);
        var error = Assert.Throws<ArgumentException>(select);
        Assert.That(error!.ParamName, Is.Null);
        Assert.That(error.Message, Is.EqualTo("Fuzz oracle status collections must have equal lengths."));
    }
}
