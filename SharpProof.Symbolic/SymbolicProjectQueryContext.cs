using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicProjectConfiguration(
    SmtAnalysisOptions smtOptions,
    SharpProofAnalysisBudget analysisLimits)
{
    public SmtAnalysisOptions SmtOptions { get; } = smtOptions;

    public SharpProofAnalysisBudget AnalysisLimits { get; } = analysisLimits;

    public static SymbolicProjectConfiguration FromAnalyzerOptions(AnalyzerOptions analyzerOptions)
    {
        if (analyzerOptions == null) throw new ArgumentNullException(nameof(analyzerOptions));

        var mode = GetSmtMode(analyzerOptions, SmtAnalysisOptions.Default.Mode);
        var defaults = SmtAnalysisOptions.ForMode(mode);
        var smtOptions = new SmtAnalysisOptions(
                mode,
                TimeSpan.FromMilliseconds(AnalyzerConfigurationValueReader.GetInteger(
                    analyzerOptions,
                    "sharpproof_smt_timeout_ms",
                    (int)defaults.QueryTimeout.TotalMilliseconds,
                    1)),
                TimeSpan.FromMilliseconds(AnalyzerConfigurationValueReader.GetInteger(
                    analyzerOptions,
                    "sharpproof_smt_method_budget_ms",
                    (int)defaults.MethodBudget.TotalMilliseconds,
                    1)),
                AnalyzerConfigurationValueReader.GetInteger(
                    analyzerOptions,
                    "sharpproof_smt_max_path_conditions",
                    defaults.MaxPathConditions,
                    1),
                AnalyzerConfigurationValueReader.GetInteger(
                    analyzerOptions,
                    "sharpproof_smt_max_expression_nodes",
                    defaults.MaxExpressionNodes,
                    1),
                true)
            .WithLifecycle(new SmtSolverLifecycleOptions(
                AnalyzerConfigurationValueReader.GetInteger(
                    analyzerOptions,
                    "sharpproof_smt_transient_retry_count",
                    SmtSolverLifecycleOptions.Default.MaxTransientRetries,
                    0),
                GetBool(
                    analyzerOptions,
                    "sharpproof_smt_recycle_context_on_transient_failure",
                    true),
                GetBool(
                    analyzerOptions,
                    "sharpproof_smt_dispose_thread_context_on_service_dispose",
                    false)));

        var analysisLimits = SharpProofAnalysisBudget.FromNamedValues(
            SharpProofAnalysisBudget.Default,
            (name, fallback) => AnalyzerConfigurationValueReader.GetInteger(
                analyzerOptions,
                "sharpproof_analysis_max_" + name.Replace('-', '_'),
                fallback,
                1));

        return new SymbolicProjectConfiguration(smtOptions, analysisLimits);
    }

    private static SmtAnalysisMode GetSmtMode(AnalyzerOptions options, SmtAnalysisMode fallback)
    {
        if (!AnalyzerConfigurationValueReader.TryGetGlobalOption(
                options, "sharpproof_smt_mode", out var value)) return fallback;

        return SmtConfigurationValueRegistry.TryParseMode(value, out var mode) ? mode : fallback;
    }

    private static bool GetBool(AnalyzerOptions options, string key, bool fallback)
    {
        if (!AnalyzerConfigurationValueReader.TryGetGlobalOption(options, key, out var value)) return fallback;

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback
        };
    }

}

internal sealed class SymbolicProjectQueryContext(
    Compilation compilation,
    SyntaxTree syntaxTree,
    AnalyzerOptions analyzerOptions,
    string projectName,
    string? projectFilePath = null,
    string? solutionFilePath = null,
    IEnumerable<string>? analyzerConfigPaths = null)
{
    private readonly ImmutableArray<string> _analyzerConfigPaths = NormalizePaths(analyzerConfigPaths);
    private readonly ImmutableArray<string> _additionalFilePaths = NormalizePaths(
        (analyzerOptions ?? throw new ArgumentNullException(nameof(analyzerOptions)))
        .AdditionalFiles.Select(static file => file.Path));

    public Compilation Compilation { get; } = ValidateCompilation(compilation, syntaxTree);

    public SyntaxTree SyntaxTree { get; } = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));

    public AnalyzerOptions AnalyzerOptions { get; } =
        analyzerOptions ?? throw new ArgumentNullException(nameof(analyzerOptions));

    public SymbolicSourceInput SourceInput { get; } = SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation);

    public SymbolicProjectConfiguration Configuration { get; } =
        SymbolicProjectConfiguration.FromAnalyzerOptions(analyzerOptions);

    public string ProjectName { get; } = NormalizeProjectName(projectName);

    public string? ProjectFilePath { get; } = NormalizeOptionalPath(projectFilePath);

    public string? SolutionFilePath { get; } = NormalizeOptionalPath(solutionFilePath);

    public IReadOnlyList<string> AnalyzerConfigPaths => _analyzerConfigPaths;

    public IReadOnlyList<string> AdditionalFilePaths => _additionalFilePaths;

    public bool HasBaseline => _additionalFilePaths.Any(static path =>
        string.Equals(Path.GetFileName(path), "SharpProof.Baseline.json", StringComparison.OrdinalIgnoreCase));

    public int EffectSummaryFileCount => _additionalFilePaths.Count(static path =>
        Path.GetFileName(path).EndsWith(".SharpProof.EffectSummary.json", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetFileName(path), "SharpProof.EffectSummary.json", StringComparison.OrdinalIgnoreCase));

    public SymbolicQueryOptions CreateQueryOptions(
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicSourceQueryFilter? filter = null)
    {
        return new SymbolicQueryOptions(
                smtAnalysis: smtAnalysis,
                impliedConditions: impliedConditions,
                includeExpressionProgramPoints: includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts: includeCurrentStatementCompletionFacts,
                filter: filter)
            .WithAnalysisLimits(Configuration.AnalysisLimits);
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

    private static Compilation ValidateCompilation(Compilation? compilation, SyntaxTree? syntaxTree)
    {
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));
        if (!compilation.SyntaxTrees.Contains(syntaxTree))
            throw new ArgumentException("Syntax tree must belong to the project compilation.", nameof(syntaxTree));
        return compilation;
    }

    private static string NormalizeProjectName(string? projectName) =>
        string.IsNullOrWhiteSpace(projectName)
            ? throw new ArgumentException("Project name is required.", nameof(projectName))
            : projectName!.Trim();

    private static string? NormalizeOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        return Path.GetFullPath(path!.Trim());
    }

    private static StringComparer PathComparer =>
        Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
