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
        ValidateCanonicalData(lines);
        return string.Join("\n", Header.Concat(lines)) + "\n";
    }

    internal static string[] ReadDataLines(string path)
    {
        return ParseDocument(File.ReadAllBytes(path)).DataLines;
    }

    internal static ImmutableArray<CorpusObservation> ReadObservations(
        string path)
    {
        return ParseDocument(File.ReadAllBytes(path)).Observations;
    }

    internal static string[] Parse(byte[] bytes)
    {
        return ParseDocument(bytes).DataLines;
    }

    private static ParsedSnapshot ParseDocument(byte[] bytes)
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
        return new ParsedSnapshot(data, ParseCanonicalData(data));
    }

    private static void ValidateCanonicalData(string[] lines)
    {
        _ = ParseCanonicalData(lines);
    }

    private static ImmutableArray<CorpusObservation> ParseCanonicalData(
        string[] lines)
    {
        var observations = ImmutableArray.CreateBuilder<CorpusObservation>(
            lines.Length);
        string? previousCanonical = null;
        for (var index = 0; index < lines.Length; index++)
        {
            if (!TryParseData(lines[index], out var observation) ||
                previousCanonical != null &&
                StringComparer.Ordinal.Compare(
                    previousCanonical,
                    lines[index]) > 0)
            {
                throw Invalid();
            }

            var canonical = observation.ToCanonicalLine();
            if (!string.Equals(
                    lines[index],
                    canonical,
                    StringComparison.Ordinal))
            {
                throw Invalid();
            }

            observations.Add(observation);
            previousCanonical = canonical;
        }

        return observations.ToImmutable();
    }

    internal static bool TryParseData(
        string? line,
        out CorpusObservation expectation)
    {
        expectation = null!;
        if (string.IsNullOrEmpty(line) || line[0] == '#')
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

    private static InvalidDataException Invalid()
    {
        return new InvalidDataException(
            "Corpus snapshot does not use the canonical schema-3 byte format.");
    }

    private sealed record ParsedSnapshot(
        string[] DataLines,
        ImmutableArray<CorpusObservation> Observations);
}
