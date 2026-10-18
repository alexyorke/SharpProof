using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpProof.Worker.Protocol;

public static partial class WorkerProtocolJson
{
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = false,
            MaxDepth = MaximumJsonDepth,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false
        };
        options.Converters.Add(new JsonStringEnumConverter(
            namingPolicy: null,
            allowIntegerValues: false));
        return options;
    }

    private sealed class Validator
    {
        private ImmutableList<WorkerProtocolError> _errors = ImmutableList<WorkerProtocolError>.Empty;

        internal int Count => _errors.Count;
        internal WorkerProtocolValidationResult Result => new(_errors);

        internal void Add(string code)
        {
            code = RepositoryParameterGuard.NotNull(code, nameof(code));
            _errors = _errors.Add(new WorkerProtocolError
            {
                Code = code,
                Message = $"Protocol invariant '{code}' was not satisfied."
            });
        }

        internal Validator Check(bool valid, string code)
        {
            code = RepositoryParameterGuard.NotNull(code, nameof(code));
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
            rules = RepositoryParameterGuard.NotNull(rules, nameof(rules));
            foreach (var rule in rules)
            {
                Check(rule.IsValid(value), prefix + rule.Code);
            }

            return this;
        }

        internal WorkerProtocolValidationResult Fail(string code)
        {
            code = RepositoryParameterGuard.NotNull(code, nameof(code));
            Add(code);
            return Result;
        }
    }
}
