using System.Collections.Immutable;
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
        var expectedFeatures = callable.SelectedFeatures;
        var expectedReasons = callable.SelectionReasons;
        var expected = CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        callable.SelectedFeatures = [.. callable.SelectedFeatures.Reverse()];
        callable.SelectionReasons = [.. callable.SelectionReasons.Reverse()];

        var producerFeatures = callable.SelectedFeatures;
        var producerReasons = callable.SelectionReasons;
        var actual = CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(callable.SelectedFeatures.Equals(producerFeatures), Is.True);
        Assert.That(callable.SelectionReasons.Equals(producerReasons), Is.True);
        var decoded = CompilerManifestArtifactJson.Deserialize(actual, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(decoded.Manifest.Callables.Single().SelectedFeatures, Is.EqualTo(expectedFeatures));
        Assert.That(decoded.Manifest.Callables.Single().SelectionReasons, Is.EqualTo(expectedReasons));
    }
}
