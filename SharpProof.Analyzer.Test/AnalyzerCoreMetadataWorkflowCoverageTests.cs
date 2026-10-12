using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreMetadataWorkflowCoverageTests
{
    [TestCase(false, "")]
    [TestCase(true, "")]
    [TestCase(false, "Reviewed ordinary boundary.")]
    [TestCase(true, "Reviewed ordinary boundary.")]
    public async Task ImportedWorkflowReasonsRetainValidationAndSuppressionBehavior(bool suppressing, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        var hasReason = !string.IsNullOrEmpty(reason);
        var attributeName = suppressing ? "SharpProofSuppress" : "SharpProofTrusted";
        var reference = AnalyzerTestHost.EmitReference(
            "using SharpProof.Attributes; public static class Api { [" + attributeName + "(\"" + reason +
            "\")] public static int Value() => 1; }", "AnalyzerCoreMetadataWorkflowReason");
        var sourceCompilation = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes; public static class Fixture { [EnforcePure] public static int Target() => 1; }",
            ["SP0024"], additionalReferences: [reference]);
        var factory = new RecordingSessionFactory();
        var sourceDiagnostics = await AnalyzerTestHost.AnalyzeAsync(sourceCompilation, "effects", analyzer: new SharpProofAnalyzer(factory));
        Assert.That(sourceDiagnostics, Is.Empty);
        Assert.That(factory.Session, Is.Not.Null);
        var session = factory.Session!;
        var compilation = session.Compilation;
        Assert.That(compilation.SyntaxTrees.Single(), Is.SameAs(sourceCompilation.SyntaxTrees.Single()));
        Assert.That(compilation.References, Is.EqualTo(sourceCompilation.References));
        var method = compilation.GetTypeByMetadataName("Api")!.GetMembers("Value").OfType<IMethodSymbol>().Single();
        var attribute = method.GetAttributes().Single();
        Assert.That(attribute.ApplicationSyntaxReference, Is.Null);
        Assert.That(method.Locations, Has.Length.EqualTo(1));
        Assert.That(method.Locations[0].IsInMetadata, Is.True);
        var diagnostics = new List<Diagnostic>();
        var suppressed = SharpProofControlAttributePolicy.ValidateDeclaredScopeAndShouldSuppress(
            method, session, diagnostics.Add, CancellationToken.None);
        Assert.That(suppressed, Is.EqualTo(suppressing && hasReason));
        if (!hasReason)
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
            AnalyzerTestHost.AssertMessageContains(diagnostics[0], "expected a non-empty reason");
            Assert.That(diagnostics[0].Location, Is.SameAs(Location.None));
        }
        else
        {
            Assert.That(diagnostics, Is.Empty);
        }
    }
}
