using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorBuilder = System.Collections.Immutable.ImmutableArray<SharpProof.Worker.Protocol.WorkerProtocolError>.Builder;

namespace SharpProof.Worker.Protocol;

internal readonly struct WorkerProtocolJsonPropertyShape(
    string name,
    string type)
{
    internal readonly string Name = name;
    internal readonly string Type = type;
}

internal sealed class WorkerProtocolJsonObjectShape(
    WorkerProtocolJsonPropertyShape[] properties)
{
    internal WorkerProtocolJsonPropertyShape[] Properties { get; } =
        properties;
}

public static partial class WorkerProtocolJson
{
    private static JsonDocument ParseAndEnsureJsonShape(
        string json,
        string rootType)
    {
        json = json ?? throw new ArgumentNullException(nameof(json));

        var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        try
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("A JSON object is required.");
            }

            if (!WorkerProtocolMetadata.JsonObjectShapes.TryGetValue(
                    rootType,
                    out var shape))
            {
                throw new JsonException("The JSON root type is not declared.");
            }
            EnsureObjectShape(document.RootElement, shape);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static void EnsureObjectShape(
        JsonElement value,
        WorkerProtocolJsonObjectShape shape)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A JSON object is required.");
        }
        if (value.GetPropertyCount() != shape.Properties.Length)
        {
            throw new JsonException(
                "Every declared JSON property is required exactly once.");
        }
        var index = 0;
        foreach (var property in value.EnumerateObject())
        {
            var expected = shape.Properties[index];
            if (!property.NameEquals(expected.Name))
            {
                throw new JsonException(
                    "JSON properties must use the exact declared name and order.");
            }
            EnsureValueShape(property.Value, expected.Type);
            index++;
        }
    }

    private static void EnsureValueShape(JsonElement value, string declaredType)
    {
        var allowsNull = declaredType.EndsWith("?", StringComparison.Ordinal);
        if (allowsNull)
        {
            declaredType = declaredType.Substring(0, declaredType.Length - 1);
            if (value.ValueKind == JsonValueKind.Null)
            {
                return;
            }
        }
        if (declaredType.EndsWith("[]", StringComparison.Ordinal))
        {
            EnsureArrayShape(
                value,
                declaredType.Substring(0, declaredType.Length - 2));
            return;
        }
        const string immutableArrayPrefix = "ImmutableArray<";
        if (declaredType.StartsWith(
                immutableArrayPrefix,
                StringComparison.Ordinal) &&
            declaredType.EndsWith(">", StringComparison.Ordinal))
        {
            EnsureArrayShape(
                value,
                declaredType.Substring(
                    immutableArrayPrefix.Length,
                    declaredType.Length - immutableArrayPrefix.Length - 1));
            return;
        }
        if (WorkerProtocolMetadata.JsonObjectShapes.TryGetValue(
                declaredType,
                out var objectShape))
        {
            EnsureObjectShape(value, objectShape);
            return;
        }
        if (declaredType == "string")
        {
            RequireValueKind(value, JsonValueKind.String);
            return;
        }
        if (declaredType == "bool")
        {
            if (value.ValueKind is not (
                    JsonValueKind.True or JsonValueKind.False))
            {
                throw new JsonException("A JSON boolean is required.");
            }
            return;
        }
        if (declaredType is "int" or "uint" or "long")
        {
            RequireValueKind(value, JsonValueKind.Number);
            return;
        }
        EnsureCanonicalEnum(value, declaredType);
    }

    private static void EnsureArrayShape(
        JsonElement value,
        string elementType)
    {
        RequireValueKind(value, JsonValueKind.Array);
        foreach (var item in value.EnumerateArray())
        {
            EnsureValueShape(item, elementType);
        }
    }

    internal static void EnsureCanonicalEnum(
        JsonElement value,
        string declaredType)
    {
        RequireValueKind(value, JsonValueKind.String);
        if (!WorkerProtocolMetadata.TryGetEnumType(
                declaredType,
                out var enumType))
        {
            throw new JsonException("The declared JSON enum type is invalid.");
        }

        if (!WorkerProtocolMetadata.IsFlagsEnum(declaredType))
        {
            if (!WorkerProtocolMetadata.IsCanonicalEnumValue(value, declaredType))
            {
                throw new JsonException("The JSON enum value is invalid.");
            }

            return;
        }

        var text = value.GetString();
        if (text == null)
        {
            throw new JsonException("The JSON enum value is invalid.");
        }

        object parsed;
        try
        {
            parsed = Enum.Parse(enumType, text, ignoreCase: false);
        }
        catch (Exception exception) when (
            exception is ArgumentException or OverflowException or FormatException)
        {
            throw new JsonException("The JSON enum value is invalid.", exception);
        }
        if (!string.Equals(parsed.ToString(), text, StringComparison.Ordinal))
        {
            throw new JsonException("The JSON enum spelling is not canonical.");
        }
    }

    private static void RequireValueKind(
        JsonElement value,
        JsonValueKind expected)
    {
        if (value.ValueKind != expected)
        {
            throw new JsonException(
                $"JSON token kind '{expected}' is required.");
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = false,
            MaxDepth = MaximumJsonDepth,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(
            namingPolicy: null,
            allowIntegerValues: false));
        return options;
    }

    private sealed class ManifestWriter
        : IDisposable
    {
        private readonly StringBuilder? _builder;
        private readonly IncrementalHash? _hash;
        private readonly byte[]? _buffer;

        internal ManifestWriter()
        {
            _builder = new();
        }

        internal ManifestWriter(IncrementalHash hash)
        {
            _hash = hash ?? throw new ArgumentNullException(nameof(hash));
            _buffer = ArrayPool<byte>.Shared.Rent(4096);
        }

        internal ManifestWriter Add(int value)
        {
            return Add(value.ToString(CultureInfo.InvariantCulture));
        }

        internal ManifestWriter Add(string? value)
        {
            if (value == null)
            {
                Append("-1:;");
            }
            else
            {
                Append(value.Length.ToString(CultureInfo.InvariantCulture));
                Append(":");
                Append(value);
                Append(";");
            }

            return this;
        }

        internal ManifestWriter AddItems<T>(
            string domain,
            T[]? source,
            IEnumerable<T> ordered,
            Action<ManifestWriter, T> write)
        {
            Add(domain).Add(source?.Length ?? -1);
            foreach (var value in ordered)
            {
                write(this, value);
            }

            return this;
        }

        internal ManifestWriter AddLocation(
            string domain,
            WorkerSourceLocation? value)
        {
            if (value == null)
            {
                return Add(domain).Add(-1);
            }

            return Add(domain).Add(5)
                .Add("location.path").Add(value.Path)
                .Add("location.start").Add(value.Start)
                .Add("location.length").Add(value.Length)
                .Add("location.line").Add(value.Line)
                .Add("location.column").Add(value.Column);
        }

        public override string ToString()
        {
            return _builder?.ToString() ??
                throw new InvalidOperationException(
                    "A streaming manifest writer has no string payload.");
        }

        public void Dispose()
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }
        }

        private void Append(string value)
        {
            if (_builder != null)
            {
                _builder.Append(value);
                return;
            }

            var hash = _hash!;
            var buffer = _buffer!;
            var byteCount = s_strictUtf8.GetByteCount(value);
            if (byteCount <= buffer.Length)
            {
                var written = s_strictUtf8.GetBytes(
                    value,
                    0,
                    value.Length,
                    buffer,
                    0);
                hash.AppendData(buffer, 0, written);
                return;
            }

            hash.AppendData(s_strictUtf8.GetBytes(value));
        }
    }

    private sealed class Validator
    {
        private readonly ErrorBuilder _errors =
            ImmutableArray.CreateBuilder<WorkerProtocolError>();

        internal int Count => _errors.Count;
        internal WorkerProtocolValidationResult Result => new(_errors);

        internal void Add(string code)
        {
            _errors.Add(new WorkerProtocolError
            {
                Code = code,
                Message = $"Protocol invariant '{code}' was not satisfied."
            });
        }

        internal Validator Check(bool valid, string code)
        {
            if (!valid)
            {
                Add(code);
            }

            return this;
        }

        internal Validator Defined<T>(
            T value,
            T unspecified,
            string code)
            where T : struct, Enum
        {
            return Check(IsDefined(value, unspecified), code);
        }

        internal Validator Rules<T>(
            T value,
            IEnumerable<WorkerProtocolRule<T>> rules,
            string prefix = "")
        {
            foreach (var rule in rules)
            {
                Check(rule.IsValid(value), prefix + rule.Code);
            }

            return this;
        }

        internal WorkerProtocolValidationResult Fail(string code)
        {
            Add(code);
            return Result;
        }
    }
}
