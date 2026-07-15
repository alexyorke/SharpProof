using System.Globalization;
using NUnit.Framework;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

internal sealed record SymbolicStateDifferentialSnapshot(
    string? NormalizedStateKey,
    SymbolicLoweringSupport Support,
    SymbolicUnknownReason UnknownReason,
    string ProvenanceKey,
    string TruncationKey);

internal static class SymbolicStateDifferentialHarness
{
    internal static SymbolicStateDifferentialSnapshot Capture(
        SymbolicLoweringResult<SymbolicState> result,
        SymbolicAnalysisTruncationInfo? truncation = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        return Capture(
            result.Value,
            result.Support,
            result.UnknownReason,
            result.Provenance,
            truncation);
    }

    internal static SymbolicStateDifferentialSnapshot Capture(
        SymbolicState? state,
        SymbolicLoweringSupport support,
        SymbolicUnknownReason unknownReason,
        IEnumerable<SymbolicLoweringProvenance>? provenance = null,
        SymbolicAnalysisTruncationInfo? truncation = null)
    {
        return new SymbolicStateDifferentialSnapshot(
            state?.Normalize().NormalizedProofKey,
            support,
            unknownReason,
            CreateProvenanceKey(provenance),
            CreateTruncationKey(truncation));
    }

    internal static void AssertEquivalent(
        SymbolicStateDifferentialSnapshot expected,
        SymbolicStateDifferentialSnapshot actual,
        string context)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.NormalizedStateKey, Is.EqualTo(expected.NormalizedStateKey), context + ": state");
            Assert.That(actual.Support, Is.EqualTo(expected.Support), context + ": support");
            Assert.That(actual.UnknownReason, Is.EqualTo(expected.UnknownReason), context + ": unknown reason");
            Assert.That(actual.ProvenanceKey, Is.EqualTo(expected.ProvenanceKey), context + ": provenance");
            Assert.That(actual.TruncationKey, Is.EqualTo(expected.TruncationKey), context + ": truncation");
        });
    }

    private static string CreateProvenanceKey(IEnumerable<SymbolicLoweringProvenance>? provenance)
    {
        return string.Join(
            "\n",
            provenance?.Select(static item => string.Join(
                "|",
                Encode(item.Stage),
                item.SourceSpan.Start.ToString(CultureInfo.InvariantCulture),
                item.SourceSpan.Length.ToString(CultureInfo.InvariantCulture),
                Encode(item.Detail))) ?? Enumerable.Empty<string>());
    }

    private static string CreateTruncationKey(SymbolicAnalysisTruncationInfo? truncation)
    {
        if (truncation == null || !truncation.IsTruncated) return string.Empty;

        var normalized = SymbolicAnalysisTruncationInfo.Combine(new[] { truncation });
        return string.Join(
            "\n",
            normalized.Events.Select(static item => string.Join(
                "|",
                item.Kind.ToString(),
                item.Limit.ToString(CultureInfo.InvariantCulture),
                item.Observed.ToString(CultureInfo.InvariantCulture),
                Encode(item.Provenance),
                item.SourceSpanStart?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)));
    }

    private static string Encode(string value)
    {
        value ??= string.Empty;
        return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
    }
}
