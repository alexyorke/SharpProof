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
    internal static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind) => SymbolicFactFactory.TryGetValueKind(
        type, SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
        SymbolicTypeFacts.IsSymbolicReferenceLikeType, out kind);
}
