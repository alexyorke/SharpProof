using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

public abstract class CommonBugAnalyzerTestBase
{
    protected static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        return AnalyzerTestHost.GetDiagnosticsAsync(source, analyzerFeatures: AnalyzerFeatures.CommonBugs);
    }

    protected static void AssertHas(ImmutableArray<Diagnostic> diagnostics, string diagnosticId)
    {
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain(diagnosticId));
    }

    protected static void AssertMissing(ImmutableArray<Diagnostic> diagnostics, string diagnosticId)
    {
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Not.Contain(diagnosticId));
    }
}
