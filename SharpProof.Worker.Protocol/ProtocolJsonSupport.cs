using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorBuilder = System.Collections.Immutable.ImmutableArray<SharpProof.Worker.Protocol.WorkerProtocolError>.Builder;

namespace SharpProof.Worker.Protocol;

public static partial class WorkerProtocolJson
{
    private static void EnsureRootProperties(
        string json,
        IEnumerable<string> requiredProperties)
    {
        json = json ?? throw new ArgumentNullException(nameof(json));

        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A JSON object is required.");
        }

        EnsureUniquePropertyNames(document.RootElement);
        var names = new HashSet<string>(
            document.RootElement.EnumerateObject().Select(static property => property.Name),
            StringComparer.Ordinal);
        if (requiredProperties.Any(property => !names.Contains(property)))
        {
            throw new JsonException("A required JSON property is missing.");
        }
    }

    private static void EnsureUniquePropertyNames(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                EnsureUniquePropertyNames(item);
            }

            return;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new JsonException("Duplicate JSON properties are not permitted.");
            }

            EnsureUniquePropertyNames(property.Value);
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
