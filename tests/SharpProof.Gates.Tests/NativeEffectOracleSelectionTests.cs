using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class NativeEffectOracleSelectionTests
{
    [TestCase(1, false)]
    [TestCase(200, true)]
    public async Task ExplicitLimitPreservesPinnedMethodSelection(int maximumMethods, bool exhaustive)
    {
        var root = RepositoryLayout.FindRoot();
        var document = OpenSourceCorpusCatalog.Load(root);
        var report = await NativeEffectOracleGate.RunAsync(root, maximumMethods: maximumMethods, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(report.UniverseMethodCount, Is.EqualTo(document.Methods.Length));
        Assert.That(report.CheckedMethodCount, Is.EqualTo(maximumMethods));
        Assert.That(report.Exhaustive, Is.EqualTo(exhaustive));
        Assert.That(report.Rows.Select(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.MethodId)),
            Is.EqualTo(document.Methods.Take(maximumMethods).Select(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Id))));
        Assert.That(report.RuntimeContradictions, Is.Zero);
        Assert.That(report.Passed, Is.True);
    }

    [TestCase("OSS0004")]
    [TestCase("OSS0041")]
    [TestCase("OSS0043")]
    [TestCase("OSS0103")]
    [TestCase("OSS0149")]
    public async Task CorpusMutableMethodsRetainConcreteMutationWitnesses(string methodId)
    {
        ArgumentNullException.ThrowIfNull(methodId);
        await CheckCorpusObservableViolation(methodId);
    }

    [TestCase("OSS0059")]
    [TestCase("OSS0144")]
    public async Task CorpusResourceConstructorsHaveConcreteSynchronizationWitnesses(string methodId)
    {
        ArgumentNullException.ThrowIfNull(methodId);
        await CheckCorpusObservableViolation(methodId);
    }

    [Test]
    public async Task ResourceConstructorWithUnsupportedEntryInitializationRemainsUnknown()
    {
        await CheckCorpusObservableViolation("OSS0139", unknownEntryInitialization: true);
    }

    private static async Task CheckCorpusObservableViolation(string methodId, bool unknownEntryInitialization = false)
    {
        ArgumentNullException.ThrowIfNull(methodId);
        var root = RepositoryLayout.FindRoot();
        var document = OpenSourceCorpusCatalog.Load(root);
        document = document with { Methods = [.. document.Methods.Where(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Id == methodId)] };
        Assert.That(document.Methods, Has.Length.EqualTo(1));
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, TestContext.CurrentContext.CancellationToken, purity: true);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, root, "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken),
            out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single(preparation => preparation is null ? throw new ArgumentNullException(nameof(preparation)) : preparation.EffectClaims.Length != 0);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out _, out var failure, TestContext.CurrentContext.CancellationToken), Is.True,
            "construction=" + failure + "; blocks=" + candidate.Program.Blocks.Length);
        var report = await NativeEffectOracleGate.ObserveAsync(compilation, [methodId], root, "selected-prefix-witness", 200,
            TestContext.CurrentContext.CancellationToken, purity: true);
        var row = report.Rows.Single();
        if (unknownEntryInitialization)
        {
            Assert.That(preparation.Total!.EffectsCompleteAtEntry, Is.False);
            Assert.That(row.NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(row.NativeReason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
        else
        {
            Assert.That(row.NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted),
            row.NativeReason + "; total=" + row.HasTotalBody + "; abstraction=" + row.HasBodyAbstraction);
        }
        Assert.That(report.Passed, Is.True);
    }
    [TestCase(-1)]
    [TestCase(201)]
    public void LimitsOutsidePinnedUniverseAreRejected(int maximumMethods)
    {
        var error = Assert.ThrowsAsync<ArgumentOutOfRangeException>(new Func<Task>(() =>
            NativeEffectOracleGate.RunAsync(RepositoryLayout.FindRoot(), maximumMethods: maximumMethods, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.ParamName, Is.EqualTo(nameof(maximumMethods)));
    }
}
