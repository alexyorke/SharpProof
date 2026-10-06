using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ArtifactManifestEnumCanonicalizationControlTests
{
    [Test]
    public void ValidEnumArraysStillCanonicalizeWithoutChangingPayload()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public static class Subject {
                [EnforcePure] public static int Identity(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """);
        var callable = artifact.Manifest.Callables.Single();
        Assert.That(callable.SelectedFeatures, Has.Length.EqualTo(2));
        Assert.That(callable.SelectionReasons, Has.Length.EqualTo(2));
        var expected = CompilerManifestArtifactJson.Serialize(artifact);
        callable.SelectedFeatures = [.. callable.SelectedFeatures.Reverse()];
        callable.SelectionReasons = [.. callable.SelectionReasons.Reverse()];

        var actual = CompilerManifestArtifactJson.Serialize(artifact);
        Assert.That(actual, Is.EqualTo(expected));
        var decoded = CompilerManifestArtifactJson.Deserialize(actual);
        Assert.That(decoded.Manifest.Callables.Single().SelectedFeatures, Is.EqualTo(callable.SelectedFeatures));
        Assert.That(decoded.Manifest.Callables.Single().SelectionReasons, Is.EqualTo(callable.SelectionReasons));
    }
}
