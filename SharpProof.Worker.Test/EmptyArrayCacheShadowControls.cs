using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EmptyArrayCacheShadowControls
{
    [TestCase("return System.Array.Empty<Cell>().Length;")]
    [TestCase("return Count();")]
    [TestCase("try { return System.Array.Empty<Cell>().Length; } finally { x++; }")]
    [TestCase("try { return Count(); } finally { x++; }")]
    public async Task CacheInitializationRemainsAnEffectBoundaryInEveryLowering(string body)
    {
        var source = "using SharpProof.Attributes; public sealed class Cell {} public static class Subject { " +
            "[ZeroAllocations] public static int Target(int x) { " + body + " } private static int Count(params Cell[] values) => values.Length; }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var decoded = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
        var graph = decoded.ReachableSource!;
        var root = graph.Roots.Single().BodyId;
        var summary = EffectSummaryFixpoint.ComputeValidated(graph)[root];
        Assert.That((summary.MayEffects | summary.UnknownEffects).HasFlag(SourceMayEffect.Allocation), Is.True,
            "Shadow source summaries must retain possible cache initialization allocation");
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }
}
