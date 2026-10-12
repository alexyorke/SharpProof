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
        var attributeName = suppressing ? "SharpProofSuppress" : "SharpProofTrusted";
        var reference = AnalyzerTestHost.EmitReference(
            "using SharpProof.Attributes; public static class Api { [" + attributeName + "(\"" + reason +
            "\")] public static int Value() => 1; }", "AnalyzerCoreMetadataWorkflowReason");
        var compilation = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes; public static class Fixture { [EnforcePure] public static int Target() => 1; }",
            ["SP0024"], additionalReferences: [reference]);
        var factory = new RecordingSessionFactory();
        var sourceDiagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "effects", analyzer: new SharpProofAnalyzer(factory));
        Assert.That(sourceDiagnostics, Is.Empty);
        Assert.That(factory.Session, Is.Not.Null);
        var session = factory.Session!;
        Assert.That(session.Compilation, Is.SameAs(compilation));
        var method = compilation.GetTypeByMetadataName("Api")!.GetMembers("Value").OfType<IMethodSymbol>().Single();
        var attribute = method.GetAttributes().Single();
        Assert.That(attribute.ApplicationSyntaxReference, Is.Null);
        Assert.That(method.Locations, Has.Length.EqualTo(1));
        Assert.That(method.Locations[0].IsInMetadata, Is.True);
        var diagnostics = new List<Diagnostic>();
        var suppressed = SharpProofControlAttributePolicy.ValidateDeclaredScopeAndShouldSuppress(
            method, session, diagnostics.Add, CancellationToken.None);
        Assert.That(suppressed, Is.EqualTo(suppressing && reason.Length != 0));
        if (reason.Length == 0)
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
