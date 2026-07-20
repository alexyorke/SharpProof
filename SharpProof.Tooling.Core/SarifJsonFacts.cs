using System.Text.Json;

namespace SharpProof.Tools.Shared;

internal static class SarifJsonFacts {
    internal static IEnumerable<JsonElement> EnumerateResults(JsonElement sarifRoot) {
        if (!sarifRoot.TryGetProperty("runs", out var runs) ||
            runs.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var run in runs.EnumerateArray()) {
            if (!run.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var result in results.EnumerateArray()) yield return result;
        }
    }

    internal static string? GetStringProperty(JsonElement element, string propertyName) {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    internal static string? GetMessageText(JsonElement result) {
        return result.TryGetProperty("message", out var message) &&
               message.ValueKind == JsonValueKind.Object
            ? GetStringProperty(message, "text")
            : null;
    }

    internal static string? GetEvidenceProperty(
        JsonElement properties,
        string propertyName,
        bool includeCustomProperties = false) {
        var value = GetStringProperty(properties, propertyName);
        if (!string.IsNullOrWhiteSpace(value)) return value.Trim();

        if (!includeCustomProperties ||
            !properties.TryGetProperty("customProperties", out var customProperties) ||
            customProperties.ValueKind != JsonValueKind.Object)
            return null;

        value = GetStringProperty(customProperties, propertyName);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
