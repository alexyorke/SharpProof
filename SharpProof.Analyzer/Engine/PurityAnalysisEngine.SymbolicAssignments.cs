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
                currentState,
                valueOperation.Syntax,
                semanticModel,
                compilation,
                cancellationToken);
            nextState = nextState.WithIncrementedSmtSymbolVersion(writtenLocalSymbol);
            nextState = AddPreservedOwnedDisposableAliasFacts(
                nextState,
                ownedDisposableAliases,
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
        PurityAnalysisState currentState,
        SyntaxNode reassignmentSyntax,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var reassignedTerm = CreateSymbolicReferenceTerm(reassignedSymbol, currentState);
        var builder = ImmutableArray.CreateBuilder<ISymbol>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var hasSymbolicObligation = HasUnreleasedOwnedResourceObligation(reassignedTerm, currentState);
        if (hasSymbolicObligation)
            AddSymbolicAliasSymbolsToPreserve(
                reassignedSymbol,
                reassignedTerm,
                currentState,
                builder,
                seen);

        if (!hasSymbolicObligation &&
            IsUndisposedFreshDisposableLocalBeforeReassignment(
                reassignedSymbol,
                reassignmentSyntax,
                semanticModel,
                compilation,
                cancellationToken))
            AddSyntacticAliasSymbolsToPreserve(
                reassignedSymbol,
                reassignmentSyntax,
                semanticModel,
                cancellationToken,
                builder,
                seen);

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

        return nextState.WithPathConditionsAndState(nextState.PathConditions, pathState);
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

    private static bool IsUndisposedFreshDisposableLocalBeforeReassignment(
        ISymbol reassignedSymbol,
        SyntaxNode reassignmentSyntax,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (reassignedSymbol is not ILocalSymbol localSymbol) return false;

        var declaratorSyntax = localSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault();
        if (declaratorSyntax?.Initializer?.Value == null ||
            declaratorSyntax.SpanStart >= reassignmentSyntax.SpanStart)
            return false;

        var initializerOperation = semanticModel.GetOperation(declaratorSyntax.Initializer.Value, cancellationToken);
        if (!IsOwnedDisposableObjectCreationValue(initializerOperation!, compilation)) return false;

        return !WasAnySymbolDisposedBeforeObservation(
            EnumerateSyntacticAliases(localSymbol, reassignmentSyntax, semanticModel, cancellationToken)
                .Prepend(localSymbol),
            reassignmentSyntax,
            semanticModel,
            cancellationToken);
    }

    private static void AddSyntacticAliasSymbolsToPreserve(
        ISymbol reassignedSymbol,
        SyntaxNode reassignmentSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ImmutableArray<ISymbol>.Builder builder,
        HashSet<ISymbol> seen)
    {
        if (reassignedSymbol is not ILocalSymbol localSymbol) return;

        foreach (var aliasSymbol in EnumerateSyntacticAliases(localSymbol, reassignmentSyntax, semanticModel,
                     cancellationToken))
            if (!SymbolEqualityComparer.Default.Equals(aliasSymbol, reassignedSymbol) &&
                seen.Add(aliasSymbol))
                builder.Add(aliasSymbol);
    }

    private static IEnumerable<ILocalSymbol> EnumerateSyntacticAliases(
        ILocalSymbol sourceLocal,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var containingBlock = observationSyntax.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock == null) yield break;

        foreach (var declarator in containingBlock.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (declarator.SpanStart >= observationSyntax.SpanStart ||
                declarator.Initializer?.Value == null ||
                semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol aliasSymbol ||
                semanticModel.GetSymbolInfo(declarator.Initializer.Value, cancellationToken).Symbol is not ILocalSymbol
                    initializerSymbol ||
                !SymbolEqualityComparer.Default.Equals(initializerSymbol, sourceLocal))
                continue;

            yield return aliasSymbol;
        }
    }

    private static bool WasAnySymbolDisposedBeforeObservation(
        IEnumerable<ISymbol> symbols,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbolSet = new HashSet<ISymbol>(symbols, SymbolEqualityComparer.Default);
        if (symbolSet.Count == 0) return false;

        var containingBlock = observationSyntax.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock == null) return false;

        foreach (var invocation in containingBlock.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (invocation.SpanStart >= observationSyntax.SpanStart ||
                invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name.Identifier.ValueText is not (nameof(IDisposable.Dispose) or "DisposeAsync") ||
                semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not
                { } disposedSymbol)
                continue;

            if (symbolSet.Contains(disposedSymbol)) return true;
        }

        return false;
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