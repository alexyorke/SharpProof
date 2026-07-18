using System.Text.Json;

namespace SharpProof.Analyzer;

internal static class AnalyzerJsonElementReader
{
    public static string? GetTrimmedStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var valueElement) ||
            valueElement.ValueKind != JsonValueKind.String)
            return null;

        var value = valueElement.GetString();
        if (string.IsNullOrWhiteSpace(value)) return null;

        return value!.Trim();
    }
}
