using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreWorkflowCoverageTests
{
    [Test]
    public async Task AnAnonymousPreconditionReportsItsUnsupportedInvocationPlacement()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Fixture { public static int Target() { " +
            "var callback = (int value) => { Contract.Requires(value > 0); return value; }; return callback(1); } }",
            "contracts", ["SP0024"]);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "preconditions inside lambda and anonymous methods cannot be enforced at delegate invocation sites");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ANestedWorkflowControlRequiresANonemptyReason(bool suppressing)
    {
        var attribute = suppressing ? "SharpProofSuppress" : "SharpProofTrusted";
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Fixture { public static int Target() { " +
            "var callback = [" + attribute + "(null)] () => 1; return callback(); } }",
            "effects", ["SP0024"]);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], "expected a non-empty reason");
    }

    [Test]
    public async Task AnInvalidTypeWorkflowControlIsReportedOnceAcrossItsMethodScopes()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; [SharpProofTrusted(\"\")] public static class Fixture { " +
            "[EnforcePure] public static int Target() => 1; }",
            "effects", ["SP0024"], new SharpProofAnalyzer(factory));
        AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], "expected a non-empty reason");
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [Test]
    public async Task AnUnusedExternLocalDeclarationHasNoExecutableEffectOwner()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Fixture { [EnforcePure] public static int Target() { " +
            "[System.Runtime.InteropServices.DllImport(\"Unused\")] static extern int Local(); return 1; } }",
            "effects", ["SP0002"], new SharpProofAnalyzer(factory));
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        Assert.That(factory.Outcomes.ContainsKey("Local"), Is.False);
    }
}
