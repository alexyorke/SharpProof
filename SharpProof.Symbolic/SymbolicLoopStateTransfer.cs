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

    private static IReadOnlyList<ISymbol> GetConditionDependencySymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        SymbolMutationFacts.GetReferencedLocalAndParameterSymbols(root, semanticModel, cancellationToken);
}
