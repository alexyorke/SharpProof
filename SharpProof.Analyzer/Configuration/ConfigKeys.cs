namespace SharpProof.Analyzer.Configuration;

internal static class ConfigKeys {
    public const string AttributeStubNamespaces = "sharpproof_attribute_stub_namespaces";
    public const string TrustedBoundaryReviewMode = "sharpproof_trusted_boundary_review_mode";


    public const string SuggestInferredContracts = "sharpproof_suggest_inferred_contracts";
    public const string SuggestInferredContractsScope = "sharpproof_suggest_inferred_contracts_scope";
    public const string SuggestInferredContractsKinds = "sharpproof_suggest_inferred_contracts_kinds";
    public const string SuggestInferredContractsMinimumConfidence =
        "sharpproof_suggest_inferred_contracts_minimum_confidence";

    public const string EmitExplanations = "sharpproof_emit_explanations";
    public const string RuntimeHazardMode = "sharpproof_runtime_hazard_mode";
    public const string ReportNullableInconclusive = "sharpproof_report_nullable_inconclusive";
    public const string SuppressProvenDiagnostics = "sharpproof_suppress_proven_diagnostics";
    public const string SuppressionDiagnosticIds = "sharpproof_suppression_diagnostic_ids";
    public const string ReportExceptions = "sharpproof_report_exceptions";
    public const string CheckedExceptions = "sharpproof_checked_exceptions";
    public const string SmtMode = "sharpproof_smt_mode";
    public const string SmtTimeoutMs = "sharpproof_smt_timeout_ms";
    public const string SmtMethodBudgetMs = "sharpproof_smt_method_budget_ms";
    public const string SmtMaxPathConditions = "sharpproof_smt_max_path_conditions";
    public const string SmtMaxExpressionNodes = "sharpproof_smt_max_expression_nodes";
    public const string SmtTransientRetryCount = "sharpproof_smt_transient_retry_count";
    public const string SmtRecycleContextOnTransientFailure =
        "sharpproof_smt_recycle_context_on_transient_failure";
    public const string SmtDisposeThreadContextOnServiceDispose =
        "sharpproof_smt_dispose_thread_context_on_service_dispose";
    public const string AnalysisMaxMergedIfElseFacts = "sharpproof_analysis_max_merged_if_else_facts";
    public const string AnalysisMaxMergedSwitchFacts = "sharpproof_analysis_max_merged_switch_facts";
    public const string AnalysisMaxMergedTryFacts = "sharpproof_analysis_max_merged_try_facts";
    public const string AnalysisMaxTryCompletionBranches = "sharpproof_analysis_max_try_completion_branches";
    public const string AnalysisMaxFiniteForeachElementFacts =
        "sharpproof_analysis_max_finite_foreach_element_facts";
    public const string AnalysisMaxScopedBlockCompletionStatements =
        "sharpproof_analysis_max_scoped_block_completion_statements";
    public const string AnalysisMaxStructuralNullStateDepth =
        "sharpproof_analysis_max_structural_null_state_depth";
    public const string AnalysisMaxMergedPathConditions = "sharpproof_analysis_max_merged_path_conditions";
    public const string AnalysisMaxMergeableFactsPerTargetPerState =
        "sharpproof_analysis_max_mergeable_facts_per_target_per_state";
    public const string AnalysisMaxFactChoiceCombinationsPerTarget =
        "sharpproof_analysis_max_fact_choice_combinations_per_target";
    public const string AnalysisMaxGuardFactsPerTargetPerState =
        "sharpproof_analysis_max_guard_facts_per_target_per_state";
}
