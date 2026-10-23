using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SharpProof.Tool;

internal enum CommandOutputFormat
{
    Text,
    Json,
    Markdown
}

internal static class CommandPresentationFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static CommandOutputFormat ParseFormat(string? format)
    {
        return format switch
        {
            null or "text" => CommandOutputFormat.Text,
            "json" => CommandOutputFormat.Json,
            "markdown" => CommandOutputFormat.Markdown,
            _ => throw new ArgumentException("--format must be text, json, or markdown.", nameof(format))
        };
    }

    internal static async Task WriteAsync(TextWriter writer, CommandPresentation presentation, CommandOutputFormat format, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(presentation);
        cancellationToken.ThrowIfCancellationRequested();
        var text = format switch
        {
            CommandOutputFormat.Text => presentation.Text,
            CommandOutputFormat.Json => JsonSerializer.Serialize(presentation, JsonOptions) + "\n",
            CommandOutputFormat.Markdown => Markdown(presentation),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported command output format.")
        };
        await writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static string Markdown(CommandPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        var text = new StringBuilder();
        text.Append("# SharpProof ").Append(Escape(presentation.Command)).Append("\n\n");
        text.Append("Schema: ").Append(Escape(presentation.Schema)).Append(" v").Append(presentation.SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append("  \n");
        text.Append("Status: ").Append(Escape(presentation.Status)).Append("; exit code: ").Append(presentation.ExitCode.ToString(CultureInfo.InvariantCulture)).Append("\n\n");
        text.Append(Escape(presentation.Scope)).Append('\n');
        if (!presentation.Messages.IsEmpty)
        {
            text.Append("\n## Output\n\n");
            foreach (var message in presentation.Messages)
            {
                text.Append("> ").Append(Escape(message)).Append('\n');
            }
        }
        if (!presentation.Notes.IsEmpty)
        {
            text.Append("\n## Notes\n\n");
            foreach (var note in presentation.Notes)
            {
                text.Append("- ").Append(Escape(note)).Append('\n');
            }
        }
        if (!presentation.Outcomes.IsEmpty)
        {
            text.Append("\n## Outcomes\n\n| Kind | Id | Outcome | Reason | Location | Regression |\n| --- | --- | --- | --- | --- | --- |\n");
            foreach (var outcome in presentation.Outcomes)
            {
                text.Append("| ").Append(Escape(outcome.Kind)).Append(" | ").Append(Escape(outcome.Id)).Append(" | ").Append(Escape(outcome.Outcome))
                    .Append(" | ").Append(Escape(outcome.Reason)).Append(" | ").Append(Escape(outcome.Location?.Display() ?? string.Empty))
                    .Append(" | ").Append(outcome.IsRegression is { } regression ? Boolean(regression) : string.Empty).Append(" |\n");
            }
            var hasDetails = false;
            foreach (var outcome in presentation.Outcomes)
            {
                if (outcome.Details.IsEmpty)
                {
                    continue;
                }
                if (!hasDetails)
                {
                    text.Append("\n## Outcome details\n\n| Kind | Id | Name | Value | Location |\n| --- | --- | --- | --- | --- |\n");
                    hasDetails = true;
                }
                foreach (var detail in outcome.Details)
                {
                    text.Append("| ").Append(Escape(outcome.Kind)).Append(" | ").Append(Escape(outcome.Id))
                        .Append(" | ").Append(Escape(detail.Name)).Append(" | ").Append(Escape(detail.Value))
                        .Append(" | ").Append(Escape(detail.Location?.Display() ?? string.Empty)).Append(" |\n");
                }
            }
        }
        if (!presentation.Issues.IsEmpty)
        {
            text.Append("\n## Issues\n\n| Severity | Code | Message | Location |\n| --- | --- | --- | --- |\n");
            foreach (var issue in presentation.Issues)
            {
                text.Append("| ").Append(Escape(issue.Severity)).Append(" | ").Append(Escape(issue.Code))
                    .Append(" | ").Append(Escape(issue.Message)).Append(" | ").Append(Escape(issue.Location?.Display() ?? string.Empty)).Append(" |\n");
            }
        }
        if (!presentation.GeneratedFiles.IsEmpty)
        {
            text.Append("\n## Generated files\n\n| Path | Changed | Written |\n| --- | --- | --- |\n");
            foreach (var file in presentation.GeneratedFiles)
            {
                text.Append("| ").Append(Escape(file.Path)).Append(" | ").Append(Boolean(file.Changed)).Append(" | ").Append(Boolean(file.Written)).Append(" |\n");
            }
        }
        return text.ToString();
    }

    private static string Boolean(bool value)
    {
        return value ? "true" : "false";
    }

    private static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = new StringBuilder();
        foreach (var character in value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))
        {
            switch (character)
            {
                case '&':
                    text.Append("&amp;");
                    break;
                case '<':
                    text.Append("&lt;");
                    break;
                case '>':
                    text.Append("&gt;");
                    break;
                case '\n':
                    text.Append("<br>");
                    break;
                case '\\':
                case '`':
                case '*':
                case '_':
                case '[':
                case ']':
                case '(':
                case ')':
                case '#':
                case '+':
                case '-':
                case '.':
                case '!':
                case '|':
                case '{':
                case '}':
                    text.Append('\\').Append(character);
                    break;
                default:
                    text.Append(character);
                    break;
            }
        }
        return text.ToString();
    }
}
