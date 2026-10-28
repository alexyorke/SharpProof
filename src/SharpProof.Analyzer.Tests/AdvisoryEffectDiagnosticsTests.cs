using System.Collections.Immutable;
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
        System.ArgumentNullException.ThrowIfNull(attribute);
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(detail);
        System.ArgumentNullException.ThrowIfNull(id);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(Source(attribute, body), "effects", [id],
            new SharpProofAnalyzer(factory));
        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, [id]);
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
        AnalyzerTestHost.AssertIds(diagnostics, ["SP0047"]);
    }

    [TestCase("System.Exception", "System.InvalidOperationException", false)]
    [TestCase("System.ArgumentException", "System.InvalidOperationException", true)]
    // The default constructor can throw from user culture callbacks before
    // the explicit throw, so the narrow exception policy needs a warning.
    [TestCase("System.InvalidOperationException", "System.InvalidOperationException", true)]
    [TestCase("System.InvalidOperationException", "System.InvalidOperationException", false, true)]
    public async Task ExplicitThrowsRespectAllowedExceptionTypes(string allowed, string thrown, bool warns, bool explicitMessage = false)
    {
        System.ArgumentNullException.ThrowIfNull(allowed);
        System.ArgumentNullException.ThrowIfNull(thrown);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            Source($"[AllowedExceptions(typeof({allowed}))]", $"throw new {thrown}(" + (explicitMessage ? "\"message\"" : "") + ");"), "effects",
            ["SP0046"], new SharpProofAnalyzer(factory));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
                warns ? Is.EqualTo(["SP0046"]) : Is.Empty);
            Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
    }

    [TestCase("System.ArgumentException", "return 10 / x;", true)]
    [TestCase("System.ArithmeticException", "return 10 / x;", false)]
    [TestCase("System.Exception", "return 10 / x;", false)]
    [TestCase("System.ArgumentException", "throw new System.InvalidOperationException(\"message\");", true)]
    [TestCase("System.InvalidOperationException", "throw new System.InvalidOperationException(\"message\");", false)]
    public async Task EffectContractChecksExceptionTypesAfterPermittingThrows(string allowed, string body, bool warns)
    {
        System.ArgumentNullException.ThrowIfNull(allowed);
        System.ArgumentNullException.ThrowIfNull(body);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            Source("[EffectContract(SharpProofEffect.Throws | SharpProofEffect.Allocates, " +
                $"ThrownExceptions = new[] {{ typeof({allowed}) }}, Complete = true)]", body),
            "effects", ["SP0052", "SP0047", "SP0024"], new SharpProofAnalyzer(factory));
        AnalyzerTestHost.AssertIds(diagnostics, warns ? ["SP0052"] : []);
        if (warns)
        { AnalyzerTestHost.AssertMessageContains(diagnostics[0], "undeclared effects: Throws"); }
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task EffectContractAccountsForCaughtAndEscapingFaultAllocations(bool declaresAllocation, bool catchesException)
    {
        var effects = "SharpProofEffect.Throws" + (declaresAllocation ? " | SharpProofEffect.Allocates" : "");
        var body = catchesException
            ? "try { return 10 / x; } catch (System.Exception) { return 0; }"
            : "return 10 / x;";
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            Source("[EffectContract(" + effects + ", ThrownExceptions = new[] { typeof(System.Exception) }, Complete = true)]", body),
            "effects", ["SP0052", "SP0047", "SP0024"], new SharpProofAnalyzer(factory));
        AnalyzerTestHost.AssertIds(diagnostics, declaresAllocation ? [] : ["SP0052"]);
        if (!declaresAllocation)
        { AnalyzerTestHost.AssertMessageContains(diagnostics[0], "undeclared effects: Allocates"); }
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [Test]
    public async Task CaughtFaultNeedsAllocationPermissionWithoutAnEscapingThrowPermission()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            Source("[EffectContract(SharpProofEffect.Allocates, Complete = true)]",
                "try { return 10 / x; } catch (System.Exception) { return 0; }"),
            "effects", ["SP0052", "SP0047", "SP0024"]);
        Assert.That(diagnostics, Is.Empty);
    }

    [TestCase("SharpProofEffect.None", true)]
    [TestCase("SharpProofEffect.Throws, ThrownExceptions = new[] { typeof(System.InvalidOperationException) }", false)]
    public async Task ATrustedBoundaryDecidesTheClaimsItsContractSatisfies(string effects, bool proven)
    {
        System.ArgumentNullException.ThrowIfNull(effects);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Fixture { [SharpProofTrusted(\"Reviewed.\")] " +
            "[EffectContract(" + effects + ", Complete = true)] [DoesNotThrow] public static extern int Target(); }",
            "effects", ["SP0046"], new SharpProofAnalyzer(factory));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), proven ? Is.Empty : Is.EqualTo(["SP0046"]));
            Assert.That(factory.Outcomes["Target"],
                Is.EqualTo(proven ? AnalyzerSemanticOutcome.Proven : AnalyzerSemanticOutcome.Unknown));
        }
    }

    private static string Source(string attribute, string body)
    {
        System.ArgumentNullException.ThrowIfNull(attribute);
        System.ArgumentNullException.ThrowIfNull(body);
        return "using SharpProof.Attributes; public static class Fixture { public static int State; " +
            attribute + " public static int Target(int x, object gate) { " + body + " } }";
    }
}
