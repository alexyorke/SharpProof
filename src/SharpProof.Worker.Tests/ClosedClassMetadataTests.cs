using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ClosedClassMetadataTests
{
    private const string Source = "using SharpProof.Attributes; public class A { } public class B : A { } public class Other { } " +
        "public static class C { public static bool Target(A a, B b, Other other) { Contract.Ensures(Contract.Result<bool>()); return object.ReferenceEquals(a, b); } }";

    [Test]
    public void ClassAncestrySurvivesCanonicalRoundTrip()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source);
        var total = artifact.Callables.Single().Total!;
        var graph = total.Graph;
        var types = graph.Types;
        var derived = graph.Variables[total.Parameters[1].Entry].Type;
        var parent = graph.Variables[total.Parameters[0].Entry].Type;
        Assert.That(derived, Is.GreaterThanOrEqualTo(0));
        Assert.That(parent, Is.GreaterThanOrEqualTo(0));
        Assert.That(types[derived].ClosedClassBase, Is.EqualTo(parent));
        Assert.That(types[parent].ClosedClassBase, Is.EqualTo(-1));
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, TestContext.CurrentContext.CancellationToken);
        var encoded = CompilerTotalCallableArtifactCodec.Encode(preparations.Single().Total)!;
        Assert.That(encoded.Graph.Types.Select(row => row is null ? throw new ArgumentNullException(nameof(row)) : row.ClosedClassBase), Is.EqualTo(types.Select(row => row is null ? throw new ArgumentNullException(nameof(row)) : row.ClosedClassBase)));
    }

    [TestCase("cycle")]
    [TestCase("missing-parent")]
    [TestCase("negative")]
    [TestCase("invalid-index")]
    [TestCase("scalar-parent")]
    [TestCase("object-parent")]
    [TestCase("scalar-class")]
    public void MalformedClassAncestryCannotBePrepared(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source);
        var total = artifact.Callables.Single().Total!;
        var graph = total.Graph;
        var derived = graph.Variables[total.Parameters[1].Entry].Type;
        var parent = graph.Types[derived].ClosedClassBase!.Value;
        var types = graph.Types.ToBuilder();
        switch (mutation)
        {
            case "cycle":
                types[derived] = types[derived] with { ClosedClassBase = derived };
                break;
            case "missing-parent":
                types[parent] = types[parent] with { ClosedClassBase = null };
                break;
            case "negative":
                types[derived] = types[derived] with { ClosedClassBase = -2 };
                break;
            case "invalid-index":
                types[derived] = types[derived] with { ClosedClassBase = graph.Types.Length };
                break;
            case "scalar-parent":
                types[derived] = types[derived] with { ClosedClassBase = 0 };
                break;
            case "object-parent":
                types[derived] = types[derived] with { ClosedClassBase = 3 };
                break;
            case "scalar-class":
                types[0] = types[0] with { ClosedClassBase = -1 };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = total with { Graph = graph with { Types = types.ToImmutable() } } }) };
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _, TestContext.CurrentContext.CancellationToken)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SourceBoundDigestRejectsOmittedOrChangedClassMetadata(bool omit)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source);
        var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken));
        var expected = SharpProof.Worker.Protocol.WorkerProtocolJson.ComputeSha256((bytes).ToImmutableArray());
        var graph = artifact.Callables.Single().Total!.Graph;
        var types = graph.Types.ToBuilder();
        for (var index = 0; index < types.Count; index++)
        {
            var row = types[index];
            ArgumentNullException.ThrowIfNull(row);
            if (row.ClosedClassBase.HasValue)
            { types[index] = row with { ClosedClassBase = omit ? null : -1 }; }
        }
        artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = artifact.Callables.Single().Total! with { Graph = graph with { Types = types.ToImmutable() } } }) };
        var changed = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken));
        Assert.Throws<InvalidDataException>((Action)(() => ArtifactValidator.Decode((changed).ToImmutableArray(), expected, TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public async Task LegacyMetadataRemainsReadableButCannotCertifyAnImpossibleFieldAlias()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; public class A { } public class B { } public class Holder { public A First; public B Second; } " +
            "public static class C { public static bool Target(Holder h) { Contract.Requires(h != null && h.First != null && h.Second != null); " +
            "Contract.Ensures(Contract.Result<bool>()); return !object.ReferenceEquals(h.First, h.Second); } }");
        var callables = artifact.Callables.ToBuilder();
        for (var index = 0; index < callables.Count; index++)
        {
            var callable = callables[index];
            if (callable.Total is { } total)
            {
                callables[index] = callable with { Total = total with { Graph = total.Graph with { Types = total.Graph.Types.Select(type => type is null ? throw new ArgumentNullException(nameof(type)) : type with { ClosedClassBase = null }).ToImmutableArray() } } };
            }
        }
        artifact = artifact with { Callables = callables.ToImmutable() };
        using var project = new ShadowTestProject(artifact);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(SharpProof.Worker.Protocol.WorkerClaimOutcome.Unknown));
    }

    [TestCase("missing-interface")]
    [TestCase("scalar-interface")]
    [TestCase("class-interface")]
    [TestCase("self-cycle")]
    [TestCase("negative-index")]
    [TestCase("duplicate")]
    [TestCase("empty")]
    [TestCase("oversized")]
    [TestCase("interface-base")]
    public void MalformedInterfaceEvidenceCannotBePrepared(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; public interface A { } public interface B { } public class Impl : A, B { } " +
            "public static class C { public static bool Target(A a, Impl b) { Contract.Ensures(Contract.Result<bool>()); return object.ReferenceEquals(a, b); } }");
        var total = artifact.Callables.Single().Total!;
        var graph = total.Graph;
        var face = graph.Variables[total.Parameters[0].Entry].Type;
        var implementation = graph.Variables[total.Parameters[1].Entry].Type;
        Assert.That(graph.Types[face].InterfaceReference, Is.True);
        Assert.That(graph.Types[implementation].ImplementedInterfaces!.Value, Has.Length.EqualTo(2));
        var types = graph.Types.ToBuilder();
        switch (mutation)
        {
            case "missing-interface":
                types[face] = types[face] with { InterfaceReference = false };
                break;
            case "scalar-interface":
                types[0] = types[0] with { InterfaceReference = true };
                break;
            case "class-interface":
                types[implementation] = types[implementation] with { InterfaceReference = true };
                break;
            case "self-cycle":
                types[face] = types[face] with { ImplementedInterfaces = ImmutableArray.Create(face) };
                break;
            case "negative-index":
                types[implementation] = types[implementation] with { ImplementedInterfaces = ImmutableArray.Create(-1) };
                break;
            case "duplicate":
                types[implementation] = types[implementation] with { ImplementedInterfaces = ImmutableArray.Create(face, face) };
                break;
            case "empty":
                types[implementation] = types[implementation] with { ImplementedInterfaces = ImmutableArray<int>.Empty };
                break;
            case "oversized":
                types[implementation] = types[implementation] with { ImplementedInterfaces = Enumerable.Repeat(face, 129).ToImmutableArray() };
                break;
            case "interface-base":
                types[implementation] = types[implementation] with { ClosedClassBase = face };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = total with { Graph = graph with { Types = types.ToImmutable() } } }) };
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _, TestContext.CurrentContext.CancellationToken)));
    }
}
