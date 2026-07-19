using System.Text;

namespace SharpProof.Analyzer.Configuration;

internal static class ExplainDiagnosticProperties
{
    internal static ImmutableDictionary<string, string?> Add(
        ImmutableDictionary<string, string?> properties,
        Location? location,
        string? contractText = null,
        string? proofStatus = null,
        string? unknownReason = null,
        string? impliedConditionText = null)
    {
        properties = SharpProofEvidenceSchema.AddDiagnosticProperties(properties);

        if (location != null && location != Location.None && location.IsInSource)
        {
            var lineSpan = location.GetLineSpan();
            var path = string.IsNullOrWhiteSpace(lineSpan.Path)
                ? location.SourceTree?.FilePath
                : lineSpan.Path;
            var normalizedPath = path?.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedPath))
            {
                var explainPath = normalizedPath!;
                var line = lineSpan.StartLinePosition.Line + 1;
                var column = lineSpan.StartLinePosition.Character + 1;
                properties = properties
                    .SetItem(SharpProofDiagnostics.ExplainFileProperty, explainPath)
                    .SetItem(SharpProofDiagnostics.ExplainLineProperty, line.ToString(CultureInfo.InvariantCulture))
                    .SetItem(SharpProofDiagnostics.ExplainColumnProperty, column.ToString(CultureInfo.InvariantCulture))
                    .SetItem(SharpProofDiagnostics.ExplainQueryProperty,
                        CreateExplainQuery(explainPath, line, column, impliedConditionText));
            }
        }

        if (!string.IsNullOrWhiteSpace(contractText))
            properties = properties.SetItem(SharpProofDiagnostics.ExplainContractProperty, contractText!.Trim());

        var normalizedProofStatus = NormalizeToken(proofStatus);
        if (!string.IsNullOrWhiteSpace(normalizedProofStatus))
            properties = properties.SetItem(SharpProofDiagnostics.ExplainProofStatusProperty, normalizedProofStatus);

        var normalizedUnknownReason = NormalizeToken(unknownReason);
        if (!string.IsNullOrWhiteSpace(normalizedUnknownReason))
            properties = properties.SetItem(SharpProofDiagnostics.ExplainUnknownReasonProperty,
                normalizedUnknownReason);

        return properties;
    }

    internal static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value!.Trim();
        var builder = new StringBuilder(trimmed.Length);
        var lastWasSeparator = false;
        var lastWasLowerOrDigit = false;
        foreach (var character in trimmed)
            if (char.IsLetterOrDigit(character))
            {
                if (char.IsUpper(character) && lastWasLowerOrDigit && !lastWasSeparator) builder.Append('_');

                builder.Append(char.ToLowerInvariant(character));
                lastWasSeparator = false;
                lastWasLowerOrDigit = char.IsLower(character) || char.IsDigit(character);
            }
            else if (!lastWasSeparator)
            {
                builder.Append('_');
                lastWasSeparator = true;
                lastWasLowerOrDigit = false;
            }

        return builder.ToString().Trim('_');
    }

    private static string CreateExplainQuery(
        string path,
        int line,
        int column,
        string? contractText)
    {
        var builder = new StringBuilder();
        builder.Append("SharpProof.SymbolicCli explain --file ");
        builder.Append(Quote(path));
        builder.Append(" --line ");
        builder.Append(line.ToString(CultureInfo.InvariantCulture));
        builder.Append(" --column ");
        builder.Append(column.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(contractText))
        {
            builder.Append(" --implies ");
            builder.Append(Quote(contractText!.Trim()));
        }

        return builder.ToString();
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
