using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EmptyArrayCacheCompleteShadowTests
{
    [TestCase("return System.Array.Empty<int>().Length;")]
    [TestCase("return Count();")]
    public void CompleteScalarShadowCannotEraseCacheAllocation(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class Subject { " +
            "[ZeroAllocations] public static int Target(int x) { " + body + " } private static int Count(params int[] values) => values.Length; }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var decoded = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var graph = decoded.ReachableSource!;
        var root = graph.Roots.Single().BodyId;
        Assert.That(graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.BodyId == root)).Graph, Is.Not.Null);
        var summary = EffectSummaryFixpoint.ComputeValidated(graph, cancellationToken: TestContext.CurrentContext.CancellationToken)[root];
        Assert.That((summary.MayEffects | summary.UnknownEffects).HasFlag(SourceMayEffect.Allocation), Is.True);
    }
}
