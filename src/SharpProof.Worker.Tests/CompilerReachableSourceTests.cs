using System.Collections.Immutable;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerReachableSourceTests
{
    [TestCase("int Root(int x) => System.Math.Abs(x);", false, true)]
    [TestCase("int[] Root() => System.Array.Empty<int>();", true, false)]
    [TestCase("string Root(string x) => string.Concat(x, \"x\");", false, false)]
    [TestCase("string Root(string x, string y) => x + y;", false, false)]
    [TestCase("int[] Root(int n) => new int[n];", false, false)]
    public void ApprovedScalarApiModelsRetainTheirRuntimeEffectBoundaries(string method, bool cacheAllocation, bool exceptionResources)
    {
        System.ArgumentNullException.ThrowIfNull(method);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; static class Subject { [ZeroAllocations] public static " + method + " }");
        var body = artifact.ReachableSource!.Bodies.Single();
        Assert.That(body.Graph, Is.Not.Null);
        Assert.That(body.IsCallSkeleton, Is.False);
        Assert.That(body.SourceCalls, Is.Empty);
        var decoded = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken).ReachableSource!;
        var summary = EffectSummaryFixpoint.ComputeValidated(decoded, cancellationToken: TestContext.CurrentContext.CancellationToken).Values.Single();
        Assert.That(summary.UnknownEffects,
            Is.EqualTo(exceptionResources ? SourceMayEffect.All : cacheAllocation ? SourceMayEffect.Allocation : SourceMayEffect.None));
        if (cacheAllocation)
        {
            Assert.That(summary.MayEffects, Is.EqualTo(SourceMayEffect.None));
            Assert.That(summary.UnknownExceptions, Is.False);
            Assert.That(summary.MayDiverge, Is.False);
        }
        if (exceptionResources)
        {
            // Math.Abs is exact on normal paths, but its overflow message can
            // invoke arbitrary culture callbacks. A whole-body may summary
            // includes that guarded resource boundary.
            Assert.That(summary.UnknownExceptions, Is.True);
            Assert.That(summary.MayDiverge, Is.True);
            Assert.That(summary.ExceptionKinds & (1 << (int)IrExceptionKind.Overflow), Is.Not.Zero);
        }
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
        var body = artifact.ReachableSource!.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.IsCallSkeleton));
        Assert.That(body.SourceCalls, Has.Length.EqualTo(1));
        var decoded = PortableIrGraphCodec.Decode(body.Graph!, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(decoded.Instructions.OfType<IrAllocationInstruction>(), Is.Empty);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken);
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
        var root = artifact.ReachableSource!.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.IsCallSkeleton));
        Assert.That(root.SourceCalls, Has.Length.EqualTo(1));
        Assert.That(PortableIrGraphCodec.Decode(root.Graph!, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken).Instructions.OfType<IrThrowInstruction>(), Is.Empty);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken);
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
        var shared = graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.MethodIdentity.Contains("Shared", StringComparison.Ordinal)));
        Assert.That(shared.Graph, Is.Not.Null);
        Assert.That(PortableIrGraphCodec.Decode(shared.Graph!, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken).Program, Is.Not.Null);
        Assert.That(graph.Bodies.Where(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.BodyId != shared.BodyId)).All(body =>
            body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Callees.SequenceEqual(ImmutableArray.Create(shared.BodyId)) && body.Graph != null && body.IsCallSkeleton &&
            body.SourceCalls.Single().CalleeBodyId == shared.BodyId)), Is.True);
        var roundTrip = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(roundTrip.ReachableSource!.Bodies.Select(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.BodyId)),
            Is.EqualTo(graph.Bodies.Select(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.BodyId))));
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
        Assert.That(graph.Bodies.All(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null && body.IsCallSkeleton && body.SourceCalls.Length == 1 && body.Callees.Length == 1 &&
            body.Callees[0] != body.BodyId)), Is.True);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public void UnrelatedConstantChangesPreserveTheSelectedSemanticArtifact()
    {
        var source = GoldenTest.Load("lowering", "total-unused-constant-stability").Source;
        var first = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var changed = CompilerTotalCallableArtifactTests.CreateArtifact(
            source.Replace("\"first\"", "\"considerably-longer\"", StringComparison.Ordinal));
        Assert.That(JsonSerializer.Serialize(changed.Manifest, WorkerProtocolJson.SharedOptions),
            Is.EqualTo(JsonSerializer.Serialize(first.Manifest, WorkerProtocolJson.SharedOptions)));
        Assert.That(JsonSerializer.Serialize(changed.Callables, WorkerProtocolJson.SharedOptions),
            Is.EqualTo(JsonSerializer.Serialize(first.Callables, WorkerProtocolJson.SharedOptions)));
        Assert.That(JsonSerializer.Serialize(changed.ReachableSource, WorkerProtocolJson.SharedOptions),
            Is.EqualTo(JsonSerializer.Serialize(first.ReachableSource, WorkerProtocolJson.SharedOptions)));
        var originalSnapshot = first.Compilation.SyntaxTrees.Single();
        var changedSnapshot = changed.Compilation.SyntaxTrees.Single();
        Assert.That(changedSnapshot.Path, Is.EqualTo(originalSnapshot.Path));
        Assert.That(changedSnapshot.Sha256, Is.Not.EqualTo(originalSnapshot.Sha256));
        Assert.That(changedSnapshot.TextLength, Is.GreaterThan(originalSnapshot.TextLength));
        var roundTrip = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(changed, TestContext.CurrentContext.CancellationToken),
            out _, TestContext.CurrentContext.CancellationToken);
        Assert.That(roundTrip.Compilation.SyntaxTrees.Single().Sha256, Is.EqualTo(changedSnapshot.Sha256));
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
        Assert.That(graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.MethodIdentity.Contains("Local", StringComparison.Ordinal))).Callees,
            Is.EqualTo(ImmutableArray.Create(graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.MethodIdentity.Contains("Helper", StringComparison.Ordinal))).BodyId)));
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
    [TestCase("public static int Root(Value value) { value += 1; return 0; } public struct Value { public static implicit operator int(Value value) => 1; public static implicit operator Value(int value) => default; }")]
    [TestCase("public static int Root(Value value) => value[^1]; public class Value { public int Length => 2; public int this[int index] => index; }")]
    [TestCase("public static Value Root(Value value) => value[1..^1]; public class Value { public int Length => 3; public Value Slice(int start,int length) => this; }")]
    public void UncollectedImplicitSourceCallsStayIncomplete(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact($$"""
            using SharpProof.Attributes;
            static class Subject { [ZeroAllocations] {{members}} }
            """);
        Assert.That(artifact.ReachableSource!.CollectionComplete, Is.False);
        Assert.That(artifact.ReachableSource.Bodies[0].CallsComplete, Is.False);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken);
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
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public void CollectionBuilderStaysAnIncompleteCallBoundary()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using SharpProof.Attributes;
            [CollectionBuilder(typeof(Builder), nameof(Builder.Create))]
            public sealed class Bag : IEnumerable<int> {
                public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            public static class Builder {
                public static Bag Create(ReadOnlySpan<int> values) => new Bag();
            }
            public static class Subject {
                [ZeroAllocations] public static int Root() {
                    Bag values = [1];
                    return 0;
                }
            }
            """);
        Assert.That(artifact.ReachableSource!.CollectionComplete, Is.False);
        Assert.That(artifact.ReachableSource.Bodies[0].CallsComplete, Is.False);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken);
    }

    [TestCase("public static int Root((int,int) value) { var (a,b) = value; return a+b; }")]
    [TestCase("public static int Root((int,(int,int)) value) { var (a,(b,c)) = value; return a+b+c; }")]
    [TestCase("public static int Root((int,int) value) => value is (1,2) ? 1 : 0;")]
    [TestCase("public static int Root(int value) { value++; return value; }")]
    [TestCase("public static int Root(int[] values) => values[^1];")]
    [TestCase("public static int[] Root(int[] values) => values[1..^1];")]
    [TestCase("public static string Root(dynamic value) => nameof(value.P);")]
    [TestCase("public static string Root() => nameof(P); static int P => 1;")]
    public void OperationsWithoutHiddenCallsKeepSourceCollectionComplete(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
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
        System.ArgumentNullException.ThrowIfNull(members);
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
        System.ArgumentNullException.ThrowIfNull(mutation);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(SharedSource);
        var graph = artifact.ReachableSource!;
        var leaf = graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null && !body.IsCallSkeleton));
        switch (mutation)
        {
            case "missing":
                {
                    graph = graph with { Bodies = graph.Bodies.Where(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body != leaf)).ToImmutableArray() };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "duplicate":
                {
                    graph = graph with { Bodies = [.. graph.Bodies, leaf] };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "identity":
                {
                    leaf = leaf with { BodyId = "wrong" };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null && !body.IsCallSkeleton)), leaf) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "root":
                {
                    graph = graph with { Roots = graph.Roots.SetItem(0, graph.Roots[0] with { CallableId = "wrong" }) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "root-swap":
                {
                    var changedFixture = (graph.Roots[1].BodyId, graph.Roots[0].BodyId);
                    graph = graph with { Roots = graph.Roots.SetItem(0, graph.Roots[0] with { BodyId = changedFixture.Item1 }) };
                    artifact = artifact with { ReachableSource = graph };
                    graph = graph with { Roots = graph.Roots.SetItem(1, graph.Roots[1] with { BodyId = changedFixture.Item2 }) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "edge":
                {
                    leaf = leaf with { Callees = ["wrong"] };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null && !body.IsCallSkeleton)), leaf) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "graph":
                {
                    leaf = leaf with { Graph = leaf.Graph! with { Semantics = IrExecutionSemantics.Legacy } };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null && !body.IsCallSkeleton)), leaf) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "location":
                {
                    leaf = leaf with { Graph = leaf.Graph!.ReplaceRow(leaf.Graph!.Operations.First(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.SourceSpan != null)).SourceSpan!, leaf.Graph!.Operations.First(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.SourceSpan != null)).SourceSpan! with { Document = "wrong.cs" }) };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null && !body.IsCallSkeleton)), leaf) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "document":
                {
                    graph = graph with { Documents = graph.Documents.SetItem(0, graph.Documents[0] with { MaximumBodyEnd = 0 }) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "unreachable":
                foreach (var body in graph.Bodies)
                {
                    {
                        {
                            graph = graph with { Bodies = graph.Bodies.ReplaceReference(body, body with { Callees = [] }) };
                            artifact = artifact with { ReachableSource = graph };
                        }
                    }
                }
                break;
        }
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken)));
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
        System.ArgumentNullException.ThrowIfNull(mutation);
        var source = mutation == "duplicate" ? SharedSource.Replace("=> Shared(x);", "=> Shared(x) + Shared(x);", StringComparison.Ordinal) : SharedSource;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var graph = artifact.ReachableSource!;
        var caller = graph.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.IsCallSkeleton));
        var callee = graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.BodyId == caller.SourceCalls[0].CalleeBodyId));
        switch (mutation)
        {
            case "missing":
                {
                    caller = caller with { SourceCalls = [] };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.IsCallSkeleton)), caller) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "duplicate":
                {
                    caller = caller with { SourceCalls = [caller.SourceCalls[0], caller.SourceCalls[0]] };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.IsCallSkeleton)), caller) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "index":
                {
                    caller = caller with { SourceCalls = caller.SourceCalls.SetItem(0, caller.SourceCalls[0] with { InstructionIndex = 0 }) };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.IsCallSkeleton)), caller) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "callee":
                {
                    caller = caller with { SourceCalls = caller.SourceCalls.SetItem(0, caller.SourceCalls[0] with { CalleeBodyId = caller.BodyId }) };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.IsCallSkeleton)), caller) };
                    artifact = artifact with { ReachableSource = graph };
                }
                {
                    caller = caller with { Callees = [caller.BodyId] };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.IsCallSkeleton)), caller) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "signature":
                {
                    callee = callee with { ParameterTypes = callee.ParameterTypes.SetItem(0, "wrong") };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.BodyId == caller.SourceCalls[0].CalleeBodyId)), callee) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "return":
                {
                    callee = callee with { ReturnType = null };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.BodyId == caller.SourceCalls[0].CalleeBodyId)), callee) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "static":
                {
                    callee = callee with { IsStatic = false };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.BodyId == caller.SourceCalls[0].CalleeBodyId)), callee) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "identity":
                {
                    callee = callee with { CallIdentity = "wrong" };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.Single(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.BodyId == caller.SourceCalls[0].CalleeBodyId)), callee) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
            case "marker":
                {
                    caller = caller with { IsCallSkeleton = false };
                    graph = graph with { Bodies = graph.Bodies.ReplaceReference(graph.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.IsCallSkeleton)), caller) };
                    artifact = artifact with { ReachableSource = graph };
                }
                break;
        }
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken)));
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
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out _, cancellationToken: TestContext.CurrentContext.CancellationToken);
    }
}
