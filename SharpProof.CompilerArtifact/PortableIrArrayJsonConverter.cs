using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpProof.CompilerArtifact;

// The wire schema distinguishes an explicitly null array from an omitted array
// (whose property initializer supplies an empty value).
internal sealed class PortableIrArrayJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        typeToConvert = RepositoryParameterGuard.NotNull(typeToConvert, nameof(typeToConvert));
        return typeToConvert.IsGenericType &&
            typeToConvert.GetGenericTypeDefinition() == typeof(ImmutableArray<>);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        typeToConvert = RepositoryParameterGuard.NotNull(typeToConvert, nameof(typeToConvert));
        options = RepositoryParameterGuard.NotNull(options, nameof(options));
        return (JsonConverter)Activator.CreateInstance(
            typeof(ArrayConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
    }

    private sealed class ArrayConverter<T> : JsonConverter<ImmutableArray<T>>
    {
        public ArrayConverter() { }

        public override bool HandleNull => true;

        public override ImmutableArray<T> Read(
            ref Utf8JsonReader reader /* SPINT001: all values valid - JSON token validity is checked before reading */,
            Type typeToConvert, JsonSerializerOptions options)
        {
            typeToConvert = RepositoryParameterGuard.NotNull(typeToConvert, nameof(typeToConvert));
            options = RepositoryParameterGuard.NotNull(options, nameof(options));
            if (reader.TokenType == JsonTokenType.Null)
            { return default; }
            if (reader.TokenType != JsonTokenType.StartArray)
            { throw new JsonException(); }
            var items = ImmutableList<T>.Empty;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                { return [.. items]; }
                items = items.Add(JsonSerializer.Deserialize<T>(ref reader, options)!);
            }
            throw new JsonException();
        }

        public override void Write(
            Utf8JsonWriter writer,
            ImmutableArray<T> value /* SPINT001: all values valid - default is the wire null sentinel */,
            JsonSerializerOptions options)
        {
            writer = RepositoryParameterGuard.NotNull(writer, nameof(writer));
            options = RepositoryParameterGuard.NotNull(options, nameof(options));
            if (value.IsDefault)
            { writer.WriteNullValue(); return; }
            writer.WriteStartArray();
            foreach (var item in value)
            { JsonSerializer.Serialize(writer, item, options); }
            writer.WriteEndArray();
        }
    }
}
