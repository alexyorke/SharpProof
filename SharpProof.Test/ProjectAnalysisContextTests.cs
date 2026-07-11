using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class ProjectAnalysisContextTests
{
    [Test]
    public async Task ProjectContext_PreservesAnalyzerOptionsAndRunsBuildEquivalentAnalyzer()
    {
        const string source = """
                              using System;
                              using SharpProof.Attributes;

                              public static class ProjectSurface
                              {
                                  [Pure]
                                  public static void Write() => Console.WriteLine("impure");
                              }
                              """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            Path.GetFullPath("src/ProjectSurface.cs"));
        var compilation = CSharpCompilation.Create(
            "ProjectAnalysisContext",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var additionalFiles = ImmutableArray.Create<AdditionalText>(
            new AnalyzerTestHost.InMemoryAdditionalText(
                Path.GetFullPath("SharpProof.Baseline.json"),
                """
                {
                  "diagnostics": [
                    { "id": "SP0002", "symbol": "M:Other.Type", "path": "Other.cs" }
                  ]
                }
                """),
            new AnalyzerTestHost.InMemoryAdditionalText(
                Path.GetFullPath("Fixture.SharpProof.EffectSummary.json"),
                "{ \"GeneratedPurityCatalog\": { \"Entries\": [] } }"));
        var analyzerOptions = AnalyzerTestHost.CreateAnalyzerOptions(
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_enable_effect_summary_json", "false")
                .Add("sharpproof_smt_mode", "deep")
                .Add("sharpproof_smt_timeout_ms", "1234")
                .Add("sharpproof_analysis_max_merged_if_else_facts", "17"),
            additionalFiles,
            autoEnableEffectSummaryJsonForAdditionalFiles: false);

        var context = new SharpProofProjectAnalysisContext(
            compilation,
            syntaxTree,
            analyzerOptions,
            "ProjectFixture",
            Path.GetFullPath("ProjectFixture.csproj"),
            analyzerConfigPaths: new[] { Path.GetFullPath(".editorconfig") });
        var diagnostics = await context.GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.That(context.SourceInput.Compilation, Is.SameAs(compilation));
        Assert.That(context.SourceInput.SyntaxTree, Is.SameAs(syntaxTree));
        Assert.That(context.SmtOptions.Mode, Is.EqualTo(SmtAnalysisMode.Deep));
        Assert.That(context.SmtOptions.QueryTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(1234)));
        Assert.That(context.AnalysisLimits.MaxMergedIfElseFacts, Is.EqualTo(17));
        Assert.That(context.SymbolicContext.Configuration.SmtOptions, Is.EqualTo(context.SmtOptions));
        Assert.That(
            context.SymbolicContext.CreateQueryOptions().AnalysisLimits,
            Is.EqualTo(context.AnalysisLimits));
        Assert.That(context.HasBaseline, Is.True);
        Assert.That(context.EffectSummaryFileCount, Is.EqualTo(1));
        Assert.That(context.AnalyzerConfigPaths, Has.Count.EqualTo(1));
        Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(diagnostic => diagnostic.Id == "SP0002"));
    }
}
