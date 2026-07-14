using System.Text.Json;

namespace SharpProof.Tools.Shared;

internal static class SarifJsonFacts
{
    internal static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    internal static string? GetMessageText(JsonElement result)
    {
        return result.TryGetProperty("message", out var message) &&
               message.ValueKind == JsonValueKind.Object
            ? GetStringProperty(message, "text")
            : null;
    }
}
