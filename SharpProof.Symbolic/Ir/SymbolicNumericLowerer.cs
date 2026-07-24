namespace SharpProof.Symbolic.Ir;
internal static class SymbolicNumericLowerer {
    internal static bool TryLowerIntegralMathClampInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (!IsIntegralMathInvocation(operation, 3) ||
            !TryLowerIntegralMathArgument(operation, 0, context, out var value) ||
            !TryLowerIntegralMathArgument(operation, 1, context, out var min) ||
            !TryLowerIntegralMathArgument(operation, 2, context, out var max) ||
            min is SymbolicIntegerConstantTerm minConstant &&
            max is SymbolicIntegerConstantTerm maxConstant &&
            minConstant.Value > maxConstant.Value)
            return false;
        var belowMin = SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.LessThan,
            value,
            min,
            invocation,
            "ir.known-api.math.clamp.below-min");
        var aboveMax = SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.GreaterThan,
            value,
            max,
            invocation,
            "ir.known-api.math.clamp.above-max");
        term = new SymbolicConditionalTerm(belowMin, min, new SymbolicConditionalTerm(aboveMax, max, value));
        return true;
    }
    internal static bool TryLowerIntegralMathAbsInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (!IsIntegralMathInvocation(operation, 1) ||
            !TryLowerIntegralMathArgument(operation, 0, context, out var value))
            return false;
        var nonNegative = SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.GreaterThanOrEqual,
            value,
            new SymbolicIntegerConstantTerm(0),
            invocation,
            "ir.known-api.math.abs.non-negative");
        term = new SymbolicConditionalTerm(
            nonNegative,
            value,
            new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, new SymbolicIntegerConstantTerm(0), value));
        return true;
    }
    internal static bool TryLowerIntegralMathMinMaxInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (!IsIntegralMathInvocation(operation, 2) ||
            !TryLowerIntegralMathArgument(operation, 0, context, out var left) ||
            !TryLowerIntegralMathArgument(operation, 1, context, out var right))
            return false;
        var method = operation.TargetMethod;
        var comparisonOperator = method.Name == nameof(Math.Min)
            ? SymbolicRelationOperator.LessThanOrEqual
            : SymbolicRelationOperator.GreaterThanOrEqual;
        var comparison = SymbolicIrLowerer.CreateRelationCondition(
            comparisonOperator,
            left,
            right,
            invocation,
            "ir.known-api.math." + method.Name.ToLowerInvariant());
        term = new SymbolicConditionalTerm(comparison, left, right);
        return true;
    }
    private static bool TryLowerIntegralMathArgument(
        IInvocationOperation operation,
        int parameterIndex,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        return parameterIndex >= 0 &&
               parameterIndex < operation.TargetMethod.Parameters.Length &&
               SymbolicTypeLowerer.IsIntegerSmtType(operation.TargetMethod.Parameters[parameterIndex].Type) &&
               SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, parameterIndex, out var argumentExpression) &&
               SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(argumentExpression, context), out term) &&
               term.Kind == SharpProof.ProofCore.Smt.SmtValueKind.Int;
    }
    private static bool IsIntegralMathInvocation(IInvocationOperation operation, int expectedArity) =>
        operation.TargetMethod is { IsStatic: true } method &&
        method.Parameters.Length == expectedArity &&
        SymbolicTypeLowerer.IsIntegerSmtType(method.ReturnType) &&
        method.Parameters.All(static parameter => SymbolicTypeLowerer.IsIntegerSmtType(parameter.Type));
    internal static bool TryLowerBigIntegerStaticValueMember(ISymbol? memberSymbol, out SymbolicTerm term) {
        long? value = memberSymbol is IPropertySymbol property && IsBigIntegerType(property.Type)
            ? property.Name switch { "Zero" => 0, "One" => 1, "MinusOne" => -1, _ => null }
            : null;
        term = value is { } integer ? new SymbolicIntegerConstantTerm(integer) : null!;
        return term != null;
    }
    internal static bool IsBigIntegerType(ITypeSymbol type) => SymbolicTypeFacts.IsBigIntegerType(type);
}
