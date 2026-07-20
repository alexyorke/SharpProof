namespace SharpProof.Symbolic.Ir;

internal static class SymbolicLoweringValueFacts
{
    internal static bool TryGetStableVariableSymbol(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out ISymbol symbol)
    {
        if (expression is IdentifierNameSyntax)
        {
            symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol!;
            return symbol is ILocalSymbol or IParameterSymbol;
        }

        symbol = null!;
        return false;
    }

    internal static bool TryGetIntegralConstant(object value, out long result)
    {
        try
        {
            switch (value)
            {
                case char charValue:
                    result = charValue;
                    return true;
                case sbyte sbyteValue:
                    result = sbyteValue;
                    return true;
                case byte byteValue:
                    result = byteValue;
                    return true;
                case short shortValue:
                    result = shortValue;
                    return true;
                case ushort ushortValue:
                    result = ushortValue;
                    return true;
                case int intValue:
                    result = intValue;
                    return true;
                case uint uintValue:
                    result = uintValue;
                    return true;
                case long longValue:
                    result = longValue;
                    return true;
                case ulong ulongValue when ulongValue <= long.MaxValue:
                    result = (long)ulongValue;
                    return true;
            }
        }
        catch (OverflowException)
        {
        }

        result = 0;
        return false;
    }

    internal static bool TryGetIntegralConstant(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out long result)
    {
        var constant = semanticModel.GetConstantValue(UnwrapExpression(expression), cancellationToken);
        if (constant is { HasValue: true, Value: not null })
            return TryGetIntegralConstant(constant.Value, out result);

        result = 0;
        return false;
    }

    internal static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression) =>
        CSharpSyntaxFacts.UnwrapExpression(expression, ExpressionCastUnwrapPolicy.NullableOnly);
}
