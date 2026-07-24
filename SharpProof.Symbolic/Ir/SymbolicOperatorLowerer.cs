namespace SharpProof.Symbolic.Ir;
internal static class SymbolicOperatorLowerer {
    internal static bool TryLowerBuiltInBooleanBitwiseCondition(
        BinaryExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (expression.Kind() is not (SyntaxKind.BitwiseAndExpression or
            SyntaxKind.BitwiseOrExpression or
            SyntaxKind.ExclusiveOrExpression) ||
            context.SemanticModel.GetOperation(expression, context.CancellationToken) is not
                Microsoft.CodeAnalysis.Operations.IBinaryOperation {
                    OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_Boolean
                } ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(expression.Left, context), out var left) ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(expression.Right, context), out var right))
            return false;
        condition = expression.Kind() switch {
            SyntaxKind.BitwiseAndExpression =>
                new SymbolicBinaryCondition(SymbolicConditionOperator.And, left, right),
            SyntaxKind.BitwiseOrExpression =>
                new SymbolicBinaryCondition(SymbolicConditionOperator.Or, left, right),
            _ => new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                new SymbolicBinaryCondition(SymbolicConditionOperator.And, left, new SymbolicNotCondition(right)),
                new SymbolicBinaryCondition(SymbolicConditionOperator.And, new SymbolicNotCondition(left), right))
        };
        return true;
    }
    internal static bool CanCompareTerms(SymbolicTerm left, SymbolicTerm right, SymbolicRelationOperator op) {
        if (op is not SymbolicRelationOperator.Equal and not SymbolicRelationOperator.NotEqual &&
            left.Kind != SmtValueKind.Int)
            return false;
        return left.Kind == right.Kind ||
               (left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference) ||
               (right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference);
    }
    internal static bool HasBuiltInNullSemantics(Microsoft.CodeAnalysis.Operations.IBinaryOperation operation) {
        if (operation.OperatorMethod == null) return true;
        if (operation.OperatorMethod.ContainingType is not { IsRecord: true })
            return false;
        return IsNullConstant(operation.LeftOperand) || IsNullConstant(operation.RightOperand);
    }
    private static bool IsNullConstant(IOperation operation) =>
        operation.ConstantValue is { HasValue: true, Value: null };
    internal static bool IsEqualityExpression(BinaryExpressionSyntax binaryExpression)
        => binaryExpression.IsKind(SyntaxKind.EqualsExpression) ||
               binaryExpression.IsKind(SyntaxKind.NotEqualsExpression);
    internal static bool TryGetRelationOperator(SyntaxKind kind, out SymbolicRelationOperator op) =>
        TryMap(kind switch {
            SyntaxKind.EqualsExpression => SymbolicRelationOperator.Equal,
            SyntaxKind.NotEqualsExpression => SymbolicRelationOperator.NotEqual,
            SyntaxKind.LessThanExpression => SymbolicRelationOperator.LessThan,
            SyntaxKind.LessThanOrEqualExpression => SymbolicRelationOperator.LessThanOrEqual,
            SyntaxKind.GreaterThanExpression => SymbolicRelationOperator.GreaterThan,
            SyntaxKind.GreaterThanOrEqualExpression => SymbolicRelationOperator.GreaterThanOrEqual,
            _ => (SymbolicRelationOperator?)null
        }, out op);
    internal static bool TryGetRelationalPatternOperator(
        SyntaxKind tokenKind, bool negate, out SymbolicRelationOperator op) =>
        TryMap(tokenKind switch {
            SyntaxKind.GreaterThanToken => negate
                ? SymbolicRelationOperator.LessThanOrEqual
                : SymbolicRelationOperator.GreaterThan,
            SyntaxKind.GreaterThanEqualsToken => negate
                ? SymbolicRelationOperator.LessThan
                : SymbolicRelationOperator.GreaterThanOrEqual,
            SyntaxKind.LessThanToken => negate
                ? SymbolicRelationOperator.GreaterThanOrEqual
                : SymbolicRelationOperator.LessThan,
            SyntaxKind.LessThanEqualsToken => negate
                ? SymbolicRelationOperator.GreaterThan
                : SymbolicRelationOperator.LessThanOrEqual,
            _ => (SymbolicRelationOperator?)null
        }, out op);
    internal static bool TryGetBinaryTermOperator(SyntaxKind kind, out SymbolicBinaryTermOperator op) =>
        TryMap(kind switch {
            SyntaxKind.AddExpression => SymbolicBinaryTermOperator.Add,
            SyntaxKind.SubtractExpression => SymbolicBinaryTermOperator.Subtract,
            SyntaxKind.MultiplyExpression => SymbolicBinaryTermOperator.Multiply,
            SyntaxKind.DivideExpression => SymbolicBinaryTermOperator.Divide,
            SyntaxKind.ModuloExpression => SymbolicBinaryTermOperator.Remainder,
            _ => (SymbolicBinaryTermOperator?)null
        }, out op);
    internal static bool TryGetBinaryTermOperator(
        SmtIntegerBinaryOperator smtOperator, out SymbolicBinaryTermOperator op) =>
        TryAlignedOperator((int)smtOperator, out op);
    internal static SmtIntegerBinaryOperator GetSmtIntegerBinaryOperator(SymbolicBinaryTermOperator op) =>
        op is >= SymbolicBinaryTermOperator.Add and <= SymbolicBinaryTermOperator.Remainder
            ? (SmtIntegerBinaryOperator)(int)op
            : throw new ArgumentOutOfRangeException(nameof(op), op, null);
    private static bool TryAlignedOperator(int value, out SymbolicBinaryTermOperator op) {
        op = (SymbolicBinaryTermOperator)value;
        return op is >= SymbolicBinaryTermOperator.Add and <= SymbolicBinaryTermOperator.Remainder;
    }
    private static bool TryMap<T>(T? candidate, out T value) where T : struct {
        value = candidate.GetValueOrDefault();
        return candidate.HasValue;
    }
}
