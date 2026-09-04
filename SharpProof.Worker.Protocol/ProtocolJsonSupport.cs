using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Globalization;
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
    private enum JsonValueShapeKind
    {
        Array,
        Object,
        String,
        Boolean,
        Number,
        Enum
    }

    private sealed class JsonValueShape
    {
        internal JsonValueShape(
            bool allowsNull,
            string declaredType,
            JsonValueShapeKind kind,
            string? elementType = null,
            WorkerProtocolJsonObjectShape? objectShape = null)
        {
            AllowsNull = allowsNull;
            DeclaredType = declaredType;
            Kind = kind;
            ElementType = elementType;
            ObjectShape = objectShape;
        }

        internal bool AllowsNull { get; }
        internal string DeclaredType { get; }
        internal JsonValueShapeKind Kind { get; }
        internal string? ElementType { get; }
        internal WorkerProtocolJsonObjectShape? ObjectShape { get; }
    }

    private static readonly ConcurrentDictionary<string, Type> s_enumTypes =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, JsonValueShape> s_valueShapes =
        new(StringComparer.Ordinal);

    private static void EnsureJsonShape(
        JsonElement root,
        string rootType)
    {
        if (!WorkerProtocolMetadata.JsonObjectShapes.TryGetValue(
                rootType,
                out var shape))
        {
            throw new JsonException("The JSON root type is not declared.");
        }
        try
        {
            EnsureObjectShape(root, shape);
        }
        catch (InvalidOperationException exception)
        {
            throw new JsonException(
                "The JSON contains an invalid UTF-16 string.", exception);
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
        var index = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (index >= shape.Properties.Length)
            {
                throw new JsonException(
                    "Every declared JSON property is required exactly once.");
            }

            var expected = shape.Properties[index];
            if (!string.Equals(
                    property.Name,
                    expected.Name,
                    StringComparison.Ordinal))
            {
                throw new JsonException(
                    "JSON properties must use the exact declared name and order.");
            }
            EnsureValueShape(property.Value, expected.Type);
            index++;
        }
        if (index != shape.Properties.Length)
        {
            throw new JsonException(
                "Every declared JSON property is required exactly once.");
        }
    }

    private static void EnsureValueShape(JsonElement value, string declaredType)
    {
        var shape = s_valueShapes.GetOrAdd(
            declaredType,
            static type => CreateValueShape(type));
        if (shape.AllowsNull && value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        switch (shape.Kind)
        {
            case JsonValueShapeKind.Array:
                EnsureArrayShape(value, shape.ElementType!);
                return;
            case JsonValueShapeKind.Object:
                EnsureObjectShape(value, shape.ObjectShape!);
                return;
            case JsonValueShapeKind.String:
                RequireValueKind(value, JsonValueKind.String);
                EnsureNoLoneSurrogates(value.GetString());
                return;
            case JsonValueShapeKind.Boolean:
                if (value.ValueKind is not (
                        JsonValueKind.True or JsonValueKind.False))
                {
                    throw new JsonException("A JSON boolean is required.");
                }
                return;
            case JsonValueShapeKind.Number:
                RequireValueKind(value, JsonValueKind.Number);
                return;
            case JsonValueShapeKind.Enum:
                EnsureCanonicalEnum(value, shape.DeclaredType);
                return;
            default:
                throw new JsonException("The declared JSON value type is invalid.");
        }
    }

    private static JsonValueShape CreateValueShape(string declaredType)
    {
        var allowsNull = declaredType.EndsWith("?", StringComparison.Ordinal);
        var normalizedType = allowsNull
            ? declaredType.Substring(0, declaredType.Length - 1)
            : declaredType;
        if (normalizedType.EndsWith("[]", StringComparison.Ordinal))
        {
            return new JsonValueShape(
                allowsNull,
                normalizedType,
                JsonValueShapeKind.Array,
                normalizedType.Substring(0, normalizedType.Length - 2));
        }

        const string immutableArrayPrefix = "ImmutableArray<";
        if (normalizedType.StartsWith(
                immutableArrayPrefix,
                StringComparison.Ordinal) &&
            normalizedType.EndsWith(">", StringComparison.Ordinal))
        {
            return new JsonValueShape(
                allowsNull,
                normalizedType,
                JsonValueShapeKind.Array,
                normalizedType.Substring(
                    immutableArrayPrefix.Length,
                    normalizedType.Length - immutableArrayPrefix.Length - 1));
        }

        if (WorkerProtocolMetadata.JsonObjectShapes.TryGetValue(
                normalizedType,
                out var objectShape))
        {
            return new JsonValueShape(
                allowsNull,
                normalizedType,
                JsonValueShapeKind.Object,
                objectShape: objectShape);
        }

        var kind = normalizedType switch
        {
            "string" => JsonValueShapeKind.String,
            "bool" => JsonValueShapeKind.Boolean,
            "int" or "uint" or "long" => JsonValueShapeKind.Number,
            _ => JsonValueShapeKind.Enum
        };
        return new JsonValueShape(allowsNull, normalizedType, kind);
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

    private static void EnsureCanonicalEnum(
        JsonElement value,
        string declaredType)
    {
        RequireValueKind(value, JsonValueKind.String);
        var enumType = s_enumTypes.GetOrAdd(
            declaredType,
            static typeName =>
            {
                var type = typeof(WorkerProtocolJson).Assembly.GetType(
                    typeof(WorkerProtocolJson).Namespace + "." + typeName,
                    throwOnError: false,
                    ignoreCase: false);
                return type is { IsEnum: true } ? type : typeof(void);
            });
        var text = value.GetString();
        if (!enumType.IsEnum || text == null)
        {
            throw new JsonException("The declared JSON enum type is invalid.");
        }
        EnsureNoLoneSurrogates(text);
        object parsed;
        try
        {
            parsed = Enum.Parse(enumType, text, ignoreCase: false);
        }
        catch (Exception exception) when (
            exception is ArgumentException or OverflowException)
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

    private static void EnsureNoLoneSurrogates(string? value)
    {
        if (value != null && !Utf16WellFormedness.IsWellFormed(value))
        {
            throw new JsonException(
                "JSON strings must not contain lone UTF-16 surrogates.");
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
    {
        private readonly StringBuilder _builder = new();

        internal ManifestWriter Add(int value)
        {
            return Add(value.ToString(CultureInfo.InvariantCulture));
        }

        internal ManifestWriter Add(string? value)
        {
            if (value == null)
            {
                _builder.Append("-1:;");
            }
            else
            {
                _builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(value).Append(';');
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
            return Add(domain).Add(value == null ? -1 : 5)
                .Add("location.path").Add(value?.Path)
                .Add("location.start").Add(value?.Start ?? -1)
                .Add("location.length").Add(value?.Length ?? -1)
                .Add("location.line").Add(value?.Line ?? -1)
                .Add("location.column").Add(value?.Column ?? -1);
        }

        public override string ToString()
        {
            return _builder.ToString();
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
