using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine;

internal static class PuritySymbolicStateFacts {
    internal static SymbolicVariableTerm CreateSymbolicReferenceTerm(
        ISymbol symbol,
        PurityAnalysisState currentState) => new SymbolicVariableTerm(
            GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion),
            SmtValueKind.Reference);

    internal static bool HasSymbolicBorrowFactForLocal(
        ILocalSymbol localSymbol,
        PurityAnalysisState currentState,
        SymbolicBorrowKind? borrowKind = null) =>
        SymbolicStateMerger.ExactAliasComponentFactAny(
            CreateSymbolicReferenceTerm(localSymbol, currentState), currentState.PathState.Facts,
            (fact, term) => fact.Atom is SymbolicBorrowAtom borrow &&
                            Equals(borrow.Borrow, term) &&
                            (!borrowKind.HasValue || borrow.Kind == borrowKind.Value));

    internal static bool HasSymbolicBorrowerFactForSymbol(
        ISymbol ownerSymbol,
        PurityAnalysisState currentState) =>
        SymbolicStateMerger.ExactAliasComponentFactAny(
            CreateSymbolicReferenceTerm(ownerSymbol, currentState), currentState.PathState.Facts,
            static (fact, term) => fact.Atom is SymbolicBorrowAtom borrow && Equals(borrow.Owner, term));

    internal static bool TryCreateMutableBorrowConflictEvidence(
        IOperation operation,
        ISymbol? targetSymbol,
        PurityAnalysisState currentState,
        string ruleName,
        out PurityEvidence evidence) {
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
        out PurityEvidence evidence) {
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
                cancellationToken)) {
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
        CancellationToken cancellationToken) {
        var containingBlock = writeSyntax.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock == null) return false;

        var borrowedLocals = new HashSet<ISymbol>(SymbolEq.Default);
        var changed = true;
        while (changed) {
            changed = false;
            foreach (var declarator in containingBlock.DescendantNodes().OfType<VariableDeclaratorSyntax>()) {
                if (declarator.SpanStart >= writeSyntax.SpanStart ||
                    declarator.Initializer?.Value is not RefExpressionSyntax refExpression ||
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol refLocal ||
                    semanticModel.GetSymbolInfo(refExpression.Expression, cancellationToken).Symbol is not ILocalSymbol
                        sourceLocal)
                    continue;

                if ((SymbolEq.AreEqual(sourceLocal, targetLocal) ||
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
        CancellationToken cancellationToken) {
        foreach (var identifierName in containingBlock.DescendantNodes().OfType<IdentifierNameSyntax>()) {
            if (identifierName.SpanStart <= writeSyntax.SpanStart) continue;

            if (semanticModel.GetSymbolInfo(identifierName, cancellationToken).Symbol is ILocalSymbol usedLocal &&
                SymbolEq.AreEqual(usedLocal, localSymbol))
                return true;
        }

        return false;
    }

    internal static bool HasSymbolicOwnedFactForSymbol(
        ISymbol symbol,
        PurityAnalysisState currentState) =>
        SymbolicStateMerger.ExactAliasComponentFactAny(
            CreateSymbolicReferenceTerm(symbol, currentState), currentState.PathState.Facts,
            static (fact, term) =>
                fact.Atom is SymbolicOwnershipAtom { Escaped: false } ownership &&
                Equals(ownership.Value, term) ||
                fact.Atom is SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime &&
                Equals(lifetime.Resource, term));

    internal static bool HasSymbolicFreshMutableObjectFactForSymbol(
        ISymbol symbol,
        PurityAnalysisState currentState) =>
        SymbolicStateMerger.ExactAliasComponentFactAny(
            CreateSymbolicReferenceTerm(symbol, currentState), currentState.PathState.Facts,
            static (fact, term) =>
                fact.Atom is SymbolicFreshnessAtom freshness &&
                Equals(freshness.Value, term) &&
                fact.Provenance.StartsWith("analyzer.object.acquire.", StringComparison.Ordinal));

}
