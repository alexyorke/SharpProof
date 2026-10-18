using System.Text.Json;

namespace SharpProof.Host;

internal sealed record VerifierDiagnostic(
    string Severity,
    string Code,
    string File,
    int Line,
    int Column,
    string Message)
{
    public string Severity { get; init; } = Severity ?? throw new System.ArgumentNullException(nameof(Severity));
    public string Code { get; init; } = Code ?? throw new System.ArgumentNullException(nameof(Code));
    public string File { get; init; } = File ?? throw new System.ArgumentNullException(nameof(File));
    public string Message { get; init; } = Message ?? throw new System.ArgumentNullException(nameof(Message));
}

internal static class VerifierDiagnosticTransport
{
    internal const string Prefix = "##sharpproof-diagnostic-v1##";

    internal static string Serialize(VerifierDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        Validate(diagnostic);
        return Prefix + JsonSerializer.Serialize(new
        {
            schema = 1,
            severity = diagnostic.Severity,
            code = diagnostic.Code,
            file = diagnostic.File,
            line = diagnostic.Line,
            column = diagnostic.Column,
            message = diagnostic.Message
        });
    }

    internal static bool TryDeserialize(
        string line,
        out VerifierDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(line);
        diagnostic = null!;
        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                line.AsMemory(Prefix.Length));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            const int AllFieldsMask = (1 << 7) - 1;
            var fields = 0;
            foreach (var property in root.EnumerateObject())
            {
                var field = property.Name switch
                {
                    "schema" => 1 << 0,
                    "severity" => 1 << 1,
                    "code" => 1 << 2,
                    "file" => 1 << 3,
                    "line" => 1 << 4,
                    "column" => 1 << 5,
                    "message" => 1 << 6,
                    _ => 0
                };
                if (field == 0 || (fields & field) != 0)
                {
                    return false;
                }
                fields |= field;
            }
            if (fields != AllFieldsMask ||
                root.GetProperty("schema").GetInt32() != 1)
            {
                return false;
            }

            var parsed = new VerifierDiagnostic(
                root.GetProperty("severity").GetString()!,
                root.GetProperty("code").GetString()!,
                root.GetProperty("file").GetString()!,
                root.GetProperty("line").GetInt32(),
                root.GetProperty("column").GetInt32(),
                root.GetProperty("message").GetString()!);
            Validate(parsed);
            diagnostic = parsed;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or
                InvalidOperationException or JsonException or
                KeyNotFoundException or OverflowException)
        {
            return false;
        }
    }

    private static void Validate(VerifierDiagnostic diagnostic)
    {
        if (diagnostic.Severity is not ("warning" or "error") ||
            diagnostic.Code is not (
                VerifierDiagnosticCodes.IncompleteSelectedCallable or
                VerifierDiagnosticCodes.AssumptionsDeclared or
                VerifierDiagnosticCodes.RefutedContract) ||
            diagnostic.File == null || diagnostic.Message == null ||
            diagnostic.Line < 0 || diagnostic.Column < 0)
        {
            throw new ArgumentException(
                "The verifier diagnostic transport payload is invalid.",
                nameof(diagnostic));
        }
    }
}
