using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicProjectConfiguration
{
    private SymbolicProjectConfiguration(
        SmtAnalysisOptions smtOptions,
        SymbolicAnalysisLimits analysisLimits)
    {
        SmtOptions = smtOptions;
        AnalysisLimits = analysisLimits;
    }

    public SmtAnalysisOptions SmtOptions { get; }

    public SymbolicAnalysisLimits AnalysisLimits { get; }

    public static SymbolicProjectConfiguration FromAnalyzerOptions(AnalyzerOptions analyzerOptions)
    {
        if (analyzerOptions == null) throw new ArgumentNullException(nameof(analyzerOptions));

        var mode = GetSmtMode(analyzerOptions, SmtAnalysisOptions.Default.Mode);
        var defaults = SmtAnalysisOptions.ForMode(mode);
        var smtOptions = new SmtAnalysisOptions(
                mode,
                TimeSpan.FromMilliseconds(GetPositiveInt(
                    analyzerOptions,
                    "sharpproof_smt_timeout_ms",
                    (int)defaults.QueryTimeout.TotalMilliseconds)),
                TimeSpan.FromMilliseconds(GetPositiveInt(
                    analyzerOptions,
                    "sharpproof_smt_method_budget_ms",
                    (int)defaults.MethodBudget.TotalMilliseconds)),
                GetPositiveInt(
                    analyzerOptions,
                    "sharpproof_smt_max_path_conditions",
                    defaults.MaxPathConditions),
                GetPositiveInt(
                    analyzerOptions,
                    "sharpproof_smt_max_expression_nodes",
                    defaults.MaxExpressionNodes),
                true)
            .WithLifecycle(new SmtSolverLifecycleOptions(
                GetNonNegativeInt(
                    analyzerOptions,
                    "sharpproof_smt_transient_retry_count",
                    SmtSolverLifecycleOptions.Default.MaxTransientRetries),
                GetBool(
                    analyzerOptions,
                    "sharpproof_smt_recycle_context_on_transient_failure",
                    true),
                GetBool(
                    analyzerOptions,
                    "sharpproof_smt_dispose_thread_context_on_service_dispose",
                    false)));

        var analysisDefaults = SymbolicAnalysisLimits.Default;
        var analysisLimits = new SymbolicAnalysisLimits(
            GetPositiveInt(
                analyzerOptions,
                "sharpproof_analysis_max_merged_if_else_facts",
                analysisDefaults.MaxMergedIfElseFacts),
            GetPositiveInt(
                analyzerOptions,
                "sharpproof_analysis_max_merged_switch_facts",
                analysisDefaults.MaxMergedSwitchFacts),
            GetPositiveInt(
                analyzerOptions,
                "sharpproof_analysis_max_merged_try_facts",
                analysisDefaults.MaxMergedTryFacts),
            GetPositiveInt(
                analyzerOptions,
                "sharpproof_analysis_max_try_completion_branches",
                analysisDefaults.MaxTryCompletionBranches),
            GetPositiveInt(
                analyzerOptions,
                "sharpproof_analysis_max_finite_foreach_element_facts",
                analysisDefaults.MaxFiniteForeachElementFacts),
            GetPositiveInt(
                analyzerOptions,
                "sharpproof_analysis_max_scoped_block_completion_statements",
                analysisDefaults.MaxScopedBlockCompletionStatements),
            GetPositiveInt(
                analyzerOptions,
                "sharpproof_analysis_max_structural_null_state_depth",
                analysisDefaults.MaxStructuralNullStateDepth),
            GetPositiveInt(
                analyzerOptions,
                "sharpproof_analysis_max_merged_path_conditions",
                analysisDefaults.MaxMergedPathConditions),
            GetPositiveInt(
                analyzerOptions,
                "sharpproof_analysis_max_mergeable_facts_per_target_per_state",
                analysisDefaults.MaxMergeableFactsPerTargetPerState),
            GetPositiveInt(
                analyzerOptions,
                "sharpproof_analysis_max_fact_choice_combinations_per_target",
                analysisDefaults.MaxFactChoiceCombinationsPerTarget),
            GetPositiveInt(
                analyzerOptions,
                "sharpproof_analysis_max_guard_facts_per_target_per_state",
                analysisDefaults.MaxGuardFactsPerTargetPerState));

        return new SymbolicProjectConfiguration(smtOptions, analysisLimits);
    }

    private static SmtAnalysisMode GetSmtMode(AnalyzerOptions options, SmtAnalysisMode fallback)
    {
        if (!TryGetGlobalOption(options, "sharpproof_smt_mode", out var value)) return fallback;

        return SmtConfigurationValueRegistry.TryParseMode(value, out var mode) ? mode : fallback;
    }

    private static int GetPositiveInt(AnalyzerOptions options, string key, int fallback)
    {
        return AnalyzerConfigurationValueReader.GetInteger(options, key, fallback, 1);
    }

    private static int GetNonNegativeInt(AnalyzerOptions options, string key, int fallback)
    {
        return AnalyzerConfigurationValueReader.GetInteger(options, key, fallback, 0);
    }

    private static bool GetBool(AnalyzerOptions options, string key, bool fallback)
    {
        if (!TryGetGlobalOption(options, key, out var value)) return fallback;

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback
        };
    }

    private static bool TryGetGlobalOption(AnalyzerOptions options, string key, out string value)
    {
        return AnalyzerConfigurationValueReader.TryGetGlobalOption(options, key, out value);
    }
}

internal sealed class SymbolicProjectQueryContext
{
    private readonly ImmutableArray<string> _analyzerConfigPaths;
    private readonly ImmutableArray<string> _additionalFilePaths;

    public SymbolicProjectQueryContext(
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
        SourceInput = SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation);
        Configuration = SymbolicProjectConfiguration.FromAnalyzerOptions(analyzerOptions);
    }

    public Compilation Compilation { get; }

    public SyntaxTree SyntaxTree { get; }

    public AnalyzerOptions AnalyzerOptions { get; }

    public SymbolicSourceInput SourceInput { get; }

    public SymbolicProjectConfiguration Configuration { get; }

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

    public SharpProofAnalysisSession CreateAnalysisSession(
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicSourceQueryFilter? filter = null)
    {
        return SharpProofAnalysisSession.Create(
            SourceInput,
            CreateQueryOptions(
                smtAnalysis,
                impliedConditions,
                includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts,
                filter));
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
