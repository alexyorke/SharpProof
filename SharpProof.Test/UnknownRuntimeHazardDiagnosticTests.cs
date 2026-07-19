using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class UnknownRuntimeHazardDiagnosticTests
{
    private const string UnknownDivisionSource = @"
public class TestClass
{
    public int Divide(int divisor)
    {
        return 10 / divisor;
    }
}";

    [Test]
    public async Task UnknownsMode_ReportsInformationalCandidateWithStableEvidence()
    {
        var diagnostics = await GetDiagnosticsAsync(UnknownDivisionSource, "unknowns");
        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics,
            "SP0033");

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Info));
        Assert.That(diagnostic.GetMessage(), Does.Contain("DivideByZero"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("10 / divisor"));
        Assert.That(diagnostic.Properties["sharpproof.runtime_hazard.kind"],
            Is.EqualTo("DivideByZero"));
        Assert.That(diagnostic.Properties["sharpproof.runtime_hazard.status"],
            Is.EqualTo("Unknown"));
        Assert.That(diagnostic.Properties["sharpproof.runtime_hazard.status_reason"], Is.Not.Empty);
        Assert.That(diagnostic.Properties["sharpproof.runtime_hazard.trigger"], Is.Not.Empty);
        Assert.That(diagnostic.Properties["sharpproof.runtime_hazard.proof_backend"], Is.Not.Empty);
        Assert.That(diagnostic.Properties["sharpproof.runtime_hazard.unknown_reason"], Is.Not.Empty);
        Assert.That(diagnostic.Properties["sharpproof.unknown.code"],
            Is.EqualTo("runtime_hazard.unknown"));
        Assert.That(diagnostic.Properties["sharpproof.unknown.category"],
            Is.EqualTo(SymbolicUnknownReasonCategory.Unknown.ToString()));
        Assert.That(diagnostic.Properties["sharpproof.unknown.source"],
            Is.EqualTo(SymbolicUnknownReasonSource.RuntimeHazard.ToString()));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.BaselineSymbolProperty], Is.Not.Empty);
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.BaselinePathProperty], Is.Not.Empty);
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.BaselineOperationKindProperty],
            Is.EqualTo("DivideExpression"));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.BaselineEvidenceKeyProperty], Is.Not.Empty);
        Assert.That(diagnostic.Properties["sharpproof.explain.proof_status"], Is.EqualTo("unknown"));
        Assert.That(diagnostics.Any(candidate => candidate.Id == "SP0011"),
            Is.False);
    }

    [TestCase("none")]
    [TestCase("sites")]
    [TestCase("summaries")]
    [TestCase("all")]
    public async Task ExistingModes_DoNotReportUnknownCandidates(string mode)
    {
        var diagnostics = await GetDiagnosticsAsync(UnknownDivisionSource, mode);

        Assert.That(diagnostics.Any(diagnostic =>
            diagnostic.Id == "SP0033"), Is.False);
    }

    [Test]
    public async Task SitesAndUnknownsMode_ReportsProvenAndUnknownSitesSeparately()
    {
        const string source = @"
public class TestClass
{
    public int Evaluate(int divisor)
    {
        if (divisor == int.MinValue)
        {
            string value = null!;
            return value.Length;
        }

        return 10 / divisor;
    }
}";

        var diagnostics = await GetDiagnosticsAsync(source, "sites-and-unknowns");

        Assert.That(diagnostics.Count(diagnostic =>
            diagnostic.Id == "SP0011"), Is.EqualTo(1));
        Assert.That(diagnostics.Count(diagnostic =>
            diagnostic.Id == "SP0033"), Is.EqualTo(1));
        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "SP0010"),
            Is.False);
    }

    [Test]
    public async Task AllAndUnknownsMode_ReportsSummaryProvenSiteAndUnknownCandidate()
    {
        const string source = @"
public class TestClass
{
    public int Evaluate(int divisor)
    {
        if (divisor == int.MinValue)
        {
            string value = null!;
            return value.Length;
        }

        return 10 / divisor;
    }
}";

        var diagnostics = await GetDiagnosticsAsync(source, "all-and-unknowns");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "SP0010"),
            Is.True);
        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "SP0011"),
            Is.True);
        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "SP0033"),
            Is.True);
    }

    [Test]
    public async Task PragmaSuppression_HidesUnknownCandidate()
    {
        const string source = @"
#pragma warning disable SP0033
public class TestClass
{
    public int Divide(int divisor)
    {
        return 10 / divisor;
    }
}
#pragma warning restore SP0033";

        var diagnostics = await GetDiagnosticsAsync(source, "unknowns");

        Assert.That(diagnostics.Any(diagnostic =>
            diagnostic.Id == "SP0033"), Is.False);
    }

    private static Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source, string mode)
    {
        return AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_runtime_hazard_mode", mode),
            false,
            ImmutableArray<AdditionalText>.Empty,
            sourcePath: "src/UnknownHazards.cs",
            concurrentAnalysis: true);
    }
}
