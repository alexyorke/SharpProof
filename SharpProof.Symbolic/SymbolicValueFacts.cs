namespace SharpProof.Symbolic;
internal static class SymbolicValueFacts {
    internal static bool IsIntegralOrDecimalZero(object? value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal &&
        Convert.ToDecimal(value, CultureInfo.InvariantCulture) == 0m;
    public static bool TryGetInvocationArgumentExpression(
        IInvocationOperation invocationOperation,
        int parameterIndex,
        out ExpressionSyntax expression) =>
        TryGetArgumentExpression(invocationOperation.Arguments, parameterIndex, out expression);
    internal static bool TryGetObjectCreationArgumentExpression(
        IObjectCreationOperation objectCreationOperation,
        int parameterIndex,
        out ExpressionSyntax expression) {
        expression = null!;
        return objectCreationOperation.Constructor != null &&
               TryGetArgumentExpression(objectCreationOperation.Arguments, parameterIndex, out expression);
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
            if (!TryGetInvocationArgumentExpression(invocationOperation, ordinal, out var expression)) {
                expressions = default;
                return false;
            }
            builder.Add(expression);
        }
        expressions = builder.MoveToImmutable();
        return true;
    }
    private static bool TryGetArgumentExpression(
        ImmutableArray<IArgumentOperation> arguments,
        int parameterIndex,
        out ExpressionSyntax expression) {
        foreach (var argument in arguments)
            if (argument.Parameter?.Ordinal == parameterIndex &&
                argument.Value.Syntax is ExpressionSyntax argumentExpression) {
                expression = argumentExpression;
                return true;
            }
        expression = null!;
        return false;
    }
}
