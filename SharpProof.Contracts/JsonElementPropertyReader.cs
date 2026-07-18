using System.Text.Json;

namespace SharpProof.Schema;

internal readonly record struct EvidenceSchemaJsonProperties(
    bool HasVersion,
    JsonElement Version,
    bool HasCompatibility,
    JsonElement Compatibility);

internal static class JsonElementPropertyReader
{
    internal static EvidenceSchemaJsonProperties ReadEvidenceSchemaProperties(
        JsonElement element,
        string versionPropertyName,
        string compatibilityPropertyName)
    {
        var hasVersion = TryGetPropertyIgnoreCase(element, versionPropertyName, out var version);
        var hasCompatibility = TryGetPropertyIgnoreCase(
            element,
            compatibilityPropertyName,
            out var compatibility);
        return new EvidenceSchemaJsonProperties(hasVersion, version, hasCompatibility, compatibility);
    }

    internal static bool TryGetPropertyIgnoreCase(
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
}
