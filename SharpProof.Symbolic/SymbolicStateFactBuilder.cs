namespace SharpProof.Symbolic;
internal static class SymbolicStateFactBuilder {
    internal static bool TryCreateSymbolTerm(ISymbol symbol, out SymbolicTerm term) {
        if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is not { } type ||
            !TryGetValueKind(type, out var kind)) {
            term = null!;
            return false;
        }
        term = new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(symbol), kind);
        return true;
    }
    internal static bool CanCompareIrTerms(SymbolicTerm left, SymbolicTerm right) => left.Kind == right.Kind ||
               (left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference) ||
               (right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference);
    internal static void AddSymbolReferenceNullCondition(
        ref SymbolicState state,
        ISymbol symbol,
        SyntaxNode source,
        bool isNull,
        string provenance) {
        if (!TryCreateSymbolTerm(symbol, out var term) || term.Kind != SmtValueKind.Reference)
            return;
        state = SymbolicOperationTransferKernel.Assume(
            state, CreateReferenceNullCondition(term, source, isNull, provenance), true, source.Span, provenance).State;
    }
    internal static SymbolicCondition CreateReferenceNullCondition(SymbolicTerm reference, SyntaxNode source,
        bool isNull, string provenance, string? evidenceKey = null) =>
        new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(isNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                reference, new SymbolicNullTerm()),
            source, provenance, evidenceKey: evidenceKey));
    internal static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind) => SymbolicFactFactory.TryGetValueKind(
            type,
            SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
            IsProgramPointReferenceLikeType,
            out kind);
    private static bool IsProgramPointReferenceLikeType(ITypeSymbol type) =>
        SymbolicTypeFacts.IsSymbolicReferenceLikeType(type);
}
