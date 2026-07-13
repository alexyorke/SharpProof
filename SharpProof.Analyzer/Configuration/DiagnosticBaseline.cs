using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer.Configuration;

internal sealed class DiagnosticBaseline
{
    private const string BaselineFileName = "SharpProof.Baseline.json";

    private static readonly JsonDocumentOptions BaselineJsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static readonly DiagnosticBaseline Empty = new(ImmutableArray<BaselineEntry>.Empty);

    private readonly ImmutableArray<BaselineEntry> _entries;

    private DiagnosticBaseline(ImmutableArray<BaselineEntry> entries)
    {
        _entries = entries;
    }

    public static DiagnosticBaseline FromOptions(AnalyzerOptions options, CancellationToken cancellationToken)
    {
        var builder = ImmutableArray.CreateBuilder<BaselineEntry>();
        foreach (var additionalFile in options.AdditionalFiles)
        {
            if (!string.Equals(Path.GetFileName(additionalFile.Path), BaselineFileName,
                    StringComparison.OrdinalIgnoreCase)) continue;

            var text = additionalFile.GetText(cancellationToken)?.ToString();
            if (text == null || string.IsNullOrWhiteSpace(text)) continue;

            foreach (var entry in ParseEntries(text, additionalFile.Path)) builder.Add(entry);
        }

        return builder.Count == 0 ? Empty : new DiagnosticBaseline(builder.ToImmutable());
    }

    public bool IsSuppressed(string diagnosticId, ISymbol symbol, SyntaxTree syntaxTree)
    {
        if (_entries.IsDefaultOrEmpty) return false;

        var symbolIds = GetSymbolIds(symbol);
        var sourcePath = syntaxTree.FilePath ?? string.Empty;

        foreach (var entry in _entries)
            foreach (var symbolId in symbolIds)
                if (entry.Matches(diagnosticId, symbolId, sourcePath))
                    return true;

        return false;
    }

    public bool IsSuppressed(Diagnostic diagnostic)
    {
        if (_entries.IsDefaultOrEmpty) return false;

        if (!TryGetProperty(diagnostic.Properties, SharpProofDiagnostics.BaselineSymbolProperty, out var symbolId) ||
            !TryGetProperty(diagnostic.Properties, SharpProofDiagnostics.BaselinePathProperty, out var sourcePath))
            return false;

        var symbolIds = GetDiagnosticSymbolIds(diagnostic.Properties, symbolId);
        foreach (var entry in _entries)
            foreach (var candidateSymbolId in symbolIds)
                if (entry.Matches(diagnostic.Id, candidateSymbolId, sourcePath, diagnostic))
                    return true;

        return false;
    }

    internal static ImmutableArray<string> GetSymbolIds(ISymbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var documentationId = DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition);
        if (!string.IsNullOrWhiteSpace(documentationId)) builder.Add(documentationId!);

        builder.Add(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));

        if (symbol is IMethodSymbol methodSymbol && methodSymbol.ContainingType != null)
            builder.Add(GetCompactMethodId(methodSymbol));

        return builder.Distinct(StringComparer.Ordinal).ToImmutableArray();
    }

    internal static string GetPreferredSymbolId(ISymbol symbol)
    {
        if (symbol is IMethodSymbol methodSymbol && methodSymbol.ContainingType != null)
            return GetCompactMethodId(methodSymbol);

        var documentationId = DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition);
        if (!string.IsNullOrWhiteSpace(documentationId)) return documentationId!;

        return symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    internal static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized.Substring(2);

        return normalized;
    }

    private static string GetCompactMethodId(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var methodName = methodSymbol.MetadataName == ".ctor" ? "#ctor" : methodSymbol.MetadataName;
        return "M:" + containingType + "." + methodName;
    }

    private static ImmutableArray<BaselineEntry> ParseEntries(string json, string baselinePath)
    {
        var builder = ImmutableArray.CreateBuilder<BaselineEntry>();
        var baseDirectory = GetBaseDirectory(baselinePath);
        try
        {
            using var document = JsonDocument.Parse(json, BaselineJsonOptions);
            if (!HasReadCompatibleEvidenceSchema(document.RootElement, requireDocumentSchema: true))
                return builder.ToImmutable();
            AddEntries(document.RootElement, baseDirectory, builder);
        }
        catch (JsonException)
        {
        }

        return builder.ToImmutable();
    }

    private static bool HasReadCompatibleEvidenceSchema(
        JsonElement element,
        bool requireDocumentSchema = false)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return !requireDocumentSchema &&
                   element.EnumerateArray().All(static item => HasReadCompatibleEvidenceSchema(item));

        if (element.ValueKind != JsonValueKind.Object) return true;

        var hasVersion = TryGetPropertyIgnoreCase(element, "evidenceSchemaVersion", out var versionElement);
        var hasCompatibility = TryGetPropertyIgnoreCase(
            element,
            "evidenceSchemaCompatibility",
            out var compatibilityElement);
        var requiresSchema = requireDocumentSchema ||
                             TryGetPropertyIgnoreCase(element, "diagnostics", out _) ||
                             (TryGetPropertyIgnoreCase(element, "id", out _) &&
                              TryGetPropertyIgnoreCase(element, "symbol", out _) &&
                              TryGetPropertyIgnoreCase(element, "path", out _));
        if (hasVersion || hasCompatibility || requiresSchema)
        {
            if (!hasVersion ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out var version) ||
                !SharpProofEvidenceSchema.IsReadCompatible(version))
                return false;

            if (!hasCompatibility ||
                compatibilityElement.ValueKind != JsonValueKind.String ||
                !string.Equals(
                    compatibilityElement.GetString(),
                    SharpProofEvidenceSchema.CompatibilityPolicy,
                    StringComparison.Ordinal))
                return false;
        }

        foreach (var property in element.EnumerateObject())
            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object &&
                !HasReadCompatibleEvidenceSchema(property.Value))
                return false;

        return true;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }

        value = default;
        return false;
    }

    private static void AddEntries(
        JsonElement element,
        string baseDirectory,
        ImmutableArray<BaselineEntry>.Builder builder)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) AddEntries(item, baseDirectory, builder);

            return;
        }

        if (element.ValueKind != JsonValueKind.Object) return;

        TryAddEntry(element, baseDirectory, builder);
        foreach (var property in element.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Array ||
                property.Value.ValueKind == JsonValueKind.Object)
                AddEntries(property.Value, baseDirectory, builder);
    }

    private static void TryAddEntry(
        JsonElement element,
        string baseDirectory,
        ImmutableArray<BaselineEntry>.Builder builder)
    {
        string? id = null;
        string? symbol = null;
        string? path = null;
        string? contractText = null;
        string? operationKind = null;
        string? evidenceKey = null;
        int? line = null;
        int? column = null;

        foreach (var property in element.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value)) continue;

                value = value!.Trim();
                if (string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.Name, "diagnosticId", StringComparison.OrdinalIgnoreCase))
                    id = value;
                else if (string.Equals(property.Name, "symbol", StringComparison.OrdinalIgnoreCase))
                    symbol = value;
                else if (string.Equals(property.Name, "path", StringComparison.OrdinalIgnoreCase))
                    path = value;
                else if (string.Equals(property.Name, "contract", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(property.Name, "contractText", StringComparison.OrdinalIgnoreCase))
                    contractText = value;
                else if (string.Equals(property.Name, "operationKind", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(property.Name, "operation_kind", StringComparison.OrdinalIgnoreCase))
                    operationKind = value;
                else if (string.Equals(property.Name, "evidenceKey", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(property.Name, "evidence_key", StringComparison.OrdinalIgnoreCase))
                    evidenceKey = value;
            }
            else if (property.Value.ValueKind == JsonValueKind.Number)
            {
                if (string.Equals(property.Name, "line", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.TryGetInt32(out var parsedLine))
                    line = parsedLine;
                else if (string.Equals(property.Name, "column", StringComparison.OrdinalIgnoreCase) &&
                         property.Value.TryGetInt32(out var parsedColumn))
                    column = parsedColumn;
            }

        if (!string.IsNullOrWhiteSpace(id) &&
            !string.IsNullOrWhiteSpace(symbol) &&
            !string.IsNullOrWhiteSpace(path))
            builder.Add(new BaselineEntry(
                id!,
                symbol!,
                path!,
                baseDirectory,
                line,
                column,
                contractText,
                operationKind,
                evidenceKey));
    }

    private static string GetBaseDirectory(string baselinePath)
    {
        if (string.IsNullOrWhiteSpace(baselinePath)) return string.Empty;

        var directory = Path.GetDirectoryName(baselinePath);
        return string.IsNullOrWhiteSpace(directory) ? string.Empty : NormalizePath(directory!);
    }

    private static bool TryGetProperty(
        ImmutableDictionary<string, string?> properties,
        string propertyName,
        out string value)
    {
        if (properties.TryGetValue(propertyName, out var propertyValue) &&
            !string.IsNullOrWhiteSpace(propertyValue))
        {
            value = propertyValue!.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static ImmutableArray<string> GetDiagnosticSymbolIds(
        ImmutableDictionary<string, string?> properties,
        string primarySymbolId)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        builder.Add(primarySymbolId);
        if (TryGetProperty(properties, SharpProofDiagnostics.BaselineSymbolAliasesProperty, out var aliases))
            foreach (var alias in aliases.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = alias.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed)) builder.Add(trimmed);
            }

        return builder.Distinct(StringComparer.Ordinal).ToImmutableArray();
    }

    private readonly struct BaselineEntry
    {
        public BaselineEntry(
            string diagnosticId,
            string symbolId,
            string path,
            string baseDirectory,
            int? line,
            int? column,
            string? contractText,
            string? operationKind,
            string? evidenceKey)
        {
            DiagnosticId = diagnosticId;
            SymbolId = symbolId;
            Path = NormalizePath(path);
            AbsolutePath = MakeAbsolutePath(path, baseDirectory);
            Line = line;
            Column = column;
            ContractText = NormalizeOptional(contractText);
            OperationKind = NormalizeOptional(operationKind);
            EvidenceKey = NormalizeOptional(evidenceKey);
        }

        private string DiagnosticId { get; }
        private string SymbolId { get; }
        private string Path { get; }
        private string AbsolutePath { get; }
        private int? Line { get; }
        private int? Column { get; }
        private string? ContractText { get; }
        private string? OperationKind { get; }
        private string? EvidenceKey { get; }

        public bool Matches(string diagnosticId, string symbolId, string sourcePath)
        {
            return string.Equals(DiagnosticId, diagnosticId, StringComparison.Ordinal) &&
                   string.Equals(SymbolId, symbolId, StringComparison.Ordinal) &&
                   MatchesPath(sourcePath);
        }

        public bool Matches(
            string diagnosticId,
            string symbolId,
            string sourcePath,
            Diagnostic diagnostic)
        {
            return Matches(diagnosticId, symbolId, sourcePath) &&
                   MatchesLocation(diagnostic.Location) &&
                   MatchesOptionalProperty(ContractText, diagnostic, SharpProofDiagnostics.BaselineContractProperty) &&
                   MatchesOptionalProperty(OperationKind, diagnostic,
                       SharpProofDiagnostics.BaselineOperationKindProperty) &&
                   MatchesOptionalProperty(EvidenceKey, diagnostic, SharpProofDiagnostics.BaselineEvidenceKeyProperty);
        }

        private bool MatchesPath(string sourcePath)
        {
            var normalizedSourcePath = NormalizePath(sourcePath);
            return string.Equals(Path, normalizedSourcePath, StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrWhiteSpace(AbsolutePath) &&
                    string.Equals(AbsolutePath, normalizedSourcePath, StringComparison.OrdinalIgnoreCase));
        }

        private bool MatchesLocation(Location location)
        {
            if (!Line.HasValue && !Column.HasValue) return true;

            if (location == Location.None || !location.IsInSource) return false;

            var lineSpan = location.GetLineSpan();
            if (lineSpan.Path == null) return false;

            var actualLine = lineSpan.StartLinePosition.Line + 1;
            var actualColumn = lineSpan.StartLinePosition.Character + 1;
            return (!Line.HasValue || Line.Value == actualLine) &&
                   (!Column.HasValue || Column.Value == actualColumn);
        }

        private static bool MatchesOptionalProperty(
            string? expected,
            Diagnostic diagnostic,
            string propertyName)
        {
            return string.IsNullOrWhiteSpace(expected) ||
                   (TryGetProperty(diagnostic.Properties, propertyName, out var actual) &&
                    string.Equals(expected, actual, StringComparison.Ordinal));
        }

        private static string MakeAbsolutePath(string path, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory)) return string.Empty;

            if (System.IO.Path.IsPathRooted(path)) return NormalizePath(path);

            return NormalizePath(System.IO.Path.Combine(baseDirectory, path));
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }
    }
}
