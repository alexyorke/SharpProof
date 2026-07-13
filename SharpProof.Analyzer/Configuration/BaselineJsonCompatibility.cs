using System.Text.Json;
using SharpProof.Schema;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer.Configuration;

internal static class BaselineJsonCompatibility
{
    internal static JsonDocumentOptions DocumentOptions { get; } = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    internal static bool TryValidateEvidenceSchemaTree(
        JsonElement element,
        string versionPropertyName,
        string compatibilityPropertyName,
        bool requireRootSchema,
        Func<JsonElement, bool> requiresNestedSchema,
        out EvidenceSchemaValidationFailure failure)
    {
        return TryValidateEvidenceSchemaTree(
            element,
            versionPropertyName,
            compatibilityPropertyName,
            requireRootSchema,
            requiresNestedSchema,
            isRoot: true,
            out failure);
    }

    internal static bool TryValidateEvidenceSchema(
        JsonElement element,
        string versionPropertyName,
        string compatibilityPropertyName,
        bool required,
        out EvidenceSchemaValidationFailure failure)
    {
        var (hasVersion, versionElement, hasCompatibility, compatibilityElement) =
            JsonElementCompatibility.ReadEvidenceSchemaProperties(
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

    internal static bool HasPropertyIgnoreCase(JsonElement element, string propertyName)
    {
        return JsonElementCompatibility.TryGetPropertyIgnoreCase(element, propertyName, out _);
    }

    internal static BaselineEntryJsonFields ReadEntryFields(JsonElement element)
    {
        string? id = null;
        string? symbol = null;
        string? path = null;
        string? contractText = null;
        string? operationKind = null;
        string? evidenceKey = null;
        int? line = null;
        int? column = null;
        var hasCandidateProperty = false;

        foreach (var property in element.EnumerateObject())
        {
            var isId = IsProperty(property, "id") || IsProperty(property, "diagnosticId");
            var isSymbol = IsProperty(property, "symbol");
            var isPath = IsProperty(property, "path");
            if (isId || isSymbol || isPath) hasCandidateProperty = true;

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value)) continue;

                value = value!.Trim();
                if (isId)
                    id = value;
                else if (isSymbol)
                    symbol = value;
                else if (isPath)
                    path = value;
                else if (IsProperty(property, "contract") || IsProperty(property, "contractText"))
                    contractText = value;
                else if (IsProperty(property, "operationKind") || IsProperty(property, "operation_kind"))
                    operationKind = value;
                else if (IsProperty(property, "evidenceKey") || IsProperty(property, "evidence_key"))
                    evidenceKey = value;
            }
            else if (property.Value.ValueKind == JsonValueKind.Number)
            {
                if (IsProperty(property, "line") && property.Value.TryGetInt32(out var parsedLine))
                    line = parsedLine;
                else if (IsProperty(property, "column") && property.Value.TryGetInt32(out var parsedColumn))
                    column = parsedColumn;
            }
        }

        return new BaselineEntryJsonFields(
            hasCandidateProperty,
            id,
            symbol,
            path,
            contractText,
            operationKind,
            evidenceKey,
            line,
            column);
    }

    private static bool TryValidateEvidenceSchemaTree(
        JsonElement element,
        string versionPropertyName,
        string compatibilityPropertyName,
        bool requireRootSchema,
        Func<JsonElement, bool> requiresNestedSchema,
        bool isRoot,
        out EvidenceSchemaValidationFailure failure)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            if (isRoot && requireRootSchema)
            {
                failure = new EvidenceSchemaValidationFailure(
                    EvidenceSchemaValidationFailureKind.Missing,
                    IsRoot: true);
                return false;
            }

            foreach (var item in element.EnumerateArray())
                if (!TryValidateEvidenceSchemaTree(
                        item,
                        versionPropertyName,
                        compatibilityPropertyName,
                        requireRootSchema: false,
                        requiresNestedSchema,
                        isRoot: false,
                        out failure))
                    return false;

            failure = default;
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            failure = default;
            return true;
        }

        var required = isRoot ? requireRootSchema : requiresNestedSchema(element);
        if (!TryValidateEvidenceSchema(
                element,
                versionPropertyName,
                compatibilityPropertyName,
                required,
                out failure))
        {
            failure = failure.WithRoot(isRoot);
            return false;
        }

        foreach (var property in element.EnumerateObject())
            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object &&
                !TryValidateEvidenceSchemaTree(
                    property.Value,
                    versionPropertyName,
                    compatibilityPropertyName,
                    requireRootSchema: false,
                    requiresNestedSchema,
                    isRoot: false,
                    out failure))
                return false;

        failure = default;
        return true;
    }

    private static bool IsProperty(JsonProperty property, string propertyName)
    {
        return string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase);
    }
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
    bool IsRoot = false)
{
    internal EvidenceSchemaValidationFailure WithRoot(bool isRoot)
    {
        return new EvidenceSchemaValidationFailure(Kind, Version, isRoot);
    }
}

internal enum EvidenceSchemaValidationFailureKind
{
    None,
    Missing,
    NonNumericVersion,
    UnsupportedVersion,
    InvalidCompatibility
}
