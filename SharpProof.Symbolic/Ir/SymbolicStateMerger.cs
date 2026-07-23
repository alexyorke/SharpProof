namespace SharpProof.Symbolic.Ir;
internal static class SymbolicStateMerger {
    internal static ImmutableArray<SymbolicCondition> MergePathConditionsAcrossAll(IReadOnlyList<SymbolicState> states)
        => MergePathConditionsAcrossAll(
            states.Select(static state => (IReadOnlyList<SymbolicCondition>)state.PathConditions).ToArray());
    internal static ImmutableArray<SymbolicCondition> MergePathConditionsAcrossAll(
        IReadOnlyList<IReadOnlyList<SymbolicCondition>> conditionSets) {
        if (conditionSets.Count == 0) return [];
        var commonKeys = new HashSet<string>(
            conditionSets[0].Select(SymbolicState.CreateProofConditionKey),
            StringComparer.Ordinal);
        for (var index = 1; index < conditionSets.Count; index++)
            commonKeys.IntersectWith(conditionSets[index].Select(SymbolicState.CreateProofConditionKey));
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        return [.. conditionSets[0].Where(condition => {
            var key = SymbolicState.CreateProofConditionKey(condition);
            return commonKeys.Contains(key) && emitted.Add(key);
        })];
    }
    internal static ImmutableArray<SymbolicFact> IntersectFactsAcrossAll(
        IReadOnlyList<SymbolicState> states,
        Func<SymbolicFact, SymbolicFact, bool>? equivalent = null) {
        if (states.Count == 0) return [];
        var common = states[0].Facts;
        for (var index = 1; index < states.Count && !common.IsEmpty; index++) {
            var candidateFacts = states[index].Facts;
            common = [.. common.Where(fact => candidateFacts.Any(candidate => equivalent?.Invoke(fact, candidate) ??
                SymbolicState.CreateProofFactKey(fact) == SymbolicState.CreateProofFactKey(candidate)))];
        }
        return common;
    }
    internal static SymbolicState MergePathStatesAcrossAll(
        IReadOnlyList<SymbolicState> states,
        Func<SymbolicFact, SymbolicFact, bool> equivalent,
        int phiScope) {
        if (states.Count == 0) return new SymbolicState();
        var versions = MergePhiVersions(states, phiScope);
        var normalized = states.Select(state => RewriteToVersions(state, versions)).ToArray();
        var facts = IntersectFactsAcrossAll(normalized, equivalent);
        return new SymbolicState(facts, MergePathConditionsAcrossAll(normalized), versions);
    }
    private static ImmutableDictionary<string, int> MergePhiVersions(IReadOnlyList<SymbolicState> states, int phiScope) {
        var keys = states.SelectMany(static state => state.SymbolVersions.Keys)
            .Distinct(StringComparer.Ordinal);
        var builder = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var key in keys) {
            var versions = states.Select(state => state.SymbolVersions.TryGetValue(key, out var version) ? version : 0)
                .Distinct()
                .Take(2)
                .ToArray();
            builder[key] = versions.Length == 1 ? versions[0] : checked(phiScope * 2 + 1);
        }
        return builder.ToImmutable();
    }
    private static SymbolicState RewriteToVersions(SymbolicState state, ImmutableDictionary<string, int> versions) =>
        new(
            state.Facts.Select(fact => SymbolicIrVersionRewriter.RewriteToCurrentVersions(fact, versions)),
            state.PathConditions.Select(condition => SymbolicIrVersionRewriter.RewriteToCurrentVersions(condition, versions)),
            versions,
            state.IsContradictory);
    internal static SymbolicState MergeCompletionStates(
        IReadOnlyList<SymbolicState> states,
        SymbolicState entryState,
        Microsoft.CodeAnalysis.SyntaxNode source) {
        if (states.Count == 1) return states[0];
        var retainedFacts = entryState.Facts.ToList();
        var retainedConditions = entryState.PathConditions.ToList();
        var addedCount = 0;
        AddLimitedCommonItems(
            IntersectFactsAcrossAll(states),
            retainedFacts,
            entryState.Facts.Select(SymbolicState.CreateProofFactKey),
            SymbolicState.CreateProofFactKey, source, ref addedCount);
        AddLimitedCommonItems(
            MergePathConditionsAcrossAll(states),
            retainedConditions,
            entryState.PathConditions.Select(SymbolicState.CreateProofConditionKey),
            SymbolicState.CreateProofConditionKey, source, ref addedCount);
        var commonVersions = states[0].SymbolVersions.Where(pair => states.Skip(1).All(state =>
            state.SymbolVersions.TryGetValue(pair.Key, out var version) && version == pair.Value));
        return new SymbolicState(retainedFacts, retainedConditions, commonVersions, states.All(static state
            => state.IsContradictory)).Normalize();
    }
    private static void AddLimitedCommonItems<T>(
        IEnumerable<T> candidates,
        ICollection<T> retained,
        IEnumerable<string> retainedKeys,
        Func<T, string> getKey,
        Microsoft.CodeAnalysis.SyntaxNode source,
        ref int addedCount) {
        var limit = SymbolicAnalysisLimitContext.Limits.MaxMergedTryFacts;
        var keys = new HashSet<string>(retainedKeys, StringComparer.Ordinal);
        foreach (var candidate in candidates.Where(candidate => keys.Add(getKey(candidate)))) {
            if (addedCount >= limit) {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.TryFactMerge, limit, addedCount + 1, source,
                    "program_point.try_fact_merge");
                return;
            }
            retained.Add(candidate);
            addedCount++;
        }
    }
}
