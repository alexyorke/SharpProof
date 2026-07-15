using System.Collections.Immutable;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicStateMerger
{
    private static readonly PathConditionMergeStrategy<SymbolicCondition> Strategy = new(
        SymbolicState.CreateProofConditionKey,
        TryGetMergeTargetKey,
        static conditions => Combine(SymbolicConditionOperator.And, conditions),
        static conditions => Combine(SymbolicConditionOperator.Or, conditions));

    internal static ImmutableArray<SymbolicCondition> MergePathConditionsAcrossAll(
        IReadOnlyList<SymbolicState> states)
    {
        var limits = SymbolicAnalysisLimitContext.Limits;
        return PathConditionMergeEngine.MergeAcrossAll(
            states.Select(static state => (IReadOnlyList<SymbolicCondition>)state.PathConditions).ToArray(),
            Strategy,
            new PathConditionMergeLimits(
                limits.MaxMergedPathConditions,
                limits.MaxMergeableFactsPerTargetPerState,
                limits.MaxFactChoiceCombinationsPerTarget,
                limits.MaxGuardFactsPerTargetPerState));
    }

    internal static SymbolicCondition CreateGuardedChoice(
        SymbolicCondition guard,
        SymbolicCondition value) =>
        value is SymbolicConstantCondition { Value: false }
            ? new SymbolicNotCondition(guard)
            : new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                new SymbolicNotCondition(guard),
                value);

    internal static SymbolicState MergeCompletionStates(
        IReadOnlyList<SymbolicState> states,
        SymbolicState entryState,
        Microsoft.CodeAnalysis.SyntaxNode source)
    {
        if (states.Count == 1) return states[0];

        var commonFactKeys = new HashSet<string>(
            states[0].Facts.Select(SymbolicState.CreateProofFactKey),
            StringComparer.Ordinal);
        foreach (var branch in states.Skip(1))
            commonFactKeys.IntersectWith(branch.Facts.Select(SymbolicState.CreateProofFactKey));

        var retainedFacts = entryState.Facts.ToList();
        var retainedConditions = entryState.PathConditions.ToList();
        var addedCount = 0;
        AddLimitedCommonItems(
            states[0].Facts.Where(fact => commonFactKeys.Contains(SymbolicState.CreateProofFactKey(fact))),
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
        return new SymbolicState(
            retainedFacts,
            retainedConditions,
            commonVersions,
            states.All(static state => state.IsContradictory)).Normalize();
    }

    private static void AddLimitedCommonItems<T>(
        IEnumerable<T> candidates,
        ICollection<T> retained,
        IEnumerable<string> retainedKeys,
        Func<T, string> getKey,
        Microsoft.CodeAnalysis.SyntaxNode source,
        ref int addedCount)
    {
        var limit = SymbolicAnalysisLimitContext.Limits.MaxMergedTryFacts;
        var keys = new HashSet<string>(retainedKeys, StringComparer.Ordinal);
        foreach (var candidate in candidates.Where(candidate => keys.Add(getKey(candidate))))
        {
            if (addedCount >= limit)
            {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.TryFactMerge, limit, addedCount + 1, source,
                    "program_point.try_fact_merge");
                return;
            }

            retained.Add(candidate);
            addedCount++;
        }
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

    private static bool TryGetMergeTargetKey(SymbolicCondition condition, out string targetKey)
    {
        if (TryGetMergeTarget(condition, out var target))
        {
            targetKey = SymbolicState.CreateProofTermKey(target);
            return true;
        }

        targetKey = string.Empty;
        return false;
    }

    private static bool TryGetMergeTarget(SymbolicCondition condition, out SymbolicTerm target)
    {
        if (condition is SymbolicFactCondition { Fact.Atom: SymbolicRelationAtom relation } &&
            (TryGetTargetTerm(relation.Left, out target) || TryGetTargetTerm(relation.Right, out target)))
            return true;

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
