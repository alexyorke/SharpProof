using System.Collections.Immutable;
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
    private static void EnsureJsonShape(
        string json,
        string rootType)
    {
        json = json ?? throw new ArgumentNullException(nameof(json));

        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
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
    }

    private static void EnsureObjectShape(
        JsonElement value,
        WorkerProtocolJsonObjectShape shape)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A JSON object is required.");
        }
        var properties = value.EnumerateObject().ToArray();
        if (properties.Length != shape.Properties.Length)
        {
            throw new JsonException(
                "Every declared JSON property is required exactly once.");
        }
        for (var index = 0; index < properties.Length; index++)
        {
            var expected = shape.Properties[index];
            var property = properties[index];
            if (!string.Equals(
                    property.Name,
                    expected.Name,
                    StringComparison.Ordinal))
            {
                throw new JsonException(
                    "JSON properties must use the exact declared name and order.");
            }
            EnsureValueShape(property.Value, expected.Type);
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

    private static void EnsureCanonicalEnum(
        JsonElement value,
        string declaredType)
    {
        RequireValueKind(value, JsonValueKind.String);
        var enumType = typeof(WorkerProtocolJson).Assembly.GetType(
            typeof(WorkerProtocolJson).Namespace + "." + declaredType,
            throwOnError: false,
            ignoreCase: false);
        var text = value.GetString();
        if (enumType == null || !enumType.IsEnum || text == null)
        {
            throw new JsonException("The declared JSON enum type is invalid.");
        }
        object parsed;
        try
        {
            parsed = Enum.Parse(enumType, text, ignoreCase: false);
        }
        catch (ArgumentException exception)
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
