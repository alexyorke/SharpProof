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
    private readonly ImmutableArray<string> _analyzerConfigPaths;
    private readonly ImmutableArray<string> _additionalFilePaths;

    public SharpProofProjectAnalysisContext(
        Compilation compilation,
        SyntaxTree syntaxTree,
        AnalyzerOptions analyzerOptions,
        string projectName,
        string? projectFilePath = null,
        string? solutionFilePath = null,
        IEnumerable<string>? analyzerConfigPaths = null)
    {
        Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
        AnalyzerOptions = analyzerOptions ?? throw new ArgumentNullException(nameof(analyzerOptions));
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("Project name is required.", nameof(projectName));

        if (!compilation.SyntaxTrees.Contains(syntaxTree))
            throw new ArgumentException("Syntax tree must belong to the project compilation.", nameof(syntaxTree));

        ProjectName = projectName.Trim();
        ProjectFilePath = NormalizeOptionalPath(projectFilePath);
        SolutionFilePath = NormalizeOptionalPath(solutionFilePath);
        _analyzerConfigPaths = NormalizePaths(analyzerConfigPaths);
        _additionalFilePaths = NormalizePaths(analyzerOptions.AdditionalFiles.Select(static file => file.Path));
        SymbolicContext = new SymbolicProjectQueryContext(
            compilation,
            syntaxTree,
            analyzerOptions,
            ProjectName,
            ProjectFilePath,
            SolutionFilePath,
            _analyzerConfigPaths);
        SourceInput = SymbolicContext.SourceInput;

        var configuration = AnalyzerConfiguration.FromOptions(analyzerOptions);
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

    public IReadOnlyList<string> AnalyzerConfigPaths => _analyzerConfigPaths;

    public IReadOnlyList<string> AdditionalFilePaths => _additionalFilePaths;

    public bool HasBaseline => _additionalFilePaths.Any(static path =>
        string.Equals(Path.GetFileName(path), "SharpProof.Baseline.json", StringComparison.OrdinalIgnoreCase));

    public int EffectSummaryFileCount => _additionalFilePaths.Count(static path =>
        Path.GetFileName(path).EndsWith(".SharpProof.EffectSummary.json", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetFileName(path), "SharpProof.EffectSummary.json", StringComparison.OrdinalIgnoreCase));

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

    private static ImmutableArray<string> NormalizePaths(IEnumerable<string>? paths)
    {
        return paths?
                   .Where(static path => !string.IsNullOrWhiteSpace(path))
                   .Select(static path => NormalizeOptionalPath(path)!)
                   .Distinct(PathComparer)
                   .OrderBy(static path => path, PathComparer)
                   .ToImmutableArray() ?? ImmutableArray<string>.Empty;
    }

    private static string? NormalizeOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        return Path.GetFullPath(path!.Trim());
    }

    private static StringComparer PathComparer =>
        Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
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
