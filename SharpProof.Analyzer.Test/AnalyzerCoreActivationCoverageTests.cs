using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreActivationCoverageTests
{
    [Test]
    public async Task ACompilationWithoutTheContractApiDoesNotCreateAnAdvisorySession()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public static class Fixture { public static int Target() => 1; }", [])
            .WithReferences(TestMetadataReferences.WithoutSharpProof);
        Assert.That(compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract"), Is.Null);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, mode: null, analyzer: new SharpProofAnalyzer(factory));
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [Test]
    public async Task ACompiledClosedNetmoduleContractActivatesOrdinaryCallerAnalysis()
    {
        var external = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes; public static class Guard { public static int Accept([Positive] int value) => value; }", [])
            .WithAssemblyName("ExternalClosedNetmodule");
        external = external.WithOptions(external.Options.WithOutputKind(OutputKind.NetModule));
        var reference = MetadataReference.CreateFromImage(AnalyzerTestHost.EmitImage(external), MetadataReferenceProperties.Module);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "public static class Fixture { public static int Target() => 1; }",
            null, ["SP0027"], new SharpProofAnalyzer(factory), additionalReferences: [reference]);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.EqualTo(1));
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task AClosedReferenceContractActivatesOrdinaryCallerAnalysis(bool compiled, bool generated)
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
            "public sealed class Fixture { public int Field = 1; public int Value { get; } = 1; " +
            "public int Target() { int Local() => 1; return Local(); } }",
            null, ["SP0027"], new SharpProofAnalyzer(factory), additionalReferences: [reference],
            filePath: generated ? "input.g.cs" : "input.cs");
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.EqualTo(1));
        if (generated)
        {
            Assert.That(factory.Outcomes.ContainsKey("Target"), Is.False);
        }
        else
        {
            Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
            Assert.That(factory.Outcomes.ContainsKey("Local"), Is.False);
        }
    }

    [Test]
    public void DefaultEngineRejectsMissingAnalysisContext()
    {
        var engine = new SharpProofAnalyzerEngine();
        Assert.Throws<ArgumentNullException>((Action)(() => engine.RegisterActions(null!)));
    }
}
