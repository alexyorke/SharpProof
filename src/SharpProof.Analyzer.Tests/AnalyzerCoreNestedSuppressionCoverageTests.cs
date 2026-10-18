using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreNestedSuppressionCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SelectedNestedCallablesRetainReviewedSuppression(bool localFunction)
    {
        var nested = localFunction
            ? "[EnforcePure, SharpProofSuppress(\"Reviewed nested callable.\")] int Nested() => 1; return Nested();"
            : "System.Func<int> nested = [EnforcePure, SharpProofSuppress(\"Reviewed nested callable.\")] () => 1; return nested();";
        var compilation = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes; public static class Fixture { public static int Target() { " + nested + " } }",
            ["SP0002", "SP0024"]);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "effects", analyzer: new SharpProofAnalyzer(factory));
        Assert.That(diagnostics, Is.Empty);
        var kind = localFunction ? MethodKind.LocalFunction : MethodKind.AnonymousFunction;
        Assert.That(factory.GetOutcomes(kind).Count(), Is.EqualTo(1));
        Assert.That(factory.GetOutcomes(kind).Single(), Is.EqualTo(AnalyzerSemanticOutcome.Suppressed));
    }
}
