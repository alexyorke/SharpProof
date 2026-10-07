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
        var source = "using SharpProof.Attributes; public static class Subject { " +
            "[ZeroAllocations] public static int Target(int x) { " + body + " } private static int Count(params int[] values) => values.Length; }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var decoded = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
        var graph = decoded.ReachableSource!;
        var root = graph.Roots.Single().BodyId;
        Assert.That(graph.Bodies.Single(body => body.BodyId == root).Graph, Is.Not.Null);
        var summary = EffectSummaryFixpoint.ComputeValidated(graph)[root];
        Assert.That((summary.MayEffects | summary.UnknownEffects).HasFlag(SourceMayEffect.Allocation), Is.True);
    }
}
