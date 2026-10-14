using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerRecursiveMayEffectUnionCoverageTests
{
    [Test]
    public void ActualThreeMemberSourceCyclePropagatesDistinctLocalEffects()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                private static int state;
                [ZeroAllocations] public static object Target(int value) => Allocate(value);
                private static object Allocate(int value) {
                    object allocated = new object();
                    return Write(value);
                }
                private static object Write(int value) {
                    state = value;
                    return Target(value);
                }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var decoded = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
        Assert.That(decoded.ReachableSource, Is.Not.Null);
        var graph = decoded.ReachableSource!;
        Assert.That(graph.CollectionComplete, Is.True);
        Assert.That(graph.Bodies, Has.Length.EqualTo(3));
        var ids = graph.Bodies.Select(body => body.BodyId).ToHashSet(StringComparer.Ordinal);
        Assert.That(ids, Has.Count.EqualTo(3));
        Assert.That(graph.Bodies.All(body => body.Graph != null && body.CallsComplete &&
            body.EffectsCompleteAtEntry && body.IsCallSkeleton && body.SourceCalls.Length == 1 &&
            body.Callees.Length == 1 && body.Callees[0] != body.BodyId && ids.Contains(body.Callees[0])), Is.True);
        var locals = graph.Bodies.Select(body => PortableIrGraphCodec.Decode(body.Graph!)).ToArray();
        Assert.That(locals.All(local => local.Program != null), Is.True);
        var allocations = locals.Where(local => local.Instructions.OfType<IrAllocationInstruction>().Any()).ToArray();
        var writes = locals.Where(local => local.Instructions.OfType<IrWriteInstruction>()
            .Any(write => IrWriteSites.IsObservable(local.Factory, write))).ToArray();
        Assert.That(allocations, Has.Length.EqualTo(1));
        Assert.That(writes, Has.Length.EqualTo(1));
        Assert.That(allocations[0], Is.Not.SameAs(writes[0]));
        Assert.That(writes[0].Instructions.OfType<IrWriteInstruction>()
            .Any(write => write.Region == IrWriteRegion.Static), Is.True);

        var summaries = EffectSummaryFixpoint.ComputeValidated(graph);
        Assert.That(summaries.Keys, Is.EquivalentTo(ids));
        Assert.That(summaries.Values.All(summary =>
            summary.MayEffects == (SourceMayEffect.Allocation | SourceMayEffect.NonlocalWrite) &&
            summary.UnknownEffects == SourceMayEffect.None && summary.MayDiverge &&
            summary.CompletionUnknown), Is.True);
    }
}
