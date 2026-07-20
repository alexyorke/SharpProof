namespace SharpProof.Symbolic;

internal static class PathConditionMergeEngine {
    internal static ImmutableArray<SymbolicCondition> MergeAcrossAll(
        IReadOnlyList<IReadOnlyList<SymbolicCondition>> conditionSets,
        SharpProofAnalysisBudget limits) {
        if (conditionSets.Count == 0) return ImmutableArray<SymbolicCondition>.Empty;

        var common = GetCommonConditions(conditionSets);
        if (conditionSets.Count < 2) return common;

        var commonKeys = new HashSet<string>(
            common.Select(SymbolicState.CreateProofConditionKey),
            StringComparer.Ordinal);
        var states = conditionSets
            .Select(conditions => new StatePathFacts(conditions, commonKeys, limits))
            .ToArray();
        if (states.Any(static state => state.FactsByTarget.Count == 0)) return common;

        var targets = new HashSet<string>(states[0].FactsByTarget.Keys, StringComparer.Ordinal);
        for (var index = 1; index < states.Length; index++)
            targets.IntersectWith(states[index].FactsByTarget.Keys);

        var builder = common.ToBuilder();
        var emittedCount = 0;
        foreach (var target in targets.OrderBy(static key => key, StringComparer.Ordinal)) {
            var combinationCount = 0;
            foreach (var choices in EnumerateChoices(states, target, limits)) {
                combinationCount++;
                if (combinationCount > limits.MaxFactChoiceCombinationsPerTarget) {
                    RecordLimit(
                        SymbolicAnalysisLimitKind.FactChoiceCombinationsPerTarget,
                        limits.MaxFactChoiceCombinationsPerTarget,
                        combinationCount,
                        "state_merge.fact_choice_combinations");
                    break;
                }

                if (choices.Select(static choice => SymbolicState.CreateProofConditionKey(choice.Condition))
                    .Distinct(StringComparer.Ordinal)
                    .Count() == 1)
                    continue;

                var branches = new SymbolicCondition[states.Length];
                for (var index = 0; index < states.Length; index++) {
                    var guard = states[index].CreateGuard(target);
                    branches[index] = guard == null
                        ? choices[index].Condition
                        : SymbolicStateMerger.Combine(
                            SymbolicConditionOperator.And,
                            new[] { guard, choices[index].Condition });
                }

                var merged = SymbolicStateMerger.Combine(SymbolicConditionOperator.Or, branches);
                if (!commonKeys.Add(SymbolicState.CreateProofConditionKey(merged))) continue;
                if (emittedCount >= limits.MaxMergedPathConditions) {
                    RecordLimit(
                        SymbolicAnalysisLimitKind.MergedPathConditions,
                        limits.MaxMergedPathConditions,
                        emittedCount + 1,
                        "state_merge.merged_path_conditions");
                    return builder.ToImmutable();
                }

                builder.Add(merged);
                emittedCount++;
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<SymbolicCondition> GetCommonConditions(
        IReadOnlyList<IReadOnlyList<SymbolicCondition>> conditionSets) {
        var commonKeys = new HashSet<string>(
            conditionSets[0].Select(SymbolicState.CreateProofConditionKey),
            StringComparer.Ordinal);
        for (var index = 1; index < conditionSets.Count; index++)
            commonKeys.IntersectWith(conditionSets[index].Select(SymbolicState.CreateProofConditionKey));

        var builder = ImmutableArray.CreateBuilder<SymbolicCondition>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var condition in conditionSets[0]) {
            var key = SymbolicState.CreateProofConditionKey(condition);
            if (commonKeys.Contains(key) && emitted.Add(key)) builder.Add(condition);
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<PathFact[]> EnumerateChoices(
        IReadOnlyList<StatePathFacts> states,
        string target,
        SharpProofAnalysisBudget limits) =>
        EnumerateChoices(states, target, 0, new PathFact[states.Count], limits);

    private static IEnumerable<PathFact[]> EnumerateChoices(
        IReadOnlyList<StatePathFacts> states,
        string target,
        int stateIndex,
        PathFact[] selected,
        SharpProofAnalysisBudget limits) {
        if (stateIndex == states.Count) {
            yield return selected.ToArray();
            yield break;
        }

        foreach (var fact in states[stateIndex].FactsByTarget[target]
                     .Take(limits.MaxMergeableFactsPerTargetPerState)) {
            selected[stateIndex] = fact;
            foreach (var choices in EnumerateChoices(states, target, stateIndex + 1, selected, limits))
                yield return choices;
        }
    }

    private static void RecordLimit(
        SymbolicAnalysisLimitKind kind,
        int limit,
        int observed,
        string provenance) =>
        SymbolicAnalysisLimitContext.Record(kind, limit, observed, null, provenance);

    private sealed class StatePathFacts {
        private readonly ImmutableArray<SymbolicCondition> branches;
        private readonly ImmutableArray<PathFact> facts;
        private readonly SharpProofAnalysisBudget limits;

        internal StatePathFacts(
            IEnumerable<SymbolicCondition> conditions,
            ISet<string> commonKeys,
            SharpProofAnalysisBudget limits) {
            this.limits = limits;
            var factsByTarget = new Dictionary<string, List<PathFact>>(StringComparer.Ordinal);
            var localBranches = ImmutableArray.CreateBuilder<SymbolicCondition>();
            var localFacts = ImmutableArray.CreateBuilder<PathFact>();
            foreach (var condition in conditions) {
                var conditionKey = SymbolicState.CreateProofConditionKey(condition);
                if (commonKeys.Contains(conditionKey)) continue;
                if (!SymbolicStateMerger.TryGetMergeTargetKey(condition, out var targetKey)) {
                    localBranches.Add(condition);
                    continue;
                }

                var fact = new PathFact(condition, targetKey);
                if (!factsByTarget.TryGetValue(targetKey, out var targetFacts)) {
                    targetFacts = new List<PathFact>();
                    factsByTarget.Add(targetKey, targetFacts);
                }

                targetFacts.Add(fact);
                localFacts.Add(fact);
            }

            branches = localBranches.ToImmutable();
            facts = localFacts.ToImmutable();
            FactsByTarget = factsByTarget.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToArray(),
                StringComparer.Ordinal);
            foreach (var pair in FactsByTarget)
                if (pair.Value.Length > limits.MaxMergeableFactsPerTargetPerState)
                    RecordLimit(
                        SymbolicAnalysisLimitKind.MergeableFactsPerTargetPerState,
                        limits.MaxMergeableFactsPerTargetPerState,
                        pair.Value.Length,
                        "state_merge.facts_per_target_per_state");
        }

        internal IReadOnlyDictionary<string, PathFact[]> FactsByTarget { get; }

        internal SymbolicCondition? CreateGuard(string targetKey) {
            var conditions = new List<SymbolicCondition>(branches);
            var guardFactCount = 0;
            foreach (var fact in facts) {
                if (string.Equals(fact.TargetKey, targetKey, StringComparison.Ordinal)) continue;
                if (guardFactCount >= limits.MaxGuardFactsPerTargetPerState) {
                    RecordLimit(
                        SymbolicAnalysisLimitKind.GuardFactsPerTargetPerState,
                        limits.MaxGuardFactsPerTargetPerState,
                        guardFactCount + 1,
                        "state_merge.guard_facts_per_target_per_state");
                    break;
                }

                conditions.Add(fact.Condition);
                guardFactCount++;
            }

            return conditions.Count == 0
                ? null
                : SymbolicStateMerger.Combine(SymbolicConditionOperator.And, conditions);
        }
    }

    private sealed record PathFact(
        SymbolicCondition Condition,
        string TargetKey);
}
