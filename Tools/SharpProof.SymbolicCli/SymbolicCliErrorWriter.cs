using System.Text.Json;
using SharpProof.Symbolic;

internal static class SymbolicCliErrorWriter
{
    public static int Write(Exception exception, IReadOnlyList<string> arguments)
    {
        if (exception == null) throw new ArgumentNullException(nameof(exception));
        if (arguments == null) throw new ArgumentNullException(nameof(arguments));

        var error = SymbolicErrorClassifier.FromException(exception);
        if (ShouldWriteJson(arguments))
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(
                new SymbolicErrorEnvelope(error),
                SymbolicCliOutputPolicy.JsonOptions));
        }
        else
        {
            Console.Error.WriteLine($"{error.Code} [{error.Category}]: {error.Message}");
            if (error.Category == SymbolicErrorCategory.Usage)
                Console.Error.WriteLine(SymbolicCliOptions.Usage);
        }

        return error.RecommendedExitCode;
    }

    public static SymbolicQueryException CreateException(
        string code,
        SymbolicErrorCategory category,
        string message,
        int exitCode,
        string? detailName = null,
        string? detailValue = null,
        Exception? innerException = null)
    {
        var details = string.IsNullOrWhiteSpace(detailName)
            ? null
            : new[]
            {
                new KeyValuePair<string, string>(detailName!, detailValue ?? string.Empty)
            };
        var error = new SymbolicError(code, category, message, exitCode, details: details);
        return innerException == null
            ? new SymbolicQueryException(error)
            : new SymbolicQueryException(error, innerException);
    }

    private static bool ShouldWriteJson(IReadOnlyList<string> arguments)
    {
        return arguments.Any(static argument =>
            SymbolicCliOutputPolicy.RequestsJsonErrors(argument));
    }
}
