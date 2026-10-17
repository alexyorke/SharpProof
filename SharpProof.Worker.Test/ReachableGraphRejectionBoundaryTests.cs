using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ReachableGraphRejectionBoundaryTests
{
    [TestCase("types")]
    [TestCase("roots")]
    [TestCase("term")]
    [TestCase("valid")]
    public void CheckedArtifactApisKeepReachableGraphRejectionBoundary(string mutation)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; static class Subject { [ZeroAllocations] public static int Target(int value) => value + 1; }");
        var graph = artifact.ReachableSource!.Bodies.Single().Graph!;
        Assert.That(graph, Is.Not.Null);
        switch (mutation)
        {
            case "types":
                graph.Types = null!;
                break;
            case "roots":
                graph.Roots = [int.MaxValue];
                break;
            case "term":
                graph.Terms = [null!];
                break;
            case "valid":
                CompilerManifestArtifactJson.Validate(artifact, cancellationToken: CancellationToken.None);
                var payload = CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: CancellationToken.None);
                Assert.That(CompilerManifestArtifactJson.Deserialize(payload, cancellationToken: CancellationToken.None).ReachableSource!.Bodies, Has.Length.EqualTo(1));
                return;
        }
        var malformed = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: CancellationToken.None);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.Deserialize(malformed, cancellationToken: CancellationToken.None)));
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.Validate(artifact, cancellationToken: CancellationToken.None)));
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: CancellationToken.None)));
    }
}
