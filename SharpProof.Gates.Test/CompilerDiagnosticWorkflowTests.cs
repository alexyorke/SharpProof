using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class CompilerDiagnosticWorkflowTests
{
    [TestCase(ReportDiagnostic.Default, "Warning")]
    [TestCase(ReportDiagnostic.Warn, "Warning")]
    [TestCase(ReportDiagnostic.Error, "Error")]
    [TestCase(ReportDiagnostic.Info, "Info")]
    [TestCase(ReportDiagnostic.Hidden, "Hidden")]
    [TestCase((ReportDiagnostic)99, "Warning")]
    public void CanonicalDiagnosticUsesTheConfiguredSeverityOfARealCompilerWarning(
        ReportDiagnostic severity, string expected)
    {
        var compilation = AnalyzerGateHost.CreateCompilation(
            "public static class C { public static void Value() { int unused; } }");
        var diagnostic = compilation.GetDiagnostics().Single(static item => item.Id == "CS0168");
        var options = compilation.Options.WithSpecificDiagnosticOptions(
            ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(diagnostic.Id, severity));
        var canonical = CorpusGate.CanonicalizeDiagnostic(diagnostic, options);
        Assert.That(canonical, Does.StartWith($"CS0168@{expected}@input.cs:"));
    }

    [Test]
    public void CanonicalDiagnosticRejectsAWarningConfiguredAsSuppressed()
    {
        var compilation = AnalyzerGateHost.CreateCompilation(
            "public static class C { public static void Value() { int unused; } }");
        var diagnostic = compilation.GetDiagnostics().Single(static item => item.Id == "CS0168");
        var options = compilation.Options.WithSpecificDiagnosticOptions(
            ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(diagnostic.Id, ReportDiagnostic.Suppress));
        var error = Assert.Throws<InvalidOperationException>((Action)(() =>
            CorpusGate.CanonicalizeDiagnostic(diagnostic, options)));
        Assert.That(error!.Message, Is.EqualTo("Suppressed diagnostic CS0168 was reported."));
    }

    [Test]
    public void CanonicalDiagnosticRepresentsACompilerDiagnosticWithoutASourceLocation()
    {
        var original = AnalyzerGateHost.CreateCompilation("public static class C { }");
        var compilation = original.WithOptions(original.Options.WithOutputKind(OutputKind.ConsoleApplication));
        var diagnostic = compilation.GetDiagnostics().Single(static item => item.Id == "CS5001");
        Assert.That(diagnostic.Location, Is.EqualTo(Location.None));
        Assert.That(CorpusGate.CanonicalizeDiagnostic(diagnostic, compilation.Options),
            Does.StartWith("CS5001@Error@none@"));
    }

    [Test]
    public void CompilationValidationPreservesTheRealCompilerError()
    {
        var compilation = AnalyzerGateHost.CreateCompilation(
            "public static class C { public static int Value() => MissingValue; }");
        var diagnostic = compilation.GetDiagnostics().Single(static item => item.Id == "CS0103");
        var error = Assert.Throws<InvalidDataException>((Action)(() =>
            AnalyzerGateHost.ThrowIfCompilationHasErrors(compilation, 1,
                static message => new InvalidDataException(message), CancellationToken.None)));
        Assert.That(error!.Message, Is.EqualTo(diagnostic.ToString()));
    }

    [Test]
    public void DiagnosticAssignmentRejectsAnIncompleteCountVector()
    {
        var compilation = AnalyzerGateHost.CreateCompilation(
            "public static class C { public static void Value() { int unused; } }");
        var diagnostic = compilation.GetDiagnostics().Single(static item => item.Id == "CS0168");
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            OpenSourceCorpusRunner.RequireCompleteDiagnosticAssignment([diagnostic], [])));
        Assert.That(error!.ParamName, Is.EqualTo("assignmentCounts"));
        Assert.That(error.Message, Does.StartWith("Diagnostic assignment counts do not match the diagnostics."));
    }

    [Test]
    public void SemanticAnalysisRejectsAnOrdinaryUnresolvedNameBeforeRunningTheAnalyzer()
    {
        var error = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
            await AnalyzerGateHost.AnalyzeWithSemanticOutcomesAsync(
                "public static class C { public static int Value() => MissingValue; }", "effects")));
        Assert.That(error!.Message, Does.Contain("CS0103"));
        Assert.That(error.Message, Does.Contain("MissingValue"));
    }

    [Test]
    public async Task AnalysisSupportsTheDefaultConfigurationForOrdinaryUnannotatedSource()
    {
        var compilation = AnalyzerGateHost.CreateCompilation(
            "public static class C { public static int Value() => 1; }");
        var diagnostics = await AnalyzerGateHost.AnalyzeAsync(
            compilation, new SharpProofAnalyzer(), mode: null, concurrentAnalysis: false);
        Assert.That(diagnostics, Is.Empty);
    }
}
