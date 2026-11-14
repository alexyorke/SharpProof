using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace SharpProof.Gates.Corpus;

// Renders Corpus/verdicts-by-source.md: for each pinned source, how many
// selected methods end in each verdict and, for Unknown, which diagnostic
// explains it. The table is a function of the manifest and the snapshot, so
// a test can reject a stale copy.
internal static class OpenSourceReasonTable
{
    internal const string FileName = "verdicts-by-source.md";
    private const string Silent = "Unknown without a diagnostic";
    private const string MessageReasonSeparator = "': ";

    internal static string Render(
        OpenSourceCorpusDocument document,
        ImmutableArray<CorpusObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(document);
        RepositoryParameterGuard.RequireInitialized(observations.IsDefault, nameof(observations), "Array must be initialized.");
        var observed = observations.ToImmutableDictionary(
            static observation => observation.CaseId,
            StringComparer.Ordinal);
        var sources = document.Sources.Select(static source => source.Id).ToImmutableArray();
        var counts = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var method in document.Methods)
        {
            var row = observed.TryGetValue($"{method.Id}.baseline", out var observation)
                ? Describe(observation)
                : Silent;
            if (!counts.TryGetValue(row, out var bySource))
            {
                bySource = new int[sources.Length];
                counts.Add(row, bySource);
            }
            bySource[sources.IndexOf(method.SourceId, StringComparer.Ordinal)]++;
        }

        var text = new StringBuilder();
        text.Append("# Verdicts and Unknown reasons by source\n\n");
        text.Append("`corpus-update` writes this file from [oss-methods.json](oss-methods.json) and ");
        text.Append("[expected.canonical.snapshot](expected.canonical.snapshot); a gate test rejects a stale copy. ");
        text.Append("Do not edit it by hand. Each selected method is counted once. An Unknown is listed under its ");
        text.Append("diagnostics; for SP0047 the row also names the reason the analysis stopped. ");
        text.Append("[Unknown reasons](../../../docs/unknown-reasons.md) explains them.\n\n");
        text.Append("| Verdict or diagnostic | Total | ").AppendJoin(" | ", sources.Select(static id => $"`{id}`")).Append(" |\n");
        text.Append("| --- | ---: |").Insert(text.Length, " ---: |", sources.Length).Append('\n');
        var ordered = counts
            .OrderBy(static row => Rank(row.Key))
            .ThenByDescending(static row => row.Value.Sum())
            .ThenBy(static row => row.Key, StringComparer.Ordinal);
        foreach (var row in ordered)
        {
            AppendRow(text, row.Key, row.Value);
        }
        var totals = new int[sources.Length];
        foreach (var bySource in counts.Values)
        {
            for (var index = 0; index < totals.Length; index++)
            {
                totals[index] += bySource[index];
            }
        }
        AppendRow(text, "**Total**", totals);
        return text.ToString();
    }

    // Verdict rows first, then Unknown reasons.
    private static int Rank(string row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row switch
        {
            nameof(CorpusVerdict.Proven) => 0,
            nameof(CorpusVerdict.Refuted) => 1,
            _ => 2
        };
    }

    private static void AppendRow(StringBuilder text, string label, int[] bySource)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(bySource);
        text.Append("| ").Append(label).Append(" | ")
            .Append(bySource.Sum().ToString(CultureInfo.InvariantCulture));
        foreach (var count in bySource)
        {
            text.Append(" | ").Append(count.ToString(CultureInfo.InvariantCulture));
        }
        text.Append(" |\n");
    }

    private static string Describe(CorpusObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Verdict is CorpusVerdict.Proven or CorpusVerdict.Refuted)
        {
            return observation.Verdict.ToString();
        }
        var reasons = observation.Diagnostics
            .Select(DescribeDiagnostic)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        return reasons.IsEmpty ? Silent : string.Join(" + ", reasons);
    }

    // A snapshot diagnostic is id@severity@location@base64-message. SP0047
    // messages end in "': <reason>".
    private static string DescribeDiagnostic(string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var fields = diagnostic.Split('@', 4);
        if (fields.Length != 4)
        {
            throw new InvalidDataException("Malformed corpus snapshot diagnostic.");
        }
        if (!string.Equals(fields[0], "SP0047", StringComparison.Ordinal))
        {
            return fields[0];
        }
        var message = Encoding.UTF8.GetString(Convert.FromBase64String(fields[3]));
        var separator = message.LastIndexOf(MessageReasonSeparator, StringComparison.Ordinal);
        return separator < 0
            ? fields[0]
            : $"{fields[0]} `{message[(separator + MessageReasonSeparator.Length)..]}`";
    }
}
