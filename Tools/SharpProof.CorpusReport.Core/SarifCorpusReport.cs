using System.Collections.Immutable;
using System.Text.Json;

namespace SharpProof.Tools.CorpusReport;

public sealed record SarifCorpusInput(string InputName, string SarifPath);

public static class SarifCorpusReport
{
    private const string CategoryProperty = "sharpproof.impurity.category";
    private const string RuleNameProperty = "sharpproof.impurity.rule";
    private const string OperationKindProperty = "sharpproof.impurity.operation_kind";
    private const string SymbolProperty = "sharpproof.impurity.symbol";
    private const string CatalogSourceProperty = "sharpproof.impurity.catalog_source";
    private const string CalleeChainProperty = "sharpproof.impurity.callee_chain";
    private const string ExceptionSymbolProperty = "sharpproof.exceptions.symbol";
    private const string ExceptionTypesProperty = "sharpproof.exceptions.types";
    private const string ExceptionCategoriesProperty = "sharpproof.exceptions.categories";
    private const string ExceptionSourcesProperty = "sharpproof.exceptions.sources";
    private const string ExceptionEdgesProperty = "sharpproof.exceptions.edges";

    private static readonly ImmutableHashSet<string> CatalogMissCategories =
        ImmutableHashSet.Create(StringComparer.Ordinal, "unknown_external_call", "unsupported_operation");

    private static readonly ImmutableHashSet<string> FalsePositiveCandidateCategories =
        ImmutableHashSet.Create(StringComparer.Ordinal, "unknown_external_call", "dynamic_dispatch",
            "unsupported_operation", "unresolved_delegate_target");

    public static CorpusReportSummary CreateFromSarifFiles(IEnumerable<string> sarifPaths)
    {
        return CreateFromSarifFiles(sarifPaths.Select(path => new SarifCorpusInput(path, path)));
    }

    public static CorpusReportSummary CreateFromSarifFiles(IEnumerable<SarifCorpusInput> inputs)
    {
        var builder = new SummaryBuilder();
        foreach (var input in inputs) builder.AddSarifJson(input.InputName, File.ReadAllText(input.SarifPath));

        return builder.Build();
    }

    public static CorpusReportSummary CreateFromSarifJson(string inputName, string sarifJson)
    {
        var builder = new SummaryBuilder();
        builder.AddSarifJson(inputName, sarifJson);
        return builder.Build();
    }

    private sealed class SummaryBuilder
    {
        private readonly Dictionary<string, (string Category, int Count)> _catalogMisses = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _categories = new(StringComparer.Ordinal);

        private readonly ImmutableArray<DiagnosticEvidenceItem>.Builder _diagnostics =
            ImmutableArray.CreateBuilder<DiagnosticEvidenceItem>();

        private readonly Dictionary<string, int> _exceptionCategories = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _exceptionSources = new(StringComparer.Ordinal);

        private readonly Dictionary<string, (string Category, int Count)> _falsePositiveCandidates =
            new(StringComparer.Ordinal);

        private readonly ImmutableArray<string>.Builder _inputs = ImmutableArray.CreateBuilder<string>();
        private readonly Dictionary<string, int> _operationKinds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _ruleNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _symbols = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _unknownOperationKinds = new(StringComparer.Ordinal);

        private int _sp0002Count;
        private int _sp0004Count;
        private int _sp0009Count;
        private int _sp0010Count;
        private int _sp0011Count;
        private int _totalSharpProofDiagnostics;

        public void AddSarifJson(string inputName, string sarifJson)
        {
            _inputs.Add(inputName);
            using var document = JsonDocument.Parse(sarifJson);
            if (!document.RootElement.TryGetProperty("runs", out var runs) ||
                runs.ValueKind != JsonValueKind.Array)
                return;

            foreach (var run in runs.EnumerateArray())
            {
                if (!run.TryGetProperty("results", out var results) ||
                    results.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var result in results.EnumerateArray()) AddResult(inputName, result);
            }
        }

        public CorpusReportSummary Build()
        {
            return new CorpusReportSummary(
                _inputs.ToImmutable(),
                _sp0002Count,
                _sp0004Count,
                _sp0009Count,
                _sp0010Count,
                _sp0011Count,
                _totalSharpProofDiagnostics,
                _diagnostics.ToImmutable(),
                ToImmutableSortedDictionary(_categories),
                ToImmutableSortedDictionary(_exceptionCategories),
                ToImmutableSortedDictionary(_ruleNames),
                ToImmutableSortedDictionary(_operationKinds),
                ToImmutableSortedDictionary(_unknownOperationKinds),
                ToRankedItems(_symbols),
                ToRankedItems(_exceptionSources),
                ToCategorizedRankedItems(_catalogMisses),
                ToCategorizedRankedItems(_falsePositiveCandidates));
        }

        private void AddResult(string inputName, JsonElement result)
        {
            var ruleId = GetStringProperty(result, "ruleId");
            if (ruleId is null || !ruleId.StartsWith("SP", StringComparison.Ordinal)) return;

            _totalSharpProofDiagnostics++;
            var message = GetMessageText(result);
            if (ruleId == "SP0002")
                _sp0002Count++;
            else if (ruleId == "SP0004")
                _sp0004Count++;
            else if (ruleId == "SP0009")
                _sp0009Count++;
            else if (ruleId == "SP0010")
                _sp0010Count++;
            else if (ruleId == "SP0011") _sp0011Count++;

            if (!result.TryGetProperty("properties", out var properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                _diagnostics.Add(new DiagnosticEvidenceItem(inputName, ruleId, message, null, null, null, null, null,
                    null, null, null, null, null));
                return;
            }

            var category = GetEvidenceProperty(properties, CategoryProperty);
            var ruleName = GetEvidenceProperty(properties, RuleNameProperty);
            var operationKind = GetEvidenceProperty(properties, OperationKindProperty);
            var symbol = GetEvidenceProperty(properties, SymbolProperty);
            var catalogSource = GetEvidenceProperty(properties, CatalogSourceProperty);
            var calleeChain = GetEvidenceProperty(properties, CalleeChainProperty);
            var exceptionSymbol = GetEvidenceProperty(properties, ExceptionSymbolProperty);
            var exceptionTypes = GetEvidenceProperty(properties, ExceptionTypesProperty);
            var exceptionCategories = GetEvidenceProperty(properties, ExceptionCategoriesProperty);
            var exceptionSources = GetEvidenceProperty(properties, ExceptionSourcesProperty);
            var exceptionEdges = GetEvidenceProperty(properties, ExceptionEdgesProperty);

            _diagnostics.Add(new DiagnosticEvidenceItem(
                inputName,
                ruleId,
                message,
                category,
                ruleName,
                operationKind,
                symbol,
                catalogSource,
                calleeChain,
                exceptionSymbol,
                exceptionTypes,
                exceptionCategories,
                exceptionSources,
                exceptionEdges));

            if (ruleId == "SP0010" || ruleId == "SP0011")
            {
                IncrementSeparatedValues(_exceptionCategories, exceptionCategories);
                IncrementSeparatedValues(_exceptionSources, exceptionSources);

                if (string.IsNullOrWhiteSpace(exceptionSources))
                    IncrementExceptionEdgeSources(_exceptionSources, exceptionEdges);

                return;
            }

            if (ruleId != "SP0002") return;

            IncrementIfPresent(_categories, category);
            IncrementIfPresent(_ruleNames, ruleName);
            IncrementIfPresent(_operationKinds, operationKind);
            IncrementIfPresent(_symbols, symbol);

            if (string.Equals(category, "unsupported_operation", StringComparison.Ordinal))
                IncrementIfPresent(_unknownOperationKinds, operationKind);

            if (category != null && symbol != null && CatalogMissCategories.Contains(category))
                IncrementCategorized(_catalogMisses, category, symbol);

            if (category != null && symbol != null && FalsePositiveCandidateCategories.Contains(category))
                IncrementCategorized(_falsePositiveCandidates, category, symbol);
        }

        private static string? GetStringProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string? GetEvidenceProperty(JsonElement element, string propertyName)
        {
            var value = GetStringProperty(element, propertyName);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? GetMessageText(JsonElement result)
        {
            return result.TryGetProperty("message", out var message) &&
                   message.ValueKind == JsonValueKind.Object
                ? GetStringProperty(message, "text")
                : null;
        }

        private static void IncrementIfPresent(Dictionary<string, int> values, string? key)
        {
            if (!string.IsNullOrWhiteSpace(key)) Increment(values, key);
        }

        private static void Increment(Dictionary<string, int> values, string key)
        {
            values[key] = values.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        private static void IncrementCategorized(Dictionary<string, (string Category, int Count)> values,
            string category, string value)
        {
            var key = category + "|" + value;
            values[key] = values.TryGetValue(key, out var existing)
                ? (category, existing.Count + 1)
                : (category, 1);
        }

        private static void IncrementSeparatedValues(Dictionary<string, int> values, string? separatedValues)
        {
            if (string.IsNullOrWhiteSpace(separatedValues)) return;

            foreach (var item in separatedValues.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var value = item.Trim();
                if (value.Length > 0) Increment(values, value);
            }
        }

        private static void IncrementExceptionEdgeSources(Dictionary<string, int> values, string? exceptionEdges)
        {
            if (string.IsNullOrWhiteSpace(exceptionEdges)) return;

            try
            {
                using var document = JsonDocument.Parse(exceptionEdges);
                if (document.RootElement.ValueKind != JsonValueKind.Array) return;

                var uniqueSources = new HashSet<string>(StringComparer.Ordinal);
                foreach (var edge in document.RootElement.EnumerateArray())
                {
                    if (edge.ValueKind != JsonValueKind.Object) continue;

                    var exceptionType = GetStringProperty(edge, "ExceptionType");
                    var category = GetStringProperty(edge, "Category");
                    var sourcePath = GetStringProperty(edge, "SourcePath");
                    if (!string.IsNullOrWhiteSpace(exceptionType) &&
                        !string.IsNullOrWhiteSpace(category) &&
                        !string.IsNullOrWhiteSpace(sourcePath))
                        uniqueSources.Add(exceptionType.Trim() + "=" + category.Trim() + ":" + sourcePath.Trim());
                }

                foreach (var source in uniqueSources) Increment(values, source);
            }
            catch (JsonException)
            {
                // Ignore malformed additive edge payloads and preserve legacy aggregation behavior.
            }
        }

        private static ImmutableDictionary<string, int> ToImmutableSortedDictionary(Dictionary<string, int> values)
        {
            return values
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        private static ImmutableArray<RankedItem> ToRankedItems(Dictionary<string, int> values)
        {
            return values
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new RankedItem(pair.Key, pair.Value))
                .ToImmutableArray();
        }

        private static ImmutableArray<RankedItem> ToCategorizedRankedItems(
            Dictionary<string, (string Category, int Count)> values)
        {
            return values
                .Select(pair =>
                {
                    var separatorIndex = pair.Key.IndexOf('|');
                    var symbol = separatorIndex >= 0 ? pair.Key[(separatorIndex + 1)..] : pair.Key;
                    return new RankedItem(symbol, pair.Value.Count, pair.Value.Category);
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Category, StringComparer.Ordinal)
                .ThenBy(item => item.Value, StringComparer.Ordinal)
                .ToImmutableArray();
        }
    }
}