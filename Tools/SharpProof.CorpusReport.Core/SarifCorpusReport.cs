using System.Text.Json;
using static SharpProof.Tools.Shared.SarifJsonFacts;

namespace SharpProof.Tools.CorpusReport;

public sealed record SarifCorpusInput(string InputName, string SarifPath);

public static class SarifCorpusReport {
    private const string EffectCategoryProperty = "sharpproof.effects.category";
    private const string EffectFlagsProperty = "sharpproof.effects.flags";
    private const string CapabilityFlagsProperty = "sharpproof.effects.capabilities";
    private const string VerdictProperty = "sharpproof.explain.proof_status";
    private const string UnknownReasonProperty = "sharpproof.explain.unknown_reason";
    private const string SymbolProperty = "sharpproof.baseline.symbol";
    private const string ExceptionTypesProperty = "sharpproof.exceptions.types";
    private const string ExceptionCategoriesProperty = "sharpproof.exceptions.categories";
    private const string ExceptionSourcesProperty = "sharpproof.exceptions.sources";
    private const string ExceptionEdgesProperty = "sharpproof.exceptions.edges";

    public static CorpusReportSummary CreateFromSarifFiles(IEnumerable<string> sarifPaths) =>
        CreateFromSarifFiles(sarifPaths.Select(path => new SarifCorpusInput(path, path)));

    public static CorpusReportSummary CreateFromSarifFiles(IEnumerable<SarifCorpusInput> inputs) {
        var builder = new SummaryBuilder();
        foreach (var input in inputs) builder.AddSarifJson(input.InputName, File.ReadAllText(input.SarifPath));
        return builder.Build();
    }

    private sealed class SummaryBuilder {
        private readonly ImmutableArray<string>.Builder _inputs = ImmutableArray.CreateBuilder<string>();
        private readonly ImmutableArray<DiagnosticEvidenceItem>.Builder _diagnostics =
            ImmutableArray.CreateBuilder<DiagnosticEvidenceItem>();
        private readonly Dictionary<string, int> _categories = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _effects = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _capabilities = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _verdicts = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Category, string Value), int> _unknowns = new();
        private readonly Dictionary<string, int> _exceptionSources = new(StringComparer.Ordinal);
        private int _sp0002;
        private int _sp0004;
        private int _sp0009;
        private int _exceptionDiagnostics;
        private int _total;

        internal void AddSarifJson(string inputName, string sarifJson) {
            _inputs.Add(inputName);
            using var document = JsonDocument.Parse(sarifJson);
            foreach (var result in EnumerateResults(document.RootElement)) AddResult(inputName, result);
        }

        internal CorpusReportSummary Build() => new(
            _inputs.ToImmutable(), _sp0002, _sp0004, _sp0009, _exceptionDiagnostics, _total,
            _diagnostics.ToImmutable(), Sorted(_categories), Sorted(_effects), Sorted(_capabilities), Sorted(_verdicts),
            Rank(_unknowns.Select(pair => new RankedItem(pair.Key.Value, pair.Value, pair.Key.Category))),
            Rank(_exceptionSources.Select(pair => new RankedItem(pair.Key, pair.Value))));

        private void AddResult(string inputName, JsonElement result) {
            var ruleId = GetStringProperty(result, "ruleId");
            if (ruleId is null || !ruleId.StartsWith("SP", StringComparison.Ordinal)) return;
            _total++;
            if (ruleId == "SP0002") _sp0002++;
            else if (ruleId == "SP0004") _sp0004++;
            else if (ruleId == "SP0009") _sp0009++;
            else if (ruleId is "SP0010" or "SP0011") _exceptionDiagnostics++;

            var message = GetMessageText(result);
            if (!result.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) {
                _diagnostics.Add(new DiagnosticEvidenceItem(inputName, ruleId, message, null, null, null, null, null,
                    null, null, null, null, null));
                return;
            }

            var category = GetEvidenceProperty(properties, EffectCategoryProperty);
            var effects = GetEvidenceProperty(properties, EffectFlagsProperty);
            var capabilities = GetEvidenceProperty(properties, CapabilityFlagsProperty);
            var verdict = GetEvidenceProperty(properties, VerdictProperty);
            var unknown = GetEvidenceProperty(properties, UnknownReasonProperty);
            var symbol = GetEvidenceProperty(properties, SymbolProperty);
            var exceptionTypes = GetEvidenceProperty(properties, ExceptionTypesProperty);
            var exceptionCategories = GetEvidenceProperty(properties, ExceptionCategoriesProperty);
            var exceptionSources = GetEvidenceProperty(properties, ExceptionSourcesProperty);
            var exceptionEdges = GetEvidenceProperty(properties, ExceptionEdgesProperty);
            _diagnostics.Add(new DiagnosticEvidenceItem(inputName, ruleId, message, category, effects, capabilities,
                verdict, unknown, symbol, exceptionTypes, exceptionCategories, exceptionSources, exceptionEdges));

            Increment(_categories, category);
            IncrementSeparated(_effects, effects);
            IncrementSeparated(_capabilities, capabilities);
            Increment(_verdicts, verdict);
            if (!string.IsNullOrWhiteSpace(unknown))
                Increment(_unknowns, (unknown!, symbol ?? "<unresolved boundary>"));
            IncrementSeparated(_exceptionSources, exceptionSources);
        }

        private static void Increment<TKey>(Dictionary<TKey, int> values, TKey? key) where TKey : notnull {
            if (key is null || string.IsNullOrWhiteSpace(key.ToString())) return;
            values[key] = values.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        private static void IncrementSeparated(Dictionary<string, int> values, string? text) {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (var value in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                Increment(values, value.Trim());
        }

        private static ImmutableDictionary<string, int> Sorted(Dictionary<string, int> values) => values
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToImmutableDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

        private static ImmutableArray<RankedItem> Rank(IEnumerable<RankedItem> values) => values
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.Category, StringComparer.Ordinal)
            .ThenBy(static item => item.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
