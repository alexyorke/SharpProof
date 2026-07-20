namespace SharpProof.Analyzer.Configuration;

internal static class BaselineDiagnosticProperties {
    internal static ImmutableDictionary<string, string?> Add(
        ImmutableDictionary<string, string?> properties,
        ISymbol symbol,
        SyntaxTree syntaxTree,
        string? operationKind = null,
        string? contractText = null,
        string? evidenceKey = null) {
        properties = Add(
            properties,
            DiagnosticBaseline.GetPreferredSymbolId(symbol),
            syntaxTree.FilePath ?? string.Empty,
            operationKind,
            contractText,
            evidenceKey);
        var aliases = DiagnosticBaseline.GetSymbolIds(symbol);
        if (!aliases.IsDefaultOrEmpty)
            properties = properties.SetItem(
                DiagnosticPropertyNames.BaselineSymbolAliasesProperty,
                string.Join("\n", aliases));

        return properties;
    }

    internal static ImmutableDictionary<string, string?> Add(
        ImmutableDictionary<string, string?> properties,
        string symbolId,
        string path,
        string? operationKind = null,
        string? contractText = null,
        string? evidenceKey = null) {
        properties = SharpProofEvidenceSchema.AddDiagnosticProperties(properties);

        if (!string.IsNullOrWhiteSpace(symbolId))
            properties = properties.SetItem(DiagnosticPropertyNames.BaselineSymbolProperty, symbolId.Trim());

        var normalizedPath = DiagnosticBaseline.NormalizePath(path);
        if (!string.IsNullOrWhiteSpace(normalizedPath))
            properties = properties.SetItem(DiagnosticPropertyNames.BaselinePathProperty, normalizedPath);

        if (!string.IsNullOrWhiteSpace(operationKind))
            properties = properties.SetItem(DiagnosticPropertyNames.BaselineOperationKindProperty, operationKind!.Trim());

        if (!string.IsNullOrWhiteSpace(contractText))
            properties = properties.SetItem(DiagnosticPropertyNames.BaselineContractProperty, contractText!.Trim());

        if (!string.IsNullOrWhiteSpace(evidenceKey))
            properties = properties.SetItem(DiagnosticPropertyNames.BaselineEvidenceKeyProperty, evidenceKey!.Trim());

        return properties;
    }
}
