using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine;

internal static class PuritySymbolicStateFacts
{
    internal static SymbolicVariableTerm CreateSymbolicReferenceTerm(
        ISymbol symbol,
        PurityAnalysisState currentState)
    {
        return new SymbolicVariableTerm(
            GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion),
            SmtValueKind.Reference);
    }

    internal static bool HasSymbolicBorrowFactForLocal(
        ILocalSymbol localSymbol,
        PurityAnalysisState currentState,
        SymbolicBorrowKind? borrowKind = null)
    {
        var localTerm = CreateSymbolicReferenceTerm(localSymbol, currentState);
        return HasSymbolicBorrowFactForTerm(
            localTerm,
            currentState,
            borrowKind,
            new HashSet<SymbolicTerm>());
    }

    internal static bool HasSymbolicBorrowerFactForSymbol(
        ISymbol ownerSymbol,
        PurityAnalysisState currentState)
    {
        var ownerTerm = CreateSymbolicReferenceTerm(ownerSymbol, currentState);
        return HasSymbolicBorrowerFactForTerm(
            ownerTerm,
            currentState,
            new HashSet<SymbolicTerm>());
    }

    internal static bool TryCreateMutableBorrowConflictEvidence(
        IOperation operation,
        ISymbol? targetSymbol,
        PurityAnalysisState currentState,
        string ruleName,
        out PurityEvidence evidence)
    {
        evidence = PurityEvidence.None;
        if (targetSymbol == null ||
            !HasSymbolicBorrowerFactForSymbol(targetSymbol, currentState))
            return false;

        evidence = PurityEvidence.Create(
            "mutable_state_write",
            ruleName,
            operation,
            operation.Syntax,
            targetSymbol,
            "analyzer.borrow.mutable-conflict");
        return true;
    }

    internal static bool TryCreateMutableBorrowConflictEvidence(
        IOperation operation,
        ISymbol? targetSymbol,
        PurityAnalysisState currentState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string ruleName,
        out PurityEvidence evidence)
    {
        if (TryCreateMutableBorrowConflictEvidence(
                operation,
                targetSymbol,
                currentState,
                ruleName,
                out evidence))
            return true;

        if (targetSymbol is ILocalSymbol targetLocal &&
            HasActiveRefLocalBorrowAfterWrite(
                targetLocal,
                operation.Syntax,
                semanticModel,
                cancellationToken))
        {
            evidence = PurityEvidence.Create(
                "mutable_state_write",
                ruleName,
                operation,
                operation.Syntax,
                targetLocal,
                "analyzer.borrow.mutable-conflict");
            return true;
        }

        evidence = PurityEvidence.None;
        return false;
    }

    private static bool HasActiveRefLocalBorrowAfterWrite(
        ILocalSymbol targetLocal,
        SyntaxNode writeSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var containingBlock = writeSyntax.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock == null) return false;

        var borrowedLocals = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var declarator in containingBlock.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (declarator.SpanStart >= writeSyntax.SpanStart ||
                    declarator.Initializer?.Value is not RefExpressionSyntax refExpression ||
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol refLocal ||
                    semanticModel.GetSymbolInfo(refExpression.Expression, cancellationToken).Symbol is not ILocalSymbol
                        sourceLocal)
                    continue;

                if ((SymbolEqualityComparer.Default.Equals(sourceLocal, targetLocal) ||
                     borrowedLocals.Contains(sourceLocal)) &&
                    borrowedLocals.Add(refLocal))
                    changed = true;
            }
        }

        foreach (var borrowedLocal in borrowedLocals.OfType<ILocalSymbol>())
            if (IsLocalUsedAfter(borrowedLocal, writeSyntax, containingBlock, semanticModel, cancellationToken))
                return true;

        return false;
    }

    private static bool IsLocalUsedAfter(
        ILocalSymbol localSymbol,
        SyntaxNode writeSyntax,
        BlockSyntax containingBlock,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var identifierName in containingBlock.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifierName.SpanStart <= writeSyntax.SpanStart) continue;

            if (semanticModel.GetSymbolInfo(identifierName, cancellationToken).Symbol is ILocalSymbol usedLocal &&
                SymbolEqualityComparer.Default.Equals(usedLocal, localSymbol))
                return true;
        }

        return false;
    }

    private static bool HasSymbolicBorrowerFactForTerm(
        SymbolicTerm ownerTerm,
        PurityAnalysisState currentState,
        HashSet<SymbolicTerm> visitedTerms)
    {
        if (!visitedTerms.Add(ownerTerm)) return false;

        foreach (var fact in currentState.PathState.Facts)
            if (fact.Polarity &&
                fact.Confidence == SymbolicFactConfidence.Exact &&
                fact.Atom is SymbolicBorrowAtom borrow &&
                Equals(borrow.Owner, ownerTerm))
                return true;

        foreach (var aliasTerm in EnumerateSymbolicAliasTerms(ownerTerm, currentState))
            if (HasSymbolicBorrowerFactForTerm(aliasTerm, currentState, visitedTerms))
                return true;

        return false;
    }

    private static bool HasSymbolicBorrowFactForTerm(
        SymbolicTerm localTerm,
        PurityAnalysisState currentState,
        SymbolicBorrowKind? borrowKind,
        HashSet<SymbolicTerm> visitedTerms)
    {
        if (!visitedTerms.Add(localTerm)) return false;

        foreach (var fact in currentState.PathState.Facts)
        {
            if (!fact.Polarity ||
                fact.Confidence != SymbolicFactConfidence.Exact ||
                fact.Atom is not SymbolicBorrowAtom borrow ||
                !Equals(borrow.Borrow, localTerm) ||
                (borrowKind.HasValue && borrow.Kind != borrowKind.Value))
                continue;

            return true;
        }

        foreach (var aliasTerm in EnumerateSymbolicAliasTerms(localTerm, currentState))
            if (HasSymbolicBorrowFactForTerm(aliasTerm, currentState, borrowKind, visitedTerms))
                return true;

        return false;
    }

    internal static bool HasSymbolicOwnedFactForSymbol(
        ISymbol symbol,
        PurityAnalysisState currentState)
    {
        var symbolTerm = CreateSymbolicReferenceTerm(symbol, currentState);
        return HasSymbolicOwnedFactForTerm(
            symbolTerm,
            currentState,
            new HashSet<SymbolicTerm>());
    }

    private static bool HasSymbolicOwnedFactForTerm(
        SymbolicTerm symbolTerm,
        PurityAnalysisState currentState,
        HashSet<SymbolicTerm> visitedTerms)
    {
        if (!visitedTerms.Add(symbolTerm)) return false;

        foreach (var fact in currentState.PathState.Facts)
        {
            if (!fact.Polarity ||
                fact.Confidence != SymbolicFactConfidence.Exact)
                continue;

            if (fact.Atom is SymbolicOwnershipAtom { Escaped: false } ownership &&
                Equals(ownership.Value, symbolTerm))
                return true;

            if (fact.Atom is SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime &&
                Equals(lifetime.Resource, symbolTerm))
                return true;
        }

        foreach (var aliasTerm in EnumerateSymbolicAliasTerms(symbolTerm, currentState))
            if (HasSymbolicOwnedFactForTerm(aliasTerm, currentState, visitedTerms))
                return true;

        return false;
    }

    internal static IEnumerable<SymbolicTerm> EnumerateSymbolicAliasTerms(
        SymbolicTerm symbolTerm,
        PurityAnalysisState currentState)
    {
        return EnumerateExactAliasNeighbors(symbolTerm, currentState.PathState.Facts);
    }

    internal static IEnumerable<SymbolicTerm> EnumerateExactAliasNeighbors(
        SymbolicTerm term,
        IEnumerable<SymbolicFact> facts)
    {
        foreach (var fact in facts)
        {
            if (!fact.Polarity ||
                fact.Confidence != SymbolicFactConfidence.Exact ||
                fact.Atom is not SymbolicAliasAtom { MayAlias: true } alias)
                continue;

            if (Equals(alias.Target, term)) yield return alias.Source;

            if (Equals(alias.Source, term)) yield return alias.Target;
        }
    }

    internal static HashSet<SymbolicTerm> CollectExactReleasedResources(SymbolicState state)
    {
        return new HashSet<SymbolicTerm>(
            EnumerateExactResourceReleases(state).Select(static release => release.Resource));
    }

    internal static IEnumerable<(SymbolicTerm Resource, ISymbol? Symbol)> EnumerateExactResourceReleases(
        SymbolicState state)
    {
        foreach (var fact in state.Facts)
            if (PurityAnalysisStateMerger.TryGetExactResourceRelease(
                    fact,
                    out var releasedResource,
                    out var releasedSymbol))
                yield return (releasedResource, releasedSymbol);
    }

    internal static PurityAnalysisState AddAssignedValueFact(
        PurityAnalysisState currentState,
        ISymbol targetSymbol,
        IOperation? valueOperation,
        PurityAnalysisState valueState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nextState = AddAssignedAliasFact(
            currentState,
            targetSymbol,
            valueOperation,
            valueState);
        if (valueOperation?.Syntax is not ExpressionSyntax valueExpression) return nextState;
        var trackedType = SymbolicFactFactory.GetTrackedSymbolType(targetSymbol);
        if (trackedType != null &&
            (trackedType.SpecialType == SpecialType.System_Boolean ||
             SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType(trackedType)))
        {
            nextState = PurityOperationTransferAdapter.ApplyAssignment(
                nextState,
                targetSymbol,
                valueOperation,
                semanticModel,
                cancellationToken,
                valueState,
                out _);
        }

        var pathState = nextState.PathState;
        SymbolicTrackedAssignmentStateTransfer.AddFacts(
            ref pathState,
            targetSymbol,
            valueExpression,
            semanticModel,
            cancellationToken,
            currentState.GetSmtSymbolVersion,
            valueState.GetSmtSymbolVersion,
            "analyzer.assignment");
        return nextState.WithPathState(pathState);
    }

    internal static PurityAnalysisState AddAssignedAliasFact(
        PurityAnalysisState currentState,
        ISymbol targetSymbol,
        IOperation? valueOperation,
        PurityAnalysisState valueState)
    {
        var sourceSymbol = TryResolveTrackedSymbol(valueOperation, valueState);
        if (valueOperation == null ||
            sourceSymbol == null ||
            SymbolEqualityComparer.Default.Equals(sourceSymbol, targetSymbol) ||
            SymbolicFactFactory.GetTrackedSymbolType(sourceSymbol)?.IsReferenceType != true ||
            SymbolicFactFactory.GetTrackedSymbolType(targetSymbol)?.IsReferenceType != true)
            return currentState;

        var sourceTerm = CreateSymbolicReferenceTerm(sourceSymbol, valueState);
        var targetTerm = CreateSymbolicReferenceTerm(targetSymbol, currentState);
        var aliasFact = SymbolicOwnershipFactFactory.CreateAlias(
            sourceTerm,
            targetTerm,
            true,
            valueOperation.Syntax,
            "analyzer.assignment.alias",
            targetSymbol,
            "evidence.assignment.alias");

        return currentState.WithPathState(
            currentState.PathState.AddFact(aliasFact));
    }

    internal static PurityAnalysisState AddDeclaredBorrowFact(
        PurityAnalysisState currentState,
        ILocalSymbol declaredSymbol,
        IOperation initializerValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isRefInitializer = initializerValue.Syntax.Parent is RefExpressionSyntax ||
                               initializerValue.Syntax.Ancestors().OfType<RefExpressionSyntax>().Any();
        if (!isRefInitializer &&
            declaredSymbol.RefKind is not (RefKind.Ref or RefKind.Out or RefKind.In or RefKind.RefReadOnly))
            return currentState;

        var sourceSymbol = TryResolveTrackedSymbol(initializerValue, currentState) ??
                           TryResolveRefInitializerSymbol(initializerValue.Syntax, semanticModel, currentState,
                               cancellationToken);
        if (sourceSymbol == null) return currentState;

        var borrowKind = declaredSymbol.RefKind is RefKind.In or RefKind.RefReadOnly
            ? SymbolicBorrowKind.Shared
            : SymbolicBorrowKind.Mutable;
        var sourceTerm = CreateSymbolicReferenceTerm(sourceSymbol, currentState);
        var borrowTerm = CreateSymbolicReferenceTerm(declaredSymbol, currentState);
        var borrowFact = SymbolicOwnershipFactFactory.CreateBorrow(
            sourceTerm,
            borrowTerm,
            borrowKind,
            initializerValue.Syntax,
            "analyzer.declaration.borrow",
            declaredSymbol,
            "evidence.declaration.borrow");

        return currentState.WithPathState(
            currentState.PathState.AddFact(borrowFact));
    }

    private static ISymbol? TryResolveRefInitializerSymbol(
        SyntaxNode initializerSyntax,
        SemanticModel semanticModel,
        PurityAnalysisState currentState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var refExpression = initializerSyntax.AncestorsAndSelf().OfType<RefExpressionSyntax>().FirstOrDefault();
        if (refExpression == null) return null;

        if (semanticModel.GetOperation(refExpression.Expression, cancellationToken) is { } operation &&
            TryResolveTrackedSymbol(operation, currentState) is { } operationSymbol)
            return operationSymbol;

        return semanticModel.GetSymbolInfo(refExpression.Expression, cancellationToken).Symbol;
    }

}
