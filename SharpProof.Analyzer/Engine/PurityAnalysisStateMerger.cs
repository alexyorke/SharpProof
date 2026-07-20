using PotentialTargets = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PotentialTargets;
using PurityAnalysisResult = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PurityAnalysisResult;

namespace SharpProof.Analyzer.Engine;

internal static class PurityAnalysisStateMerger {
    internal static PurityAnalysisState MergeStates(
        PurityAnalysisState first, PurityAnalysisState second, int phiScope) =>
        MergeStatesAcrossAll(new[] { first, second }, phiScope);

    internal static PurityAnalysisState MergeStatesAcrossAll(
        IReadOnlyList<PurityAnalysisState> states,
        int phiScope) {
        if (states.Count == 0) return PurityAnalysisState.Pure;

        var hasImpurity = false;
        SyntaxNode? impurityNode = null;
        var impurityEvidence = PurityEvidence.None;
        var captures = ImmutableDictionary<CaptureId, PurityAnalysisResult>.Empty;
        foreach (var state in states) {
            if (state.HasPotentialImpurity &&
                (!hasImpurity || state.FirstImpureSyntaxNode != null &&
                    (impurityNode == null || state.FirstImpureSyntaxNode.SpanStart < impurityNode.SpanStart))) {
                hasImpurity = true;
                impurityNode = state.FirstImpureSyntaxNode;
                impurityEvidence = state.FirstImpurityEvidence;
            }

            foreach (var pair in state.FlowCaptures)
                if (!captures.TryGetValue(pair.Key, out var existing) || existing.IsPure)
                    captures = captures.SetItem(pair.Key, pair.Value);
        }

        return new PurityAnalysisState(
            hasImpurity,
            impurityNode,
            IntersectCommon(
                states,
                static state => state.DelegateTargetMap,
                SymbolEq.Default,
                static (left, right) => (true, PotentialTargets.Merge(left, right))),
            captures,
            IntersectCommon(
                states,
                static state => state.FlowCaptureTargets,
                null,
                static (left, right) => (true, PotentialTargets.Merge(left, right))),
            impurityEvidence,
            SymbolicStateMerger.MergePathStatesAcrossAll(
                states.Select(static state => state.PathState).ToArray(),
                SymbolicStateMerger.AreEvidenceEquivalentFacts,
                phiScope),
            IntersectCommon(
                states,
                static state => state.FlowCaptureSymbols,
                null,
                static (left, right) => SymbolEq.AreEqual(left, right)
                    ? (true, left)
                    : (false, left)));
    }

    private static ImmutableDictionary<TKey, TValue> IntersectCommon<TKey, TValue>(
        IReadOnlyList<PurityAnalysisState> states,
        Func<PurityAnalysisState, ImmutableDictionary<TKey, TValue>> select,
        IEqualityComparer<TKey>? comparer,
        Func<TValue, TValue, (bool Keep, TValue Value)> merge)
        where TKey : notnull {
        var result = comparer == null
            ? ImmutableDictionary.CreateBuilder<TKey, TValue>()
            : ImmutableDictionary.CreateBuilder<TKey, TValue>(comparer);
        foreach (var pair in select(states[0])) result[pair.Key] = pair.Value;
        for (var index = 1; index < states.Count; index++) {
            var current = select(states[index]);
            foreach (var key in result.Keys.ToArray()) {
                if (!current.TryGetValue(key, out var other)) {
                    result.Remove(key);
                    continue;
                }

                var (keep, value) = merge(result[key], other);
                if (keep) result[key] = value;
                else result.Remove(key);
            }
        }

        return result.ToImmutable();
    }
}
