using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

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
            SharpProofDiagnostics.UnknownRuntimeHazardId);

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Info));
        Assert.That(diagnostic.GetMessage(), Does.Contain("DivideByZero"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("10 / divisor"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.RuntimeHazardKindProperty],
            Is.EqualTo("DivideByZero"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.RuntimeHazardStatusProperty],
            Is.EqualTo("Unknown"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.RuntimeHazardStatusReasonProperty], Is.Not.Empty);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.RuntimeHazardTriggerProperty], Is.Not.Empty);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.RuntimeHazardProofBackendProperty], Is.Not.Empty);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.RuntimeHazardUnknownReasonProperty], Is.Not.Empty);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.BaselineSymbolProperty], Is.Not.Empty);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.BaselinePathProperty], Is.Not.Empty);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.BaselineOperationKindProperty],
            Is.EqualTo("DivideExpression"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.BaselineEvidenceKeyProperty], Is.Not.Empty);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainProofStatusProperty], Is.EqualTo("unknown"));
        Assert.That(diagnostics.Any(candidate => candidate.Id == SharpProofDiagnostics.UncaughtExceptionSiteId),
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
            diagnostic.Id == SharpProofDiagnostics.UnknownRuntimeHazardId), Is.False);
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
            diagnostic.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.EqualTo(1));
        Assert.That(diagnostics.Count(diagnostic =>
            diagnostic.Id == SharpProofDiagnostics.UnknownRuntimeHazardId), Is.EqualTo(1));
        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId),
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

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId),
            Is.True);
        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.UncaughtExceptionSiteId),
            Is.True);
        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.UnknownRuntimeHazardId),
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
            diagnostic.Id == SharpProofDiagnostics.UnknownRuntimeHazardId), Is.False);
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
