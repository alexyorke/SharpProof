namespace SharpProof.Analyzer.Configuration
{

    internal static class ConfigKeys
    {
        public const string KnownImpureMethods = "sharpproof_known_impure_methods";
        public const string KnownPureMethods = "sharpproof_known_pure_methods";
        public const string KnownImpureNamespaces = "sharpproof_known_impure_namespaces";
        public const string KnownImpureTypes = "sharpproof_known_impure_types";
        public const string PurityProfile = "sharpproof_purity_profile";

        /// <summary>When false, SP0004 (missing [EnforcePure]) is not reported. Default: true.</summary>
        public const string SuggestMissingEnforcePure = "sharpproof_suggest_missing_enforce_pure";
        public const string SuggestMissingEnforcePureScope = "sharpproof_suggest_missing_enforce_pure_scope";
        public const string SuggestMissingEnforcePureExcludeGenerated = "sharpproof_suggest_missing_enforce_pure_exclude_generated";
        public const string SuggestMissingEnforcePureExcludeTests = "sharpproof_suggest_missing_enforce_pure_exclude_tests";
        public const string SuggestMissingEnforcePureMinComplexity = "sharpproof_suggest_missing_enforce_pure_min_complexity";
        public const string SuggestMissingEnforcePureNamespaceFilters = "sharpproof_suggest_missing_enforce_pure_namespace_filters";
        public const string EmitExplanations = "sharpproof_emit_explanations";
        public const string ReportBclFallbackGuesses = "sharpproof_report_bcl_fallback_guesses";
        public const string RuntimeHazardMode = "sharpproof_runtime_hazard_mode";
        public const string ReportExceptions = "sharpproof_report_exceptions";
        public const string CheckedExceptions = "sharpproof_checked_exceptions";
        public const string EnableEffectSummaryJson = "sharpproof_enable_effect_summary_json";
        public const string SmtMode = "sharpproof_smt_mode";
        public const string SmtTimeoutMs = "sharpproof_smt_timeout_ms";
        public const string SmtMethodBudgetMs = "sharpproof_smt_method_budget_ms";
        public const string SmtMaxPathConditions = "sharpproof_smt_max_path_conditions";
        public const string SmtMaxExpressionNodes = "sharpproof_smt_max_expression_nodes";
    }
}
