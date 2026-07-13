using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    internal static PurityAnalysisState ApplyWrittenLocalStateUpdates(
        PurityAnalysisState currentState,
        ILocalSymbol[] writtenLocalSymbols,
        IOperation valueOperation,
        PurityAnalysisState valueState,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken cancellationToken)
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
            nextState = AddAssignedValueFact(
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
        }

        foreach (var writtenLocalSymbol in writtenLocalSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        }

        foreach (var writtenLocalSymbol in writtenLocalSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        var reassignedTerm = CreateSymbolicReferenceTerm(reassignedSymbol, currentState);
        var shouldPreserve = aliasState switch
        {
            PreservedAliasState.OwnedDisposable =>
                HasUnreleasedOwnedResourceObligation(reassignedTerm, currentState),
            PreservedAliasState.Disposed =>
                HasDisposedResourceFactForTerm(reassignedTerm, currentState, new HashSet<SymbolicTerm>()),
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

        var pathState = nextState.PathState;
        foreach (var aliasSymbol in aliasSymbols)
        {
            var aliasTerm = CreateSymbolicReferenceTerm(aliasSymbol, nextState);
            foreach (var fact in CreatePreservedAliasFacts(aliasTerm, aliasSymbol, source, aliasState))
                pathState = pathState.AddFact(fact);
        }

        return nextState.WithPathState(pathState);
    }

    private static ImmutableArray<SymbolicFact> CreatePreservedAliasFacts(
        SymbolicTerm aliasTerm,
        ISymbol aliasSymbol,
        SyntaxNode source,
        PreservedAliasState aliasState)
    {
        if (aliasState == PreservedAliasState.OwnedDisposable)
        {
            return SymbolicOwnershipFactFactory.CreateFreshOwned(
                    aliasTerm,
                    source,
                    "analyzer.resource.alias-preserve",
                    aliasSymbol,
                    "evidence.resource.alias-preserve")
                .Add(SymbolicOwnershipFactFactory.CreateDisposal(
                    aliasTerm,
                    SymbolicDisposalState.NotDisposed,
                    source,
                    "analyzer.resource.alias-preserve.disposal",
                    aliasSymbol,
                    "evidence.resource.alias-preserve"));
        }

        return ImmutableArray.Create(
            SymbolicOwnershipFactFactory.CreateDisposal(
                aliasTerm,
                SymbolicDisposalState.Disposed,
                source,
                "analyzer.resource.alias-preserve.disposed",
                aliasSymbol,
                "evidence.resource.alias-preserve.disposed"),
            SymbolicOwnershipFactFactory.CreateResourceLifetime(
                aliasTerm,
                SymbolicResourceLifetimeState.Released,
                source,
                "analyzer.resource.alias-preserve.disposed.lifetime",
                aliasSymbol,
                "evidence.resource.alias-preserve.disposed"));
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
        var releasedResources = CollectExactReleasedResources(state.PathState);
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
               !IsResourceReleased(resourceTerm, releasedResources, state, new HashSet<SymbolicTerm>());
    }

    internal static PurityAnalysisState ApplyAssignedDelegateTargets(
        PurityAnalysisState currentState,
        ISymbol? targetSymbol,
        ITypeSymbol? targetType,
        IOperation? valueOperation,
        ILocalSymbol[] writtenLocalSymbols,
        PurityAnalysisState valueState,
        CancellationToken cancellationToken,
        string logScope,
        string unresolvedReason)
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

        if (localSymbol.RefKind == RefKind.None) yield break;

        foreach (var syntaxReference in localSymbol.DeclaringSyntaxReferences)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax declaratorSyntax ||
                declaratorSyntax.Initializer?.Value == null)
                continue;

            var initializerSyntax = declaratorSyntax.Initializer.Value;
            if (initializerSyntax is RefExpressionSyntax refExpressionSyntax)
                initializerSyntax = refExpressionSyntax.Expression;

            if (context.SemanticModel.GetOperation(initializerSyntax, context.CancellationToken) is not
                { } initializerOperation) continue;

            if (TryResolveSymbol(SkipImplicitConversions(initializerOperation)) is not ILocalSymbol targetLocalSymbol)
                continue;

            foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(targetLocalSymbol, context, visited))
                yield return writtenLocalSymbol;
        }
    }
}
