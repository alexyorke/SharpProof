using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EffectSummaryFixpointTests
{
    [Test]
    public void EffectFreeRecursionDoesNotInventEffectsOrNormalCompletion()
    {
        var graph = Graph("[ZeroAllocations] public static int Root(int x) => Root(x);");
        var summary = EffectSummaryFixpoint.ComputeValidated(graph).Values.Single();
        Assert.That(summary.MayEffects, Is.EqualTo(SourceMayEffect.None));
        Assert.That(summary.UnknownEffects, Is.EqualTo(SourceMayEffect.None));
        Assert.That(summary.MayDiverge, Is.True);
        Assert.That(summary.CompletionUnknown, Is.True);
    }

    [Test]
    public void AllocationPropagatesThroughAnEntireRecursiveComponentAndItsCaller()
    {
        var graph = Graph("""
            [ZeroAllocations] public static object Root(int x) => First(x);
            static object First(int x) => Second(x);
            static object Second(int x) { object allocated = new object(); return First(x); }
            """);
        var summaries = EffectSummaryFixpoint.ComputeValidated(graph);
        Assert.That(summaries, Has.Count.EqualTo(3));
        Assert.That(summaries.Values.All(summary => summary.MayEffects.HasFlag(SourceMayEffect.Allocation) &&
            summary.UnknownEffects == SourceMayEffect.None && summary.MayDiverge), Is.True);
        var expected = summaries.ToArray();
        graph.Bodies = graph.Bodies.Reverse().ToArray();
        foreach (var body in graph.Bodies)
        { body.Callees = body.Callees.Reverse().ToArray(); }
        Assert.That(EffectSummaryFixpoint.ComputeValidated(graph).ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void UnknownExternalBoundaryPropagatesThroughRecursion()
    {
        var graph = Graph("""
            [ZeroAllocations] public static int Root(int x) => First(x);
            static int First(int x) => Second(x);
            static int Second(int x) => First(System.Math.Abs(x));
            """);
        Assert.That(EffectSummaryFixpoint.ComputeValidated(graph).Values.All(summary =>
            summary.UnknownEffects == SourceMayEffect.All && summary.UnknownExceptions && summary.MayDiverge), Is.True);
    }

    [Test]
    public void CaughtCalleeFaultsStayConservativeAndRetainExceptionAllocation()
    {
        var graph = Graph("""
            [ZeroAllocations] public static int Root(int x) {
                try { return Divide(x); } catch (System.DivideByZeroException) { return 0; }
            }
            static int Divide(int x) => 10 / x;
            """);
        var root = graph.Roots.Single().BodyId;
        var summary = EffectSummaryFixpoint.ComputeValidated(graph)[root];
        Assert.That(summary.ExceptionKinds & (1 << (int)IrExceptionKind.DivideByZero), Is.Not.Zero);
        Assert.That(summary.MayEffects.HasFlag(SourceMayEffect.Allocation), Is.True);
        Assert.That(summary.CompletionUnknown, Is.True);
    }

    [Test]
    public void IncompleteEntryInitializationCannotBecomeEffectFree()
    {
        var graph = Graph("""
            static readonly object State = new object();
            [ZeroAllocations] public static int Root() => 1;
            """);
        Assert.That(graph.Bodies.Single().EffectsCompleteAtEntry, Is.False);
        Assert.That(EffectSummaryFixpoint.ComputeValidated(graph).Values.Single().UnknownEffects,
            Is.EqualTo(SourceMayEffect.All));
    }

    [Test]
    public void LocalLoopsRemainPossiblyDivergent()
    {
        var graph = Graph("[ZeroAllocations] public static int Root(int x) { while (x > 0) { x--; } return x; }");
        var summary = EffectSummaryFixpoint.ComputeValidated(graph).Values.Single();
        Assert.That(summary.MayDiverge, Is.True);
        Assert.That(summary.CompletionUnknown, Is.True);
    }

    [TestCase("static int State; [ZeroAllocations] public static int Root(int x) { State = x; return x; }", (int)SourceMayEffect.NonlocalWrite)]
    [TestCase("[ZeroAllocations] public static int Root(object gate) { lock (gate) {} return 1; }", (int)SourceMayEffect.Synchronization)]
    public void LocalNonlocalWritesAndLocksRetainTheirEffectFacet(string members, int expected)
    {
        var summary = EffectSummaryFixpoint.ComputeValidated(Graph(members)).Values.Single();
        Assert.That(summary.MayEffects.HasFlag((SourceMayEffect)expected), Is.True);
        Assert.That(summary.UnknownEffects, Is.EqualTo(SourceMayEffect.None));
    }

    [Test]
    public void LocalAssignmentsDoNotBecomeNonlocalWrites()
    {
        var summary = EffectSummaryFixpoint.ComputeValidated(
            Graph("[ZeroAllocations] public static int Root(int x) { x++; return x; }")).Values.Single();
        Assert.That(summary.MayEffects.HasFlag(SourceMayEffect.NonlocalWrite), Is.False);
        Assert.That(summary.UnknownEffects, Is.EqualTo(SourceMayEffect.None));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MaximumDepthGraphsUseAnIterativeBoundedTraversal(bool cycle)
    {
        var leaf = Graph("[ZeroAllocations] public static int Root() => 1;").Bodies.Single();
        // Exercise the algorithm's graph bound independently of compilation;
        // the same already validated, effect-free local IR is reused.
        var bodies = Enumerable.Range(0, 4096).Select(index => new CompilerSourceBodyArtifact
        {
            BodyId = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CallsComplete = true,
            EffectsCompleteAtEntry = true,
            Graph = leaf.Graph,
            Callees = index < 4095 ? [(index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)] : cycle ? ["0"] : []
        }).ToArray();
        var summaries = EffectSummaryFixpoint.ComputeValidated(new CompilerReachableSourceArtifact { Bodies = bodies });
        Assert.That(summaries, Has.Count.EqualTo(4096));
        Assert.That(summaries.Values.All(summary => summary.MayEffects == SourceMayEffect.None && summary.MayDiverge == cycle), Is.True);
    }

    [Test]
    public void CancelledSummaryConstructionStopsImmediately()
    {
        var graph = Graph("[ZeroAllocations] public static int Root() => 1;");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() => EffectSummaryFixpoint.ComputeValidated(graph, cancelled.Token)));
    }

    private static CompilerReachableSourceArtifact Graph(string members)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; static class Subject { " + members + " }");
        return CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _).ReachableSource!;
    }
}
