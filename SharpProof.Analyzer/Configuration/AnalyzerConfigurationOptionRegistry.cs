namespace SharpProof.Analyzer.Configuration;
internal static class AnalyzerConfigurationOptionRegistry {
    public static ImmutableArray<AnalyzerConfigurationOption> All { get; } = [
        Positive("sharpproof_analysis_max_fact_choice_combinations_per_target"),
        Positive("sharpproof_analysis_max_finite_foreach_element_facts"),
        Positive("sharpproof_analysis_max_guard_facts_per_target_per_state"),
        Positive("sharpproof_analysis_max_mergeable_facts_per_target_per_state"),
        Positive("sharpproof_analysis_max_merged_if_else_facts"),
        Positive("sharpproof_analysis_max_merged_path_conditions"),
        Positive("sharpproof_analysis_max_merged_switch_facts"),
        Positive("sharpproof_analysis_max_merged_try_facts"),
        Positive("sharpproof_analysis_max_scoped_block_completion_statements"),
        Positive("sharpproof_analysis_max_structural_null_state_depth"),
        Positive("sharpproof_analysis_max_try_completion_branches"),
        Bool("sharpproof_smt_dispose_thread_context_on_service_dispose"),
        Positive("sharpproof_smt_max_expression_nodes"),
        Positive("sharpproof_smt_max_path_conditions"),
        Positive("sharpproof_smt_method_budget_ms"),
        new("sharpproof_smt_mode", AnalyzerConfigurationValueKind.SmtMode, ["bounded", "deep"]),
        Bool("sharpproof_smt_recycle_context_on_transient_failure"),
        Positive("sharpproof_smt_timeout_ms"),
        new("sharpproof_smt_transient_retry_count", AnalyzerConfigurationValueKind.NonNegativeInteger)
    ];
    internal static bool IsAcceptedValue(AnalyzerConfigurationOption option, string? value) =>
        !string.IsNullOrWhiteSpace(value) && option.AllowedValues.Contains(value!.Trim().ToLowerInvariant(), StringComparer.Ordinal);
    private static AnalyzerConfigurationOption Positive(string key) =>
        new(key, AnalyzerConfigurationValueKind.PositiveInteger);
    private static AnalyzerConfigurationOption Bool(string key) =>
        new(key, AnalyzerConfigurationValueKind.Bool);
}
internal sealed record AnalyzerConfigurationOption(
    string Key,
    AnalyzerConfigurationValueKind ValueKind,
    ImmutableArray<string> AllowedValues = default);
internal enum AnalyzerConfigurationValueKind { Bool, NonNegativeInteger, PositiveInteger, SmtMode }
