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
            .Add("sharpproof.contract.attribute", attributeName)
            .Add("sharpproof.contract.argument", argument)
            .Add("sharpproof.contract.invalid_reason", reason);

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
            AnalyzerDiagnosticCatalog.Get("InvalidContractArgumentRule"),
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
