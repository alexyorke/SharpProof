using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer.Configuration;

internal sealed class DiagnosticBaseline
{
    private const string BaselineFileName = "SharpProof.Baseline.json";

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

        return GetSymbolIds(symbol)[0];
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
            using var document = JsonDocument.Parse(json, BaselineJsonCompatibility.DocumentOptions);
            if (!BaselineJsonCompatibility.TryValidateBaselineEvidenceSchemaTree(
                    document.RootElement,
                    requireRootSchema: true,
                    out _))
                return builder.ToImmutable();
            AddEntries(document.RootElement, baseDirectory, builder);
        }
        catch (JsonException)
        {
        }

        return builder.ToImmutable();
    }

    private static void AddEntries(
        JsonElement element,
        string baseDirectory,
        ImmutableArray<BaselineEntry>.Builder builder)
    {
        BaselineJsonCompatibility.VisitJsonTree(element, (candidate, _) =>
        {
            if (candidate.ValueKind == JsonValueKind.Object)
                TryAddEntry(candidate, baseDirectory, builder);
            return true;
        });
    }

    private static void TryAddEntry(
        JsonElement element,
        string baseDirectory,
        ImmutableArray<BaselineEntry>.Builder builder)
    {
        var fields = BaselineJsonCompatibility.ReadEntryFields(element);
        if (fields.IsValid)
            builder.Add(new BaselineEntry(
                fields.Id!,
                fields.Symbol!,
                fields.Path!,
                baseDirectory,
                fields.Line,
                fields.Column,
                fields.ContractText,
                fields.OperationKind,
                fields.EvidenceKey));
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
