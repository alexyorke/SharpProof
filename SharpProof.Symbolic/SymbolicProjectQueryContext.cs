namespace SharpProof.Symbolic;
internal sealed record SymbolicProjectConfiguration(SmtAnalysisOptions SmtOptions, SharpProofAnalysisBudget AnalysisLimits) {
    public static SymbolicProjectConfiguration FromAnalyzerOptions(AnalyzerOptions analyzerOptions) {
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
                GetBool(analyzerOptions, "sharpproof_smt_recycle_context_on_transient_failure", true),
                GetBool(analyzerOptions, "sharpproof_smt_dispose_thread_context_on_service_dispose", false)));
        var analysisLimits = SharpProofAnalysisBudget.FromNamedValues(
            SharpProofAnalysisBudget.Default,
            (name, fallback) => AnalyzerConfigurationValueReader.GetInteger(
                analyzerOptions,
                "sharpproof_analysis_max_" + name.Replace('-', '_'),
                fallback,
                1));
        return new SymbolicProjectConfiguration(smtOptions, analysisLimits);
    }
    private static SmtAnalysisMode GetSmtMode(AnalyzerOptions options, SmtAnalysisMode fallback) {
        if (!AnalyzerConfigurationValueReader.TryGetGlobalOption(options, "sharpproof_smt_mode", out var value)) return fallback;
        return value.Trim().ToLowerInvariant() switch {
            "bounded" => SmtAnalysisMode.Bounded,
            "deep" => SmtAnalysisMode.Deep,
            _ => fallback
        };
    }
    private static bool GetBool(AnalyzerOptions options, string key, bool fallback) {
        if (!AnalyzerConfigurationValueReader.TryGetGlobalOption(options, key, out var value)) return fallback;
        return value.Trim().ToLowerInvariant() switch {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback
        };
    }
}
