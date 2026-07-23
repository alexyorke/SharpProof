namespace SharpProof.Symbolic;

internal static class SymbolicLoopStateTransfer {
    internal static bool AnyConditionSymbolMutatedInStatement(
        ExpressionSyntax condition,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        SymbolicMutationInventory.Create(statement, semanticModel, cancellationToken)
            .MutatesAny(GetConditionDependencySymbols(condition, semanticModel, cancellationToken), exactTargets: true);

    internal static bool AnyConditionSymbolInvalidatedInStatement(
        ExpressionSyntax condition,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var symbols = GetConditionDependencySymbols(condition, semanticModel, cancellationToken);
        var inventory = SymbolicMutationInventory.Create(statement, semanticModel, cancellationToken);
        return symbols.Count != 0 && symbols.Any(symbol => inventory.InvalidatesSymbol(symbol, mutableExposures: true));
    }

    internal static bool ReferenceIdentityFactIsInvalidatedInStatement(
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var symbol = semanticModel.GetSymbolInfo(
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression), cancellationToken).Symbol
            ?.OriginalDefinition;
        return symbol is ILocalSymbol or IParameterSymbol
            ? SymbolicMutationInventory.Create(statement, semanticModel, cancellationToken).MutatesSymbol(symbol)
            : AnyConditionSymbolInvalidatedInStatement(expression, statement, semanticModel, cancellationToken);
    }

    internal static bool ExpressionMutatesAnySymbol(
        ExpressionSyntax expression,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        SymbolicMutationInventory.Create(expression, semanticModel, cancellationToken).MutatesAny(symbols);

    private static IReadOnlyList<ISymbol> GetConditionDependencySymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var symbols = new List<ISymbol>();
        SymbolicBranchCompletionStateTransfer.AddReferencedSymbols(root, semanticModel, cancellationToken, symbols);
        SymbolicBranchCompletionStateTransfer.AddDeclaredPatternSymbols(root, semanticModel, cancellationToken, symbols);
        SymbolicBranchCompletionStateTransfer.AddMemberNotNullWhenTargetSymbols(root, semanticModel, cancellationToken, symbols);
        return symbols;
    }
}
