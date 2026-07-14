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
