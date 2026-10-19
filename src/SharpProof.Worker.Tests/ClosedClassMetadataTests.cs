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
        switch (mutation)
        {
            case "cycle":
                graph.Types[derived].ClosedClassBase = derived;
                break;
            case "missing-parent":
                graph.Types[parent].ClosedClassBase = null;
                break;
            case "negative":
                graph.Types[derived].ClosedClassBase = -2;
                break;
            case "invalid-index":
                graph.Types[derived].ClosedClassBase = graph.Types.Length;
                break;
            case "scalar-parent":
                graph.Types[derived].ClosedClassBase = 0;
                break;
            case "object-parent":
                graph.Types[derived].ClosedClassBase = 3;
                break;
            case "scalar-class":
                graph.Types[0].ClosedClassBase = -1;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _, TestContext.CurrentContext.CancellationToken)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SourceBoundDigestRejectsOmittedOrChangedClassMetadata(bool omit)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source);
        var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken));
        var expected = SharpProof.Worker.Protocol.WorkerProtocolJson.ComputeSha256(bytes);
        var graph = artifact.Callables.Single().Total!.Graph;
        foreach (var row in graph.Types.Where(row => row is null ? throw new ArgumentNullException(nameof(row)) : row.ClosedClassBase.HasValue))
        { row.ClosedClassBase = omit ? null : -1; }
        var changed = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken));
        Assert.Throws<InvalidDataException>((Action)(() => ArtifactValidator.Decode(changed, expected, TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public async Task LegacyMetadataRemainsReadableButCannotCertifyAnImpossibleFieldAlias()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; public class A { } public class B { } public class Holder { public A First; public B Second; } " +
            "public static class C { public static bool Target(Holder h) { Contract.Requires(h != null && h.First != null && h.Second != null); " +
            "Contract.Ensures(Contract.Result<bool>()); return !object.ReferenceEquals(h.First, h.Second); } }");
        foreach (var callable in artifact.Callables)
        {
            if (callable.Total is { } total)
            {
                foreach (var type in total.Graph.Types)
                { type.ClosedClassBase = null; }
            }
        }
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
        Assert.That(graph.Types[implementation].ImplementedInterfaces, Has.Length.EqualTo(2));
        switch (mutation)
        {
            case "missing-interface":
                graph.Types[face].InterfaceReference = false;
                break;
            case "scalar-interface":
                graph.Types[0].InterfaceReference = true;
                break;
            case "class-interface":
                graph.Types[implementation].InterfaceReference = true;
                break;
            case "self-cycle":
                graph.Types[face].ImplementedInterfaces = [face];
                break;
            case "negative-index":
                graph.Types[implementation].ImplementedInterfaces = [-1];
                break;
            case "duplicate":
                graph.Types[implementation].ImplementedInterfaces = [face, face];
                break;
            case "empty":
                graph.Types[implementation].ImplementedInterfaces = [];
                break;
            case "oversized":
                graph.Types[implementation].ImplementedInterfaces = Enumerable.Repeat(face, 129).ToArray();
                break;
            case "interface-base":
                graph.Types[implementation].ClosedClassBase = face;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _, TestContext.CurrentContext.CancellationToken)));
    }
}
