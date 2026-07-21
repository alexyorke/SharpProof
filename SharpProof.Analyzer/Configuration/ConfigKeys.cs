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
}
