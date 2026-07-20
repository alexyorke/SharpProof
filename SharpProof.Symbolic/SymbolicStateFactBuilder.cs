namespace SharpProof.Symbolic;

internal static class SymbolicStateFactBuilder
{
    internal static bool TryCreateSymbolTerm(ISymbol symbol, out SymbolicTerm term)
    {
        if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is not { } type ||
            !TryGetValueKind(type, out var kind))
        {
            term = null!;
            return false;
        }

        term = new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(symbol), kind);
        return true;
    }

    internal static bool CanCompareIrTerms(SymbolicTerm left, SymbolicTerm right)
    {
        return left.Kind == right.Kind ||
               (left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference) ||
               (right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference);
    }

    internal static void AddSymbolReferenceNullCondition(
        ref SymbolicState state,
        ISymbol symbol,
        SyntaxNode source,
        bool isNull,
        string provenance)
    {
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

    internal static bool TryCreateReferenceNullCondition(
        ExpressionSyntax expression,
        bool isNull,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        out SymbolicCondition condition,
        Func<ISymbol, int>? getSymbolVersion = null)
    {
        var lowering = SymbolicSemanticPipeline.LowerReferenceTerm(
            expression,
            new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion));
        if (lowering is not { IsExact: true, Value: { } reference })
        {
            condition = null!;
            return false;
        }

        condition = CreateReferenceNullCondition(reference, expression, isNull, provenance);
        return true;
    }

    internal static SymbolicState AddReferenceNullCondition(
        SymbolicState state,
        ExpressionSyntax expression,
        bool isNull,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        Func<ISymbol, int>? getSymbolVersion = null)
    {
        if (!TryCreateReferenceNullCondition(
            expression,
            isNull,
            semanticModel,
            cancellationToken,
            provenance,
            out var condition,
            getSymbolVersion))
            return state;

        return SymbolicOperationTransferKernel.Assume(
            state, condition, true, expression.Span, provenance).State;
    }

    internal static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
    {
        return SymbolicFactFactory.TryGetValueKind(
            type,
            SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
            IsProgramPointReferenceLikeType,
            out kind);
    }

    private static bool IsProgramPointReferenceLikeType(ITypeSymbol type) =>
        SymbolicTypeFacts.IsSymbolicReferenceLikeType(type);
}
