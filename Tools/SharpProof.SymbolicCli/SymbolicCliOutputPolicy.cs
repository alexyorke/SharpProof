using System.Text.Json;
using System.Text.Json.Serialization;

internal static class SymbolicCliOutputPolicy
{
    public const string ErrorJson = "--error-json";
    public const string Json = "--json";
    public const string RequestJson = "--request-json";
    public const string RequestJsonStdin = "--request-json-stdin";
    public const string Sarif = "--sarif";
    public const string Markdown = "--markdown";

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool RequestsJsonErrors(string argument) =>
        argument is ErrorJson or Json or RequestJson or RequestJsonStdin or Sarif;
}
