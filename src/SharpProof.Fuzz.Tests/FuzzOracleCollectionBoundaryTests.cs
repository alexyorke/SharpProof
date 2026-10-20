using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzOracleCollectionBoundaryTests
{
    [TestCase(0, "frontend")]
    [TestCase(1, "smt")]
    [TestCase(2, "partial")]
    public void FailureSelectionRejectsDefaultCohortBeforeLengthComparison(
        int defaultOracle, string parameterName)
    {
        ImmutableArray<FuzzOracleStatus> frontend = defaultOracle == 0 ? default : [];
        ImmutableArray<FuzzOracleStatus> smt = defaultOracle == 1 ? default : [];
        ImmutableArray<FuzzOracleStatus> partial = defaultOracle == 2 ? default : [];
        ArgumentNullException.ThrowIfNull(parameterName);
        Action select = () => FuzzRunner.SelectFailureKeys(frontend, smt, partial);
        var error = Assert.Throws<ArgumentNullException>(select);
        Assert.That(error!.ParamName, Is.EqualTo(parameterName));
    }

    [Test]
    public void SourceCampaignRejectsDefaultCases()
    {
        Func<Task> replay = async () =>
            await FuzzRunner.ReplaySourceCasesAsync(default,
                cancellationToken: TestContext.CurrentContext.CancellationToken);
        var error = Assert.ThrowsAsync<ArgumentNullException>(replay);
        Assert.That(error!.ParamName, Is.EqualTo("sourceCases"));
    }

    [Test]
    public void MetadataCompilationRejectsDefaultCasesBeforeResourceCreation()
    {
        Action create = () =>
        {
            using var compilation = new MetadataSourceCompilation(default);
        };
        var error = Assert.Throws<ArgumentNullException>(create);
        Assert.That(error!.ParamName, Is.EqualTo("cases"));
    }

    [Test]
    public void MetadataReplayRejectsDefaultCasesWithoutChangingResults()
    {
        var replay = new MetadataProgramSourceReplay(12345);
        var before = replay.Result;
        Func<Task> compare = async () =>
            await replay.CompareBatchAsync(default, 0,
                TestContext.CurrentContext.CancellationToken);
        var error = Assert.ThrowsAsync<ArgumentNullException>(compare);
        Assert.That(error!.ParamName, Is.EqualTo("cases"));
        Assert.That(replay.Result, Is.EqualTo(before));
    }

    [Test]
    public void TotalReplayRejectsDefaultCasesWithoutChangingResults()
    {
        var replay = new TotalProgramSourceReplay(12345);
        var before = replay.Result;
        Func<Task> compare = async () =>
            await replay.CompareBatchAsync(default, 0,
                TestContext.CurrentContext.CancellationToken);
        var error = Assert.ThrowsAsync<ArgumentNullException>(compare);
        Assert.That(error!.ParamName, Is.EqualTo("cases"));
        Assert.That(replay.Result, Is.EqualTo(before));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task FailureSelectionRequiresEqualActualOracleCohortLengths(int absentOracle)
    {
        var frontend = FrontendDifferentialOracle.Compare(
            new SmallCSharpCaseGenerator(12345).Next(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var finite = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, factory.Boolean(true), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var partial = await PartialTermSmtDifferentialOracle.CompareAsync(factory,
            PartialTermSmtCaseGenerator.Create(factory, 12345), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(frontend.Status, Is.EqualTo(FuzzOracleStatus.Agreement), frontend.Detail);
        Assert.That(finite.Status, Is.EqualTo(FuzzOracleStatus.Agreement), finite.Detail);
        Assert.That(partial.Status, Is.EqualTo(FuzzOracleStatus.Agreement), partial.Detail);
        Assert.That(FuzzRunner.SelectFailureKeys([frontend.Status], [finite.Status], [partial.Status]),
            Is.Empty);

        ImmutableArray<FuzzOracleStatus> frontendStatuses = absentOracle == 0 ? [] : [frontend.Status];
        ImmutableArray<FuzzOracleStatus> finiteStatuses = absentOracle == 1 ? [] : [finite.Status];
        ImmutableArray<FuzzOracleStatus> partialStatuses = absentOracle == 2 ? [] : [partial.Status];
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
