using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// The analyzer only advises on effect claims: it names the first site that
// could violate each claim and never proves a body. Z3 decides in the build.
[TestFixture]
public sealed class AdvisoryEffectDiagnosticsTests
{
    [TestCase("[ZeroAllocations]", "var box = new object(); return x;", "SP0045", "'new object()' may allocate")]
    [TestCase("[DoesNotThrow]", "return 10 / x;", "SP0046", "System.DivideByZeroException")]
    [TestCase("[EnforcePure]", "State = x; return x;", "SP0002", "")]
    [TestCase("[AllowedCapabilities(SharpProofCapability.None)]", "lock (gate) { } return x;", "SP0016", "Synchronization")]
    [TestCase("[EffectContract(SharpProofEffect.None, Complete = true)]", "State = x; return x;", "SP0052", "WritesStaticState")]
    public async Task ReportsTheFirstSiteThatCouldViolateAClaim(string attribute, string body, string id, string detail)
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(Source(attribute, body), "effects", [id],
            new SharpProofAnalyzer(factory));
        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, id);
            AnalyzerTestHost.AssertMessageContains(diagnostics[0], detail);
            Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task ABodyWithoutViolatingSitesIsStillNotProven()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            Source("[ZeroAllocations, EnforcePure, DoesNotThrow]", "return x + 1;"), "effects",
            ["SP0002", "SP0045", "SP0046", "SP0047"], new SharpProofAnalyzer(factory));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task ABodyTheIrCannotLowerIsReportedIncomplete()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            Source("[ZeroAllocations]", "dynamic value = x; return (int)value;"), "effects", ["SP0047"]);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0047");
    }

    [TestCase("SharpProofEffect.None", true)]
    [TestCase("SharpProofEffect.Throws, ThrownExceptions = new[] { typeof(System.InvalidOperationException) }", false)]
    public async Task ATrustedBoundaryDecidesTheClaimsItsContractSatisfies(string effects, bool proven)
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Fixture { [SharpProofTrusted(\"Reviewed.\")] " +
            "[EffectContract(" + effects + ", Complete = true)] [DoesNotThrow] public static extern int Target(); }",
            "effects", ["SP0046"], new SharpProofAnalyzer(factory));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), proven ? Is.Empty : Is.EqualTo(["SP0046"]));
            Assert.That(factory.Outcomes["Target"],
                Is.EqualTo(proven ? AnalyzerSemanticOutcome.Proven : AnalyzerSemanticOutcome.Unknown));
        }
    }

    private static string Source(string attribute, string body)
    {
        return "using SharpProof.Attributes; public static class Fixture { public static int State; " +
            attribute + " public static int Target(int x, object gate) { " + body + " } }";
    }
}
