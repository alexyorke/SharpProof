using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpProof.Schema;

public sealed record BaselineDocument(
    [property: JsonPropertyName("diagnostics")]
    ImmutableArray<BaselineEntry> Diagnostics) {
    [JsonPropertyName("version")] public int Version { get; init; } = 1;

    [JsonPropertyName("evidenceSchemaVersion")]
    public int EvidenceSchemaVersion { get; init; } = SharpProofEvidenceSchema.CurrentVersion;
}

public sealed record BaselineEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("line")] int? Line = null,
    [property: JsonPropertyName("column")] int? Column = null,
    [property: JsonPropertyName("contract")] string? Contract = null,
    [property: JsonPropertyName("operationKind")] string? OperationKind = null,
    [property: JsonPropertyName("evidenceKey")] string? EvidenceKey = null,
    [property: JsonPropertyName("evidenceSchemaVersion")]
    int EvidenceSchemaVersion = SharpProofEvidenceSchema.CurrentVersion);

internal static class BaselineSchemaContract {
    internal static JsonDocumentOptions DocumentOptions { get; } = new() {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    internal static bool TryValidateTree(JsonElement root, out BaselineSchemaValidationFailure failure) {
        if (!TryValidate(root, "evidenceSchemaVersion", true, out failure)) {
            failure = failure with { IsRoot = true };
            return false;
        }

        if (!root.TryGetProperty("diagnostics", out var diagnostics) ||
            diagnostics.ValueKind != JsonValueKind.Array)
            return true;

        foreach (var entry in diagnostics.EnumerateArray())
            if (entry.ValueKind == JsonValueKind.Object &&
                !TryValidate(
                    entry,
                    "evidenceSchemaVersion",
                    true,
                    out failure))
                return false;

        failure = default;
        return true;
    }

    internal static bool TryValidate(
        JsonElement element,
        string versionPropertyName,
        bool required,
        out BaselineSchemaValidationFailure failure,
        bool allowStringVersion = false) {
        var hasVersion = TryGetPropertyIgnoreCase(element, versionPropertyName, out var versionElement);
        if (!hasVersion) {
            failure = required
                ? new BaselineSchemaValidationFailure(BaselineSchemaValidationFailureKind.Missing)
                : default;
            return !required;
        }

        var version = 0;
        if (!(versionElement.ValueKind == JsonValueKind.Number && versionElement.TryGetInt32(out version) ||
              allowStringVersion &&
              versionElement.ValueKind == JsonValueKind.String &&
              int.TryParse(versionElement.GetString(), out version))) {
            failure = new BaselineSchemaValidationFailure(BaselineSchemaValidationFailureKind.NonNumericVersion);
            return false;
        }

        if (version != SharpProofEvidenceSchema.CurrentVersion) {
            failure = new BaselineSchemaValidationFailure(
                BaselineSchemaValidationFailureKind.UnsupportedVersion,
                version);
            return false;
        }

        failure = default;
        return true;
    }

    internal static BaselineEntryFields ReadEntryFields(JsonElement element) => new(
        HasAnyProperty(element, "id", "symbol", "path"),
        ReadString(element, "id"),
        ReadString(element, "symbol"),
        ReadString(element, "path"),
        ReadString(element, "message"),
        ReadInt32(element, "line"),
        ReadInt32(element, "column"),
        ReadString(element, "contract"),
        ReadString(element, "operationKind"),
        ReadString(element, "evidenceKey"));

    internal static ImmutableArray<BaselineEntry> Deduplicate(IEnumerable<BaselineEntry> entries) {
        var seen = new HashSet<BaselineEntryKey>();
        var result = ImmutableArray.CreateBuilder<BaselineEntry>();
        foreach (var entry in entries.OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static entry => entry.Id, StringComparer.Ordinal)
                     .ThenBy(static entry => entry.Symbol, StringComparer.Ordinal)) {
            var normalized = entry with { Path = NormalizePath(entry.Path ?? string.Empty) };
            if (seen.Add(BaselineEntryKey.Create(normalized))) result.Add(normalized);
        }

        return result.ToImmutable();
    }

    internal static bool MatchesOptionalIdentity(BaselineEntry expected, BaselineEntry actual) =>
        (!expected.Line.HasValue || expected.Line == actual.Line) &&
        (!expected.Column.HasValue || expected.Column == actual.Column) &&
        MatchesOptional(expected.Contract, actual.Contract) &&
        MatchesOptional(expected.OperationKind, actual.OperationKind) &&
        MatchesOptional(expected.EvidenceKey, actual.EvidenceKey);

    internal static bool Matches(
        BaselineEntry expected,
        BaselineEntry actual,
        string? alternateExpectedPath = null) =>
        string.Equals(expected.Id, actual.Id, StringComparison.Ordinal) &&
        string.Equals(expected.Symbol, actual.Symbol, StringComparison.Ordinal) &&
        (string.Equals(expected.Path, actual.Path, StringComparison.OrdinalIgnoreCase) ||
         !string.IsNullOrWhiteSpace(alternateExpectedPath) &&
         string.Equals(alternateExpectedPath, actual.Path, StringComparison.OrdinalIgnoreCase)) &&
        MatchesOptionalIdentity(expected, actual);

    internal static bool HasOptionalIdentity(BaselineEntry entry) =>
        entry.Line.HasValue ||
        entry.Column.HasValue ||
        !string.IsNullOrWhiteSpace(entry.Contract) ||
        !string.IsNullOrWhiteSpace(entry.OperationKind) ||
        !string.IsNullOrWhiteSpace(entry.EvidenceKey);

    internal static void ValidateOrThrow(
        JsonElement element,
        string versionPropertyName,
        string surfaceName,
        bool required,
        bool allowStringVersion = false) {
        if (element.ValueKind != JsonValueKind.Object)
            throw new NotSupportedException(surfaceName + " must be a JSON object.");
        if (TryValidate(
                element,
                versionPropertyName,
                required,
                out var failure,
                allowStringVersion))
            return;

        throw failure.Kind switch {
            BaselineSchemaValidationFailureKind.Missing =>
                new NotSupportedException(surfaceName + " must declare the current evidence schema."),
            _ => new NotSupportedException(surfaceName + " has an invalid " + versionPropertyName + ".")
        };
    }

    internal static void ValidateTreeOrThrow(JsonElement root) {
        if (root.ValueKind != JsonValueKind.Object)
            throw new NotSupportedException("baseline must be a JSON object.");
        if (TryValidateTree(root, out var failure)) return;

        ValidateFailureOrThrow(failure, failure.IsRoot ? "baseline" : "baseline diagnostic");
    }

    private static void ValidateFailureOrThrow(BaselineSchemaValidationFailure failure, string surface) {
        throw failure.Kind switch {
            BaselineSchemaValidationFailureKind.Missing =>
                new NotSupportedException(surface + " must declare the current evidence schema."),
            _ => new NotSupportedException(surface + " has an invalid evidenceSchemaVersion.")
        };
    }

    internal static string NormalizePath(string path) {
        if (path == null) throw new ArgumentNullException(nameof(path));

        var trimmed = path.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            trimmed = uri.IsFile ? uri.LocalPath : uri.ToString();
        else if (trimmed.IndexOf('%') >= 0)
            trimmed = Uri.UnescapeDataString(trimmed);

        var normalized = trimmed.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized.Substring(2);

        var prefix = string.Empty;
        var segmentStart = 0;
        if (normalized.StartsWith("//", StringComparison.Ordinal)) {
            prefix = "//";
            segmentStart = 2;
        }
        else if (normalized.StartsWith("/", StringComparison.Ordinal)) {
            prefix = "/";
            segmentStart = 1;
        }
        else if (normalized.Length >= 3 && normalized[1] == ':' && normalized[2] == '/') {
            prefix = normalized.Substring(0, 3);
            segmentStart = 3;
        }

        var segments = new List<string>();
        foreach (var segment in normalized.Substring(segmentStart).Split('/')) {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..") {
                if (segments.Count > 0 && segments[segments.Count - 1] != "..")
                    segments.RemoveAt(segments.Count - 1);
                else if (prefix.Length == 0)
                    segments.Add(segment);
                continue;
            }

            segments.Add(segment);
        }

        return prefix + string.Join("/", segments);
    }

    private static bool HasAnyProperty(JsonElement element, params string[] names) =>
        names.Any(name => element.TryGetProperty(name, out _));

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value) {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
                value = property.Value;
                return true;
            }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() is { Length: > 0 } text ? text : null
            : null;

    private static int? ReadInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static bool MatchesOptional(string? expected, string? actual) =>
        string.IsNullOrWhiteSpace(expected) ||
        string.Equals(expected!.Trim(), actual?.Trim(), StringComparison.Ordinal);

    readonly record struct BaselineEntryKey(
        string Id,
        string Symbol,
        string Path,
        int? Line,
        int? Column,
        string? Contract,
        string? OperationKind,
        string? EvidenceKey) {
        internal static BaselineEntryKey Create(BaselineEntry entry) => new(
            entry.Id,
            entry.Symbol,
            (entry.Path ?? string.Empty).ToUpperInvariant(),
            entry.Line,
            entry.Column,
            NormalizeOptional(entry.Contract),
            NormalizeOptional(entry.OperationKind),
            NormalizeOptional(entry.EvidenceKey));

        private static string? NormalizeOptional(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }
}

internal readonly record struct BaselineEntryFields(
    bool HasCandidateProperty,
    string? Id,
    string? Symbol,
    string? Path,
    string? Message,
    int? Line,
    int? Column,
    string? Contract,
    string? OperationKind,
    string? EvidenceKey) {
    internal bool IsValid => Id != null && Symbol != null && Path != null;

    internal BaselineEntry ToEntry() => new(
        Id!, Symbol!, Path!, Message, Line, Column, Contract, OperationKind, EvidenceKey);
}

internal readonly record struct BaselineSchemaValidationFailure(
    BaselineSchemaValidationFailureKind Kind,
    int Version = 0,
    bool IsRoot = false);

internal enum BaselineSchemaValidationFailureKind {
    None,
    Missing,
    NonNumericVersion,
    UnsupportedVersion
}
