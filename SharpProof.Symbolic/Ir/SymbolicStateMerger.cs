using System.Collections.Immutable;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicStateMerger
{
    internal static ImmutableArray<SymbolicCondition> MergePathConditionsAcrossAll(
        IReadOnlyList<SymbolicState> states)
    {
        if (states.Count == 0) return ImmutableArray<SymbolicCondition>.Empty;

        var common = GetCommonConditions(states);
        if (states.Count < 2) return common;

        var limits = SymbolicAnalysisLimitContext.Limits;
        var commonKeys = new HashSet<string>(
            common.Select(SymbolicStructuralKey.ForCondition),
            StringComparer.Ordinal);
        var stateFacts = states
            .Select(state => new StatePathFacts(state.PathConditions, commonKeys, limits))
            .ToArray();
        if (stateFacts.Any(static state => state.FactsByTarget.Count == 0)) return common;

        var candidateTargets = new HashSet<string>(
            stateFacts[0].FactsByTarget.Keys,
            StringComparer.Ordinal);
        for (var index = 1; index < stateFacts.Length; index++)
            candidateTargets.IntersectWith(stateFacts[index].FactsByTarget.Keys);

        var builder = common.ToBuilder();
        var existingKeys = commonKeys;
        var emittedCount = 0;
        foreach (var target in candidateTargets.OrderBy(static key => key, StringComparer.Ordinal))
        {
            var combinationCount = 0;
            foreach (var choices in EnumerateFactChoices(stateFacts, target, limits))
            {
                combinationCount++;
                if (combinationCount > limits.MaxFactChoiceCombinationsPerTarget)
                {
                    SymbolicAnalysisLimitContext.Record(
                        SymbolicAnalysisLimitKind.FactChoiceCombinationsPerTarget,
                        limits.MaxFactChoiceCombinationsPerTarget,
                        combinationCount,
                        null,
                        "state_merge.fact_choice_combinations");
                    break;
                }

                if (choices.Select(static choice => choice.ConditionKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == 1)
                    continue;

                var merged = CreateConditionalMergedCondition(stateFacts, choices);
                var mergedKey = SymbolicStructuralKey.ForCondition(merged);
                if (!existingKeys.Add(mergedKey)) continue;

                if (emittedCount >= limits.MaxMergedPathConditions)
                {
                    SymbolicAnalysisLimitContext.Record(
                        SymbolicAnalysisLimitKind.MergedPathConditions,
                        limits.MaxMergedPathConditions,
                        emittedCount + 1,
                        null,
                        "state_merge.merged_path_conditions");
                    return builder.ToImmutable();
                }

                builder.Add(merged);
                emittedCount++;
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<SymbolicCondition> GetCommonConditions(IReadOnlyList<SymbolicState> states)
    {
        var commonKeys = new HashSet<string>(
            states[0].PathConditions.Select(SymbolicStructuralKey.ForCondition),
            StringComparer.Ordinal);
        for (var index = 1; index < states.Count; index++)
            commonKeys.IntersectWith(states[index].PathConditions.Select(SymbolicStructuralKey.ForCondition));

        var builder = ImmutableArray.CreateBuilder<SymbolicCondition>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var condition in states[0].PathConditions)
        {
            var key = SymbolicStructuralKey.ForCondition(condition);
            if (commonKeys.Contains(key) && emitted.Add(key)) builder.Add(condition);
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<MergeablePathFact[]> EnumerateFactChoices(
        IReadOnlyList<StatePathFacts> states,
        string target,
        SymbolicAnalysisLimits limits)
    {
        var selected = new MergeablePathFact[states.Count];
        return EnumerateFactChoices(states, target, 0, selected, limits);
    }

    private static IEnumerable<MergeablePathFact[]> EnumerateFactChoices(
        IReadOnlyList<StatePathFacts> states,
        string target,
        int stateIndex,
        MergeablePathFact[] selected,
        SymbolicAnalysisLimits limits)
    {
        if (stateIndex == states.Count)
        {
            yield return selected.ToArray();
            yield break;
        }

        foreach (var fact in states[stateIndex].FactsByTarget[target]
                     .Take(limits.MaxMergeableFactsPerTargetPerState))
        {
            selected[stateIndex] = fact;
            foreach (var choices in EnumerateFactChoices(states, target, stateIndex + 1, selected, limits))
                yield return choices;
        }
    }

    private static SymbolicCondition CreateConditionalMergedCondition(
        IReadOnlyList<StatePathFacts> states,
        IReadOnlyList<MergeablePathFact> choices)
    {
        var branches = new SymbolicCondition[states.Count];
        for (var index = 0; index < states.Count; index++)
        {
            var guard = states[index].CreateGuardForTarget(choices[index].TargetKey);
            branches[index] = guard == null
                ? choices[index].Condition
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    guard,
                    choices[index].Condition);
        }

        return Combine(SymbolicConditionOperator.Or, branches);
    }

    private static SymbolicCondition Combine(
        SymbolicConditionOperator op,
        IReadOnlyList<SymbolicCondition> conditions)
    {
        var result = conditions[0];
        for (var index = 1; index < conditions.Count; index++)
            result = new SymbolicBinaryCondition(op, result, conditions[index]);

        return result;
    }

    private sealed class StatePathFacts
    {
        private readonly ImmutableArray<SymbolicCondition> branchConditions;
        private readonly ImmutableArray<MergeablePathFact> facts;
        private readonly SymbolicAnalysisLimits limits;

        internal StatePathFacts(
            IEnumerable<SymbolicCondition> pathConditions,
            ISet<string> commonKeys,
            SymbolicAnalysisLimits limits)
        {
            this.limits = limits;
            var factsByTarget = new Dictionary<string, List<MergeablePathFact>>(StringComparer.Ordinal);
            var localBranches = ImmutableArray.CreateBuilder<SymbolicCondition>();
            var localFacts = ImmutableArray.CreateBuilder<MergeablePathFact>();
            foreach (var condition in pathConditions)
            {
                if (commonKeys.Contains(SymbolicStructuralKey.ForCondition(condition))) continue;

                if (!MergeablePathFact.TryCreate(condition, out var fact))
                {
                    localBranches.Add(condition);
                    continue;
                }

                if (!factsByTarget.TryGetValue(fact.TargetKey, out var targetFacts))
                {
                    targetFacts = new List<MergeablePathFact>();
                    factsByTarget.Add(fact.TargetKey, targetFacts);
                }

                targetFacts.Add(fact);
                localFacts.Add(fact);
            }

            branchConditions = localBranches.ToImmutable();
            facts = localFacts.ToImmutable();
            FactsByTarget = factsByTarget.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToArray(),
                StringComparer.Ordinal);
            foreach (var pair in FactsByTarget)
                if (pair.Value.Length > limits.MaxMergeableFactsPerTargetPerState)
                    SymbolicAnalysisLimitContext.Record(
                        SymbolicAnalysisLimitKind.MergeableFactsPerTargetPerState,
                        limits.MaxMergeableFactsPerTargetPerState,
                        pair.Value.Length,
                        null,
                        "state_merge.facts_per_target_per_state");
        }

        internal IReadOnlyDictionary<string, MergeablePathFact[]> FactsByTarget { get; }

        internal SymbolicCondition? CreateGuardForTarget(string targetKey)
        {
            var conditions = ImmutableArray.CreateBuilder<SymbolicCondition>();
            conditions.AddRange(branchConditions);
            var guardFactCount = 0;
            foreach (var fact in facts)
            {
                if (string.Equals(fact.TargetKey, targetKey, StringComparison.Ordinal)) continue;

                if (guardFactCount >= limits.MaxGuardFactsPerTargetPerState)
                {
                    SymbolicAnalysisLimitContext.Record(
                        SymbolicAnalysisLimitKind.GuardFactsPerTargetPerState,
                        limits.MaxGuardFactsPerTargetPerState,
                        guardFactCount + 1,
                        null,
                        "state_merge.guard_facts_per_target_per_state");
                    break;
                }

                conditions.Add(fact.Condition);
                guardFactCount++;
            }

            return conditions.Count == 0
                ? null
                : Combine(SymbolicConditionOperator.And, conditions);
        }
    }

    private sealed class MergeablePathFact
    {
        private MergeablePathFact(SymbolicCondition condition, string targetKey)
        {
            Condition = condition;
            ConditionKey = SymbolicStructuralKey.ForCondition(condition);
            TargetKey = targetKey;
        }

        internal SymbolicCondition Condition { get; }
        internal string ConditionKey { get; }
        internal string TargetKey { get; }

        internal static bool TryCreate(SymbolicCondition condition, out MergeablePathFact fact)
        {
            if (TryGetMergeTarget(condition, out var target))
            {
                fact = new MergeablePathFact(condition, SymbolicStructuralKey.ForTerm(target));
                return true;
            }

            fact = null!;
            return false;
        }

        private static bool TryGetMergeTarget(SymbolicCondition condition, out SymbolicTerm target)
        {
            if (condition is SymbolicFactCondition { Fact.Atom: SymbolicRelationAtom relation })
            {
                if (TryGetTargetTerm(relation.Left, out target) || TryGetTargetTerm(relation.Right, out target))
                    return true;
            }

            if (condition is SymbolicFactCondition
                {
                    Fact.Atom: SymbolicTruthAtom { Condition: SymbolicVariableTerm variable }
                })
            {
                target = variable;
                return true;
            }

            if (condition is SymbolicNotCondition
                {
                    Operand: SymbolicFactCondition
                    {
                        Fact.Atom: SymbolicTruthAtom { Condition: SymbolicVariableTerm negatedVariable }
                    }
                })
            {
                target = negatedVariable;
                return true;
            }

            target = null!;
            return false;
        }

        private static bool TryGetTargetTerm(SymbolicTerm term, out SymbolicTerm target)
        {
            switch (term)
            {
                case SymbolicVariableTerm:
                case SymbolicMemberTerm:
                case SymbolicElementTerm:
                case SymbolicMultiElementTerm:
                case SymbolicNullableHasValueTerm:
                case SymbolicNullableValueTerm:
                case SymbolicLengthTerm:
                case SymbolicArrayDimensionLengthTerm:
                case SymbolicCountTerm:
                case SymbolicStringContentTerm:
                    target = term;
                    return true;
                default:
                    target = null!;
                    return false;
            }
        }
    }
}
