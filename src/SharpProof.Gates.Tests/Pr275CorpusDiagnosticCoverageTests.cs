using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class Pr275CorpusDiagnosticCoverageTests
{
    [Test]
    public void DefaultAssignmentCountsRejectBeforeInspectingDiagnostics()
    {
        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
            OpenSourceCorpusRunner.RequireCompleteDiagnosticAssignment([], default)));
        Assert.That(exception!.ParamName, Is.EqualTo("assignmentCounts"));
    }

    [TestCase(0)]
    [TestCase(2)]
    public void AssignmentCountsMustHaveOneEntryForEachDiagnostic(int count)
    {
        var descriptor = new DiagnosticDescriptor("PR275", "Counted diagnostic", "Counted diagnostic",
            "Test", DiagnosticSeverity.Warning, isEnabledByDefault: true);
        var diagnostic = Diagnostic.Create(descriptor, Location.None);
        var counts = Enumerable.Repeat(1, count).ToImmutableArray();

        var exception = Assert.Throws<ArgumentException>((Action)(() =>
            OpenSourceCorpusRunner.RequireCompleteDiagnosticAssignment([diagnostic], counts)));

        Assert.That(exception!.ParamName, Is.EqualTo("assignmentCounts"));
        Assert.That(exception.Message, Does.Contain("do not match the diagnostics"));
    }

    [Test]
    public void EmptyDiagnosticResultsRequireNoAssignments()
    {
        Assert.DoesNotThrow((Action)(() =>
            OpenSourceCorpusRunner.RequireCompleteDiagnosticAssignment([], [])));
    }
}
