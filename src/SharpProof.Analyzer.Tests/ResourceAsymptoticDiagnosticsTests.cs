using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ResourceAsymptoticDiagnosticsTests
{
    [TestCase("for(int i=0;i<n;i++) {}", "O(n)")]
    [TestCase("for(int i=0;i<n;i++) { for(int j=0;j<n;j++) {} }", "O(n^2)")]
    [TestCase("checked { for(int i=1;i<n;i*=2) {} }", "O(log(n))")]
    public async Task DiagnosticLabelsGrowthOfProvenUpperBoundsWithoutClaimingTightness(string body, string expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(expected);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync("public static class C { public static void Target(int n) { " + body + " } }", null, ["SP0060"]);
        var diagnostic = diagnostics.Single();
        Assert.That(diagnostic.Properties["ResourceStatus"], Is.EqualTo("Proven"));
        Assert.That(diagnostic.Properties["ResourceAsymptoticModel"], Is.EqualTo(ResourceAsymptoticGrowth.ModelId));
        Assert.That(diagnostic.Properties["LoopIterationsAsymptoticUpperBound"], Is.EqualTo(expected));
        Assert.That(diagnostic.Properties["LoopIterationsAsymptoticInputOrdinal"], Is.EqualTo("0"));
        Assert.That(diagnostic.Properties["LoopIterationsAsymptoticInputName"], Is.EqualTo("n"));
        Assert.That(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), Does.Contain(expected + " [n=NonNegativeScalar(n)]"));
        Assert.That(diagnostic.Properties.ContainsKey("LoopIterationsAsymptoticLowerBound"), Is.False);
        Assert.That(diagnostic.Properties.ContainsKey("LoopIterationsAsymptoticTightBound"), Is.False);
    }

    [Test]
    public async Task IndependentInputsKeepNumericProofButReportUnknownSingleInputGrowth()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync("public static class C { public static void Target(int n,int m) { for(int i=0;i<n;i++) {} for(int j=0;j<m;j++) {} } }", null, ["SP0060"]);
        var diagnostic = diagnostics.Single();
        Assert.That(diagnostic.Properties["ResourceStatus"], Is.EqualTo("Proven"));
        Assert.That(diagnostic.Properties["LoopIterationsAsymptoticUnknownReason"], Is.EqualTo("AsymptoticMultipleInputs"));
        Assert.That(diagnostic.Properties.ContainsKey("LoopIterationsAsymptoticUpperBound"), Is.False);
        Assert.That(diagnostic.Properties["ArrayAllocationsAsymptoticUpperBound"], Is.EqualTo("O(0)"));
    }

    [Test]
    public async Task UnknownNumericCostsDoNotPublishAsymptoticProperties()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync("public static class C { public static void Target() { while(true) {} } }", null, ["SP0060"]);
        var diagnostic = diagnostics.Single();
        Assert.That(diagnostic.Properties["ResourceStatus"], Is.EqualTo("Unknown"));
        Assert.That(diagnostic.Properties.ContainsKey("ResourceAsymptoticModel"), Is.False);
        Assert.That(diagnostic.Properties.ContainsKey("LoopIterationsAsymptoticUpperBound"), Is.False);
    }
    [TestCase(ReportDiagnostic.Warn, false, true)]
    [TestCase(ReportDiagnostic.Error, false, true)]
    [TestCase(ReportDiagnostic.Info, false, true)]
    [TestCase(ReportDiagnostic.Hidden, false, true)]
    [TestCase(ReportDiagnostic.Suppress, false, false)]
    [TestCase(ReportDiagnostic.Default, false, false)]
    [TestCase(ReportDiagnostic.Warn, true, true)]
    [TestCase(ReportDiagnostic.Suppress, true, false)]
    public async Task CompilerOwnedSeverityOptionsActivateTheDisabledByDefaultResourceRule(ReportDiagnostic severity, bool global, bool enabled)
    {
        var compilation = AnalyzerTestHost.CreateCompilation("public static class C { public static void Target(int n) { for(int i=0;i<n;i++) {} } }", []);
        compilation = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(new ResourceSeverityOptions(severity, global)));
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, mode: null);
        Assert.That(diagnostics.Count(static diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Id == "SP0060"),
            Is.EqualTo(enabled ? 1 : 0));
    }

    private sealed class ResourceSeverityOptions(ReportDiagnostic configured, bool global) : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(tree);
            return GeneratedKind.NotGenerated;
        }

        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            ArgumentNullException.ThrowIfNull(tree);
            ArgumentNullException.ThrowIfNull(diagnosticId);
            severity = configured;
            return !global && diagnosticId == "SP0060";
        }

        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            ArgumentNullException.ThrowIfNull(diagnosticId);
            severity = configured;
            return global && diagnosticId == "SP0060";
        }
    }
}
