namespace SharpProof.Analyzer;

internal static class InvalidContractArgumentDiagnostics
{
    internal static Diagnostic Create(
        string attributeName,
        string argument,
        string reason,
        Location location,
        ISymbol? baselineSymbol = null,
        SyntaxTree? syntaxTree = null)
    {
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.ContractAttributeProperty, attributeName)
            .Add(SharpProofDiagnostics.ContractArgumentProperty, argument)
            .Add(SharpProofDiagnostics.ContractInvalidReasonProperty, reason);

        properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            baselineSymbol,
            syntaxTree,
            "InvalidContractArgument",
            argument,
            attributeName + ":" + argument + ":" + reason,
            location,
            argument,
            "invalid",
            reason);

        return Diagnostic.Create(
            SharpProofDiagnostics.InvalidContractArgumentRule,
            location,
            null,
            properties,
            new object[]
            {
                attributeName,
                argument,
                reason
            });
    }
}
