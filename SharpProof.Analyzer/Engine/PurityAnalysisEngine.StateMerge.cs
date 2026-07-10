using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static ImmutableDictionary<ISymbol, PotentialTargets> MergeDelegateTargetMapsFromBlockStates(
        IEnumerable<PurityAnalysisState> states)
    {
        var map = ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
        foreach (var state in states)
            foreach (var kvp in state.DelegateTargetMap)
                map = map.TryGetValue(kvp.Key, out var current)
                    ? map.SetItem(kvp.Key, PotentialTargets.Merge(current, kvp.Value))
                    : map.Add(kvp.Key, kvp.Value);

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
            foreach (var kvp in state.LocalConcreteTypes)
            {
                if (conflictedSymbols.Contains(kvp.Key)) continue;

                if (builder.TryGetValue(kvp.Key, out var existingType) &&
                    !SymbolEqualityComparer.Default.Equals(existingType, kvp.Value))
                {
                    builder.Remove(kvp.Key);
                    conflictedSymbols.Add(kvp.Key);
                    continue;
                }

                builder[kvp.Key] = kvp.Value;
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
        var mergedImpurity = state1.HasPotentialImpurity || state2.HasPotentialImpurity;
        var (firstImpureNode, firstImpurityEvidence) = SelectFirstImpurity(state1, state2);

        var finalMap = IntersectDelegateTargetMaps(state1.DelegateTargetMap, state2.DelegateTargetMap);
        var mergedCaptures = PurityAnalysisState.MergeFlowCaptureMapsForPair(state1.FlowCaptures, state2.FlowCaptures);
        var mergedCaptureTargets = IntersectFlowCaptureTargetMaps(state1.FlowCaptureTargets, state2.FlowCaptureTargets);
        var mergedCaptureConcreteTypes =
            IntersectFlowCaptureConcreteTypes(state1.FlowCaptureConcreteTypes, state2.FlowCaptureConcreteTypes);
        var mergedCaptureSymbols = IntersectFlowCaptureSymbols(state1.FlowCaptureSymbols, state2.FlowCaptureSymbols);
        var mergedOwnedArrayFlowCaptures =
            IntersectOwnedArrayFlowCaptures(state1.OwnedArrayFlowCaptures, state2.OwnedArrayFlowCaptures);
        var mergedOwnedLocalArrays =
            IntersectOwnedLocalArraySymbols(state1.OwnedLocalArraySymbols, state2.OwnedLocalArraySymbols);
        var mergedDefinitelyNullLocals =
            IntersectOwnedLocalArraySymbols(state1.DefinitelyNullLocalSymbols, state2.DefinitelyNullLocalSymbols);
        var mergedLocalConcreteTypes =
            IntersectLocalConcreteTypes(state1.LocalConcreteTypes, state2.LocalConcreteTypes);
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
            mergedLocalConcreteTypes,
            mergedSmtSymbolVersions,
            mergedCaptureConcreteTypes,
            MergePathConditionsAcrossAll(new[] { state1, state2 }, mergedSmtSymbolVersions),
            MergePathStatesAcrossAll(new[] { state1, state2 }),
            mergedCaptureSymbols,
            mergedOwnedArrayFlowCaptures);
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

    private static ImmutableArray<SmtFormula> MergePathConditionsAcrossAll(
        IReadOnlyList<PurityAnalysisState> states,
        ImmutableDictionary<ISymbol, int> mergedSmtSymbolVersions)
    {
        var sets = states
            .Select(state => NormalizePathConditionsForMergedState(state, mergedSmtSymbolVersions))
            .ToArray();
        if (sets.Length == 0) return ImmutableArray<SmtFormula>.Empty;

        var limits = SymbolicAnalysisLimitContext.Limits;
        return SmtPathConditionMerger.MergeAcrossAll(
            sets,
            new SmtPathConditionMergeOptions(
                limits.MaxMergedPathConditions,
                limits.MaxMergeableFactsPerTargetPerState,
                limits.MaxFactChoiceCombinationsPerTarget,
                limits.MaxGuardFactsPerTargetPerState));
    }

    private static SymbolicState MergePathStatesAcrossAll(IReadOnlyList<PurityAnalysisState> states)
    {
        if (states.Count == 0) return new SymbolicState();

        var commonFacts = states[0].PathState.Facts;
        var commonConditions = states[0].PathState.PathConditions;
        for (var index = 1; index < states.Count; index++)
        {
            commonFacts = IntersectSymbolicFacts(commonFacts, states[index].PathState.Facts);
            commonConditions = IntersectSymbolicConditions(commonConditions, states[index].PathState.PathConditions);
            if (commonFacts.IsEmpty && commonConditions.IsEmpty) break;
        }

        commonFacts = AddAllPathResourceReleaseFacts(commonFacts, states);
        return new SymbolicState(commonFacts, commonConditions);
    }

    private static ImmutableArray<SymbolicFact> IntersectSymbolicFacts(
        ImmutableArray<SymbolicFact> first,
        ImmutableArray<SymbolicFact> second)
    {
        if (first.IsDefaultOrEmpty || second.IsDefaultOrEmpty) return ImmutableArray<SymbolicFact>.Empty;

        var builder = ImmutableArray.CreateBuilder<SymbolicFact>();
        foreach (var fact in first)
            if (second.Any(secondFact => AreMergeEquivalentSymbolicFacts(fact, secondFact)))
                builder.Add(fact);

        return builder.ToImmutable();
    }

    private static bool AreMergeEquivalentSymbolicFacts(SymbolicFact first, SymbolicFact second)
    {
        return first.Polarity == second.Polarity &&
               first.Confidence == second.Confidence &&
               Equals(first.Atom, second.Atom) &&
               SymbolEqualityComparer.Default.Equals(first.Symbol, second.Symbol) &&
               string.Equals(first.EvidenceKey, second.EvidenceKey, StringComparison.Ordinal);
    }

    private static ImmutableArray<SymbolicFact> AddAllPathResourceReleaseFacts(
        ImmutableArray<SymbolicFact> commonFacts,
        IReadOnlyList<PurityAnalysisState> states)
    {
        if (states.Count == 0) return commonFacts;

        var builder = commonFacts.ToBuilder();
        foreach (var representative in states[0].PathState.Facts)
        {
            if (!TryGetExactResourceRelease(representative, out var resource, out var symbol)) continue;

            if (states.Skip(1).All(state => HasExactResourceRelease(state, resource, symbol)))
            {
                var mergedFact = representative with
                {
                    Atom = new SymbolicResourceLifetimeAtom(resource, SymbolicResourceLifetimeState.Released),
                    Provenance = "analyzer.resource.merge.all-path-release",
                    EvidenceKey = representative.EvidenceKey ?? "evidence.resource.released"
                };

                if (!builder.Any(fact => AreMergeEquivalentSymbolicFacts(fact, mergedFact))) builder.Add(mergedFact);
            }
        }

        return builder.ToImmutable();
    }

    private static bool HasExactResourceRelease(
        PurityAnalysisState state,
        SymbolicTerm resource,
        ISymbol? symbol)
    {
        return state.PathState.Facts.Any(fact =>
            TryGetExactResourceRelease(fact, out var releasedResource, out var releasedSymbol) &&
            (symbol != null
                ? SymbolEqualityComparer.Default.Equals(symbol, releasedSymbol)
                : Equals(resource, releasedResource)));
    }

    private static bool TryGetExactResourceRelease(
        SymbolicFact fact,
        out SymbolicTerm resource,
        out ISymbol? symbol)
    {
        resource = null!;
        symbol = null;
        if (!fact.Polarity ||
            fact.Confidence != SymbolicFactConfidence.Exact)
            return false;

        switch (fact.Atom)
        {
            case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Released } lifetime:
                resource = lifetime.Resource;
                symbol = fact.Symbol;
                return true;
            case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Returned } lifetime:
                resource = lifetime.Resource;
                symbol = fact.Symbol;
                return true;
            case SymbolicDisposalAtom { State: SymbolicDisposalState.Disposed } disposal:
                resource = disposal.Resource;
                symbol = fact.Symbol;
                return true;
            default:
                return false;
        }
    }

    private static ImmutableArray<SymbolicCondition> IntersectSymbolicConditions(
        ImmutableArray<SymbolicCondition> first,
        ImmutableArray<SymbolicCondition> second)
    {
        if (first.IsDefaultOrEmpty || second.IsDefaultOrEmpty) return ImmutableArray<SymbolicCondition>.Empty;

        var builder = ImmutableArray.CreateBuilder<SymbolicCondition>();
        foreach (var condition in first)
            if (second.Contains(condition))
                builder.Add(condition);

        return builder.ToImmutable();
    }

    private static ImmutableArray<SmtFormula> NormalizePathConditionsForMergedState(
        PurityAnalysisState state,
        ImmutableDictionary<ISymbol, int> mergedSmtSymbolVersions)
    {
        if (state.PathConditions.IsDefaultOrEmpty ||
            (state.SmtSymbolVersions.Count == 0 && mergedSmtSymbolVersions.Count == 0))
            return state.PathConditions;

        return SmtFormulaVersionRewriter.RewriteSymbolVersions(
            state.PathConditions,
            state.SmtSymbolVersions,
            mergedSmtSymbolVersions);
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
            null,
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
            null,
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
        return IntersectPotentialTargetMaps(first, second, null);
    }

    private static T AggregateAcrossAll<T>(
        IEnumerable<T> values,
        T empty,
        Func<T, T, T> merge,
        Func<T, bool>? stopWhen = null)
    {
        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext()) return empty;

        var merged = enumerator.Current;
        while (enumerator.MoveNext())
        {
            merged = merge(merged, enumerator.Current);
            if (stopWhen != null && stopWhen(merged)) return merged;
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
            foreach (var item in selector(state))
                builder.Add(item);

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
            return keyComparer == null
                ? ImmutableDictionary<TKey, TValue>.Empty
                : ImmutableDictionary.Create<TKey, TValue>(keyComparer);

        var builder = keyComparer == null
            ? ImmutableDictionary.CreateBuilder<TKey, TValue>()
            : ImmutableDictionary.CreateBuilder<TKey, TValue>(keyComparer);
        foreach (var kvp in first)
            if (second.TryGetValue(kvp.Key, out var otherValue) &&
                valuesEqual(kvp.Value, otherValue))
                builder[kvp.Key] = kvp.Value;

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<TKey, PotentialTargets> IntersectPotentialTargetMaps<TKey>(
        ImmutableDictionary<TKey, PotentialTargets> first,
        ImmutableDictionary<TKey, PotentialTargets> second,
        IEqualityComparer<TKey>? keyComparer)
        where TKey : notnull
    {
        if (first.IsEmpty || second.IsEmpty)
            return keyComparer == null
                ? ImmutableDictionary<TKey, PotentialTargets>.Empty
                : ImmutableDictionary.Create<TKey, PotentialTargets>(keyComparer);

        var builder = keyComparer == null
            ? ImmutableDictionary.CreateBuilder<TKey, PotentialTargets>()
            : ImmutableDictionary.CreateBuilder<TKey, PotentialTargets>(keyComparer);
        foreach (var kvp in first)
            if (second.TryGetValue(kvp.Key, out var otherTargets))
                builder[kvp.Key] = PotentialTargets.Merge(kvp.Value, otherTargets);

        return builder.ToImmutable();
    }
}
