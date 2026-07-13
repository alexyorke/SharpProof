using System.Text.Json;
using System.Text.Json.Serialization;

internal static class SymbolicCliOutputPolicy
{
    public const string ErrorJson = "--error-json";
    public const string Json = "--json";
    public const string CompactJson = "--compact-json";
    public const string Compact = "--compact";
    public const string InvariantJson = "--invariant-json";
    public const string InvariantQueryJson = "--invariant-query-json";
    public const string RequestJson = "--request-json";
    public const string RequestJsonStdin = "--request-json-stdin";
    public const string Sarif = "--sarif";
    public const string Markdown = "--markdown";

    public static JsonSerializerOptions CompactJsonOptions { get; } = CreateJsonOptions(
        false,
        JsonNamingPolicy.CamelCase,
        JsonIgnoreCondition.WhenWritingNull);

    public static JsonSerializerOptions FullJsonOptions { get; } = CreateJsonOptions(
        true,
        null,
        JsonIgnoreCondition.Never);

    public static bool RequestsJsonErrors(string argument)
    {
        return argument is ErrorJson or
            Json or
            CompactJson or
            Compact or
            InvariantJson or
            InvariantQueryJson or
            RequestJson or
            RequestJsonStdin or
            Sarif;
    }

    private static JsonSerializerOptions CreateJsonOptions(
        bool writeIndented,
        JsonNamingPolicy? namingPolicy,
        JsonIgnoreCondition ignoreCondition)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            DefaultIgnoreCondition = ignoreCondition,
            PropertyNamingPolicy = namingPolicy
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
