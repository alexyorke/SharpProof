using System.Text.Json;
using System.Text.Json.Serialization;
using SharpProof.Symbolic;

internal sealed class SymbolicCliJsonRequest
{
    public int SchemaVersion { get; init; }

    public string[]? Arguments { get; init; }

    public static async Task<string[]> ExpandArgumentsAsync(
        string[] arguments,
        TextReader standardInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardInput);

        var requestIndexes = arguments
            .Select(static (argument, index) => (argument, index))
            .Where(static item => IsRequestSelector(item.argument))
            .Select(static item => item.index)
            .ToArray();
        if (requestIndexes.Length == 0) return arguments;
        if (requestIndexes.Length != 1 || requestIndexes[0] != 0)
            throw new ArgumentException(
                "--request-json or --request-json-stdin must be the sole request selector and appear first.");

        var json = arguments[0] switch
        {
            "--request-json" when arguments.Length == 2 => arguments[1],
            "--request-json" => throw new ArgumentException(
                "--request-json requires exactly one JSON value and no other options."),
            _ when arguments.Length == 1 => await standardInput.ReadToEndAsync(cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentException("--request-json-stdin cannot be combined with other options.")
        };

        SymbolicCliJsonRequest request;
        try
        {
            request = JsonSerializer.Deserialize<SymbolicCliJsonRequest>(json, SerializerOptions) ??
                      throw new ArgumentException("The JSON request envelope cannot be null.");
        }
        catch (JsonException exception)
        {
            throw SymbolicCliErrorWriter.CreateException(
                SymbolicErrorCodes.ParseFailed,
                SymbolicErrorCategory.Parse,
                "Invalid JSON request envelope: " + exception.Message,
                SymbolicErrorExitCodes.InvalidData,
                "input",
                "request-json",
                exception);
        }

        if (request.SchemaVersion != 2)
            throw new ArgumentException("JSON request schemaVersion must be 2.");
        if (request.Arguments == null || request.Arguments.Length == 0)
            throw new ArgumentException("JSON request arguments must contain at least one CLI argument.");
        if (request.Arguments.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("JSON request arguments cannot contain null or blank values.");
        if (request.Arguments.Any(IsRequestSelector))
            throw new ArgumentException("JSON request arguments cannot contain a nested JSON request selector.");

        return request.Arguments.ToArray();
    }

    private static bool IsRequestSelector(string? argument) =>
        argument is "--request-json" or "--request-json-stdin";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}
