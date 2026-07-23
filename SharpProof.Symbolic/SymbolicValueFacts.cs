namespace SharpProof.Symbolic;
internal static class SymbolicValueFacts {
    internal static bool IsIntegralOrDecimalZero(object? value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal &&
        Convert.ToDecimal(value, CultureInfo.InvariantCulture) == 0m;
    public static bool TryGetInvocationArgumentExpression(
        IInvocationOperation invocationOperation,
        int parameterIndex,
        out ExpressionSyntax expression) {
        expression = null!;
        if (parameterIndex < 0 ||
            parameterIndex >= invocationOperation.TargetMethod.Parameters.Length)
            return false;
        var parameter = invocationOperation.TargetMethod.Parameters[parameterIndex];
        foreach (var argument in invocationOperation.Arguments)
            if (SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter) &&
                argument.Value.Syntax is ExpressionSyntax argumentExpression) {
                expression = argumentExpression;
                return true;
            }
        if (parameterIndex < invocationOperation.Arguments.Length &&
            invocationOperation.Arguments[parameterIndex].Value.Syntax is ExpressionSyntax fallbackExpression) {
            expression = fallbackExpression;
            return true;
        }
        return false;
    }
    public static bool TryGetInvocationArgumentExpressionByOrdinal(
        IInvocationOperation invocationOperation,
        int parameterIndex,
        out ExpressionSyntax expression) {
        foreach (var argument in invocationOperation.Arguments)
            if (argument.Parameter?.Ordinal == parameterIndex &&
                argument.Value.Syntax is ExpressionSyntax argumentExpression) {
                expression = argumentExpression;
                return true;
            }
        expression = null!;
        return false;
    }
    internal static bool TryGetInvocationArgumentExpressionsByOrdinal(
        IInvocationOperation invocationOperation,
        int count,
        out ImmutableArray<ExpressionSyntax> expressions) {
        if (count < 0) {
            expressions = default;
            return false;
        }
        var builder = ImmutableArray.CreateBuilder<ExpressionSyntax>(count);
        for (var ordinal = 0; ordinal < count; ordinal++) {
            if (!TryGetInvocationArgumentExpressionByOrdinal(invocationOperation, ordinal, out var expression)) {
                expressions = default;
                return false;
            }
            builder.Add(expression);
        }
        expressions = builder.MoveToImmutable();
        return true;
    }
}
