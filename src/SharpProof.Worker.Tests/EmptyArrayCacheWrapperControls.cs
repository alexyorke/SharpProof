using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EmptyArrayCacheWrapperControls
{
    [TestCase("return System.Array.Empty<Cell>().Length;")]
    [TestCase("return Count();")]
    public async Task SourceWrapperCannotLosePossibleCacheAllocation(string helperBody)
    {
        System.ArgumentNullException.ThrowIfNull(helperBody);
        var source = "using SharpProof.Attributes; public sealed class Cell {} public static class Subject { " +
            "[ZeroAllocations] public static int Target(int x) => Helper(); private static int Helper() { " + helperBody +
            " } private static int Count(params Cell[] values) => values.Length; }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var decoded = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var graph = decoded.ReachableSource!;
        var root = graph.Roots.Single().BodyId;
        var summary = EffectSummaryFixpoint.ComputeValidated(graph, cancellationToken: TestContext.CurrentContext.CancellationToken)[root];
        Assert.That((summary.MayEffects | summary.UnknownEffects).HasFlag(SourceMayEffect.Allocation), Is.True);
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }
}
