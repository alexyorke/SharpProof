using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

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
        using var enumerator = maps.GetEnumerator();
        if (!enumerator.MoveNext())
            return ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default);

        var result = enumerator.Current;
        while (enumerator.MoveNext()) result = MergeSmtSymbolVersions(result, enumerator.Current, 0);

        return result;
    }

    private static PurityAnalysisState MergeStates(
        PurityAnalysisState state1,
        PurityAnalysisState state2,
        int phiScope)
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
        var mergedSmtSymbolVersions = MergeSmtSymbolVersions(
            state1.SmtSymbolVersions,
            state2.SmtSymbolVersions,
            phiScope);

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
            MergePathStatesAcrossAll(new[] { state1, state2 }, mergedSmtSymbolVersions),
            mergedCaptureSymbols,
            mergedOwnedArrayFlowCaptures);
    }

    private static (SyntaxNode? FirstImpureNode, PurityEvidence FirstImpurityEvidence) SelectFirstImpurity(
        PurityAnalysisState state1,
        PurityAnalysisState state2)
    {
        var firstImpureNode = state1.FirstImpureSyntaxNode;
        var firstImpurityEvidence = state1.FirstImpurityEvidence;
        if (state2.HasPotentialImpurity &&
            state2.FirstImpureSyntaxNode != null &&
            (firstImpureNode == null ||
             state2.FirstImpureSyntaxNode.SpanStart < firstImpureNode.SpanStart))
        {
            firstImpureNode = state2.FirstImpureSyntaxNode;
            firstImpurityEvidence = state2.FirstImpurityEvidence;
        }
        else if (!state1.HasPotentialImpurity && state2.HasPotentialImpurity)
        {
            firstImpureNode = state2.FirstImpureSyntaxNode;
            firstImpurityEvidence = state2.FirstImpurityEvidence;
        }

        return (firstImpureNode, firstImpurityEvidence);
    }

    private static SymbolicState MergePathStatesAcrossAll(
        IReadOnlyList<PurityAnalysisState> states,
        ImmutableDictionary<ISymbol, int> mergedSmtSymbolVersions)
    {
        if (states.Count == 0) return new SymbolicState();

        var normalizedStates = states
            .Select(state => NormalizePathStateForMergedState(state.PathState, mergedSmtSymbolVersions))
            .ToArray();
        var commonFacts = normalizedStates[0].Facts;
        for (var index = 1; index < states.Count; index++)
        {
            commonFacts = IntersectSymbolicFacts(commonFacts, normalizedStates[index].Facts);
            if (commonFacts.IsEmpty) break;
        }

        var commonConditions = SymbolicStateMerger.MergePathConditionsAcrossAll(normalizedStates);
        commonFacts = MergeResourceStateFacts(commonFacts, normalizedStates);
        return new SymbolicState(commonFacts, commonConditions);
    }

    private static SymbolicState NormalizePathStateForMergedState(
        SymbolicState pathState,
        ImmutableDictionary<ISymbol, int> mergedSmtSymbolVersions)
    {
        if (mergedSmtSymbolVersions.Count == 0) return pathState;

        var targetVersions = mergedSmtSymbolVersions
            .Select(pair => new KeyValuePair<string, int>(
                SymbolicFactFactory.GetSmtVariableName(pair.Key.OriginalDefinition),
                pair.Value))
            .ToImmutableDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
        var facts = pathState.Facts
            .Select(fact => SymbolicIrVersionRewriter.RewriteToCurrentVersions(fact, targetVersions));
        var conditions = pathState.PathConditions
            .Select(condition => SymbolicIrVersionRewriter.RewriteToCurrentVersions(condition, targetVersions));
        return new SymbolicState(facts, conditions);
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

    private static ImmutableArray<SymbolicFact> MergeResourceStateFacts(
        ImmutableArray<SymbolicFact> commonFacts,
        IReadOnlyList<SymbolicState> states)
    {
        if (states.Count == 0) return commonFacts;

        var builder = commonFacts.ToBuilder();
        var resourceKeys = new List<(SymbolicTerm Resource, ISymbol? Symbol)>();
        foreach (var state in states)
        foreach (var fact in state.Facts)
        {
            if (!TryGetResourceStateIdentity(fact, out var resource, out var symbol) ||
                resourceKeys.Any(key => ResourceStateIdentityMatches(key.Resource, key.Symbol, resource, symbol)))
                continue;

            resourceKeys.Add((resource, symbol));
        }

        foreach (var (resource, symbol) in resourceKeys)
        {
            var releasedOnAllPaths = states.All(state => HasExactResourceRelease(state, resource, symbol));
            if (releasedOnAllPaths)
            {
                var representative = states
                    .SelectMany(state => state.Facts.Select(fact => (State: state, Fact: fact)))
                    .First(pair =>
                        TryGetExactResourceRelease(
                            pair.Fact,
                            out var releasedResource,
                            out var releasedSymbol) &&
                        (ResourceStateIdentityMatches(
                             resource,
                             symbol,
                             releasedResource,
                             releasedSymbol) ||
                         IsResourceReleasedViaMergedAliases(
                             resource,
                             new HashSet<SymbolicTerm> { releasedResource },
                             pair.State,
                             new HashSet<SymbolicTerm>())))
                    .Fact;

                var mergedFact = representative with
                {
                    Atom = new SymbolicResourceLifetimeAtom(resource, SymbolicResourceLifetimeState.Released),
                    Provenance = "analyzer.resource.merge.all-path-release",
                    EvidenceKey = representative.EvidenceKey ?? "evidence.resource.released",
                    Symbol = symbol ?? representative.Symbol
                };

                if (!builder.Any(fact => AreMergeEquivalentSymbolicFacts(fact, mergedFact))) builder.Add(mergedFact);
                continue;
            }

            // An obligation discharged on only some incoming paths remains outstanding after the join.
            // Plain fact intersection would otherwise erase both the released and the owned state.
            foreach (var outstandingFact in states
                         .SelectMany(static state => state.Facts)
                         .Where(fact => IsOutstandingResourceFactFor(fact, resource, symbol)))
                if (!builder.Any(fact => AreMergeEquivalentSymbolicFacts(fact, outstandingFact)))
                    builder.Add(outstandingFact);
        }

        return builder.ToImmutable();
    }

    private static bool HasExactResourceRelease(
        SymbolicState state,
        SymbolicTerm resource,
        ISymbol? symbol)
    {
        var releasedResources = new HashSet<SymbolicTerm>();
        foreach (var fact in state.Facts)
        {
            if (!TryGetExactResourceRelease(fact, out var releasedResource, out var releasedSymbol)) continue;
            if (ResourceStateIdentityMatches(resource, symbol, releasedResource, releasedSymbol)) return true;
            releasedResources.Add(releasedResource);
        }

        return IsResourceReleasedViaMergedAliases(
            resource,
            releasedResources,
            state,
            new HashSet<SymbolicTerm>());
    }

    private static bool IsResourceReleasedViaMergedAliases(
        SymbolicTerm resource,
        HashSet<SymbolicTerm> releasedResources,
        SymbolicState state,
        HashSet<SymbolicTerm> visited)
    {
        if (releasedResources.Contains(resource)) return true;
        if (!visited.Add(resource)) return false;

        foreach (var fact in state.Facts)
        {
            if (!fact.Polarity ||
                fact.Confidence != SymbolicFactConfidence.Exact ||
                fact.Atom is not SymbolicAliasAtom { MayAlias: true } alias)
                continue;

            var related = Equals(alias.Source, resource)
                ? alias.Target
                : Equals(alias.Target, resource)
                    ? alias.Source
                    : null;
            if (related != null && IsResourceReleasedViaMergedAliases(
                    related,
                    releasedResources,
                    state,
                    visited))
                return true;
        }

        return false;
    }

    private static bool TryGetResourceStateIdentity(
        SymbolicFact fact,
        out SymbolicTerm resource,
        out ISymbol? symbol)
    {
        if (TryGetExactResourceRelease(fact, out resource, out symbol)) return true;

        symbol = fact.Symbol;
        switch (fact.Atom)
        {
            case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime:
                resource = lifetime.Resource;
                return true;
            case SymbolicDisposalAtom { State: SymbolicDisposalState.NotDisposed } disposal:
                resource = disposal.Resource;
                return true;
            default:
                resource = null!;
                symbol = null;
                return false;
        }
    }

    private static bool IsOutstandingResourceFactFor(
        SymbolicFact fact,
        SymbolicTerm resource,
        ISymbol? symbol)
    {
        if (!fact.Polarity || fact.Confidence != SymbolicFactConfidence.Exact) return false;

        var outstandingResource = fact.Atom switch
        {
            SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime =>
                lifetime.Resource,
            SymbolicDisposalAtom { State: SymbolicDisposalState.NotDisposed } disposal =>
                disposal.Resource,
            _ => null
        };
        return outstandingResource != null &&
               ResourceStateIdentityMatches(resource, symbol, outstandingResource, fact.Symbol);
    }

    private static bool ResourceStateIdentityMatches(
        SymbolicTerm firstResource,
        ISymbol? firstSymbol,
        SymbolicTerm secondResource,
        ISymbol? secondSymbol)
    {
        return firstSymbol != null && secondSymbol != null
            ? SymbolEqualityComparer.Default.Equals(firstSymbol, secondSymbol)
            : Equals(firstResource, secondResource);
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

    private static ImmutableDictionary<ISymbol, int> MergeSmtSymbolVersions(
        ImmutableDictionary<ISymbol, int> first,
        ImmutableDictionary<ISymbol, int> second,
        int phiScope)
    {
        var symbols = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
        symbols.UnionWith(first.Keys);
        symbols.UnionWith(second.Keys);

        var result = ImmutableDictionary.CreateBuilder<ISymbol, int>(SymbolEqualityComparer.Default);
        foreach (var symbol in symbols)
        {
            var original = symbol.OriginalDefinition;
            var firstVersion = first.TryGetValue(original, out var left) ? left : 0;
            var secondVersion = second.TryGetValue(original, out var right) ? right : 0;
            result[original] = firstVersion == secondVersion
                ? firstVersion
                : checked(phiScope * 2 + 1);
        }

        return result.ToImmutable();
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
