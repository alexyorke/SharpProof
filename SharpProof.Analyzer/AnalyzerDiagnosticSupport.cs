namespace SharpProof.Analyzer;
internal static class ContractDiagnosticSupport {
    internal static void ReportInvalidEffectConfigurations(MethodBodyAnalysisContext context) {
        var effects = context.State.GetMethodEffects(context.CancellationToken);
        foreach (var reason in effects.UnknownReasons
                     .Where(static reason => reason.IsConfigurationRelated)
                     .Distinct())
            context.ReportDiagnostic(Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get("InvalidAnalyzerConfigurationRule"),
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node),
                "sharpproof_effect_contract.*",
                "<configured contract>",
                reason.Message));
    }
    internal static string FormatUnknownReason(SymbolicConditionProofResult proof, string contractAttributeName) {
        if (proof.UnknownReason != SymbolicUnknownReason.None &&
            proof.UnknownReason != SymbolicUnknownReason.Unknown)
            return proof.UnknownReason.ToString();
        return proof.Reason switch {
            "condition_parse_failure" => "condition parse failure",
            "condition_binding_failure" => "condition binding failure",
            "condition_not_supported" => "condition is not supported by the current bounded proof engine",
            "smt_required" => "Z3 is required for [" + contractAttributeName + "] verification",
            _ when string.IsNullOrWhiteSpace(proof.Reason) => "unknown",
            _ => proof.Reason.Replace('_', ' ')
        };
    }
}
