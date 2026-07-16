using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpProof.Symbolic;

public static class SymbolicCliJsonProjectionExtensions
{
    public static JsonElement ToCompactResult(this SymbolicCapabilityResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var projection = CreateMethodProjection(result, "capabilities");
        projection["capabilities"] = result.Capabilities;
        projection["capabilityText"] = result.CapabilityText;
        projection["hasUnknowns"] = result.HasUnknowns;
        projection["unknownReasons"] = result.UnknownReasons;
        projection["unknownReasonDetails"] = result.UnknownReasonDetails;
        projection["sites"] = result.Sites;
        return JsonSerializer.SerializeToElement(projection, SymbolicCliProjectionJson.Options);
    }

    public static JsonElement ToCompactResult(this SymbolicComplexityResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var projection = CreateMethodProjection(result, "complexity");
        projection["complexity"] = result.Complexity;
        projection["drivers"] = result.Drivers;
        projection["unknownReasons"] = result.UnknownReasons;
        projection["unknownReasonDetails"] = result.UnknownReasonDetails;
        projection["calleeSummaries"] = result.CalleeSummaries;
        return JsonSerializer.SerializeToElement(projection, SymbolicCliProjectionJson.Options);
    }

    public static JsonElement ToCompactResult(
        this SymbolicRuntimeHazardQueryResult result,
        SymbolicCompactRuntimeHazardQueryOptions? options = null)
    {
        return SymbolicCompactRuntimeHazardProjection.Create(result, options).Json;
    }

    public static JsonElement ToCompactResult(
        this SymbolicProgramPointResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        return SymbolicCompactQueryProjection.Create(SymbolicQueryResult.From(result), options).Json;
    }

    public static JsonElement ToInvariantQueryResult(
        this SymbolicProgramPointResult result,
        SymbolicCompactQueryOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        return SymbolicInvariantQueryProjection.Create(SymbolicQueryResult.From(result), options).Json;
    }

    public static JsonElement ToCompactResult(
        this SymbolicQueryResult result,
        SymbolicCompactQueryOptions? options = null) => SymbolicCompactQueryProjection.Create(result, options).Json;

    public static JsonElement ToInvariantQueryResult(
        this SymbolicQueryResult result,
        SymbolicCompactQueryOptions? options = null) => SymbolicInvariantQueryProjection.Create(result, options).Json;

    private static Dictionary<string, object?> CreateMethodProjection(SymbolicMethodResult result, string kind) =>
        new()
        {
            ["schemaVersion"] = 1,
            ["evidenceSchemaVersion"] = SharpProofEvidenceSchema.CurrentVersion,
            ["evidenceSchemaCompatibility"] = SharpProofEvidenceSchema.CompatibilityPolicy,
            ["kind"] = kind,
            ["filePath"] = result.FilePath,
            ["methodDisplayName"] = result.MethodDisplayName,
            ["declarationKind"] = result.DeclarationKind,
            ["spanStart"] = result.SpanStart,
            ["spanEnd"] = result.SpanEnd,
            ["startLine"] = result.StartLine,
            ["startColumn"] = result.StartColumn,
            ["endLine"] = result.EndLine,
            ["endColumn"] = result.EndColumn
        };

}

internal static class SymbolicCliProjectionJson
{
    internal static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new SymbolicRawJsonProjectionConverter());
        return options;
    }
}

internal interface ISymbolicRawJsonProjection
{
    JsonElement Json { get; }
}

internal sealed class SymbolicRawJsonProjectionConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeof(ISymbolicRawJsonProjection).IsAssignableFrom(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return (JsonConverter)Activator.CreateInstance(
            typeof(Converter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class Converter<T> : JsonConverter<T> where T : ISymbolicRawJsonProjection
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            value.Json.WriteTo(writer);
    }
}
