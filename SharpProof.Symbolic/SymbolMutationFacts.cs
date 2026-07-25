namespace SharpProof.Symbolic;
internal static class SymbolMutationFacts {
    internal static bool TryGetMutationTarget(SyntaxNode node, out ExpressionSyntax expression) {
        if (node is ExpressionSyntax mutationExpression &&
            CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(mutationExpression, out expression, out _))
            return true;
        switch (node) {
            case AssignmentExpressionSyntax assignment:
                expression = assignment.Left;
                return true;
            case ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None):
                expression = argument.Expression;
                return true;
            default:
                expression = null!;
                return false;
        }
    }
    internal static IReadOnlyList<ISymbol> GetReferencedLocalAndParameterSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var symbols = new List<ISymbol>();
        foreach (var expression in CSharpSyntaxFacts.DescendantNodesInExecution(root).OfType<ExpressionSyntax>())
            if (TryGetLocalOrParameterSymbol(expression, semanticModel, cancellationToken, out var symbol) &&
                symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol)))
                symbols.Add(symbol);
        return symbols;
    }
    internal static IReadOnlyList<ISymbol> GetReferencedStorageSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var symbols = new List<ISymbol>();
        foreach (var expression in CSharpSyntaxFacts.DescendantNodesInExecution(root).OfType<ExpressionSyntax>()) {
            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (symbol is IMethodSymbol { AssociatedSymbol: IPropertySymbol property })
                symbol = property;
            if (symbol is not (ILocalSymbol or IParameterSymbol or IFieldSymbol or IPropertySymbol) ||
                symbols.Any(existing => SymbolEqualityComparer.Default.Equals(existing, symbol)))
                continue;
            symbols.Add(symbol);
        }
        return symbols;
    }
    internal static bool ExpressionReferencesSymbol(
        SyntaxNode root,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) => CSharpSyntaxFacts.DescendantNodesInExecution(root)
            .OfType<ExpressionSyntax>()
            .Any(expression => {
                var referencedSymbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
                return referencedSymbol != null &&
                       SymbolEqualityComparer.Default.Equals(referencedSymbol.OriginalDefinition, symbol.OriginalDefinition);
            });
    internal static bool ExpressionMatchesSymbol(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
            => TryGetLocalOrParameterSymbol(expression, semanticModel, cancellationToken, out var expressionSymbol) &&
               SymbolEqualityComparer.Default.Equals(expressionSymbol, symbol);
    internal static bool TryGetLocalOrParameterSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol symbol) {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        return SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(expression, semanticModel, cancellationToken, out symbol);
    }
}
