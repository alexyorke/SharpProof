using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Testing;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ConfigurationDiagnosticsRegressionTests
{
    [Test]
    public async Task GlobalAndTreeConfigurationErrorsAreReportedTogether()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public static class Fixture { public static int Run() => 1; }",
            ["SP0025"]);
        var options = new FixedOptionsProvider(
            new DictionaryAnalyzerConfigOptions(
                ("sharpproof_profile", "invalid")),
            new DictionaryAnalyzerConfigOptions(
                ("sharpproof_features", "invalid")));

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            options,
            new SharpProofAnalyzer());

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0025", "SP0025");
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.GetMessage(
                    CultureInfo.InvariantCulture)),
                Has.Some.Contain("sharpproof_profile"));
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.GetMessage(
                    CultureInfo.InvariantCulture)),
                Has.Some.Contain("sharpproof_features"));
            Assert.That(
                diagnostics.Single(static diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(
                        "sharpproof_features",
                        StringComparison.Ordinal))
                    .Location.SourceTree,
                Is.SameAs(compilation.SyntaxTrees.Single()));
        }
    }

    private sealed class FixedOptionsProvider(
        AnalyzerConfigOptions global,
        AnalyzerConfigOptions tree) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree _)
        {
            return tree;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText _)
        {
            return tree;
        }
    }
}
