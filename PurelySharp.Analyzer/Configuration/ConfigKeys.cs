namespace PurelySharp.Analyzer.Configuration
{

    internal static class ConfigKeys
    {
        public const string KnownImpureMethods = "purelysharp_known_impure_methods";
        public const string KnownPureMethods = "purelysharp_known_pure_methods";
        public const string KnownImpureNamespaces = "purelysharp_known_impure_namespaces";
        public const string KnownImpureTypes = "purelysharp_known_impure_types";
        public const string PurityProfile = "purelysharp_purity_profile";

        /// <summary>When false, PS0004 (missing [EnforcePure]) is not reported. Default: true.</summary>
        public const string SuggestMissingEnforcePure = "purelysharp_suggest_missing_enforce_pure";
        public const string SuggestMissingEnforcePureScope = "purelysharp_suggest_missing_enforce_pure_scope";
        public const string SuggestMissingEnforcePureExcludeGenerated = "purelysharp_suggest_missing_enforce_pure_exclude_generated";
        public const string SuggestMissingEnforcePureExcludeTests = "purelysharp_suggest_missing_enforce_pure_exclude_tests";
        public const string SuggestMissingEnforcePureMinComplexity = "purelysharp_suggest_missing_enforce_pure_min_complexity";
        public const string SuggestMissingEnforcePureNamespaceFilters = "purelysharp_suggest_missing_enforce_pure_namespace_filters";
        public const string EmitExplanations = "purelysharp_emit_explanations";
        public const string ReportBclFallbackGuesses = "purelysharp_report_bcl_fallback_guesses";
        public const string RuntimeHazardMode = "purelysharp_runtime_hazard_mode";
        public const string ReportExceptions = "purelysharp_report_exceptions";
        public const string CheckedExceptions = "purelysharp_checked_exceptions";
        public const string EnableEffectSummaryJson = "purelysharp_enable_effect_summary_json";
        public const string SmtMode = "purelysharp_smt_mode";
        public const string SmtTimeoutMs = "purelysharp_smt_timeout_ms";
        public const string SmtMethodBudgetMs = "purelysharp_smt_method_budget_ms";
        public const string SmtMaxPathConditions = "purelysharp_smt_max_path_conditions";
        public const string SmtMaxExpressionNodes = "purelysharp_smt_max_expression_nodes";
    }
}
