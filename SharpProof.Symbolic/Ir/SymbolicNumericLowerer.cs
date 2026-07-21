namespace SharpProof.Symbolic.Ir;

internal static class SymbolicNumericLowerer {
    internal static bool TryLowerDefaultValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (!expression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DefaultLiteralExpression) &&
            expression is not DefaultExpressionSyntax)
            return false;

        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        if (type == null) return false;

        if (type.SpecialType == SpecialType.System_Boolean) {
            term = new SymbolicBooleanConstantTerm(false);
            return true;
        }

        if (SymbolicTypeLowerer.IsIntegerSmtType(type)) {
            term = new SymbolicIntegerConstantTerm(0);
            return true;
        }

        if (type.IsReferenceType) {
            term = new SymbolicNullTerm();
            return true;
        }

        return false;
    }

    internal static bool TryLowerIntegralMathClampInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (!TryGetIntegralMathInvocation(invocation, method, 3, context, out var operation) ||
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
        term = new SymbolicConditionalTerm(
            belowMin,
            min,
            new SymbolicConditionalTerm(aboveMax, max, value));
        return true;
    }

    internal static bool TryLowerIntegralMathAbsInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (!TryGetIntegralMathInvocation(invocation, method, 1, context, out var operation) ||
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
            new SymbolicBinaryTerm(
                SymbolicBinaryTermOperator.Subtract,
                new SymbolicIntegerConstantTerm(0),
                value));
        return true;
    }

    internal static bool TryLowerIntegralMathMinMaxInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (!TryGetIntegralMathInvocation(invocation, method, 2, context, out var operation) ||
            !TryLowerIntegralMathArgument(operation, 0, context, out var left) ||
            !TryLowerIntegralMathArgument(operation, 1, context, out var right))
            return false;

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
               SymbolicValueFacts.TryGetInvocationArgumentExpression(
                   operation,
                   parameterIndex,
                   out var argumentExpression) &&
               SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(argumentExpression, context), out term) &&
               term.Kind == SharpProof.ProofCore.Smt.SmtValueKind.Int;
    }

    private static bool TryGetIntegralMathInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        int expectedArity,
        SymbolicLoweringContext context,
        out IInvocationOperation operation) {
        if (method.IsStatic &&
            method.Parameters.Length == expectedArity &&
            SymbolicTypeLowerer.IsIntegerSmtType(method.ReturnType) &&
            method.Parameters.All(static parameter => SymbolicTypeLowerer.IsIntegerSmtType(parameter.Type)) &&
            context.SemanticModel.GetOperation(invocation, context.CancellationToken) is
                IInvocationOperation invocationOperation) {
            operation = invocationOperation;
            return true;
        }

        operation = null!;
        return false;
    }

    internal static bool TryLowerBigIntegerStaticValueMember(ISymbol? memberSymbol, out SymbolicTerm term) {
        if (memberSymbol is IPropertySymbol property &&
            IsBigIntegerType(property.Type)) {
            if (string.Equals(property.Name, "Zero", StringComparison.Ordinal)) {
                term = new SymbolicIntegerConstantTerm(0);
                return true;
            }

            if (string.Equals(property.Name, "One", StringComparison.Ordinal)) {
                term = new SymbolicIntegerConstantTerm(1);
                return true;
            }

            if (string.Equals(property.Name, "MinusOne", StringComparison.Ordinal)) {
                term = new SymbolicIntegerConstantTerm(-1);
                return true;
            }
        }

        term = null!;
        return false;
    }

    internal static bool IsBigIntegerType(ITypeSymbol type) => SymbolicTypeFacts.IsBigIntegerType(type);
}
