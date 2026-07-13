using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer.Configuration;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

/// <summary>
/// Provides build-loaded project context to SharpProof's analyzer and symbolic query APIs.
/// </summary>
public sealed class SharpProofProjectAnalysisContext
{
    public SharpProofProjectAnalysisContext(
        Compilation compilation,
        SyntaxTree syntaxTree,
        AnalyzerOptions analyzerOptions,
        string projectName,
        string? projectFilePath = null,
        string? solutionFilePath = null,
        IEnumerable<string>? analyzerConfigPaths = null)
    {
        SymbolicContext = new SymbolicProjectQueryContext(
            compilation,
            syntaxTree,
            analyzerOptions,
            projectName,
            projectFilePath,
            solutionFilePath,
            analyzerConfigPaths);
        Compilation = SymbolicContext.Compilation;
        SyntaxTree = SymbolicContext.SyntaxTree;
        AnalyzerOptions = SymbolicContext.AnalyzerOptions;
        SourceInput = SymbolicContext.SourceInput;
        ProjectName = SymbolicContext.ProjectName;
        ProjectFilePath = SymbolicContext.ProjectFilePath;
        SolutionFilePath = SymbolicContext.SolutionFilePath;

        var configuration = AnalyzerConfiguration.FromOptions(AnalyzerOptions);
        SmtOptions = SymbolicContext.Configuration.SmtOptions;
        AnalysisLimits = SymbolicContext.Configuration.AnalysisLimits;
        ConfigurationIssues = configuration.InvalidConfigurationValues
            .Select(static issue => new SharpProofProjectConfigurationIssue(
                issue.Key,
                issue.Value,
                issue.Reason))
            .ToImmutableArray();
    }

    public Compilation Compilation { get; }

    public SyntaxTree SyntaxTree { get; }

    public AnalyzerOptions AnalyzerOptions { get; }

    public SymbolicSourceInput SourceInput { get; }

    public SymbolicProjectQueryContext SymbolicContext { get; }

    public string ProjectName { get; }

    public string? ProjectFilePath { get; }

    public string? SolutionFilePath { get; }

    public IReadOnlyList<string> AnalyzerConfigPaths => SymbolicContext.AnalyzerConfigPaths;

    public IReadOnlyList<string> AdditionalFilePaths => SymbolicContext.AdditionalFilePaths;

    public bool HasBaseline => SymbolicContext.HasBaseline;

    public int EffectSummaryFileCount => SymbolicContext.EffectSummaryFileCount;

    public SmtAnalysisOptions SmtOptions { get; }

    public SymbolicAnalysisLimits AnalysisLimits { get; }

    public IReadOnlyList<SharpProofProjectConfigurationIssue> ConfigurationIssues { get; }

    public async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        CancellationToken cancellationToken)
    {
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new SharpProofAnalyzer());
        var analysis = Compilation.WithAnalyzers(analyzers, AnalyzerOptions);
        return await analysis.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
    }

}

public sealed class SharpProofProjectConfigurationIssue
{
    public SharpProofProjectConfigurationIssue(string key, string value, string reason)
    {
        Key = key ?? string.Empty;
        Value = value ?? string.Empty;
        Reason = reason ?? string.Empty;
    }

    public string Key { get; }

    public string Value { get; }

    public string Reason { get; }
}
