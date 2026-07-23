namespace SharpProof.Symbolic.Ir;
internal static class SymbolicLoweringValueFacts {
    internal static bool TryGetStableVariableSymbol(ExpressionSyntax expression, SymbolicLoweringContext context, out ISymbol symbol) {
        if (expression is IdentifierNameSyntax) {
            symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol!;
            return symbol is ILocalSymbol or IParameterSymbol;
        }
        symbol = null!;
        return false;
    }
    internal static bool TryGetIntegralConstant(object value, out long result) {
        if (value is not (char or sbyte or byte or short or ushort or int or uint or long or ulong)) {
            result = 0;
            return false;
        }
        try {
            result = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (OverflowException) {
            result = 0;
            return false;
        }
    }
    internal static bool TryGetIntegralConstant(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out long result) {
        var constant = semanticModel.GetConstantValue(UnwrapExpression(expression), cancellationToken);
        if (constant is { HasValue: true, Value: not null })
            return TryGetIntegralConstant(constant.Value, out result);
        result = 0;
        return false;
    }
    internal static bool TryClassifyThresholdComparison(
        SyntaxKind comparisonKind, long constant, long minimumTrue, out bool result) {
        result = comparisonKind switch {
            SyntaxKind.NotEqualsExpression when constant == minimumTrue - 1 => true,
            SyntaxKind.GreaterThanExpression when constant == minimumTrue - 1 => true,
            SyntaxKind.GreaterThanOrEqualExpression when constant == minimumTrue => true,
            _ => false
        };
        return result ||
               comparisonKind == SyntaxKind.EqualsExpression && constant == minimumTrue - 1 ||
               comparisonKind == SyntaxKind.LessThanExpression && constant == minimumTrue ||
               comparisonKind == SyntaxKind.LessThanOrEqualExpression && constant == minimumTrue - 1;
    }
    internal static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression) =>
        CSharpSyntaxFacts.UnwrapExpression(expression, ExpressionCastUnwrapPolicy.NullableOnly);
}
