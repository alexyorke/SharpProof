using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic.Ir;
using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine;

internal static partial class PurityAssignmentStateTransfer
{
    internal static PurityAnalysisState ApplyWrittenLocalStateUpdates(
        PurityAnalysisState currentState,
        ILocalSymbol[] writtenLocalSymbols,
        IOperation valueOperation,
        PurityAnalysisState valueState,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken cancellationToken,
        bool advanceDefinitionVersion = true)
    {
        var nextState = currentState;

        foreach (var writtenLocalSymbol in writtenLocalSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ownedDisposableAliases = GetAliasSymbolsToPreserve(
                writtenLocalSymbol,
                currentState,
                PreservedAliasState.OwnedDisposable);
            var disposedAliases = GetAliasSymbolsToPreserve(
                writtenLocalSymbol,
                currentState,
                PreservedAliasState.Disposed);
            if (advanceDefinitionVersion)
                nextState = nextState.WithSmtSymbolDefinitionVersion(writtenLocalSymbol, valueOperation.Syntax);
            nextState = AddPreservedAliasFacts(
                nextState,
                ownedDisposableAliases,
                valueOperation.Syntax,
                PreservedAliasState.OwnedDisposable);
            nextState = AddPreservedAliasFacts(
                nextState,
                disposedAliases,
                valueOperation.Syntax,
                PreservedAliasState.Disposed);
            nextState = PurityOperationTransferAdapter.ApplyAssignmentFacts(
                nextState,
                writtenLocalSymbol,
                valueOperation,
                valueState,
                semanticModel,
                cancellationToken);

            if (PurityConcreteReceiverResolver.TryResolveKnownConcreteType(valueOperation, valueState, compilation, out var concreteType))
                nextState = nextState.WithLocalConcreteType(writtenLocalSymbol, concreteType);
            else
                nextState = nextState.WithoutLocalConcreteType(writtenLocalSymbol);

            if (PurityKnownBclSemantics.IsOwnedLocalArrayValue(valueOperation, valueState, compilation))
            {
                nextState = nextState.WithOwnedLocalArray(writtenLocalSymbol);
                nextState = PurityResourceStateFacts.AddOwnedLocalArrayFacts(
                    nextState,
                    writtenLocalSymbol,
                    valueOperation);
            }
            else
            {
                nextState = nextState.WithoutOwnedLocalArray(writtenLocalSymbol);
            }

            nextState = PurityResourceStateFacts.AddOwnedDisposableLocalFacts(
                nextState,
                writtenLocalSymbol,
                valueOperation,
                compilation);
            nextState = PurityResourceStateFacts.AddFreshMutableObjectFacts(
                nextState,
                writtenLocalSymbol,
                valueOperation);

            if (PurityConcreteReceiverResolver.IsDefinitelyNullValue(valueOperation, valueState))
                nextState = nextState.WithDefinitelyNullLocal(writtenLocalSymbol);
            else
                nextState = nextState.WithoutDefinitelyNullLocal(writtenLocalSymbol);
        }

        return nextState;
    }

    private static ImmutableArray<ISymbol> GetAliasSymbolsToPreserve(
        ISymbol reassignedSymbol,
        PurityAnalysisState currentState,
        PreservedAliasState aliasState)
    {
        var reassignedTerm = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(reassignedSymbol, currentState);
        var shouldPreserve = aliasState switch
        {
            PreservedAliasState.OwnedDisposable =>
                HasUnreleasedOwnedResourceObligation(reassignedTerm, currentState),
            PreservedAliasState.Disposed =>
                PurityResourceStateFacts.HasDisposedResourceFactForTerm(
                    reassignedTerm,
                    currentState,
                    new HashSet<SymbolicTerm>()),
            _ => false
        };
        if (!shouldPreserve) return ImmutableArray<ISymbol>.Empty;

        var builder = ImmutableArray.CreateBuilder<ISymbol>();
        AddSymbolicAliasSymbolsToPreserve(
            reassignedSymbol,
            reassignedTerm,
            currentState,
            builder,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default));
        return builder.ToImmutable();
    }

    private static void AddSymbolicAliasSymbolsToPreserve(
        ISymbol reassignedSymbol,
        SymbolicTerm reassignedTerm,
        PurityAnalysisState currentState,
        ImmutableArray<ISymbol>.Builder builder,
        HashSet<ISymbol> seen)
    {
        foreach (var fact in currentState.PathState.Facts)
        {
            if (!fact.Polarity ||
                fact.Confidence != SymbolicFactConfidence.Exact ||
                fact.Atom is not SymbolicAliasAtom { MayAlias: true } alias ||
                !Equals(alias.Source, reassignedTerm) ||
                fact.Symbol == null ||
                SymbolEqualityComparer.Default.Equals(fact.Symbol, reassignedSymbol) ||
                !seen.Add(fact.Symbol))
                continue;

            builder.Add(fact.Symbol);
        }
    }

    private static PurityAnalysisState AddPreservedAliasFacts(
        PurityAnalysisState nextState,
        ImmutableArray<ISymbol> aliasSymbols,
        SyntaxNode source,
        PreservedAliasState aliasState)
    {
        if (aliasSymbols.IsDefaultOrEmpty) return nextState;

        foreach (var aliasSymbol in aliasSymbols)
        {
            var aliasTerm = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(aliasSymbol, nextState);
            var kind = aliasState == PreservedAliasState.OwnedDisposable
                ? SymbolicLifetimeOperationKind.AcquireDisposable
                : SymbolicLifetimeOperationKind.Dispose;
            var provenance = aliasState == PreservedAliasState.OwnedDisposable
                ? "analyzer.resource.alias-preserve"
                : "analyzer.resource.alias-preserve.disposed";
            nextState = PurityOperationTransferAdapter.ApplyLifetime(
                nextState,
                aliasTerm,
                kind,
                source,
                provenance,
                aliasSymbol,
                aliasState == PreservedAliasState.OwnedDisposable
                    ? "evidence.resource.alias-preserve"
                    : "evidence.resource.alias-preserve.disposed");
        }

        return nextState;
    }

    private enum PreservedAliasState
    {
        OwnedDisposable,
        Disposed
    }

    private static bool HasUnreleasedOwnedResourceObligation(
        SymbolicTerm resourceTerm,
        PurityAnalysisState state)
    {
        var hasOwnedResource = false;
        var releasedResources = PuritySymbolicStateFacts.CollectExactReleasedResources(state.PathState);
        foreach (var fact in state.PathState.Facts)
        {
            if (!fact.Polarity ||
                fact.Confidence != SymbolicFactConfidence.Exact)
                continue;

            switch (fact.Atom)
            {
                case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime
                    when Equals(lifetime.Resource, resourceTerm):
                    hasOwnedResource = true;
                    break;
                case SymbolicDisposalAtom { State: SymbolicDisposalState.NotDisposed } disposal
                    when Equals(disposal.Resource, resourceTerm):
                    hasOwnedResource = true;
                    break;
            }
        }

        return hasOwnedResource &&
               !PurityResourceStateFacts.IsResourceReleased(
                   resourceTerm,
                   releasedResources,
                   state,
                   new HashSet<SymbolicTerm>());
    }

    internal static PurityAnalysisState ApplyAssignedDelegateTargets(
        PurityAnalysisState currentState,
        ISymbol? targetSymbol,
        ITypeSymbol? targetType,
        IOperation? valueOperation,
        ILocalSymbol[] writtenLocalSymbols,
        PurityAnalysisState valueState,
        CancellationToken cancellationToken)
    {
        if (valueOperation == null || targetSymbol == null || targetType?.TypeKind != TypeKind.Delegate)
            return currentState;

        var nextState = currentState;
        var valueTargets = ResolvePotentialTargets(valueOperation, valueState, cancellationToken);
        if (valueTargets != null)
            foreach (var writtenTargetSymbol in GetAssignmentTargetSymbols(targetSymbol, writtenLocalSymbols))
                nextState = nextState.WithDelegateTarget(writtenTargetSymbol, valueTargets.Value);
        else
            foreach (var writtenTargetSymbol in GetAssignmentTargetSymbols(targetSymbol, writtenLocalSymbols))
                nextState = nextState.WithDelegateTarget(writtenTargetSymbol, PotentialTargets.Unresolved);

        return nextState;
    }

    private static IEnumerable<ISymbol> GetAssignmentTargetSymbols(
        ISymbol targetSymbol,
        ILocalSymbol[] writtenLocalSymbols)
    {
        if (writtenLocalSymbols.Length == 0)
        {
            yield return targetSymbol;
            yield break;
        }

        foreach (var writtenLocalSymbol in writtenLocalSymbols) yield return writtenLocalSymbol;
    }

    internal static IEnumerable<ILocalSymbol> EnumerateWrittenLocalSymbols(
        ILocalSymbol localSymbol,
        PurityAnalysisContext context)
    {
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(localSymbol, context, visited))
            yield return writtenLocalSymbol;
    }

    internal static IEnumerable<ILocalSymbol> EnumerateWrittenLocalSymbols(
        ILocalSymbol localSymbol,
        PurityAnalysisContext context,
        HashSet<ISymbol> visited)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (!visited.Add(localSymbol)) yield break;

        yield return localSymbol;

        foreach (var initializerOperation in RuleAnalysisHelper.EnumerateRefLocalInitializerOperations(
                     localSymbol,
                     context.SemanticModel,
                     context.CancellationToken))
        {
            if (TryResolveSymbol(initializerOperation) is not ILocalSymbol targetLocalSymbol)
                continue;

            foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(targetLocalSymbol, context, visited))
                yield return writtenLocalSymbol;
        }
    }
}
