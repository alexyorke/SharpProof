using System.Text;

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
        if (lines.Any(static line => !IsData(line)))
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
        if (data.Any(static line => !IsData(line)))
        {
            throw Invalid();
        }
        return data;
    }

    private static bool IsData(string? line)
    {
        return !string.IsNullOrEmpty(line) && line[0] != '#';
    }

    private static InvalidDataException Invalid()
    {
        return new InvalidDataException(
            "Corpus snapshot does not use the canonical schema-3 byte format.");
    }
}
