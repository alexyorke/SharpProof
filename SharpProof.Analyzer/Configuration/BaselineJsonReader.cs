using System.Text.Json;
using SharpProof.Schema;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer.Configuration;

internal static class BaselineJsonReader
{
    internal static JsonDocumentOptions DocumentOptions { get; } = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    internal static bool TryValidateBaselineEvidenceSchemaTree(
        JsonElement element,
        out EvidenceSchemaValidationFailure failure)
    {
        if (!TryValidateEvidenceSchema(
                element,
            "evidenceSchemaVersion",
            "evidenceSchemaCompatibility",
            required: true,
                out failure))
        {
            failure = failure with { IsRoot = true };
            return false;
        }

        if (!element.TryGetProperty("diagnostics", out var diagnostics) ||
            diagnostics.ValueKind != JsonValueKind.Array)
            return true;

        foreach (var entry in diagnostics.EnumerateArray())
            if (entry.ValueKind == JsonValueKind.Object &&
                !TryValidateEvidenceSchema(
                    entry,
                    "evidenceSchemaVersion",
                    "evidenceSchemaCompatibility",
                    required: true,
                    out failure))
                return false;

        failure = default;
        return true;
    }

    internal static bool TryValidateEvidenceSchema(
        JsonElement element,
        string versionPropertyName,
        string compatibilityPropertyName,
        bool required,
        out EvidenceSchemaValidationFailure failure)
    {
        var (hasVersion, versionElement, hasCompatibility, compatibilityElement) =
            JsonElementPropertyReader.ReadEvidenceSchemaProperties(
                element,
                versionPropertyName,
                compatibilityPropertyName);
        if (!hasVersion && !hasCompatibility)
        {
            if (!required)
            {
                failure = default;
                return true;
            }

            failure = new EvidenceSchemaValidationFailure(EvidenceSchemaValidationFailureKind.Missing);
            return false;
        }

        if (!hasVersion ||
            versionElement.ValueKind != JsonValueKind.Number ||
            !versionElement.TryGetInt32(out var version))
        {
            failure = new EvidenceSchemaValidationFailure(EvidenceSchemaValidationFailureKind.NonNumericVersion);
            return false;
        }

        if (!SharpProofEvidenceSchema.IsReadCompatible(version))
        {
            failure = new EvidenceSchemaValidationFailure(
                EvidenceSchemaValidationFailureKind.UnsupportedVersion,
                version);
            return false;
        }

        if (!hasCompatibility ||
            compatibilityElement.ValueKind != JsonValueKind.String ||
            !string.Equals(
                compatibilityElement.GetString(),
                SharpProofEvidenceSchema.CompatibilityPolicy,
                StringComparison.Ordinal))
        {
            failure = new EvidenceSchemaValidationFailure(EvidenceSchemaValidationFailureKind.InvalidCompatibility);
            return false;
        }

        failure = default;
        return true;
    }

    internal static BaselineEntryJsonFields ReadEntryFields(JsonElement element)
    {
        return new BaselineEntryJsonFields(
            HasAnyProperty(element, "id", "symbol", "path"),
            ReadString(element, "id"),
            ReadString(element, "symbol"),
            ReadString(element, "path"),
            ReadString(element, "contract"),
            ReadString(element, "operationKind"),
            ReadString(element, "evidenceKey"),
            ReadInt32(element, "line"),
            ReadInt32(element, "column"));
    }

    private static bool HasAnyProperty(JsonElement element, params string[] names) =>
        names.Any(name => element.TryGetProperty(name, out _));

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() is { Length: > 0 } text ? text : null
            : null;

    private static int? ReadInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
}

internal readonly record struct BaselineEntryJsonFields(
    bool HasCandidateProperty,
    string? Id,
    string? Symbol,
    string? Path,
    string? ContractText,
    string? OperationKind,
    string? EvidenceKey,
    int? Line,
    int? Column)
{
    internal bool IsValid => Id != null && Symbol != null && Path != null;
}

internal readonly record struct EvidenceSchemaValidationFailure(
    EvidenceSchemaValidationFailureKind Kind,
    int Version = 0,
    bool IsRoot = false);

internal enum EvidenceSchemaValidationFailureKind
{
    None,
    Missing,
    NonNumericVersion,
    UnsupportedVersion,
    InvalidCompatibility
}
