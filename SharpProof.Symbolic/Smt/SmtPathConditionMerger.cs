using System.Collections.Generic;
using System.Collections.Immutable;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Smt
{
    internal static class SmtPathConditionMerger
    {
        internal static ImmutableArray<SmtFormula> MergeAcrossAll(
            IReadOnlyList<ImmutableArray<SmtFormula>> pathConditionSets,
            SmtPathConditionMergeOptions options)
        {
            if (pathConditionSets.Count == 0)
            {
                return ImmutableArray<SmtFormula>.Empty;
            }

            var common = GetCommonPathConditions(pathConditionSets);
            var builder = common.ToBuilder();
            AddConditionalMergedPathConditions(pathConditionSets, common, builder, options);
            return builder.ToImmutable();
        }

        private static ImmutableArray<SmtFormula> GetCommonPathConditions(
            IReadOnlyList<ImmutableArray<SmtFormula>> pathConditionSets)
        {
            if (pathConditionSets.Count == 0)
            {
                return ImmutableArray<SmtFormula>.Empty;
            }

            var commonKeys = new HashSet<string>(
                pathConditionSets[0].Select(GetFormulaKey),
                StringComparer.Ordinal);
            for (var index = 1; index < pathConditionSets.Count; index++)
            {
                commonKeys.IntersectWith(pathConditionSets[index].Select(GetFormulaKey));
            }

            var builder = ImmutableArray.CreateBuilder<SmtFormula>();
            var emittedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var condition in pathConditionSets[0])
            {
                var key = GetFormulaKey(condition);
                if (commonKeys.Contains(key) && emittedKeys.Add(key))
                {
                    builder.Add(condition);
                }
            }

            return builder.ToImmutable();
        }

        private static void AddConditionalMergedPathConditions(
            IReadOnlyList<ImmutableArray<SmtFormula>> pathConditionSets,
            ImmutableArray<SmtFormula> commonConditions,
            ImmutableArray<SmtFormula>.Builder builder,
            SmtPathConditionMergeOptions options)
        {
            if (pathConditionSets.Count < 2)
            {
                return;
            }

            var existingKeys = new HashSet<string>(builder.Select(GetFormulaKey), StringComparer.Ordinal);
            var commonKeys = new HashSet<string>(commonConditions.Select(GetFormulaKey), StringComparer.Ordinal);
            var stateFacts = pathConditionSets
                .Select(conditions => new StatePathFacts(conditions, commonKeys, options))
                .ToArray();
            if (stateFacts.Any(static state => state.FactsByTarget.Count == 0))
            {
                return;
            }

            var candidateTargets = new HashSet<string>(stateFacts[0].FactsByTarget.Keys, StringComparer.Ordinal);
            for (var index = 1; index < stateFacts.Length; index++)
            {
                candidateTargets.IntersectWith(stateFacts[index].FactsByTarget.Keys);
            }

            var emittedCount = 0;
            foreach (var target in candidateTargets)
            {
                var combinationCount = 0;
                foreach (var factChoices in EnumerateFactChoices(stateFacts, target, options))
                {
                    combinationCount++;
                    if (combinationCount > options.MaxFactChoiceCombinationsPerTarget)
                    {
                        break;
                    }

                    if (factChoices.Select(static fact => fact.FactKey).Distinct(StringComparer.Ordinal).Count() == 1)
                    {
                        continue;
                    }

                    var mergedFact = CreateConditionalMergedPathCondition(stateFacts, factChoices);
                    var mergedKey = GetFormulaKey(mergedFact);
                    if (!existingKeys.Add(mergedKey))
                    {
                        continue;
                    }

                    builder.Add(mergedFact);
                    emittedCount++;
                    if (emittedCount >= options.MaxMergedPathConditions)
                    {
                        return;
                    }
                }
            }
        }

        private static IEnumerable<MergeablePathFact[]> EnumerateFactChoices(
            IReadOnlyList<StatePathFacts> stateFacts,
            string target,
            SmtPathConditionMergeOptions options)
        {
            var selectedFacts = new MergeablePathFact[stateFacts.Count];
            foreach (var choices in EnumerateFactChoices(stateFacts, target, 0, selectedFacts, options))
            {
                yield return choices;
            }
        }

        private static IEnumerable<MergeablePathFact[]> EnumerateFactChoices(
            IReadOnlyList<StatePathFacts> stateFacts,
            string target,
            int stateIndex,
            MergeablePathFact[] selectedFacts,
            SmtPathConditionMergeOptions options)
        {
            if (stateIndex == stateFacts.Count)
            {
                yield return selectedFacts.ToArray();
                yield break;
            }

            foreach (var fact in stateFacts[stateIndex].FactsByTarget[target].Take(options.MaxFactsPerTargetPerState))
            {
                selectedFacts[stateIndex] = fact;
                foreach (var choices in EnumerateFactChoices(stateFacts, target, stateIndex + 1, selectedFacts, options))
                {
                    yield return choices;
                }
            }
        }

        private static SmtFormula CreateConditionalMergedPathCondition(
            IReadOnlyList<StatePathFacts> stateFacts,
            IReadOnlyList<MergeablePathFact> factChoices)
        {
            var branchTerms = new SmtFormula[stateFacts.Count];
            for (var index = 0; index < stateFacts.Count; index++)
            {
                var branchCondition = stateFacts[index].CreateConditionForTarget(factChoices[index].TargetKey);
                branchTerms[index] = branchCondition is SmtBooleanConstant { Value: true }
                    ? factChoices[index].Formula
                    : new SmtBinaryFormula(
                        SmtBinaryOperator.And,
                        branchCondition,
                        factChoices[index].Formula);
            }

            return CreateDisjunction(branchTerms);
        }

        private static SmtFormula CreateConjunction(IReadOnlyList<SmtFormula> formulas)
        {
            if (formulas.Count == 0)
            {
                return new SmtBooleanConstant(true);
            }

            var formula = formulas[0];
            for (var index = 1; index < formulas.Count; index++)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.And, formula, formulas[index]);
            }

            return formula;
        }

        private static SmtFormula CreateDisjunction(IReadOnlyList<SmtFormula> formulas)
        {
            var formula = formulas[0];
            for (var index = 1; index < formulas.Count; index++)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.Or, formula, formulas[index]);
            }

            return formula;
        }

        private static string GetFormulaKey(SmtFormula formula)
        {
            return formula.ToString() ?? string.Empty;
        }

        private sealed class StatePathFacts
        {
            private readonly ImmutableArray<SmtFormula> branchConditions;
            private readonly ImmutableArray<MergeablePathFact> facts;
            private readonly SmtPathConditionMergeOptions options;

            public StatePathFacts(
                IEnumerable<SmtFormula> pathConditions,
                ISet<string> commonKeys,
                SmtPathConditionMergeOptions options)
            {
                this.options = options;

                var factsByTarget = new Dictionary<string, List<MergeablePathFact>>(StringComparer.Ordinal);
                var localBranchConditions = new List<SmtFormula>();
                var localFacts = ImmutableArray.CreateBuilder<MergeablePathFact>();
                foreach (var condition in pathConditions)
                {
                    var key = GetFormulaKey(condition);
                    if (commonKeys.Contains(key))
                    {
                        continue;
                    }

                    if (MergeablePathFact.TryCreate(condition, out var mergeableFact))
                    {
                        if (!factsByTarget.TryGetValue(mergeableFact.TargetKey, out var facts))
                        {
                            facts = new List<MergeablePathFact>();
                            factsByTarget.Add(mergeableFact.TargetKey, facts);
                        }

                        facts.Add(mergeableFact);
                        localFacts.Add(mergeableFact);
                        continue;
                    }

                    localBranchConditions.Add(condition);
                }

                this.branchConditions = localBranchConditions.ToImmutableArray();
                this.facts = localFacts.ToImmutable();
                FactsByTarget = factsByTarget.ToDictionary(
                    static kvp => kvp.Key,
                    static kvp => kvp.Value.ToArray(),
                    StringComparer.Ordinal);
            }

            internal IReadOnlyDictionary<string, MergeablePathFact[]> FactsByTarget { get; }

            internal SmtFormula CreateConditionForTarget(string targetKey)
            {
                var conditions = ImmutableArray.CreateBuilder<SmtFormula>();
                conditions.AddRange(branchConditions);

                var guardFactCount = 0;
                foreach (var fact in facts)
                {
                    if (string.Equals(fact.TargetKey, targetKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    conditions.Add(fact.Formula);
                    guardFactCount++;
                    if (guardFactCount >= options.MaxGuardFactsPerTargetPerState)
                    {
                        break;
                    }
                }

                return CreateConjunction(conditions);
            }
        }

        private sealed class MergeablePathFact
        {
            private MergeablePathFact(SmtFormula formula, string targetKey)
            {
                Formula = formula;
                FactKey = GetFormulaKey(formula);
                TargetKey = targetKey;
            }

            internal SmtFormula Formula { get; }

            internal string FactKey { get; }

            internal string TargetKey { get; }

            internal static bool TryCreate(SmtFormula formula, out MergeablePathFact fact)
            {
                if (TryGetMergeTargetKey(formula, out var targetKey))
                {
                    fact = new MergeablePathFact(formula, targetKey);
                    return true;
                }

                fact = null!;
                return false;
            }

            private static bool TryGetMergeTargetKey(SmtFormula formula, out string targetKey)
            {
                switch (formula)
                {
                    case SmtBinaryFormula
                    {
                        Operator: SmtBinaryOperator.Equal,
                        Left: SmtVariable target,
                        Right: { } right
                    } when target.Kind == right.Kind:
                        targetKey = GetFormulaKey(target);
                        return true;
                    case SmtBinaryFormula
                    {
                        Operator: SmtBinaryOperator.NotEqual,
                        Left: SmtVariable target,
                        Right: SmtNullConstant
                    }:
                        targetKey = GetFormulaKey(target);
                        return true;
                    case SmtBinaryFormula
                    {
                        Operator: SmtBinaryOperator.Equal,
                        Left: SmtVariable target,
                        Right: SmtNullConstant
                    }:
                        targetKey = GetFormulaKey(target);
                        return true;
                    case SmtBinaryFormula
                    {
                        Operator: SmtBinaryOperator.Equal or
                            SmtBinaryOperator.NotEqual or
                            SmtBinaryOperator.GreaterThan or
                            SmtBinaryOperator.GreaterThanOrEqual or
                            SmtBinaryOperator.LessThan or
                            SmtBinaryOperator.LessThanOrEqual,
                        Left: { } left,
                        Right: { } right
                    } when TryGetMergeTargetTermKey(left, out targetKey) ||
                           TryGetMergeTargetTermKey(right, out targetKey):
                        return true;
                    case SmtVariable { Kind: SmtValueKind.Bool } target:
                        targetKey = GetFormulaKey(target);
                        return true;
                    case SmtUnaryFormula
                    {
                        Operator: SmtUnaryOperator.Not,
                        Operand: SmtVariable { Kind: SmtValueKind.Bool } target
                    }:
                        targetKey = GetFormulaKey(target);
                        return true;
                    case SmtUnaryFormula
                    {
                        Operator: SmtUnaryOperator.Not,
                        Operand: SmtBinaryFormula
                        {
                            Operator: SmtBinaryOperator.Equal or
                                SmtBinaryOperator.NotEqual or
                                SmtBinaryOperator.GreaterThan or
                                SmtBinaryOperator.GreaterThanOrEqual or
                                SmtBinaryOperator.LessThan or
                                SmtBinaryOperator.LessThanOrEqual,
                            Left: { } left,
                            Right: { } right
                        }
                    } when TryGetMergeTargetTermKey(left, out targetKey) ||
                           TryGetMergeTargetTermKey(right, out targetKey):
                        return true;
                    default:
                        targetKey = string.Empty;
                        return false;
                }
            }

            private static bool TryGetMergeTargetTermKey(SmtFormula formula, out string targetKey)
            {
                switch (formula)
                {
                    case SmtVariable variable:
                        targetKey = GetFormulaKey(variable);
                        return true;
                    case SmtStringLengthTerm stringLength:
                        targetKey = GetFormulaKey(stringLength);
                        return true;
                    default:
                        targetKey = string.Empty;
                        return false;
                }
            }
        }
    }

    internal readonly struct SmtPathConditionMergeOptions
    {
        public SmtPathConditionMergeOptions(
            int maxMergedPathConditions,
            int maxFactsPerTargetPerState,
            int maxFactChoiceCombinationsPerTarget,
            int maxGuardFactsPerTargetPerState)
        {
            MaxMergedPathConditions = maxMergedPathConditions;
            MaxFactsPerTargetPerState = maxFactsPerTargetPerState;
            MaxFactChoiceCombinationsPerTarget = maxFactChoiceCombinationsPerTarget;
            MaxGuardFactsPerTargetPerState = maxGuardFactsPerTargetPerState;
        }

        public int MaxMergedPathConditions { get; }

        public int MaxFactsPerTargetPerState { get; }

        public int MaxFactChoiceCombinationsPerTarget { get; }

        public int MaxGuardFactsPerTargetPerState { get; }
    }
}
