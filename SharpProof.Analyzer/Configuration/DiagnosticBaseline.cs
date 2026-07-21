namespace SharpProof.Analyzer.Configuration;

internal sealed class DiagnosticBaseline {
    private const string BaselineFileName = "SharpProof.Baseline.json";

    public static readonly DiagnosticBaseline Empty = new(ImmutableArray<ResolvedBaselineEntry>.Empty);

    private readonly ImmutableArray<ResolvedBaselineEntry> _entries;

    private DiagnosticBaseline(ImmutableArray<ResolvedBaselineEntry> entries) => _entries = entries;

    public static DiagnosticBaseline FromOptions(
        AnalyzerOptions options,
        CancellationToken cancellationToken) {
        var builder = ImmutableArray.CreateBuilder<ResolvedBaselineEntry>();
        foreach (var additionalFile in options.AdditionalFiles) {
            if (!string.Equals(Path.GetFileName(additionalFile.Path), BaselineFileName,
                    StringComparison.OrdinalIgnoreCase)) continue;

            var text = additionalFile.GetText(cancellationToken)?.ToString();
            if (string.IsNullOrWhiteSpace(text)) continue;

            foreach (var entry in ParseEntries(text!, additionalFile.Path)) builder.Add(entry);
        }

        return builder.Count == 0 ? Empty : new DiagnosticBaseline(builder.ToImmutable());
    }

    public bool IsSuppressed(Diagnostic diagnostic) {
        if (_entries.IsDefaultOrEmpty) return false;

        if (!TryGetProperty(diagnostic.Properties, DiagnosticPropertyNames.BaselineSymbolProperty, out var symbolId) ||
            !TryGetProperty(diagnostic.Properties, DiagnosticPropertyNames.BaselinePathProperty, out var sourcePath))
            return false;

        var symbolIds = GetDiagnosticSymbolIds(diagnostic.Properties, symbolId);
        foreach (var entry in _entries)
            foreach (var candidateSymbolId in symbolIds)
                if (Matches(entry, diagnostic.Id, candidateSymbolId, sourcePath, diagnostic))
                    return true;

        return false;
    }

    internal static ImmutableArray<string> GetSymbolIds(ISymbol symbol) {
        var builder = ImmutableArray.CreateBuilder<string>();
        var documentationId = DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition);
        if (!string.IsNullOrWhiteSpace(documentationId)) builder.Add(documentationId!);

        builder.Add(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));

        if (symbol is IMethodSymbol methodSymbol && methodSymbol.ContainingType != null)
            builder.Add(GetCompactMethodId(methodSymbol));

        return builder.Distinct(StringComparer.Ordinal).ToImmutableArray();
    }

    internal static string GetPreferredSymbolId(ISymbol symbol) {
        if (symbol is IMethodSymbol methodSymbol && methodSymbol.ContainingType != null)
            return GetCompactMethodId(methodSymbol);

        return GetSymbolIds(symbol)[0];
    }

    internal static string NormalizePath(string path) => BaselineSchemaContract.NormalizePath(path);

    private static string GetCompactMethodId(IMethodSymbol methodSymbol) {
        var containingType = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var methodName = methodSymbol.MetadataName == ".ctor" ? "#ctor" : methodSymbol.MetadataName;
        return "M:" + containingType + "." + methodName;
    }

    private static ImmutableArray<ResolvedBaselineEntry> ParseEntries(
        string json,
        string baselinePath) {
        var builder = ImmutableArray.CreateBuilder<ResolvedBaselineEntry>();
        var baseDirectory = GetBaseDirectory(baselinePath);
        try {
            using var document = JsonDocument.Parse(json, BaselineSchemaContract.DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object) {
                return builder.ToImmutable();
            }

            if (!BaselineSchemaContract.TryValidateTree(document.RootElement, out var failure)) {
                return builder.ToImmutable();
            }

            if (document.RootElement.TryGetProperty("diagnostics", out var diagnostics) &&
                diagnostics.ValueKind == JsonValueKind.Array)
                foreach (var entry in diagnostics.EnumerateArray())
                    if (entry.ValueKind == JsonValueKind.Object) {
                        var fields = BaselineSchemaContract.ReadEntryFields(entry);
                        if (fields.IsValid) {
                            var value = fields.ToEntry();
                            builder.Add(new ResolvedBaselineEntry(
                                value with { Path = NormalizePath(value.Path) },
                                MakeAbsolutePath(value.Path, baseDirectory)));
                        }
                    }
        }
        catch (JsonException) { }

        return builder.ToImmutable();
    }

    private static string GetBaseDirectory(string baselinePath) {
        if (string.IsNullOrWhiteSpace(baselinePath)) return string.Empty;

        var directory = Path.GetDirectoryName(baselinePath);
        return string.IsNullOrWhiteSpace(directory) ? string.Empty : NormalizePath(directory!);
    }

    private static bool TryGetProperty(
        ImmutableDictionary<string, string?> properties,
        string propertyName,
        out string value) {
        if (properties.TryGetValue(propertyName, out var propertyValue) &&
            !string.IsNullOrWhiteSpace(propertyValue)) {
            value = propertyValue!.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static ImmutableArray<string> GetDiagnosticSymbolIds(
        ImmutableDictionary<string, string?> properties,
        string primarySymbolId) {
        var builder = ImmutableArray.CreateBuilder<string>();
        builder.Add(primarySymbolId);
        if (TryGetProperty(properties, DiagnosticPropertyNames.BaselineSymbolAliasesProperty, out var aliases))
            foreach (var alias in aliases.Split(['\n'], StringSplitOptions.RemoveEmptyEntries)) {
                var trimmed = alias.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed)) builder.Add(trimmed);
            }

        return builder.Distinct(StringComparer.Ordinal).ToImmutableArray();
    }

    private static bool Matches(
        ResolvedBaselineEntry resolved,
        string diagnosticId,
        string symbolId,
        string sourcePath,
        Diagnostic diagnostic) {
        var entry = resolved.Entry;
        var normalizedSourcePath = NormalizePath(sourcePath);
        int? line = null;
        int? column = null;
        if (entry.Line.HasValue || entry.Column.HasValue) {
            if (diagnostic.Location == Location.None || !diagnostic.Location.IsInSource) return false;
            var start = diagnostic.Location.GetLineSpan().StartLinePosition;
            line = start.Line + 1;
            column = start.Character + 1;
        }

        return BaselineSchemaContract.Matches(
            entry,
            new BaselineEntry(
                diagnosticId,
                symbolId,
                normalizedSourcePath,
                Line: line,
                Column: column,
                Contract: GetOptionalProperty(diagnostic, DiagnosticPropertyNames.BaselineContractProperty),
                OperationKind: GetOptionalProperty(diagnostic, DiagnosticPropertyNames.BaselineOperationKindProperty),
                EvidenceKey: GetOptionalProperty(diagnostic, DiagnosticPropertyNames.BaselineEvidenceKeyProperty)),
            resolved.AbsolutePath);
    }

    private static string? GetOptionalProperty(Diagnostic diagnostic, string propertyName) =>
        TryGetProperty(diagnostic.Properties, propertyName, out var value) ? value : null;

    private static string MakeAbsolutePath(string path, string baseDirectory) =>
        string.IsNullOrWhiteSpace(baseDirectory)
            ? string.Empty
            : NormalizePath(System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(baseDirectory, path));

    readonly record struct ResolvedBaselineEntry(BaselineEntry Entry, string AbsolutePath);
}
