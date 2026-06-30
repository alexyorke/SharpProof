using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SearchLib.Smt;

namespace PurelySharp.Analyzer.Engine
{
    internal partial class PurityAnalysisEngine
    {
        private const int MaxMergedStatePathConditions = 32;
        private const int MaxMergeableFactsPerTargetPerState = 4;
        private const int MaxMergedStateFactChoiceCombinationsPerTarget = 64;
        private const int MaxMergedStateGuardFactsPerTargetPerState = 6;

        private static ImmutableDictionary<ISymbol, PotentialTargets> MergeDelegateTargetMapsFromBlockStates(
            IEnumerable<PurityAnalysisState> states)
        {
            var map = ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
            foreach (var state in states)
            {
                foreach (var kvp in state.DelegateTargetMap)
                {
                    map = map.TryGetValue(kvp.Key, out var current)
                        ? map.SetItem(kvp.Key, PotentialTargets.Merge(current, kvp.Value))
                        : map.Add(kvp.Key, kvp.Value);
                }
            }

            return map;
        }

        private static ImmutableHashSet<ISymbol> MergeOwnedLocalArraySymbolsFromBlockStates(
            IEnumerable<PurityAnalysisState> states)
        {
            return UnionSelectedStateItems(
                states,
                static state => state.OwnedLocalArraySymbols,
                SymbolEqualityComparer.Default);
        }

        private static ImmutableHashSet<CaptureId> MergeOwnedArrayFlowCapturesFromBlockStates(
            IEnumerable<PurityAnalysisState> states)
        {
            return UnionSelectedStateItems(
                states,
                static state => state.OwnedArrayFlowCaptures);
        }

        private static ImmutableDictionary<ISymbol, INamedTypeSymbol> MergeLocalConcreteTypesFromBlockStates(
            IEnumerable<PurityAnalysisState> states)
        {
            var builder = ImmutableDictionary.CreateBuilder<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var conflictedSymbols = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var state in states)
            {
                foreach (var kvp in state.LocalConcreteTypes)
                {
                    if (conflictedSymbols.Contains(kvp.Key))
                    {
                        continue;
                    }

                    if (builder.TryGetValue(kvp.Key, out var existingType) &&
                        !SymbolEqualityComparer.Default.Equals(existingType, kvp.Value))
                    {
                        builder.Remove(kvp.Key);
                        conflictedSymbols.Add(kvp.Key);
                        continue;
                    }

                    builder[kvp.Key] = kvp.Value;
                }
            }

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<ISymbol, int> MergeSmtSymbolVersionsAcrossAll(
            IEnumerable<ImmutableDictionary<ISymbol, int>> maps)
        {
            return AggregateAcrossAll(
                maps,
                ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
                IntersectSmtSymbolVersions);
        }

        private static PurityAnalysisState MergeStates(PurityAnalysisState state1, PurityAnalysisState state2)
        {
            LogDebug($"  [Merge] Merging States: S1(Impure={state1.HasPotentialImpurity}, MapCount={state1.DelegateTargetMap.Count}) + S2(Impure={state2.HasPotentialImpurity}, MapCount={state2.DelegateTargetMap.Count})");
            var mergedImpurity = state1.HasPotentialImpurity || state2.HasPotentialImpurity;
            var (firstImpureNode, firstImpurityEvidence) = SelectFirstImpurity(state1, state2);

            var finalMap = IntersectDelegateTargetMaps(state1.DelegateTargetMap, state2.DelegateTargetMap);
            var mergedCaptures = PurityAnalysisState.MergeFlowCaptureMapsForPair(state1.FlowCaptures, state2.FlowCaptures);
            var mergedCaptureTargets = IntersectFlowCaptureTargetMaps(state1.FlowCaptureTargets, state2.FlowCaptureTargets);
            var mergedCaptureConcreteTypes = IntersectFlowCaptureConcreteTypes(state1.FlowCaptureConcreteTypes, state2.FlowCaptureConcreteTypes);
            var mergedCaptureSymbols = IntersectFlowCaptureSymbols(state1.FlowCaptureSymbols, state2.FlowCaptureSymbols);
            var mergedOwnedArrayFlowCaptures = IntersectOwnedArrayFlowCaptures(state1.OwnedArrayFlowCaptures, state2.OwnedArrayFlowCaptures);
            var mergedOwnedLocalArrays = IntersectOwnedLocalArraySymbols(state1.OwnedLocalArraySymbols, state2.OwnedLocalArraySymbols);
            var mergedDefinitelyNullLocals = IntersectOwnedLocalArraySymbols(state1.DefinitelyNullLocalSymbols, state2.DefinitelyNullLocalSymbols);
            var mergedLocalConcreteTypes = IntersectLocalConcreteTypes(state1.LocalConcreteTypes, state2.LocalConcreteTypes);
            var mergedSmtSymbolVersions = IntersectSmtSymbolVersions(state1.SmtSymbolVersions, state2.SmtSymbolVersions);

            return new PurityAnalysisState(
                mergedImpurity,
                firstImpureNode,
                finalMap,
                mergedCaptures,
                mergedCaptureTargets,
                mergedOwnedLocalArrays,
                mergedDefinitelyNullLocals,
                firstImpurityEvidence,
                localConcreteTypes: mergedLocalConcreteTypes,
                smtSymbolVersions: mergedSmtSymbolVersions,
                flowCaptureConcreteTypes: mergedCaptureConcreteTypes,
                pathConditions: MergePathConditionsAcrossAll(new[] { state1, state2 }, mergedSmtSymbolVersions),
                flowCaptureSymbols: mergedCaptureSymbols,
                ownedArrayFlowCaptures: mergedOwnedArrayFlowCaptures);
        }

        private static (SyntaxNode? FirstImpureNode, PurityEvidence FirstImpurityEvidence) SelectFirstImpurity(
            PurityAnalysisState state1,
            PurityAnalysisState state2)
        {
            var firstImpureNode = state1.FirstImpureSyntaxNode;
            var firstImpurityEvidence = state1.FirstImpurityEvidence;
            if (state1.HasPotentialImpurity &&
                state2.HasPotentialImpurity &&
                state1.FirstImpureSyntaxNode != null &&
                state2.FirstImpureSyntaxNode != null)
            {
                if (state2.FirstImpureSyntaxNode.SpanStart < state1.FirstImpureSyntaxNode.SpanStart)
                {
                    firstImpureNode = state2.FirstImpureSyntaxNode;
                    firstImpurityEvidence = state2.FirstImpurityEvidence;
                }
            }
            else if (state2.HasPotentialImpurity)
            {
                firstImpureNode = state2.FirstImpureSyntaxNode;
                firstImpurityEvidence = state2.FirstImpurityEvidence;
            }

            return (firstImpureNode, firstImpurityEvidence);
        }

        private static ImmutableArray<SmtFormula> MergePathConditions(
            ImmutableArray<SmtFormula> first,
            ImmutableArray<SmtFormula> second)
        {
            if (first.Length != second.Length)
            {
                return ImmutableArray<SmtFormula>.Empty;
            }

            for (var i = 0; i < first.Length; i++)
            {
                if (!Equals(first[i], second[i]))
                {
                    return ImmutableArray<SmtFormula>.Empty;
                }
            }

            return first;
        }

        private static ImmutableArray<SmtFormula> MergePathConditionsAcrossAll(
            IReadOnlyList<PurityAnalysisState> states,
            ImmutableDictionary<ISymbol, int> mergedSmtSymbolVersions)
        {
            var sets = states
                .Select(state => NormalizePathConditionsForMergedState(state, mergedSmtSymbolVersions))
                .ToArray();
            if (sets.Length == 0)
            {
                return ImmutableArray<SmtFormula>.Empty;
            }

            var common = GetCommonPathConditions(sets);
            var builder = common.ToBuilder();
            AddConditionalMergedPathConditions(sets, common, builder);
            return builder.ToImmutable();
        }

        private static ImmutableArray<SmtFormula> NormalizePathConditionsForMergedState(
            PurityAnalysisState state,
            ImmutableDictionary<ISymbol, int> mergedSmtSymbolVersions)
        {
            if (state.PathConditions.IsDefaultOrEmpty ||
                state.SmtSymbolVersions.Count == 0 && mergedSmtSymbolVersions.Count == 0)
            {
                return state.PathConditions;
            }

            var rewrites = CreateSmtVersionRewrites(state.SmtSymbolVersions, mergedSmtSymbolVersions);
            if (rewrites.Length == 0)
            {
                return state.PathConditions;
            }

            var builder = ImmutableArray.CreateBuilder<SmtFormula>(state.PathConditions.Length);
            foreach (var condition in state.PathConditions)
            {
                builder.Add(RewriteSmtSymbolVersions(condition, rewrites));
            }

            return builder.ToImmutable();
        }

        private static ImmutableArray<SmtVersionRewrite> CreateSmtVersionRewrites(
            ImmutableDictionary<ISymbol, int> stateSmtSymbolVersions,
            ImmutableDictionary<ISymbol, int> mergedSmtSymbolVersions)
        {
            var symbols = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
            symbols.UnionWith(stateSmtSymbolVersions.Keys);
            symbols.UnionWith(mergedSmtSymbolVersions.Keys);

            var builder = ImmutableArray.CreateBuilder<SmtVersionRewrite>();
            foreach (var symbol in symbols)
            {
                var originalDefinition = symbol.OriginalDefinition;
                var stateVersion = stateSmtSymbolVersions.TryGetValue(originalDefinition, out var currentVersion)
                    ? currentVersion
                    : 0;
                var mergedVersion = mergedSmtSymbolVersions.TryGetValue(originalDefinition, out var targetVersion)
                    ? targetVersion
                    : 0;
                if (stateVersion == mergedVersion)
                {
                    continue;
                }

                builder.Add(new SmtVersionRewrite(
                    GetSmtVariableName(originalDefinition),
                    stateVersion,
                    mergedVersion));
            }

            return builder.ToImmutable();
        }

        private static SmtFormula RewriteSmtSymbolVersions(
            SmtFormula formula,
            ImmutableArray<SmtVersionRewrite> rewrites)
        {
            switch (formula)
            {
                case SmtVariable variable:
                    var rewrittenName = RewriteSmtVariableName(variable.Name, rewrites);
                    return string.Equals(rewrittenName, variable.Name, StringComparison.Ordinal)
                        ? formula
                        : new SmtVariable(rewrittenName, variable.Kind);
                case SmtUnaryFormula unary:
                    return new SmtUnaryFormula(
                        unary.Operator,
                        RewriteSmtSymbolVersions(unary.Operand, rewrites));
                case SmtBinaryFormula binary:
                    return new SmtBinaryFormula(
                        binary.Operator,
                        RewriteSmtSymbolVersions(binary.Left, rewrites),
                        RewriteSmtSymbolVersions(binary.Right, rewrites));
                case SmtIntegerUnaryTerm unaryTerm:
                    return new SmtIntegerUnaryTerm(
                        unaryTerm.Operator,
                        RewriteSmtSymbolVersions(unaryTerm.Operand, rewrites));
                case SmtIntegerBinaryTerm binaryTerm:
                    return new SmtIntegerBinaryTerm(
                        binaryTerm.Operator,
                        RewriteSmtSymbolVersions(binaryTerm.Left, rewrites),
                        RewriteSmtSymbolVersions(binaryTerm.Right, rewrites));
                case SmtStringLengthTerm stringLength:
                    return new SmtStringLengthTerm(
                        RewriteSmtSymbolVersions(stringLength.Value, rewrites));
                case SmtStringConcatTerm stringConcat:
                    return new SmtStringConcatTerm(
                        RewriteSmtSymbolVersions(stringConcat.Left, rewrites),
                        RewriteSmtSymbolVersions(stringConcat.Right, rewrites));
                case SmtStringContainsFormula stringContains:
                    return new SmtStringContainsFormula(
                        RewriteSmtSymbolVersions(stringContains.Value, rewrites),
                        RewriteSmtSymbolVersions(stringContains.Search, rewrites));
                case SmtStringStartsWithFormula stringStartsWith:
                    return new SmtStringStartsWithFormula(
                        RewriteSmtSymbolVersions(stringStartsWith.Value, rewrites),
                        RewriteSmtSymbolVersions(stringStartsWith.Prefix, rewrites));
                case SmtStringEndsWithFormula stringEndsWith:
                    return new SmtStringEndsWithFormula(
                        RewriteSmtSymbolVersions(stringEndsWith.Value, rewrites),
                        RewriteSmtSymbolVersions(stringEndsWith.Suffix, rewrites));
                case SmtRegexMatchFormula regexMatch:
                    return new SmtRegexMatchFormula(
                        RewriteSmtSymbolVersions(regexMatch.Value, rewrites),
                        regexMatch.Pattern,
                        regexMatch.Options);
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    return new SmtRuntimeTypeTestFormula(
                        RewriteSmtSymbolVersions(runtimeTypeTest.Value, rewrites),
                        runtimeTypeTest.TypeKey);
                case SmtConditionalFormula conditional:
                    return new SmtConditionalFormula(
                        RewriteSmtSymbolVersions(conditional.Condition, rewrites),
                        RewriteSmtSymbolVersions(conditional.WhenTrue, rewrites),
                        RewriteSmtSymbolVersions(conditional.WhenFalse, rewrites),
                        conditional.ResultKind);
                default:
                    return formula;
            }
        }

        private static string RewriteSmtVariableName(
            string name,
            ImmutableArray<SmtVersionRewrite> rewrites)
        {
            var rewritten = name;
            foreach (var rewrite in rewrites)
            {
                rewritten = RewriteSmtVariableName(rewritten, rewrite);
            }

            return rewritten;
        }

        private static string RewriteSmtVariableName(string name, SmtVersionRewrite rewrite)
        {
            var fromBase = CreateSmtVersionedBaseName(rewrite.Prefix, rewrite.FromVersion);
            var toBase = CreateSmtVersionedBaseName(rewrite.Prefix, rewrite.ToVersion);
            if (string.Equals(fromBase, toBase, StringComparison.Ordinal))
            {
                return name;
            }

            var searchIndex = 0;
            while (searchIndex < name.Length)
            {
                var matchIndex = name.IndexOf(fromBase, searchIndex, StringComparison.Ordinal);
                if (matchIndex < 0)
                {
                    return name;
                }

                var endIndex = matchIndex + fromBase.Length;
                if (IsSmtVariableNameBoundary(name, endIndex))
                {
                    return name.Substring(0, matchIndex) + toBase + name.Substring(endIndex);
                }

                searchIndex = endIndex;
            }

            return name;
        }

        private static string CreateSmtVersionedBaseName(string prefix, int version)
        {
            return version > 0
                ? prefix + "@v" + version.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : prefix;
        }

        private static bool IsSmtVariableNameBoundary(string name, int index)
        {
            return index >= name.Length ||
                !char.IsDigit(name[index]) && name[index] != '@';
        }

        private readonly struct SmtVersionRewrite
        {
            public SmtVersionRewrite(string prefix, int fromVersion, int toVersion)
            {
                Prefix = prefix;
                FromVersion = fromVersion;
                ToVersion = toVersion;
            }

            public string Prefix { get; }

            public int FromVersion { get; }

            public int ToVersion { get; }
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
            ImmutableArray<SmtFormula>.Builder builder)
        {
            if (pathConditionSets.Count < 2)
            {
                return;
            }

            var existingKeys = new HashSet<string>(builder.Select(GetFormulaKey), StringComparer.Ordinal);
            var commonKeys = new HashSet<string>(commonConditions.Select(GetFormulaKey), StringComparer.Ordinal);
            var stateFacts = pathConditionSets
                .Select(conditions => new StatePathFacts(conditions, commonKeys))
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
                foreach (var factChoices in EnumerateFactChoices(stateFacts, target))
                {
                    combinationCount++;
                    if (combinationCount > MaxMergedStateFactChoiceCombinationsPerTarget)
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
                    if (emittedCount >= MaxMergedStatePathConditions)
                    {
                        return;
                    }
                }
            }
        }

        private static IEnumerable<MergeablePathFact[]> EnumerateFactChoices(
            IReadOnlyList<StatePathFacts> stateFacts,
            string target)
        {
            var selectedFacts = new MergeablePathFact[stateFacts.Count];
            foreach (var choices in EnumerateFactChoices(stateFacts, target, 0, selectedFacts))
            {
                yield return choices;
            }
        }

        private static IEnumerable<MergeablePathFact[]> EnumerateFactChoices(
            IReadOnlyList<StatePathFacts> stateFacts,
            string target,
            int stateIndex,
            MergeablePathFact[] selectedFacts)
        {
            if (stateIndex == stateFacts.Count)
            {
                yield return selectedFacts.ToArray();
                yield break;
            }

            foreach (var fact in stateFacts[stateIndex].FactsByTarget[target].Take(MaxMergeableFactsPerTargetPerState))
            {
                selectedFacts[stateIndex] = fact;
                foreach (var choices in EnumerateFactChoices(stateFacts, target, stateIndex + 1, selectedFacts))
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

            public StatePathFacts(IEnumerable<SmtFormula> pathConditions, ISet<string> commonKeys)
            {
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
                Condition = CreateConjunction(this.branchConditions);
                FactsByTarget = factsByTarget.ToDictionary(
                    static kvp => kvp.Key,
                    static kvp => kvp.Value.ToArray(),
                    StringComparer.Ordinal);
            }

            public SmtFormula Condition { get; }

            public IReadOnlyDictionary<string, MergeablePathFact[]> FactsByTarget { get; }

            public SmtFormula CreateConditionForTarget(string targetKey)
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
                    if (guardFactCount >= MaxMergedStateGuardFactsPerTargetPerState)
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

            public SmtFormula Formula { get; }

            public string FactKey { get; }

            public string TargetKey { get; }

            public static bool TryCreate(SmtFormula formula, out MergeablePathFact fact)
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

        private static ImmutableDictionary<ISymbol, PotentialTargets> MergeDelegateTargetMapsAcrossAll(
            IEnumerable<ImmutableDictionary<ISymbol, PotentialTargets>> maps)
        {
            return AggregateAcrossAll(
                maps,
                ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default),
                IntersectDelegateTargetMaps);
        }

        private static ImmutableHashSet<ISymbol> IntersectOwnedLocalArraySymbols(
            ImmutableHashSet<ISymbol> first,
            ImmutableHashSet<ISymbol> second)
        {
            return ImmutableHashSet.CreateRange(
                SymbolEqualityComparer.Default,
                first.Intersect(second, SymbolEqualityComparer.Default));
        }

        private static ImmutableHashSet<ISymbol> IntersectOwnedLocalArraySymbolsAcrossAll(
            IEnumerable<ImmutableHashSet<ISymbol>> symbolSets)
        {
            return AggregateAcrossAll(
                symbolSets,
                ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default),
                IntersectOwnedLocalArraySymbols);
        }

        private static ImmutableHashSet<CaptureId> IntersectOwnedArrayFlowCaptures(
            ImmutableHashSet<CaptureId> first,
            ImmutableHashSet<CaptureId> second)
        {
            return first.Intersect(second).ToImmutableHashSet();
        }

        private static ImmutableHashSet<CaptureId> IntersectOwnedArrayFlowCapturesAcrossAll(
            IEnumerable<ImmutableHashSet<CaptureId>> captureSets)
        {
            return AggregateAcrossAll(
                captureSets,
                ImmutableHashSet<CaptureId>.Empty,
                IntersectOwnedArrayFlowCaptures);
        }

        private static ImmutableDictionary<CaptureId, ISymbol> IntersectFlowCaptureSymbols(
            ImmutableDictionary<CaptureId, ISymbol> first,
            ImmutableDictionary<CaptureId, ISymbol> second)
        {
            return IntersectFlowCaptureSymbolMapsCore(
                first,
                second);
        }

        private static ImmutableDictionary<CaptureId, ISymbol> IntersectFlowCaptureSymbolMapsCore(
            ImmutableDictionary<CaptureId, ISymbol> first,
            ImmutableDictionary<CaptureId, ISymbol> second)
        {
            return IntersectMatchingMaps(
                first,
                second,
                keyComparer: null,
                static (left, right) => SymbolEqualityComparer.Default.Equals(left, right));
        }

        private static ImmutableDictionary<ISymbol, INamedTypeSymbol> IntersectLocalConcreteTypes(
            ImmutableDictionary<ISymbol, INamedTypeSymbol> first,
            ImmutableDictionary<ISymbol, INamedTypeSymbol> second)
        {
            return IntersectMatchingMaps(
                first,
                second,
                SymbolEqualityComparer.Default,
                static (left, right) => SymbolEqualityComparer.Default.Equals(left, right));
        }

        private static ImmutableDictionary<ISymbol, int> IntersectSmtSymbolVersions(
            ImmutableDictionary<ISymbol, int> first,
            ImmutableDictionary<ISymbol, int> second)
        {
            return IntersectMatchingMaps(
                first,
                second,
                SymbolEqualityComparer.Default,
                static (left, right) => left == right);
        }

        private static ImmutableDictionary<ISymbol, INamedTypeSymbol> IntersectLocalConcreteTypesAcrossAll(
            IEnumerable<ImmutableDictionary<ISymbol, INamedTypeSymbol>> maps)
        {
            return AggregateAcrossAll(
                maps,
                ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default),
                IntersectLocalConcreteTypes);
        }

        private static ImmutableDictionary<CaptureId, INamedTypeSymbol> IntersectFlowCaptureConcreteTypes(
            ImmutableDictionary<CaptureId, INamedTypeSymbol> first,
            ImmutableDictionary<CaptureId, INamedTypeSymbol> second)
        {
            return IntersectMatchingMaps(
                first,
                second,
                keyComparer: null,
                static (left, right) => SymbolEqualityComparer.Default.Equals(left, right));
        }

        private static ImmutableDictionary<CaptureId, INamedTypeSymbol> IntersectFlowCaptureConcreteTypesAcrossAll(
            IEnumerable<ImmutableDictionary<CaptureId, INamedTypeSymbol>> maps)
        {
            return AggregateAcrossAll(
                maps,
                ImmutableDictionary<CaptureId, INamedTypeSymbol>.Empty,
                IntersectFlowCaptureConcreteTypes);
        }

        private static ImmutableDictionary<ISymbol, PotentialTargets> IntersectDelegateTargetMaps(
            ImmutableDictionary<ISymbol, PotentialTargets> first,
            ImmutableDictionary<ISymbol, PotentialTargets> second)
        {
            return IntersectPotentialTargetMaps(first, second, SymbolEqualityComparer.Default);
        }

        private static ImmutableDictionary<CaptureId, PotentialTargets> MergeFlowCaptureTargetMapsAcrossAll(
            IEnumerable<ImmutableDictionary<CaptureId, PotentialTargets>> maps)
        {
            return AggregateAcrossAll(
                maps,
                ImmutableDictionary<CaptureId, PotentialTargets>.Empty,
                IntersectFlowCaptureTargetMaps);
        }

        private static ImmutableDictionary<CaptureId, PotentialTargets> IntersectFlowCaptureTargetMaps(
            ImmutableDictionary<CaptureId, PotentialTargets> first,
            ImmutableDictionary<CaptureId, PotentialTargets> second)
        {
            return IntersectPotentialTargetMaps(first, second, keyComparer: null);
        }

        private static T AggregateAcrossAll<T>(
            IEnumerable<T> values,
            T empty,
            Func<T, T, T> merge,
            Func<T, bool>? stopWhen = null)
        {
            using var enumerator = values.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return empty;
            }

            var merged = enumerator.Current;
            while (enumerator.MoveNext())
            {
                merged = merge(merged, enumerator.Current);
                if (stopWhen != null && stopWhen(merged))
                {
                    return merged;
                }
            }

            return merged;
        }

        private static ImmutableHashSet<T> UnionSelectedStateItems<T>(
            IEnumerable<PurityAnalysisState> states,
            Func<PurityAnalysisState, IEnumerable<T>> selector,
            IEqualityComparer<T>? comparer = null)
        {
            var builder = comparer == null
                ? ImmutableHashSet.CreateBuilder<T>()
                : ImmutableHashSet.CreateBuilder<T>(comparer);
            foreach (var state in states)
            {
                foreach (var item in selector(state))
                {
                    builder.Add(item);
                }
            }

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<TKey, TValue> IntersectMatchingMaps<TKey, TValue>(
            ImmutableDictionary<TKey, TValue> first,
            ImmutableDictionary<TKey, TValue> second,
            IEqualityComparer<TKey>? keyComparer,
            Func<TValue, TValue, bool> valuesEqual)
            where TKey : notnull
        {
            if (first.IsEmpty || second.IsEmpty)
            {
                return keyComparer == null
                    ? ImmutableDictionary<TKey, TValue>.Empty
                    : ImmutableDictionary.Create<TKey, TValue>(keyComparer);
            }

            var builder = keyComparer == null
                ? ImmutableDictionary.CreateBuilder<TKey, TValue>()
                : ImmutableDictionary.CreateBuilder<TKey, TValue>(keyComparer);
            foreach (var kvp in first)
            {
                if (second.TryGetValue(kvp.Key, out var otherValue) &&
                    valuesEqual(kvp.Value, otherValue))
                {
                    builder[kvp.Key] = kvp.Value;
                }
            }

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<TKey, PotentialTargets> IntersectPotentialTargetMaps<TKey>(
            ImmutableDictionary<TKey, PotentialTargets> first,
            ImmutableDictionary<TKey, PotentialTargets> second,
            IEqualityComparer<TKey>? keyComparer)
            where TKey : notnull
        {
            if (first.IsEmpty || second.IsEmpty)
            {
                return keyComparer == null
                    ? ImmutableDictionary<TKey, PotentialTargets>.Empty
                    : ImmutableDictionary.Create<TKey, PotentialTargets>(keyComparer);
            }

            var builder = keyComparer == null
                ? ImmutableDictionary.CreateBuilder<TKey, PotentialTargets>()
                : ImmutableDictionary.CreateBuilder<TKey, PotentialTargets>(keyComparer);
            foreach (var kvp in first)
            {
                if (second.TryGetValue(kvp.Key, out var otherTargets))
                {
                    builder[kvp.Key] = PotentialTargets.Merge(kvp.Value, otherTargets);
                }
            }

            return builder.ToImmutable();
        }
    }
}
