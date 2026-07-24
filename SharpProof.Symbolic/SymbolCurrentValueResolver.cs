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
        out ExpressionSyntax valueExpression) =>
        TryResolveCurrentSimpleValueExpression(
            symbol,
            useNode,
            semanticModel,
            cancellationToken,
            false,
            out valueExpression);
    internal static bool TryResolveCurrentSimpleValueExpression(
        ISymbol symbol,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool allowSelfReferentialAssignments,
        out ExpressionSyntax valueExpression) =>
        TryResolveCurrentSimpleValueExpression(
            symbol,
            useNode,
            semanticModel,
            cancellationToken,
            allowSelfReferentialAssignments,
            false,
            out valueExpression);
    internal static bool TryResolveCurrentSimpleValueExpression(
        ISymbol symbol,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool allowSelfReferentialAssignments,
        bool rejectMutableExposures,
        out ExpressionSyntax valueExpression) {
        valueExpression = null!;
        if (IsInvalidatedAfterUseInContainingLoop(
                symbol,
                useNode,
                semanticModel,
                cancellationToken,
                rejectMutableExposures))
            return false;
        ExpressionSyntax? currentValue = null;
        foreach (var (block, containingStatement) in CSharpSyntaxFacts
                     .EnumerateContainingBlocks(useNode, stopAtExecutionRoot: true)
                     .Reverse())
            foreach (var statement in block.Statements) {
                if (ReferenceEquals(statement, containingStatement)) break;
                var mutations = SymbolicMutationInventory.Create(statement, semanticModel, cancellationToken);
                if (statement is LocalDeclarationStatementSyntax localDeclaration) {
                    var declaredHere = false;
                    foreach (var declarator in localDeclaration.Declaration.Variables)
                        if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                            SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol)) {
                            currentValue = declarator.Initializer?.Value;
                            declaredHere = true;
                        }
                    if (!declaredHere && InvalidatesCurrentValue(
                            mutations,
                            symbol,
                            rejectMutableExposures))
                        currentValue = null;
                    continue;
                }
                if (statement is ExpressionStatementSyntax {
                    Expression: AssignmentExpressionSyntax assignment
                } &&
                    SymbolMutationFacts.ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken)) {
                    if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                        !allowSelfReferentialAssignments &&
                        SymbolMutationFacts.ExpressionReferencesSymbol(
                            assignment.Right,
                            symbol,
                            semanticModel,
                            cancellationToken)) {
                        currentValue = null;
                        continue;
                    }
                    currentValue = assignment.Right;
                    continue;
                }
                if (InvalidatesCurrentValue(mutations, symbol, rejectMutableExposures))
                    currentValue = null;
            }
        if (currentValue == null) return false;
        valueExpression = currentValue;
        return true;
    }
    private static bool InvalidatesCurrentValue(
        SymbolicMutationInventory mutations,
        ISymbol symbol,
        bool rejectMutableExposures) =>
        rejectMutableExposures
            ? mutations.InvalidatesSymbol(symbol, mutableExposures: true)
            : mutations.MutatesSymbol(symbol);
    private static bool IsInvalidatedAfterUseInContainingLoop(
        ISymbol symbol,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool rejectMutableExposures) {
        var loopBody = CSharpSyntaxFacts.GetContainingLoopBody(useNode);
        if (loopBody == null) return false;
        var mutations = SymbolicMutationInventory.Create(loopBody, semanticModel, cancellationToken);
        return rejectMutableExposures
            ? mutations.InvalidatesBetween(
                useNode.SpanStart,
                loopBody.Span.End,
                symbol,
                mutableExposures: true)
            : mutations.MutatesBetween(useNode.SpanStart, loopBody.Span.End, symbol);
    }
}
