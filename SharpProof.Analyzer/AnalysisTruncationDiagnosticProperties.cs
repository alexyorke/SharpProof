using System.Globalization;

namespace SharpProof.Analyzer;

internal static class AnalysisTruncationDiagnosticProperties
{
    internal static ImmutableDictionary<string, string?> Add(
        ImmutableDictionary<string, string?> properties,
        SymbolicAnalysisTruncationInfo truncation)
    {
        if (properties == null) throw new ArgumentNullException(nameof(properties));

        if (truncation == null) throw new ArgumentNullException(nameof(truncation));

        if (!truncation.IsTruncated) return properties;

        var orderedEvents = truncation.Events
            .OrderBy(static item => item, SymbolicAnalysisTruncationEventOrdering.Canonical)
            .ToArray();
        return properties
            .SetItem(SharpProofDiagnostics.AnalysisTruncatedProperty, bool.TrueString)
            .SetItem(
                SharpProofDiagnostics.AnalysisLimitCodesProperty,
                string.Join(",", orderedEvents.Select(static item => item.Code).Distinct(StringComparer.Ordinal)))
            .SetItem(
                SharpProofDiagnostics.AnalysisLimitEventsProperty,
                string.Join(";", orderedEvents.Select(FormatEvent)));
    }

    private static string FormatEvent(SymbolicAnalysisTruncationEvent item)
    {
        return string.Join(
            "|",
            item.Code,
            item.Limit.ToString(CultureInfo.InvariantCulture),
            item.Observed.ToString(CultureInfo.InvariantCulture),
            item.SourceSpanStart?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            item.Provenance);
    }
}
