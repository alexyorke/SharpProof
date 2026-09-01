using System.Collections.Immutable;
using System.Text;
using SharpProof.Analyzer;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates;

internal static class CorpusSnapshotFormat
{
    private static readonly string[] Header =
    [
        "# SharpProof analyzer corpus snapshot schema 3",
        "# case-id|verdict|semantic-outcome|sorted-diagnostics",
        "# diagnostic=id@effective-severity@normalized-location@base64-invariant-message"
    ];

    internal static string Render(IEnumerable<string> dataLines)
    {
        var lines = dataLines.ToArray();
        if (lines.Any(static line => !IsCanonicalData(line)) ||
            !IsCanonicalOrder(lines))
        {
            throw Invalid();
        }
        return string.Join("\n", Header.Concat(lines)) + "\n";
    }

    internal static string[] ReadDataLines(string path)
    {
        return Parse(File.ReadAllBytes(path));
    }

    internal static string[] Parse(byte[] bytes)
    {
        if (bytes.Length == 0 || (bytes.Length >= 3 && bytes[0] == 0xEF &&
                bytes[1] == 0xBB && bytes[2] == 0xBF))
        {
            throw Invalid();
        }
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\r')
            {
                throw Invalid();
            }
        }
        if (bytes[^1] != (byte)'\n' ||
            (bytes.Length > 1 && bytes[^2] == (byte)'\n'))
        {
            throw Invalid();
        }
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Corpus snapshot must be strict UTF-8.", exception);
        }
        var lines = text.Substring(0, text.Length - 1).Split('\n');
        if (lines.Length < Header.Length)
        {
            throw Invalid();
        }
        for (var index = 0; index < Header.Length; index++)
        {
            if (!string.Equals(lines[index], Header[index], StringComparison.Ordinal))
            {
                throw Invalid();
            }
        }
        var data = lines.Skip(Header.Length).ToArray();
        if (data.Any(static line => !IsCanonicalData(line)) ||
            !IsCanonicalOrder(data))
        {
            throw Invalid();
        }
        return data;
    }

    private static bool IsCanonicalData(string? line)
    {
        return TryParseData(line, out var expectation) &&
            string.Equals(
                line,
                expectation.ToCanonicalLine(),
                StringComparison.Ordinal);
    }

    internal static bool TryParseData(
        string? line,
        out CorpusObservation expectation)
    {
        expectation = null!;
        if (!IsData(line))
        {
            return false;
        }

        var parts = line!.Split('|');
        if (parts.Length != 4 ||
            !Enum.TryParse<CorpusVerdict>(
                parts[1],
                ignoreCase: false,
                out var verdict) ||
            !Enum.IsDefined(verdict) ||
            !Enum.TryParse<AnalyzerSemanticOutcome>(
                parts[2],
                ignoreCase: false,
                out var semanticOutcome) ||
            !Enum.IsDefined(semanticOutcome))
        {
            return false;
        }

        ImmutableArray<string> diagnostics = parts[3].Length == 0
            ? []
            : [.. parts[3].Split(',')
                .OrderBy(static diagnostic =>
                    diagnostic,
                    StringComparer.Ordinal)
            ];
        expectation = new CorpusObservation(
            parts[0],
            verdict,
            semanticOutcome,
            diagnostics);
        return true;
    }

    private static bool IsData(string? line)
    {
        return !string.IsNullOrEmpty(line) && line[0] != '#';
    }

    private static bool IsCanonicalOrder(string[] lines)
    {
        return lines.SequenceEqual(
            lines.OrderBy(static line => line, StringComparer.Ordinal));
    }

    private static InvalidDataException Invalid()
    {
        return new InvalidDataException(
            "Corpus snapshot does not use the canonical schema-3 byte format.");
    }
}
