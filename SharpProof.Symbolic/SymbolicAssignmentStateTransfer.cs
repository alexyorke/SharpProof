namespace SharpProof.Symbolic;
internal readonly record struct SymbolicThrowGuardedValue(
    bool HasGuard,
    ExpressionSyntax EffectiveValueExpression,
    ExpressionSyntax? GuardExpression,
    bool GuardBranchWhenTrue,
    bool RequiresNonNullValue);
internal static class SymbolicAssignmentStateTransfer {
    internal static SymbolicThrowGuardedValue GetThrowGuardedValue(ExpressionSyntax valueExpression) {
        var originalValueExpression = valueExpression;
        valueExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(originalValueExpression);
        if (valueExpression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(coalesceExpression.Right) is ThrowExpressionSyntax)
            return new SymbolicThrowGuardedValue(true, coalesceExpression.Left, null, true, true);
        if (valueExpression is ConditionalExpressionSyntax conditionalExpression) {
            if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(conditionalExpression.WhenFalse) is ThrowExpressionSyntax)
                return new SymbolicThrowGuardedValue(true, conditionalExpression.WhenTrue, conditionalExpression.Condition, true, false);
            if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(conditionalExpression.WhenTrue) is ThrowExpressionSyntax)
                return new SymbolicThrowGuardedValue(true, conditionalExpression.WhenFalse, conditionalExpression.Condition, false, false);
        }
        return new SymbolicThrowGuardedValue(false, originalValueExpression, null, true, false);
    }
    internal static bool ExpressionReferencesAnySymbol(
        SyntaxNode root,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        foreach (var symbol in symbols)
            if (SymbolMutationFacts.ExpressionReferencesSymbol(root, symbol, semanticModel, cancellationToken))
                return true;
        return false;
    }
}
