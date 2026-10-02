using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerReachableSourceTests
{
    [TestCase("int Root(int x) => System.Math.Abs(x);")]
    [TestCase("int[] Root() => System.Array.Empty<int>();")]
    [TestCase("string Root(string x) => string.Concat(x, \"x\");")]
    public void ApprovedScalarApiModelsRemainExactInShadowBodies(string method)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; static class Subject { [ZeroAllocations] public static " + method + " }");
        var body = artifact.ReachableSource!.Bodies.Single();
        Assert.That(body.Graph, Is.Not.Null);
        Assert.That(body.IsCallSkeleton, Is.False);
        Assert.That(body.SourceCalls, Is.Empty);
        var decoded = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _).ReachableSource!;
        Assert.That(EffectSummaryFixpoint.ComputeValidated(decoded).Values.Single().UnknownEffects,
            Is.EqualTo(SourceMayEffect.None));
    }

    [Test]
    public void EmptyParamsUseTheApprovedArrayEmptyModelBeforeThePreservedCall()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            static class Subject {
                [ZeroAllocations] public static int Root() => Count();
                static int Count(params int[] values) => values.Length;
            }
            """);
        var body = artifact.ReachableSource!.Bodies.Single(body => body.IsCallSkeleton);
        Assert.That(body.SourceCalls, Has.Length.EqualTo(1));
        var decoded = PortableIrGraphCodec.Decode(body.Graph!);
        Assert.That(decoded.Instructions.OfType<IrAllocationInstruction>(), Is.Empty);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
    }

    [Test]
    public void SourceMethodsNamedLikeFrameworkApisArePreservedRatherThanUsingFrameworkModels()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            static class Subject {
                [ZeroAllocations] public static int Root(int x) => System.Math.Abs(x);
            }
            namespace System { static class Math { public static int Abs(int x) => 7; } }
            """);
        var root = artifact.ReachableSource!.Bodies.Single(body => body.IsCallSkeleton);
        Assert.That(root.SourceCalls, Has.Length.EqualTo(1));
        Assert.That(PortableIrGraphCodec.Decode(root.Graph!).Instructions.OfType<IrThrowInstruction>(), Is.Empty);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
    }

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
            body.Callees.SequenceEqual(new[] { shared.BodyId }) && body.Graph != null && body.IsCallSkeleton &&
            body.SourceCalls.Single().CalleeBodyId == shared.BodyId), Is.True);
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
        Assert.That(graph.Bodies.All(body => body.Graph != null && body.IsCallSkeleton && body.SourceCalls.Length == 1 && body.Callees.Length == 1 &&
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
    [TestCase("public static int Root(System.IDisposable value) { using var resource = value; return 1; }")]
    [TestCase("public static int Root(System.IDisposable value) { using (value) { return 1; } }")]
    [TestCase("public static dynamic Root(dynamic value) => value.P;")]
    [TestCase("public static dynamic Root(dynamic value) => value[0];")]
    [TestCase("public static int Root(Value value) { var (a,b) = value; return a+b; } public class Value { public void Deconstruct(out int a, out int b) { a=1; b=2; } }")]
    [TestCase("public static int Root((Value,int) value) { var ((a,b),c) = value; return a+b+c; } public class Value { public void Deconstruct(out int a, out int b) { a=1; b=2; } }")]
    [TestCase("public static int Root((Value,int) value) { (int a,int b) = value; return a+b; } public struct Value { public static implicit operator int(Value value) => 1; }")]
    [TestCase("public static int Root(((Value,int),int) value) { ((int a,int b),int c) = value; return a+b+c; } public struct Value { public static implicit operator int(Value value) => 1; }")]
    [TestCase("public static int Root(Value value) => value is (1,2) ? 1 : 0; public class Value { public void Deconstruct(out int a, out int b) { a=1; b=2; } }")]
    [TestCase("public static int Root(object value) => value is (1,2) ? 1 : 0;")]
    [TestCase("public static object Root(Value value) => value with { }; public record Value(int A, int B);")]
    [TestCase("public static int Root(Value value) { value++; return 0; } public struct Value { public static implicit operator int(Value value) => 1; public static implicit operator Value(int value) => default; }")]
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

    [Test]
    public void ExtensionDeconstructionStaysAnIncompleteCallBoundary()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public class Value { }
            public static class Extensions {
                public static void Deconstruct(this Value value, out int a, out int b) { a=1; b=2; }
            }
            static class Subject {
                [ZeroAllocations] public static int Root(Value value) { var (a,b) = value; return a+b; }
            }
            """);
        Assert.That(artifact.ReachableSource!.CollectionComplete, Is.False);
        Assert.That(artifact.ReachableSource.Bodies[0].CallsComplete, Is.False);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
    }

    [TestCase("public static int Root((int,int) value) { var (a,b) = value; return a+b; }")]
    [TestCase("public static int Root((int,(int,int)) value) { var (a,(b,c)) = value; return a+b+c; }")]
    [TestCase("public static int Root((int,int) value) => value is (1,2) ? 1 : 0;")]
    [TestCase("public static int Root(int value) { value++; return value; }")]
    [TestCase("public static string Root(dynamic value) => nameof(value.P);")]
    [TestCase("public static string Root() => nameof(P); static int P => 1;")]
    public void OperationsWithoutHiddenCallsKeepSourceCollectionComplete(string members)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact($$"""
            using SharpProof.Attributes;
            static class Subject { [ZeroAllocations] {{members}} }
            """);
        Assert.That(artifact.ReachableSource!.CollectionComplete, Is.True);
        Assert.That(artifact.ReachableSource.Bodies[0].CallsComplete, Is.True);
    }

    [TestCase("public static int Root(System.Func<int> callback) => callback();")]
    [TestCase("public static int Root(object value) => value.GetHashCode(); " +
        "class Value { public override int GetHashCode() => Helper(); } static int Helper() => 1;")]
    public void UnknownDispatchCannotReportCompleteSourceCollection(string members)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact($$"""
            using SharpProof.Attributes;
            static class Subject { [ZeroAllocations] {{members}} }
            """);
        Assert.That(artifact.ReachableSource!.CollectionComplete, Is.False);
        Assert.That(artifact.ReachableSource.Bodies[0].CallsComplete, Is.False);
    }

    [TestCase("missing")]
    [TestCase("duplicate")]
    [TestCase("identity")]
    [TestCase("root")]
    [TestCase("root-swap")]
    [TestCase("edge")]
    [TestCase("graph")]
    [TestCase("location")]
    [TestCase("document")]
    [TestCase("unreachable")]
    public void MalformedReachableGraphIsRejected(string mutation)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(SharedSource);
        var graph = artifact.ReachableSource!;
        var leaf = graph.Bodies.Single(body => body.Graph != null && !body.IsCallSkeleton);
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
            case "root-swap":
                (graph.Roots[0].BodyId, graph.Roots[1].BodyId) =
                    (graph.Roots[1].BodyId, graph.Roots[0].BodyId);
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

    [TestCase("missing")]
    [TestCase("duplicate")]
    [TestCase("index")]
    [TestCase("callee")]
    [TestCase("signature")]
    [TestCase("return")]
    [TestCase("static")]
    [TestCase("identity")]
    [TestCase("marker")]
    public void MalformedSourceCallMappingIsRejected(string mutation)
    {
        var source = mutation == "duplicate" ? SharedSource.Replace("=> Shared(x);", "=> Shared(x) + Shared(x);", StringComparison.Ordinal) : SharedSource;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var graph = artifact.ReachableSource!;
        var caller = graph.Bodies.First(body => body.IsCallSkeleton);
        var callee = graph.Bodies.Single(body => body.BodyId == caller.SourceCalls[0].CalleeBodyId);
        switch (mutation)
        {
            case "missing":
                caller.SourceCalls = [];
                break;
            case "duplicate":
                caller.SourceCalls = [caller.SourceCalls[0], caller.SourceCalls[0]];
                break;
            case "index":
                caller.SourceCalls[0].InstructionIndex = 0;
                break;
            case "callee":
                caller.SourceCalls[0].CalleeBodyId = caller.BodyId;
                caller.Callees = [caller.BodyId];
                break;
            case "signature":
                callee.ParameterTypes[0] = "wrong";
                break;
            case "return":
                callee.ReturnType = null;
                break;
            case "static":
                callee.IsStatic = false;
                break;
            case "identity":
                callee.CallIdentity = "wrong";
                break;
            case "marker":
                caller.IsCallSkeleton = false;
                break;
        }
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _)));
    }

    [Test]
    public void StaticallyOmittedCallsDoNotLeaveUnreachableBodies()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            static class Subject {
                [ZeroAllocations] public static int Root() { return 1; return Helper(); }
                static int Helper() => 2;
            }
            """);
        Assert.That(artifact.ReachableSource!.Bodies, Has.Length.EqualTo(1));
        Assert.That(artifact.ReachableSource.Bodies[0].SourceCalls, Is.Empty);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
    }
}
