namespace SharpProof.Symbolic;

internal delegate bool TryGetPathConditionTarget<in TCondition>(
    TCondition condition,
    out string targetKey);

internal sealed record PathConditionMergeStrategy<TCondition>(
    Func<TCondition, string> GetKey,
    TryGetPathConditionTarget<TCondition> TryGetTarget,
    Func<IReadOnlyList<TCondition>, TCondition> Conjoin,
    Func<IReadOnlyList<TCondition>, TCondition> Disjoin)
    where TCondition : class;

internal readonly record struct PathConditionMergeLimits(
    int MaxMergedConditions,
    int MaxFactsPerTargetPerState,
    int MaxFactChoiceCombinationsPerTarget,
    int MaxGuardFactsPerTargetPerState);

internal static class PathConditionMergeEngine {
    internal static ImmutableArray<TCondition> MergeAcrossAll<TCondition>(
        IReadOnlyList<IReadOnlyList<TCondition>> conditionSets,
        PathConditionMergeStrategy<TCondition> strategy,
        PathConditionMergeLimits limits)
        where TCondition : class {
        if (conditionSets.Count == 0) return ImmutableArray<TCondition>.Empty;

        var common = GetCommonConditions(conditionSets, strategy.GetKey);
        if (conditionSets.Count < 2) return common;

        var commonKeys = new HashSet<string>(common.Select(strategy.GetKey), StringComparer.Ordinal);
        var states = conditionSets
            .Select(conditions => new StatePathFacts<TCondition>(conditions, commonKeys, strategy, limits))
            .ToArray();
        if (states.Any(static state => state.FactsByTarget.Count == 0)) return common;

        var targets = new HashSet<string>(states[0].FactsByTarget.Keys, StringComparer.Ordinal);
        for (var index = 1; index < states.Length; index++)
            targets.IntersectWith(states[index].FactsByTarget.Keys);

        var builder = common.ToBuilder();
        var emittedKeys = commonKeys;
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

                if (choices.Select(static choice => choice.ConditionKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == 1)
                    continue;

                var branches = new TCondition[states.Length];
                for (var index = 0; index < states.Length; index++) {
                    var guard = states[index].CreateGuard(target);
                    branches[index] = guard == null
                        ? choices[index].Condition
                        : strategy.Conjoin(new[] { guard, choices[index].Condition });
                }

                var merged = strategy.Disjoin(branches);
                if (!emittedKeys.Add(strategy.GetKey(merged))) continue;
                if (emittedCount >= limits.MaxMergedConditions) {
                    RecordLimit(
                        SymbolicAnalysisLimitKind.MergedPathConditions,
                        limits.MaxMergedConditions,
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

    private static ImmutableArray<TCondition> GetCommonConditions<TCondition>(
        IReadOnlyList<IReadOnlyList<TCondition>> conditionSets,
        Func<TCondition, string> getKey) {
        var commonKeys = new HashSet<string>(conditionSets[0].Select(getKey), StringComparer.Ordinal);
        for (var index = 1; index < conditionSets.Count; index++)
            commonKeys.IntersectWith(conditionSets[index].Select(getKey));

        var builder = ImmutableArray.CreateBuilder<TCondition>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var condition in conditionSets[0]) {
            var key = getKey(condition);
            if (commonKeys.Contains(key) && emitted.Add(key)) builder.Add(condition);
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<PathFact<TCondition>[]> EnumerateChoices<TCondition>(
        IReadOnlyList<StatePathFacts<TCondition>> states,
        string target,
        PathConditionMergeLimits limits)
        where TCondition : class {
        var selected = new PathFact<TCondition>[states.Count];
        return EnumerateChoices(states, target, 0, selected, limits);
    }

    private static IEnumerable<PathFact<TCondition>[]> EnumerateChoices<TCondition>(
        IReadOnlyList<StatePathFacts<TCondition>> states,
        string target,
        int stateIndex,
        PathFact<TCondition>[] selected,
        PathConditionMergeLimits limits)
        where TCondition : class {
        if (stateIndex == states.Count) {
            yield return selected.ToArray();
            yield break;
        }

        foreach (var fact in states[stateIndex].FactsByTarget[target]
                     .Take(limits.MaxFactsPerTargetPerState)) {
            selected[stateIndex] = fact;
            foreach (var choices in EnumerateChoices(states, target, stateIndex + 1, selected, limits))
                yield return choices;
        }
    }

    private static void RecordLimit(
        SymbolicAnalysisLimitKind kind,
        int limit,
        int observed,
        string provenance) {
        SymbolicAnalysisLimitContext.Record(kind, limit, observed, null, provenance);
    }

    private sealed class StatePathFacts<TCondition>
        where TCondition : class {
        private readonly ImmutableArray<TCondition> branches;
        private readonly ImmutableArray<PathFact<TCondition>> facts;
        private readonly PathConditionMergeStrategy<TCondition> strategy;
        private readonly PathConditionMergeLimits limits;

        internal StatePathFacts(
            IEnumerable<TCondition> conditions,
            ISet<string> commonKeys,
            PathConditionMergeStrategy<TCondition> strategy,
            PathConditionMergeLimits limits) {
            this.strategy = strategy;
            this.limits = limits;
            var factsByTarget = new Dictionary<string, List<PathFact<TCondition>>>(StringComparer.Ordinal);
            var localBranches = ImmutableArray.CreateBuilder<TCondition>();
            var localFacts = ImmutableArray.CreateBuilder<PathFact<TCondition>>();
            foreach (var condition in conditions) {
                var conditionKey = strategy.GetKey(condition);
                if (commonKeys.Contains(conditionKey)) continue;
                if (!strategy.TryGetTarget(condition, out var targetKey)) {
                    localBranches.Add(condition);
                    continue;
                }

                var fact = new PathFact<TCondition>(condition, conditionKey, targetKey);
                if (!factsByTarget.TryGetValue(targetKey, out var targetFacts)) {
                    targetFacts = new List<PathFact<TCondition>>();
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
                if (pair.Value.Length > limits.MaxFactsPerTargetPerState)
                    RecordLimit(
                        SymbolicAnalysisLimitKind.MergeableFactsPerTargetPerState,
                        limits.MaxFactsPerTargetPerState,
                        pair.Value.Length,
                        "state_merge.facts_per_target_per_state");
        }

        internal IReadOnlyDictionary<string, PathFact<TCondition>[]> FactsByTarget { get; }

        internal TCondition? CreateGuard(string targetKey) {
            var conditions = new List<TCondition>(branches);
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

            return conditions.Count == 0 ? null : strategy.Conjoin(conditions);
        }
    }

    private sealed record PathFact<TCondition>(
        TCondition Condition,
        string ConditionKey,
        string TargetKey)
        where TCondition : class;
}
