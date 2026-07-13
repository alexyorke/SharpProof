using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
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
    int Pruned,
    ImmutableArray<BaselineExplanation> Explanations);

public static class SharpProofBaseline
{
    private const int LegacyEvidenceSchemaVersion = 1;
    private const string LegacyEvidenceCompatibility = "additive-v1";
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

    public static BaselineDocument GenerateFromSarifJson(string sarifJson)
    {
        ArgumentNullException.ThrowIfNull(sarifJson);

        var entries = ImmutableArray.CreateBuilder<BaselineEntry>();
        using var document = JsonDocument.Parse(sarifJson, JsonOptions);
        if (!document.RootElement.TryGetProperty("runs", out var runs) ||
            runs.ValueKind != JsonValueKind.Array)
            return new BaselineDocument(ImmutableArray<BaselineEntry>.Empty);

        foreach (var run in runs.EnumerateArray())
        {
            if (!run.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var result in results.EnumerateArray())
            {
                var entry = TryCreateEntry(result);
                if (entry != null) entries.Add(entry);
            }
        }

        return new BaselineDocument(Deduplicate(entries));
    }

    public static BaselineDocument ParseBaselineJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var entries = ImmutableArray.CreateBuilder<BaselineEntry>();
        using var document = JsonDocument.Parse(json, JsonOptions);
        ValidateEvidenceSchemas(
            document.RootElement,
            "evidenceSchemaVersion",
            "evidenceSchemaCompatibility",
            "baseline");
        AddBaselineEntries(document.RootElement, entries);
        return new BaselineDocument(Deduplicate(entries));
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
        var currentIds = current.Diagnostics
            .Select(entry => entry.Id)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var currentSymbolsById = current.Diagnostics
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.Symbol).ToImmutableHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        var currentPathsByIdAndSymbol = current.Diagnostics
            .GroupBy(entry => (entry.Id, entry.Symbol))
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => NormalizePath(entry.Path))
                    .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase));
        var currentByIdentity = current.Diagnostics
            .GroupBy(
                entry => new BaselineBucketKey(entry.Id, entry.Symbol, NormalizePath(entry.Path)),
                BaselineBucketKeyComparer.Instance)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray(),
                BaselineBucketKeyComparer.Instance);

        var explanations = ImmutableArray.CreateBuilder<BaselineExplanation>(baseline.Diagnostics.Length);
        foreach (var entry in baseline.Diagnostics)
        {
            var normalizedPath = NormalizePath(entry.Path);
            var bucketKey = new BaselineBucketKey(entry.Id, entry.Symbol, normalizedPath);
            if (currentByIdentity.TryGetValue(bucketKey, out var matchingBucket) &&
                matchingBucket.Any(currentEntry => EntryMatchesOptionalIdentity(entry, currentEntry)))
            {
                explanations.Add(new BaselineExplanation(entry, true, GetMatchedReason(entry)));
                continue;
            }

            if (!currentIds.Contains(entry.Id))
            {
                explanations.Add(new BaselineExplanation(entry, false,
                    "no current diagnostic with id '" + entry.Id + "'"));
                continue;
            }

            if (!currentSymbolsById.TryGetValue(entry.Id, out var symbols) ||
                !symbols.Contains(entry.Symbol))
            {
                explanations.Add(new BaselineExplanation(entry, false, "diagnostic id matched but symbol did not"));
                continue;
            }

            var idAndSymbol = (entry.Id, entry.Symbol);
            if (currentPathsByIdAndSymbol.TryGetValue(idAndSymbol, out var paths) &&
                !paths.Contains(normalizedPath))
            {
                explanations.Add(new BaselineExplanation(entry, false,
                    "diagnostic id and symbol matched but path did not"));
                continue;
            }

            if (currentPathsByIdAndSymbol.ContainsKey(idAndSymbol))
            {
                explanations.Add(new BaselineExplanation(entry, false,
                    "diagnostic id, symbol, and path matched but instance identity did not"));
                continue;
            }

            explanations.Add(new BaselineExplanation(entry, false, "no matching current diagnostic"));
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

    private readonly record struct BaselineBucketKey(string Id, string Symbol, string Path);

    private sealed class BaselineBucketKeyComparer : IEqualityComparer<BaselineBucketKey>
    {
        internal static readonly BaselineBucketKeyComparer Instance = new();

        public bool Equals(BaselineBucketKey x, BaselineBucketKey y)
        {
            return string.Equals(x.Id, y.Id, StringComparison.Ordinal) &&
                   string.Equals(x.Symbol, y.Symbol, StringComparison.Ordinal) &&
                   string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(BaselineBucketKey obj)
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(obj.Id);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(obj.Symbol);
                hash = hash * 397 ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Path);
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
            baseline.Diagnostics.Length - kept.Length,
            explanations);
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

        ValidateEvidenceSchemas(
            properties,
            EvidenceSchemaVersionProperty,
            EvidenceSchemaCompatibilityProperty,
            "SARIF diagnostic");

        var symbol = GetEvidenceProperty(properties, BaselineSymbolProperty);
        if (string.IsNullOrWhiteSpace(symbol)) return null;

        var path = GetEvidenceProperty(properties, BaselinePathProperty) ?? GetResultPath(result);
        if (string.IsNullOrWhiteSpace(path)) return null;

        var (line, column) = GetResultLocation(result);
        return new BaselineEntry(
            id,
            symbol!,
            NormalizePath(path!),
            GetMessageText(result),
            line,
            column,
            GetEvidenceProperty(properties, BaselineContractProperty),
            GetEvidenceProperty(properties, BaselineOperationKindProperty),
            GetEvidenceProperty(properties, BaselineEvidenceKeyProperty));
    }

    private static void AddBaselineEntries(
        JsonElement element,
        ImmutableArray<BaselineEntry>.Builder entries)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) AddBaselineEntries(item, entries);

            return;
        }

        if (element.ValueKind != JsonValueKind.Object) return;

        TryAddBaselineEntry(element, entries);
        foreach (var property in element.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Array ||
                property.Value.ValueKind == JsonValueKind.Object)
                AddBaselineEntries(property.Value, entries);
    }

    private static void TryAddBaselineEntry(
        JsonElement element,
        ImmutableArray<BaselineEntry>.Builder entries)
    {
        string? id = null;
        string? symbol = null;
        string? path = null;
        string? message = null;
        string? contract = null;
        string? operationKind = null;
        string? evidenceKey = null;
        int? line = null;
        int? column = null;

        foreach (var property in element.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;

                if (string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.Name, "diagnosticId", StringComparison.OrdinalIgnoreCase))
                    id = value;
                else if (string.Equals(property.Name, "symbol", StringComparison.OrdinalIgnoreCase))
                    symbol = value;
                else if (string.Equals(property.Name, "path", StringComparison.OrdinalIgnoreCase))
                    path = value;
                else if (string.Equals(property.Name, "message", StringComparison.OrdinalIgnoreCase))
                    message = value;
                else if (string.Equals(property.Name, "contract", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(property.Name, "contractText", StringComparison.OrdinalIgnoreCase))
                    contract = value;
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
            entries.Add(new BaselineEntry(
                id!,
                symbol!,
                NormalizePath(path!),
                message,
                line,
                column,
                contract,
                operationKind,
                evidenceKey));
    }

    private static ImmutableArray<BaselineEntry> Deduplicate(IEnumerable<BaselineEntry> entries)
    {
        var seen = new HashSet<BaselineKey>(BaselineKey.BaselineKeyComparer.Instance);
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

    private static void ValidateEvidenceSchemas(
        JsonElement element,
        string versionPropertyName,
        string compatibilityPropertyName,
        string surfaceName)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                ValidateEvidenceSchemas(item, versionPropertyName, compatibilityPropertyName, surfaceName);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object) return;

        var hasVersion = TryGetPropertyIgnoreCase(element, versionPropertyName, out var versionElement);
        var hasCompatibility = TryGetPropertyIgnoreCase(
            element,
            compatibilityPropertyName,
            out var compatibilityElement);
        if (hasVersion || hasCompatibility)
        {
            if (!hasVersion || !TryReadSchemaVersion(versionElement, out var version))
                throw new NotSupportedException(surfaceName + " has an invalid " + versionPropertyName + ".");

            var isCurrent = ProofEvidenceSchemaContract.IsReadCompatible(version);
            var isLegacyV1 = version == LegacyEvidenceSchemaVersion;
            var isLegacyUnversioned = version == ProofEvidenceSchemaContract.LegacyUnversionedVersion;
            if (!isCurrent && !isLegacyV1 && !isLegacyUnversioned)
                throw new NotSupportedException(
                    $"Unsupported {surfaceName} {versionPropertyName} '{version}'; migration supports legacy " +
                    $"versions 0-1 and current version {ProofEvidenceSchemaContract.CurrentVersion}.");

            var expectedCompatibility = isCurrent
                ? ProofEvidenceSchemaContract.CompatibilityPolicy
                : isLegacyV1
                    ? LegacyEvidenceCompatibility
                    : null;
            if (expectedCompatibility != null &&
                (!hasCompatibility ||
                 compatibilityElement.ValueKind != JsonValueKind.String ||
                 !string.Equals(compatibilityElement.GetString(), expectedCompatibility, StringComparison.Ordinal)))
                throw new NotSupportedException(
                    surfaceName + " " + compatibilityPropertyName + " must be '" +
                    expectedCompatibility + "'.");
        }

        foreach (var property in element.EnumerateObject())
            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                ValidateEvidenceSchemas(
                    property.Value,
                    versionPropertyName,
                    compatibilityPropertyName,
                    surfaceName);
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

    private static bool TryReadSchemaVersion(JsonElement element, out int version)
    {
        if (element.ValueKind == JsonValueKind.Number) return element.TryGetInt32(out version);

        if (element.ValueKind == JsonValueKind.String)
            return int.TryParse(
                element.GetString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out version);

        version = default;
        return false;
    }

    private static string? GetEvidenceProperty(JsonElement properties, string propertyName)
    {
        var value = GetStringProperty(properties, propertyName);
        if (!string.IsNullOrWhiteSpace(value)) return value.Trim();

        if (properties.TryGetProperty("customProperties", out var customProperties) &&
            customProperties.ValueKind == JsonValueKind.Object)
        {
            value = GetStringProperty(customProperties, propertyName);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        return null;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? GetMessageText(JsonElement result)
    {
        return result.TryGetProperty("message", out var message) &&
               message.ValueKind == JsonValueKind.Object
            ? GetStringProperty(message, "text")
            : null;
    }

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
        string Id,
        string Symbol,
        string Path,
        int? Line,
        int? Column,
        string? Contract,
        string? OperationKind,
        string? EvidenceKey)
    {
        public static BaselineKey FromEntry(BaselineEntry entry)
        {
            return new BaselineKey(
                entry.Id,
                entry.Symbol,
                NormalizePath(entry.Path),
                entry.Line,
                entry.Column,
                NormalizeOptional(entry.Contract),
                NormalizeOptional(entry.OperationKind),
                NormalizeOptional(entry.EvidenceKey));
        }

        internal sealed class BaselineKeyComparer : IEqualityComparer<BaselineKey>
        {
            internal static readonly BaselineKeyComparer Instance = new();

            public bool Equals(BaselineKey x, BaselineKey y)
            {
                return string.Equals(x.Id, y.Id, StringComparison.Ordinal) &&
                       string.Equals(x.Symbol, y.Symbol, StringComparison.Ordinal) &&
                       string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase) &&
                       x.Line == y.Line &&
                       x.Column == y.Column &&
                       string.Equals(x.Contract, y.Contract, StringComparison.Ordinal) &&
                       string.Equals(x.OperationKind, y.OperationKind, StringComparison.Ordinal) &&
                       string.Equals(x.EvidenceKey, y.EvidenceKey, StringComparison.Ordinal);
            }

            public int GetHashCode(BaselineKey obj)
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(obj.Id);
                    hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(obj.Symbol);
                    hash = hash * 397 ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Path);
                    hash = hash * 397 ^ obj.Line.GetHashCode();
                    hash = hash * 397 ^ obj.Column.GetHashCode();
                    hash = hash * 397 ^ (obj.Contract == null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Contract));
                    hash = hash * 397 ^ (obj.OperationKind == null
                        ? 0
                        : StringComparer.Ordinal.GetHashCode(obj.OperationKind));
                    hash = hash * 397 ^ (obj.EvidenceKey == null
                        ? 0
                        : StringComparer.Ordinal.GetHashCode(obj.EvidenceKey));
                    return hash;
                }
            }
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
