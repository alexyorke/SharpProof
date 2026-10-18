using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ReachableGraphBoundaryControlTests
{
    private static CompilerManifestArtifact CreateArtifact()
    {
        return CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; static class Subject { [ZeroAllocations] public static int Target(int value) => Shared(value); static int Shared(int value) => value + 1; }");
    }

    [Test]
    public void ValidReachableBodiesRetainCanonicalPayload()
    {
        var artifact = CreateArtifact();
        var payload = CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var decoded = CompilerManifestArtifactJson.Deserialize(payload, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(decoded.ReachableSource!.Bodies, Has.Length.EqualTo(2));
        Assert.That(CompilerManifestArtifactJson.Serialize(decoded, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.EqualTo(payload));
    }

    [Test]
    public void CancellationPrecedesMalformedGraphRejection()
    {
        var artifact = CreateArtifact();
        artifact.ReachableSource!.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null)).Graph = artifact.ReachableSource!.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null)).Graph! with { Types = default };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() => CompilerManifestArtifactJson.Validate(artifact, cancellation.Token)));
        Assert.Throws<OperationCanceledException>(new Action(() => CompilerManifestArtifactJson.Serialize(artifact, cancellation.Token)));
    }

    [Test]
    public void GraphRejectionRetainsOriginalDecodeCause()
    {
        var artifact = CreateArtifact();
        artifact.ReachableSource!.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null)).Graph = artifact.ReachableSource!.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null)).Graph! with { Types = default };
        var exception = Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.Validate(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.That(exception!.InnerException, Is.TypeOf<InvalidDataException>());
    }
}
