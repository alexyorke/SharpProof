namespace SharpProof.Symbolic;

internal static class SymbolCurrentValueResolver {
    internal static bool TryResolveCurrentSimpleValueExpression(
        ExpressionSyntax expression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax valueExpression) {
        valueExpression = null!;
        if (!SymbolMutationFacts.TryGetLocalOrParameterSymbol(expression, semanticModel, cancellationToken, out var symbol))
            return false;

        return TryResolveCurrentSimpleValueExpression(symbol, useNode, semanticModel, cancellationToken, out valueExpression);
    }
    internal static bool TryResolveCurrentSimpleValueExpression(
        ISymbol symbol,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax valueExpression) {
        valueExpression = null!;
        if (IsMutatedAfterUseInContainingLoop(symbol, useNode, semanticModel, cancellationToken))
            return false;

        ExpressionSyntax? currentValue = null;
        foreach (var (block, containingStatement) in CSharpSyntaxFacts
                     .EnumerateContainingBlocks(useNode, stopAtExecutionRoot: true)
                     .Reverse())
            foreach (var statement in block.Statements) {
                if (ReferenceEquals(statement, containingStatement)) break;
                var mutations = SymbolicMutationInventory.Create(statement, semanticModel, cancellationToken);

                if (statement is LocalDeclarationStatementSyntax localDeclaration) {
                    foreach (var declarator in localDeclaration.Declaration.Variables)
                        if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                            SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol))
                            currentValue = declarator.Initializer?.Value;

                    if (mutations.MutatesSymbol(symbol))
                        currentValue = null;

                    continue;
                }
                if (statement is ExpressionStatementSyntax {
                    Expression: AssignmentExpressionSyntax assignment
                } &&
                    SymbolMutationFacts.ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken)) {
                    if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                        SymbolMutationFacts.ExpressionReferencesSymbol(assignment.Right, symbol, semanticModel, cancellationToken)) {
                        currentValue = null;
                        continue;
                    }
                    currentValue = assignment.Right;
                    continue;
                }
                if (mutations.MutatesSymbol(symbol))
                    currentValue = null;
            }
        if (currentValue == null) return false;

        valueExpression = currentValue;
        return true;
    }
    private static bool IsMutatedAfterUseInContainingLoop(
        ISymbol symbol,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var loopBody = CSharpSyntaxFacts.GetContainingLoopBody(useNode);
        if (loopBody == null) return false;

        return SymbolicMutationInventory.Create(loopBody, semanticModel, cancellationToken)
            .MutatesBetween(useNode.SpanStart, loopBody.Span.End, symbol);
    }
}
