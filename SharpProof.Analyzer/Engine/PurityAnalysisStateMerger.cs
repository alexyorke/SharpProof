using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using PotentialTargets = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PotentialTargets;
using PurityAnalysisResult = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PurityAnalysisResult;
using PurityAnalysisState = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PurityAnalysisState;
using PurityEvidence = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PurityEvidence;

namespace SharpProof.Analyzer.Engine;

internal static class PurityAnalysisStateMerger
{
    internal static PurityAnalysisState MergeStates(
        PurityAnalysisState state1,
        PurityAnalysisState state2,
        int phiScope)
    {
        return MergeStatesAcrossAll(new[] { state1, state2 }, phiScope);
    }

    internal static PurityAnalysisState MergeStatesAcrossAll(
        IReadOnlyList<PurityAnalysisState> states,
        int phiScope)
    {
        var (firstImpureNode, firstImpurityEvidence) = SelectFirstImpurity(states);
        return new PurityAnalysisState(
            states.Any(static state => state.HasPotentialImpurity),
            firstImpureNode,
            MergeDelegateTargetMapsAcrossAll(states.Select(static state => state.DelegateTargetMap)),
            MergeFlowCaptureMapsAcrossAll(states.Select(static state => state.FlowCaptures)),
            MergeFlowCaptureTargetMapsAcrossAll(states.Select(static state => state.FlowCaptureTargets)),
            IntersectSymbolSetsAcrossAll(states.Select(static state => state.DefinitelyNullLocalSymbols)),
            firstImpurityEvidence,
            IntersectLocalConcreteTypesAcrossAll(states.Select(static state => state.LocalConcreteTypes)),
            IntersectFlowCaptureConcreteTypesAcrossAll(states.Select(static state => state.FlowCaptureConcreteTypes)),
            MergePathStatesAcrossAll(states, phiScope),
            IntersectFlowCaptureSymbolsAcrossAll(states.Select(static state => state.FlowCaptureSymbols)),
            IntersectOwnedArrayFlowCapturesAcrossAll(states.Select(static state => state.OwnedArrayFlowCaptures)));
    }

    private static (SyntaxNode? FirstImpureNode, PurityEvidence FirstImpurityEvidence) SelectFirstImpurity(
        IEnumerable<PurityAnalysisState> states)
    {
        SyntaxNode? firstImpureNode = null;
        var firstImpurityEvidence = PurityEvidence.None;
        var foundImpurity = false;
        foreach (var state in states)
        {
            if (!state.HasPotentialImpurity) continue;
            if (!foundImpurity ||
                state.FirstImpureSyntaxNode != null &&
                (firstImpureNode == null || state.FirstImpureSyntaxNode.SpanStart < firstImpureNode.SpanStart))
            {
                firstImpureNode = state.FirstImpureSyntaxNode;
                firstImpurityEvidence = state.FirstImpurityEvidence;
                foundImpurity = true;
            }
        }

        return (firstImpureNode, firstImpurityEvidence);
    }

    private static SymbolicState MergePathStatesAcrossAll(
        IReadOnlyList<PurityAnalysisState> states,
        int phiScope)
    {
        return SymbolicStateMerger.MergePathStatesAcrossAll(
            states.Select(static state => state.PathState).ToArray(),
            SymbolicStateMerger.AreEvidenceEquivalentFacts,
            phiScope);
    }

    private static ImmutableDictionary<ISymbol, PotentialTargets> MergeDelegateTargetMapsAcrossAll(
        IEnumerable<ImmutableDictionary<ISymbol, PotentialTargets>> maps)
    {
        return AggregateAcrossAll(
            maps,
            ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default),
            IntersectDelegateTargetMaps);
    }

    private static ImmutableHashSet<ISymbol> IntersectSymbolSets(
        ImmutableHashSet<ISymbol> first,
        ImmutableHashSet<ISymbol> second)
    {
        return ImmutableHashSet.CreateRange(
            SymbolEqualityComparer.Default,
            first.Intersect(second, SymbolEqualityComparer.Default));
    }

    private static ImmutableHashSet<ISymbol> IntersectSymbolSetsAcrossAll(
        IEnumerable<ImmutableHashSet<ISymbol>> symbolSets)
    {
        return AggregateAcrossAll(
            symbolSets,
            ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default),
            IntersectSymbolSets);
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

    private static ImmutableDictionary<CaptureId, PurityAnalysisResult> MergeFlowCaptureMapsAcrossAll(
        IEnumerable<ImmutableDictionary<CaptureId, PurityAnalysisResult>> maps)
    {
        return AggregateAcrossAll(
            maps,
            ImmutableDictionary<CaptureId, PurityAnalysisResult>.Empty,
            MergeFlowCaptureMaps);
    }

    private static ImmutableDictionary<CaptureId, PurityAnalysisResult> MergeFlowCaptureMaps(
        ImmutableDictionary<CaptureId, PurityAnalysisResult> first,
        ImmutableDictionary<CaptureId, PurityAnalysisResult> second)
    {
        if (first.IsEmpty) return second;
        if (second.IsEmpty) return first;

        var merged = first;
        foreach (var pair in second)
            merged = merged.SetItem(
                pair.Key,
                merged.TryGetValue(pair.Key, out var existing) && !existing.IsPure
                    ? existing
                    : pair.Value);

        return merged;
    }

    private static ImmutableDictionary<CaptureId, ISymbol> IntersectFlowCaptureSymbols(
        ImmutableDictionary<CaptureId, ISymbol> first,
        ImmutableDictionary<CaptureId, ISymbol> second)
    {
        return IntersectFlowCaptureSymbolMapsCore(
            first,
            second);
    }

    private static ImmutableDictionary<CaptureId, ISymbol> IntersectFlowCaptureSymbolsAcrossAll(
        IEnumerable<ImmutableDictionary<CaptureId, ISymbol>> maps)
    {
        return AggregateAcrossAll(
            maps,
            ImmutableDictionary<CaptureId, ISymbol>.Empty,
            IntersectFlowCaptureSymbols);
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
