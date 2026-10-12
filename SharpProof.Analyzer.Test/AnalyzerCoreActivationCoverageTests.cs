using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreActivationCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task AClosedReferenceContractActivatesOrdinaryCallerAnalysis(bool compiled)
    {
        var external = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            namespace External.Contracts {
                public static class Container {
                    public static class Guard {
                        public static int Accept([Positive] int value) => value;
                    }
                }
            }
            """, []).WithAssemblyName("ExternalClosedActivation");
        MetadataReference reference = compiled
            ? MetadataReference.CreateFromImage(AnalyzerTestHost.EmitImage(external))
            : external.ToMetadataReference();
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "public static class Fixture { public static int Target() => 1; }",
            null, ["SP0027"], new SharpProofAnalyzer(factory), additionalReferences: [reference]);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.EqualTo(1));
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
    }

    [Test]
    public void DefaultEngineRejectsMissingAnalysisContext()
    {
        var engine = new SharpProofAnalyzerEngine();
        Assert.Throws<ArgumentNullException>((Action)(() => engine.RegisterActions(null!)));
    }
}
