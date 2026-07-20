using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using static SharpProof.Tools.Shared.SarifJsonFacts;
using SharpProof.Schema;

namespace SharpProof.Tools.Baseline;

public sealed record BaselineExplanation(
    BaselineEntry Entry,
    bool Matched,
    string Reason);

public sealed record BaselinePruneResult(
    BaselineDocument Baseline,
    int Kept,
    int Pruned);

public static class SharpProofBaseline
{
    public const string BaselineSymbolProperty = "sharpproof.baseline.symbol";
    public const string BaselinePathProperty = "sharpproof.baseline.path";
    public const string BaselineOperationKindProperty = "sharpproof.baseline.operation_kind";
    public const string BaselineContractProperty = "sharpproof.baseline.contract";
    public const string BaselineEvidenceKeyProperty = "sharpproof.baseline.evidence_key";
    public const string EvidenceSchemaVersionProperty = SharpProofEvidenceSchema.DiagnosticVersionProperty;

    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions InputJsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static BaselineDocument GenerateFromSarifJson(string sarifJson)
    {
        ArgumentNullException.ThrowIfNull(sarifJson);

        var entries = ImmutableArray.CreateBuilder<BaselineEntry>();
        using var document = JsonDocument.Parse(sarifJson, BaselineSchemaContract.DocumentOptions);
        foreach (var result in EnumerateResults(document.RootElement))
        {
            var entry = TryCreateEntry(result);
            if (entry != null) entries.Add(entry);
        }

        return new BaselineDocument(BaselineSchemaContract.Deduplicate(entries));
    }

    public static BaselineDocument ParseBaselineJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json, BaselineSchemaContract.DocumentOptions);
        BaselineSchemaContract.ValidateTreeOrThrow(document.RootElement);
        var baseline = JsonSerializer.Deserialize<BaselineDocument>(json, InputJsonOptions) ??
                       throw new JsonException("Baseline JSON did not contain a document.");
        return new BaselineDocument(BaselineSchemaContract.Deduplicate(
            baseline.Diagnostics.IsDefault
                ? ImmutableArray<BaselineEntry>.Empty
                : baseline.Diagnostics));
    }

    public static BaselineDocument Merge(IEnumerable<BaselineDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        return new BaselineDocument(BaselineSchemaContract.Deduplicate(
            documents.SelectMany(static document => document.Diagnostics)));
    }

    public static ImmutableArray<BaselineExplanation> Explain(
        BaselineDocument baseline,
        BaselineDocument current)
    {
        var explanations = ImmutableArray.CreateBuilder<BaselineExplanation>(baseline.Diagnostics.Length);
        foreach (var entry in baseline.Diagnostics)
        {
            var normalizedPath = BaselineSchemaContract.NormalizePath(entry.Path);
            var matchLevel = 0;
            foreach (var currentEntry in current.Diagnostics)
            {
                if (!string.Equals(currentEntry.Id, entry.Id, StringComparison.Ordinal)) continue;
                matchLevel = Math.Max(matchLevel, 1);
                if (!string.Equals(currentEntry.Symbol, entry.Symbol, StringComparison.Ordinal)) continue;
                matchLevel = Math.Max(matchLevel, 2);
                if (!string.Equals(
                        BaselineSchemaContract.NormalizePath(currentEntry.Path),
                        normalizedPath,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                matchLevel = Math.Max(matchLevel, 3);
                if (BaselineSchemaContract.MatchesOptionalIdentity(entry, currentEntry))
                {
                    matchLevel = 4;
                    break;
                }
            }

            var reason = matchLevel switch
            {
                0 => "no current diagnostic with id '" + entry.Id + "'",
                1 => "diagnostic id matched but symbol did not",
                2 => "diagnostic id and symbol matched but path did not",
                3 => "diagnostic id, symbol, and path matched but instance identity did not",
                _ => BaselineSchemaContract.HasOptionalIdentity(entry)
                    ? "matched id, symbol, path, and instance identity"
                    : "matched id, symbol, and path"
            };
            explanations.Add(new BaselineExplanation(entry, matchLevel == 4, reason));
        }

        return explanations.ToImmutable();
    }

    public static BaselinePruneResult Prune(
        BaselineDocument baseline,
        BaselineDocument current)
    {
        var explanations = Explain(baseline, current);
        var kept = explanations
            .Where(explanation => explanation.Matched)
            .Select(explanation => explanation.Entry)
            .ToImmutableArray();

        return new BaselinePruneResult(
            new BaselineDocument(kept),
            kept.Length,
            baseline.Diagnostics.Length - kept.Length);
    }

    public static string ToJson(BaselineDocument baseline)
    {
        return JsonSerializer.Serialize(baseline, OutputJsonOptions) + Environment.NewLine;
    }

    private static BaselineEntry? TryCreateEntry(JsonElement result)
    {
        var id = GetStringProperty(result, "ruleId");
        if (id == null && result.TryGetProperty("rule", out var rule) && rule.ValueKind == JsonValueKind.Object)
            id = GetStringProperty(rule, "id");
        if (id == null || !id.StartsWith("SP", StringComparison.Ordinal)) return null;

        if (!result.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
            return null;

        BaselineSchemaContract.ValidateOrThrow(
            properties,
            EvidenceSchemaVersionProperty,
            "SARIF diagnostic",
            required: false,
            allowStringVersion: true);

        var symbol = GetEvidenceProperty(properties, BaselineSymbolProperty, includeCustomProperties: true);
        if (string.IsNullOrWhiteSpace(symbol)) return null;

        var (physicalPath, line, column) = GetPhysicalLocation(result);
        var path = GetEvidenceProperty(properties, BaselinePathProperty, includeCustomProperties: true) ?? physicalPath;
        if (string.IsNullOrWhiteSpace(path)) return null;

        return new BaselineEntry(
            id,
            symbol!,
            BaselineSchemaContract.NormalizePath(path!),
            GetMessageText(result),
            line,
            column,
            GetEvidenceProperty(properties, BaselineContractProperty, includeCustomProperties: true),
            GetEvidenceProperty(properties, BaselineOperationKindProperty, includeCustomProperties: true),
            GetEvidenceProperty(properties, BaselineEvidenceKeyProperty, includeCustomProperties: true));
    }

    private static (string? Path, int? Line, int? Column) GetPhysicalLocation(JsonElement result)
    {
        if (!result.TryGetProperty("locations", out var locations) ||
            locations.ValueKind != JsonValueKind.Array)
            return default;

        foreach (var location in locations.EnumerateArray())
            if (location.ValueKind == JsonValueKind.Object &&
                location.TryGetProperty("physicalLocation", out var physicalLocation) &&
                physicalLocation.ValueKind == JsonValueKind.Object)
            {
                var path = physicalLocation.TryGetProperty("artifactLocation", out var artifactLocation) &&
                           artifactLocation.ValueKind == JsonValueKind.Object
                    ? GetStringProperty(artifactLocation, "uri")
                    : null;
                if (!physicalLocation.TryGetProperty("region", out var region) ||
                    region.ValueKind != JsonValueKind.Object)
                    return (path, null, null);

                var line = region.TryGetProperty("startLine", out var startLine) && startLine.TryGetInt32(out var l)
                    ? l
                    : (int?)null;
                var column = region.TryGetProperty("startColumn", out var startColumn) &&
                             startColumn.TryGetInt32(out var c)
                    ? c
                    : (int?)null;
                return (path, line, column);
            }

        return default;
    }

}
