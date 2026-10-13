using System.Collections.Immutable;

namespace SharpProof.Gates.Corpus;

internal static class CorpusObservationComparison
{
    internal static bool Matches(CorpusObservation expected, CorpusObservation actual)
    {
        return expected.Verdict == actual.Verdict &&
            expected.SemanticOutcome == actual.SemanticOutcome &&
            expected.Diagnostics.SequenceEqual(actual.Diagnostics, StringComparer.Ordinal);
    }

    internal static ImmutableArray<string> CompareCached(
        CorpusObservation expected, CorpusObservation first, CorpusObservation second)
    {
        if (!Matches(expected, first) || !Matches(expected, second))
        {
            return [$"Cache replay changed {expected.CaseId}."];
        }
        return [];
    }

    internal static ImmutableArray<string> CompareConcurrent(
        IReadOnlyDictionary<string, CorpusObservation> expected,
        IEnumerable<CorpusObservation> observations)
    {
        return [.. observations
            .Where(observation => !Matches(expected[observation.CaseId], observation))
            .OrderBy(static observation => observation.CaseId, StringComparer.Ordinal)
            .Select(static observation =>
                $"Concurrent replay changed {observation.CaseId}.")];
    }
}
