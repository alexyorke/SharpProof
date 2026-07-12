using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static PurityAnalysisState ApplyWrittenLocalStateUpdates(
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
            var ownedDisposableAliases = GetOwnedDisposableAliasSymbolsToPreserve(
                writtenLocalSymbol,
                currentState);
            var disposedAliases = GetDisposedAliasSymbolsToPreserve(
                writtenLocalSymbol,
                currentState);
            nextState = nextState.WithSmtSymbolDefinitionVersion(writtenLocalSymbol, valueOperation.Syntax);
            nextState = AddPreservedOwnedDisposableAliasFacts(
                nextState,
                ownedDisposableAliases,
                valueOperation.Syntax);
            nextState = AddPreservedDisposedAliasFacts(
                nextState,
                disposedAliases,
                valueOperation.Syntax);
            nextState = AddAssignedValueFact(
                nextState,
                writtenLocalSymbol,
                valueOperation,
                valueState,
                semanticModel,
                cancellationToken);

            if (TryResolveKnownConcreteType(valueOperation, valueState, compilation, out var concreteType))
                nextState = nextState.WithLocalConcreteType(writtenLocalSymbol, concreteType);
            else
                nextState = nextState.WithoutLocalConcreteType(writtenLocalSymbol);
        }

        foreach (var writtenLocalSymbol in writtenLocalSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsOwnedLocalArrayValue(valueOperation, valueState, compilation))
            {
                nextState = nextState.WithOwnedLocalArray(writtenLocalSymbol);
                nextState = AddOwnedLocalArrayFacts(
                    nextState,
                    writtenLocalSymbol,
                    valueOperation);
            }
            else
            {
                nextState = nextState.WithoutOwnedLocalArray(writtenLocalSymbol);
            }

            nextState = AddOwnedDisposableLocalFacts(
                nextState,
                writtenLocalSymbol,
                valueOperation,
                compilation);
            nextState = AddFreshMutableObjectFacts(
                nextState,
                writtenLocalSymbol,
                valueOperation);
        }

        foreach (var writtenLocalSymbol in writtenLocalSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDefinitelyNullValue(valueOperation, valueState))
                nextState = nextState.WithDefinitelyNullLocal(writtenLocalSymbol);
            else
                nextState = nextState.WithoutDefinitelyNullLocal(writtenLocalSymbol);
        }

        return nextState;
    }

    private static ImmutableArray<ISymbol> GetOwnedDisposableAliasSymbolsToPreserve(
        ISymbol reassignedSymbol,
        PurityAnalysisState currentState)
    {
        var reassignedTerm = CreateSymbolicReferenceTerm(reassignedSymbol, currentState);
        var builder = ImmutableArray.CreateBuilder<ISymbol>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (HasUnreleasedOwnedResourceObligation(reassignedTerm, currentState))
            AddSymbolicAliasSymbolsToPreserve(
                reassignedSymbol,
                reassignedTerm,
                currentState,
                builder,
                seen);

        return builder.ToImmutable();
    }

    private static ImmutableArray<ISymbol> GetDisposedAliasSymbolsToPreserve(
        ISymbol reassignedSymbol,
        PurityAnalysisState currentState)
    {
        var reassignedTerm = CreateSymbolicReferenceTerm(reassignedSymbol, currentState);
        if (!HasDisposedResourceFactForTerm(
                reassignedTerm,
                currentState,
                new HashSet<SymbolicTerm>()))
            return ImmutableArray<ISymbol>.Empty;

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

    private static PurityAnalysisState AddPreservedOwnedDisposableAliasFacts(
        PurityAnalysisState nextState,
        ImmutableArray<ISymbol> aliasSymbols,
        SyntaxNode source)
    {
        if (aliasSymbols.IsDefaultOrEmpty) return nextState;

        var pathState = nextState.PathState;
        foreach (var aliasSymbol in aliasSymbols)
        {
            var aliasTerm = CreateSymbolicReferenceTerm(aliasSymbol, nextState);
            var ownershipFacts = SymbolicOwnershipFactFactory.CreateFreshOwned(
                aliasTerm,
                source,
                "analyzer.resource.alias-preserve",
                aliasSymbol,
                "evidence.resource.alias-preserve");
            foreach (var fact in ownershipFacts) pathState = pathState.AddFact(fact);

            pathState = pathState.AddFact(SymbolicOwnershipFactFactory.CreateDisposal(
                aliasTerm,
                SymbolicDisposalState.NotDisposed,
                source,
                "analyzer.resource.alias-preserve.disposal",
                aliasSymbol,
                "evidence.resource.alias-preserve"));
        }

        return nextState.WithPathState(pathState);
    }

    private static PurityAnalysisState AddPreservedDisposedAliasFacts(
        PurityAnalysisState nextState,
        ImmutableArray<ISymbol> aliasSymbols,
        SyntaxNode source)
    {
        if (aliasSymbols.IsDefaultOrEmpty) return nextState;

        var pathState = nextState.PathState;
        foreach (var aliasSymbol in aliasSymbols)
        {
            var aliasTerm = CreateSymbolicReferenceTerm(aliasSymbol, nextState);
            pathState = pathState
                .AddFact(SymbolicOwnershipFactFactory.CreateDisposal(
                    aliasTerm,
                    SymbolicDisposalState.Disposed,
                    source,
                    "analyzer.resource.alias-preserve.disposed",
                    aliasSymbol,
                    "evidence.resource.alias-preserve.disposed"))
                .AddFact(SymbolicOwnershipFactFactory.CreateResourceLifetime(
                    aliasTerm,
                    SymbolicResourceLifetimeState.Released,
                    source,
                    "analyzer.resource.alias-preserve.disposed.lifetime",
                    aliasSymbol,
                    "evidence.resource.alias-preserve.disposed"));
        }

        return nextState.WithPathState(pathState);
    }

    private static bool HasUnreleasedOwnedResourceObligation(
        SymbolicTerm resourceTerm,
        PurityAnalysisState state)
    {
        var hasOwnedResource = false;
        var releasedResources = new HashSet<SymbolicTerm>();
        foreach (var fact in state.PathState.Facts)
        {
            if (!fact.Polarity ||
                fact.Confidence != SymbolicFactConfidence.Exact)
                continue;

            if (TryGetExactResourceRelease(fact, out var releasedResource, out _))
            {
                releasedResources.Add(releasedResource);
                continue;
            }

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

    private static PurityAnalysisState ApplyAssignedDelegateTargets(
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

    private static IEnumerable<ILocalSymbol> EnumerateWrittenLocalSymbols(
        ILocalSymbol localSymbol,
        PurityAnalysisContext context)
    {
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(localSymbol, context, visited))
            yield return writtenLocalSymbol;
    }

    private static IEnumerable<ILocalSymbol> EnumerateWrittenLocalSymbols(
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
