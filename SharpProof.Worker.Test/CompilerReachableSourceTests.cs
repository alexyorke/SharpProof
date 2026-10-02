using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerReachableSourceTests
{
    private const string SharedSource = """
        using SharpProof.Attributes;
        static class Subject {
            [ZeroAllocations] public static int First(int x) => Shared(x);
            [DoesNotThrow] public static int Second(int x) => Shared(x);
            static int Shared(int x) => x + 1;
            static object Unrelated() => new object();
        }
        """;

    [Test]
    public void ClaimedRootsShareOneLazilyLoweredLeaf()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(SharedSource);
        var graph = artifact.ReachableSource!;
        Assert.That(graph.CollectionComplete, Is.True);
        Assert.That(graph.Roots, Has.Length.EqualTo(2));
        Assert.That(graph.Bodies, Has.Length.EqualTo(3));
        var shared = graph.Bodies.Single(body => body.MethodIdentity.Contains("Shared", StringComparison.Ordinal));
        Assert.That(shared.Graph, Is.Not.Null);
        Assert.That(PortableIrGraphCodec.Decode(shared.Graph!).Program, Is.Not.Null);
        Assert.That(graph.Bodies.Where(body => body.BodyId != shared.BodyId).All(body =>
            body.Callees.SequenceEqual(new[] { shared.BodyId }) && body.Graph == null), Is.True);
        var roundTrip = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
        Assert.That(roundTrip.ReachableSource!.Bodies.Select(body => body.BodyId),
            Is.EqualTo(graph.Bodies.Select(body => body.BodyId)));
    }

    [Test]
    public void RecursiveGraphTerminatesWithoutInventingBodiesOrCompletion()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            static class Subject {
                [ZeroAllocations] public static int First(int x) => Second(x);
                static int Second(int x) => First(x);
            }
            """);
        var graph = artifact.ReachableSource!;
        Assert.That(graph.CollectionComplete, Is.True);
        Assert.That(graph.Bodies, Has.Length.EqualTo(2));
        Assert.That(graph.Bodies.All(body => body.Graph == null && body.Callees.Length == 1 &&
            body.Callees[0] != body.BodyId), Is.True);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
    }

    [Test]
    public void UnrelatedConstantChangesPreserveTheSelectedSemanticArtifact()
    {
        var source = GoldenTest.Load("lowering", "total-unused-constant-stability").Source;
        var first = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var changed = CompilerTotalCallableArtifactTests.CreateArtifact(
            source.Replace("\"first\"", "\"considerably-longer\"", StringComparison.Ordinal));
        Assert.That(CompilerManifestArtifactJson.SerializeProducerValidated(changed),
            Is.EqualTo(CompilerManifestArtifactJson.SerializeProducerValidated(first)));
    }

    [Test]
    public void ElidedCallArgumentsAndDeferredBodiesDoNotAddExecutedCallEdges()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            static class Subject {
                [ZeroAllocations] public static int Root(int x) {
                    Contract.Ensures(Contract.Result<int>() == Helper(x));
                    System.Func<int> deferred = () => Helper(x);
                    return x;
                }
                static int Helper(int x) => x;
            }
            """);
        Assert.That(artifact.ReachableSource!.Bodies, Has.Length.EqualTo(1));
        Assert.That(artifact.ReachableSource.Bodies[0].Callees, Is.Empty);
        Assert.That(artifact.ReachableSource.Bodies[0].Graph, Is.Null);
    }

    [Test]
    public void InvokedLocalFunctionRetainsItsOwnCalls()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            static class Subject {
                [ZeroAllocations] public static int Root() {
                    return Local();
                    int Local() => Helper();
                }
                static int Helper() => 1;
            }
            """);
        var graph = artifact.ReachableSource!;
        Assert.That(graph.CollectionComplete, Is.True);
        Assert.That(graph.Bodies, Has.Length.EqualTo(3));
        Assert.That(graph.Bodies.Single(body => body.MethodIdentity.Contains("Local", StringComparison.Ordinal)).Callees,
            Is.EqualTo(new[] { graph.Bodies.Single(body => body.MethodIdentity.Contains("Helper", StringComparison.Ordinal)).BodyId }));
    }

    [TestCase("public static int Root() => P; static int P => 1;")]
    [TestCase("public static object Root() => new Value(); class Value { }")]
    [TestCase("public static int Root(Value x) => (int)x; public struct Value { public static explicit operator int(Value x) => 1; }")]
    public void UncollectedImplicitSourceCallsStayIncomplete(string members)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact($$"""
            using SharpProof.Attributes;
            static class Subject { [ZeroAllocations] {{members}} }
            """);
        Assert.That(artifact.ReachableSource!.CollectionComplete, Is.False);
        Assert.That(artifact.ReachableSource.Bodies[0].CallsComplete, Is.False);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
    }

    [TestCase("missing")]
    [TestCase("duplicate")]
    [TestCase("identity")]
    [TestCase("root")]
    [TestCase("edge")]
    [TestCase("graph")]
    [TestCase("location")]
    [TestCase("document")]
    [TestCase("unreachable")]
    public void MalformedReachableGraphIsRejected(string mutation)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(SharedSource);
        var graph = artifact.ReachableSource!;
        var leaf = graph.Bodies.Single(body => body.Graph != null);
        switch (mutation)
        {
            case "missing":
                graph.Bodies = graph.Bodies.Where(body => body != leaf).ToArray();
                break;
            case "duplicate":
                graph.Bodies = [.. graph.Bodies, leaf];
                break;
            case "identity":
                leaf.BodyId = "wrong";
                break;
            case "root":
                graph.Roots[0].CallableId = "wrong";
                break;
            case "edge":
                leaf.Callees = ["wrong"];
                break;
            case "graph":
                leaf.Graph!.Semantics = IrExecutionSemantics.Legacy;
                break;
            case "location":
                leaf.Graph!.Operations.First(operation => operation.SourceSpan != null).SourceSpan!.Document = "wrong.cs";
                break;
            case "document":
                graph.Documents[0].MaximumBodyEnd = 0;
                break;
            case "unreachable":
                foreach (var body in graph.Bodies)
                { body.Callees = []; }
                break;
        }
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _)));
    }
}
