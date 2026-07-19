using System.Collections.Immutable;
using System.Text.Json;
using static SharpProof.Tools.Shared.SarifJsonFacts;
using System.Text.Json.Serialization;
using SharpProof.Schema;

namespace SharpProof.Tools.Baseline;

public sealed record BaselineDocument(
    [property: JsonPropertyName("diagnostics")]
    ImmutableArray<BaselineEntry> Diagnostics)
{
    [JsonPropertyName("version")] public int Version { get; init; } = 1;

    [JsonPropertyName("evidenceSchemaVersion")]
    public int EvidenceSchemaVersion { get; init; } = ProofEvidenceSchemaContract.CurrentVersion;

    [JsonPropertyName("evidenceSchemaCompatibility")]
    public string EvidenceSchemaCompatibility { get; init; } = ProofEvidenceSchemaContract.CompatibilityPolicy;
}

public sealed record BaselineEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("message")]
    string? Message = null,
    [property: JsonPropertyName("line")] int? Line = null,
    [property: JsonPropertyName("column")] int? Column = null,
    [property: JsonPropertyName("contract")]
    string? Contract = null,
    [property: JsonPropertyName("operationKind")]
    string? OperationKind = null,
    [property: JsonPropertyName("evidenceKey")]
    string? EvidenceKey = null,
    [property: JsonPropertyName("evidenceSchemaVersion")]
    int EvidenceSchemaVersion = ProofEvidenceSchemaContract.CurrentVersion,
    [property: JsonPropertyName("evidenceSchemaCompatibility")]
    string EvidenceSchemaCompatibility = ProofEvidenceSchemaContract.CompatibilityPolicy);

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
    public const string EvidenceSchemaVersionProperty = ProofEvidenceSchemaContract.DiagnosticVersionProperty;
    public const string EvidenceSchemaCompatibilityProperty =
        ProofEvidenceSchemaContract.DiagnosticCompatibilityProperty;

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

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
        using var document = JsonDocument.Parse(sarifJson, JsonOptions);
        foreach (var result in EnumerateResults(document.RootElement))
        {
            var entry = TryCreateEntry(result);
            if (entry != null) entries.Add(entry);
        }

        return new BaselineDocument(Deduplicate(entries));
    }

    public static BaselineDocument ParseBaselineJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json, JsonOptions);
        ValidateEvidenceSchema(
            document.RootElement,
            "evidenceSchemaVersion",
            "evidenceSchemaCompatibility",
            "baseline",
            required: true);
        if (document.RootElement.TryGetProperty("diagnostics", out var diagnostics) &&
            diagnostics.ValueKind == JsonValueKind.Array)
            foreach (var diagnostic in diagnostics.EnumerateArray())
                ValidateEvidenceSchema(
                    diagnostic,
                    "evidenceSchemaVersion",
                    "evidenceSchemaCompatibility",
                    "baseline diagnostic",
                    required: true);
        var baseline = JsonSerializer.Deserialize<BaselineDocument>(json, InputJsonOptions) ??
                       throw new JsonException("Baseline JSON did not contain a document.");
        return new BaselineDocument(Deduplicate(
            baseline.Diagnostics.IsDefault
                ? ImmutableArray<BaselineEntry>.Empty
                : baseline.Diagnostics));
    }

    public static BaselineDocument Merge(IEnumerable<BaselineDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        return new BaselineDocument(Deduplicate(documents.SelectMany(document => document.Diagnostics)));
    }

    public static ImmutableArray<BaselineExplanation> Explain(
        BaselineDocument baseline,
        BaselineDocument current)
    {
        var explanations = ImmutableArray.CreateBuilder<BaselineExplanation>(baseline.Diagnostics.Length);
        foreach (var entry in baseline.Diagnostics)
        {
            var normalizedPath = NormalizePath(entry.Path);
            var idMatches = current.Diagnostics
                .Where(currentEntry => string.Equals(currentEntry.Id, entry.Id, StringComparison.Ordinal))
                .ToArray();
            if (idMatches.Length == 0)
            {
                explanations.Add(new BaselineExplanation(entry, false,
                    "no current diagnostic with id '" + entry.Id + "'"));
                continue;
            }

            var symbolMatches = idMatches
                .Where(currentEntry => string.Equals(currentEntry.Symbol, entry.Symbol, StringComparison.Ordinal))
                .ToArray();
            if (symbolMatches.Length == 0)
            {
                explanations.Add(new BaselineExplanation(entry, false, "diagnostic id matched but symbol did not"));
                continue;
            }

            var pathMatches = symbolMatches
                .Where(currentEntry => string.Equals(
                    NormalizePath(currentEntry.Path),
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (pathMatches.Length == 0)
            {
                explanations.Add(new BaselineExplanation(entry, false,
                    "diagnostic id and symbol matched but path did not"));
                continue;
            }

            if (pathMatches.Any(currentEntry => EntryMatchesOptionalIdentity(entry, currentEntry)))
            {
                explanations.Add(new BaselineExplanation(entry, true, GetMatchedReason(entry)));
                continue;
            }

            explanations.Add(new BaselineExplanation(entry, false,
                "diagnostic id, symbol, and path matched but instance identity did not"));
        }

        return explanations.ToImmutable();
    }

    private static bool EntryMatchesOptionalIdentity(BaselineEntry baselineEntry, BaselineEntry currentEntry)
    {
        return MatchesOptional(baselineEntry.Line, currentEntry.Line) &&
               MatchesOptional(baselineEntry.Column, currentEntry.Column) &&
               MatchesOptional(baselineEntry.Contract, currentEntry.Contract) &&
               MatchesOptional(baselineEntry.OperationKind, currentEntry.OperationKind) &&
               MatchesOptional(baselineEntry.EvidenceKey, currentEntry.EvidenceKey);
    }

    private static bool MatchesOptional(int? expected, int? actual)
    {
        return !expected.HasValue || expected.Value == actual;
    }

    private static bool MatchesOptional(string? expected, string? actual)
    {
        return string.IsNullOrWhiteSpace(expected) ||
               string.Equals(expected.Trim(), actual?.Trim(), StringComparison.Ordinal);
    }

    private readonly record struct BaselineIdentityKey(string Id, string Symbol, string Path)
    {
        internal static BaselineIdentityKey FromEntry(BaselineEntry entry) =>
            new(entry.Id, entry.Symbol, NormalizePath(entry.Path));

        public bool Equals(BaselineIdentityKey other) =>
            string.Equals(Id, other.Id, StringComparison.Ordinal) &&
            string.Equals(Symbol, other.Symbol, StringComparison.Ordinal) &&
            string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(Id);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(Symbol);
                hash = hash * 397 ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Path);
                return hash;
            }
        }
    }

    private static string GetMatchedReason(BaselineEntry entry)
    {
        return entry.Line.HasValue ||
               entry.Column.HasValue ||
               !string.IsNullOrWhiteSpace(entry.Contract) ||
               !string.IsNullOrWhiteSpace(entry.OperationKind) ||
               !string.IsNullOrWhiteSpace(entry.EvidenceKey)
            ? "matched id, symbol, path, and instance identity"
            : "matched id, symbol, and path";
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

    public static string NormalizePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var trimmed = path.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            trimmed = uri.IsFile ? uri.LocalPath : uri.ToString();
        else if (trimmed.Contains('%', StringComparison.Ordinal)) trimmed = Uri.UnescapeDataString(trimmed);

        var normalized = trimmed.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized.Substring(2);

        var prefix = string.Empty;
        var segmentStart = 0;
        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            prefix = "//";
            segmentStart = 2;
        }
        else if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            prefix = "/";
            segmentStart = 1;
        }
        else if (normalized.Length >= 3 && normalized[1] == ':' && normalized[2] == '/')
        {
            prefix = normalized.Substring(0, 3);
            segmentStart = 3;
        }

        var segments = new List<string>();
        foreach (var segment in normalized.Substring(segmentStart).Split('/'))
        {
            if (segment.Length == 0 || string.Equals(segment, ".", StringComparison.Ordinal)) continue;

            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                if (segments.Count > 0 && !string.Equals(segments[^1], "..", StringComparison.Ordinal))
                    segments.RemoveAt(segments.Count - 1);
                else if (prefix.Length == 0)
                    segments.Add(segment);
                continue;
            }

            segments.Add(segment);
        }

        return prefix + string.Join("/", segments);
    }

    private static BaselineEntry? TryCreateEntry(JsonElement result)
    {
        var id = GetStringProperty(result, "ruleId") ?? GetNestedRuleId(result);
        if (id == null || !id.StartsWith("SP", StringComparison.Ordinal)) return null;

        if (!result.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
            return null;

        ValidateEvidenceSchema(
            properties,
            EvidenceSchemaVersionProperty,
            EvidenceSchemaCompatibilityProperty,
            "SARIF diagnostic",
            required: false);

        var symbol = GetEvidenceProperty(properties, BaselineSymbolProperty, includeCustomProperties: true);
        if (string.IsNullOrWhiteSpace(symbol)) return null;

        var path = GetEvidenceProperty(properties, BaselinePathProperty, includeCustomProperties: true) ??
                   GetResultPath(result);
        if (string.IsNullOrWhiteSpace(path)) return null;

        var (line, column) = GetResultLocation(result);
        return new BaselineEntry(
            id,
            symbol!,
            NormalizePath(path!),
            GetMessageText(result),
            line,
            column,
            GetEvidenceProperty(properties, BaselineContractProperty, includeCustomProperties: true),
            GetEvidenceProperty(properties, BaselineOperationKindProperty, includeCustomProperties: true),
            GetEvidenceProperty(properties, BaselineEvidenceKeyProperty, includeCustomProperties: true));
    }

    private static ImmutableArray<BaselineEntry> Deduplicate(IEnumerable<BaselineEntry> entries)
    {
        var seen = new HashSet<BaselineKey>();
        var result = ImmutableArray.CreateBuilder<BaselineEntry>();
        foreach (var entry in entries.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                     .ThenBy(entry => entry.Symbol, StringComparer.Ordinal))
            if (seen.Add(BaselineKey.FromEntry(entry)))
                result.Add(entry with { Path = NormalizePath(entry.Path) });

        return result.ToImmutable();
    }

    private static string? GetNestedRuleId(JsonElement result)
    {
        return result.TryGetProperty("rule", out var rule) &&
               rule.ValueKind == JsonValueKind.Object
            ? GetStringProperty(rule, "id")
            : null;
    }

    private static void ValidateEvidenceSchema(
        JsonElement element,
        string versionPropertyName,
        string compatibilityPropertyName,
        string surfaceName,
        bool required)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new NotSupportedException(surfaceName + " must be a JSON object.");

        var (hasVersion, versionElement, hasCompatibility, compatibilityElement) =
            JsonElementPropertyReader.ReadEvidenceSchemaProperties(
                element,
                versionPropertyName,
                compatibilityPropertyName);
        if (required && (!hasVersion || !hasCompatibility))
            throw new NotSupportedException(surfaceName + " must declare the current evidence schema.");

        if (hasVersion || hasCompatibility)
        {
            if (!hasVersion || !IsCurrentSchemaVersion(versionElement))
                throw new NotSupportedException(surfaceName + " has an invalid " + versionPropertyName + ".");

            if (!hasCompatibility ||
                 compatibilityElement.ValueKind != JsonValueKind.String ||
                 !string.Equals(
                     compatibilityElement.GetString(),
                     ProofEvidenceSchemaContract.CompatibilityPolicy,
                     StringComparison.Ordinal))
                throw new NotSupportedException(
                    surfaceName + " " + compatibilityPropertyName + " must be '" +
                    ProofEvidenceSchemaContract.CompatibilityPolicy + "'.");
        }
    }

    private static bool IsCurrentSchemaVersion(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) &&
        number == ProofEvidenceSchemaContract.CurrentVersion ||
        value.ValueKind == JsonValueKind.String &&
        string.Equals(
            value.GetString(),
            ProofEvidenceSchemaContract.CurrentVersion.ToString(),
            StringComparison.Ordinal);

    private static string? GetResultPath(JsonElement result)
    {
        if (!TryGetFirstPhysicalLocation(result, out var physicalLocation)) return null;

        if (!physicalLocation.TryGetProperty("artifactLocation", out var artifactLocation) ||
            artifactLocation.ValueKind != JsonValueKind.Object)
            return null;

        return GetStringProperty(artifactLocation, "uri");
    }

    private static (int? Line, int? Column) GetResultLocation(JsonElement result)
    {
        if (!TryGetFirstPhysicalLocation(result, out var physicalLocation) ||
            !physicalLocation.TryGetProperty("region", out var region) ||
            region.ValueKind != JsonValueKind.Object)
            return (null, null);

        int? line = region.TryGetProperty("startLine", out var startLine) &&
                    startLine.ValueKind == JsonValueKind.Number &&
                    startLine.TryGetInt32(out var parsedLine)
            ? parsedLine
            : null;
        int? column = region.TryGetProperty("startColumn", out var startColumn) &&
                      startColumn.ValueKind == JsonValueKind.Number &&
                      startColumn.TryGetInt32(out var parsedColumn)
            ? parsedColumn
            : null;

        return (line, column);
    }

    private static bool TryGetFirstPhysicalLocation(
        JsonElement result,
        out JsonElement physicalLocation)
    {
        physicalLocation = default;
        if (!result.TryGetProperty("locations", out var locations) ||
            locations.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var location in locations.EnumerateArray())
            if (location.ValueKind == JsonValueKind.Object &&
                location.TryGetProperty("physicalLocation", out physicalLocation) &&
                physicalLocation.ValueKind == JsonValueKind.Object)
                return true;

        return false;
    }

    private readonly record struct BaselineKey(
        BaselineIdentityKey Identity,
        int? Line,
        int? Column,
        string? Contract,
        string? OperationKind,
        string? EvidenceKey)
    {
        public static BaselineKey FromEntry(BaselineEntry entry)
        {
            return new BaselineKey(
                BaselineIdentityKey.FromEntry(entry),
                entry.Line,
                entry.Column,
                NormalizeOptional(entry.Contract),
                NormalizeOptional(entry.OperationKind),
                NormalizeOptional(entry.EvidenceKey));
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
