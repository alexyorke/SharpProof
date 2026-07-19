namespace SharpProof.Symbolic;

internal static class SymbolMutationFacts
{
    internal static bool ContainsMutation(
        SyntaxNode root,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool includeSelf = true)
    {
        return CSharpSyntaxFacts.DescendantNodesInExecution(root, includeSelf)
            .Any(node => MutatesSymbol(node, symbol, semanticModel, cancellationToken));
    }

    internal static bool MutatesSymbol(
        SyntaxNode node,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return node switch
        {
            AssignmentExpressionSyntax assignment =>
                MutatedExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken),
            ExpressionSyntax expression
                when CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(expression, out var operand, out _) =>
                ExpressionMatchesSymbol(operand, symbol, semanticModel, cancellationToken),
            ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) =>
                ExpressionMatchesSymbol(argument.Expression, symbol, semanticModel, cancellationToken),
            _ => false
        };
    }

    internal static bool TryGetMutationTarget(SyntaxNode node, out ExpressionSyntax expression)
    {
        if (node is ExpressionSyntax mutationExpression &&
            CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(mutationExpression, out expression, out _))
            return true;

        switch (node)
        {
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

    internal static bool TryGetIncrementedOrDecrementedSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol symbol,
        out int delta)
    {
        if (!CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(expression, out var operand, out delta))
        {
            symbol = null!;
            return false;
        }

        var expressionSymbol = semanticModel.GetSymbolInfo(operand, cancellationToken).Symbol;
        if (expressionSymbol is not ILocalSymbol && expressionSymbol is not IParameterSymbol)
        {
            symbol = null!;
            delta = 0;
            return false;
        }

        symbol = expressionSymbol.OriginalDefinition;
        return true;
    }

    internal static IReadOnlyList<ISymbol> GetReferencedLocalAndParameterSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        foreach (var expression in CSharpSyntaxFacts.DescendantNodesInExecution(root).OfType<ExpressionSyntax>())
            if (TryGetLocalOrParameterSymbol(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var symbol) &&
                symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol)))
                symbols.Add(symbol);

        return symbols;
    }

    internal static bool ExpressionReferencesSymbol(
        SyntaxNode root,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return CSharpSyntaxFacts.DescendantNodesInExecution(root)
            .OfType<ExpressionSyntax>()
            .Any(expression =>
            {
                var referencedSymbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
                return referencedSymbol != null &&
                       SymbolEqualityComparer.Default.Equals(
                           referencedSymbol.OriginalDefinition,
                           symbol.OriginalDefinition);
            });
    }

    internal static bool ExpressionMatchesSymbol(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return TryGetLocalOrParameterSymbol(expression, semanticModel, cancellationToken, out var expressionSymbol) &&
               SymbolEqualityComparer.Default.Equals(expressionSymbol, symbol);
    }

    internal static bool TryGetLocalOrParameterSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol symbol)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        return SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
            expression,
            semanticModel,
            cancellationToken,
            out symbol);
    }

    private static bool MutatedExpressionMatchesSymbol(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is TupleExpressionSyntax tuple)
            return tuple.Arguments.Any(argument => MutatedExpressionMatchesSymbol(
                argument.Expression,
                symbol,
                semanticModel,
                cancellationToken));

        return ExpressionMatchesSymbol(expression, symbol, semanticModel, cancellationToken);
    }
}
