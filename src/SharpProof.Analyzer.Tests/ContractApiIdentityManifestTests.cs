using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ContractApiIdentityManifestTests
{
    [Test]
    public void SourceShadowedRuntimeClauseCreatesNoProofClaim()
    {
        const string source =
            """
            namespace SharpProof.Attributes {
                public static class Contract {
                    public static void Requires(bool condition) {
                    }
                    public static void Ensures(bool condition) {
                    }
                    public static void Assume(bool condition) {
                    }
                }
            }
            public static class Subject {
                public static int Read(int value) {
                    SharpProof.Attributes.Contract.Ensures(value > 0);
                    return value;
                }
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(
            source,
            enabledIds: []);

        var result = new ClaimManifestBuilder(
            compilation,
            WorkerFeatureSet.Contracts).Build();

        Assert.That(result.Manifest.Claims, Is.Empty);
        Assert.That(result.Manifest.Callables, Has.Length.EqualTo(1));
        var callable = result.Manifest.Callables.Single();
        Assert.That(
            callable.SelectedFeatures,
            Is.EqualTo(new[] { WorkerSelectedFeature.Contracts }));
        Assert.That(
            callable.SelectionReasons,
            Is.EqualTo(new[] {
                WorkerSelectionReason.ExplicitAnnotation
            }));
        Assert.That(callable.ClaimIds, Is.Empty);
    }
}
