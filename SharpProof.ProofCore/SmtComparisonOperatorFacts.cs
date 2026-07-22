namespace SharpProof.ProofCore.Smt;

internal static class SmtComparisonOperatorFacts {
    internal static bool TryExtract(SmtFormula formula, out SmtBinaryFormula comparison, out int negationCount) {
        negationCount = 0;
        while (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated) {
            negationCount++;
            formula = negated.Operand;
        }
        if (formula is SmtBinaryFormula binary && IsComparison(binary.Operator)) {
            comparison = binary;
            return true;
        }
        comparison = null!;
        return false;
    }
    internal static SmtBinaryOperator ApplyNegations(SmtBinaryOperator op, int negationCount)
        => (negationCount & 1) == 0 ? op : Negate(op);

    internal static bool TryGetIntegerComparison(SmtFormula formula, out SmtFormula term, out SmtBinaryOperator op, out long constant) {
        term = null!;
        op = default;
        constant = default;
        if (!TryExtract(formula, out var binary, out var negationCount) || negationCount > 1)
            return false;

        var effectiveOperator = ApplyNegations(binary.Operator, negationCount);
        if (binary.Left.Kind == SmtValueKind.Int && binary.Right is SmtIntegerConstant right) {
            term = binary.Left;
            op = effectiveOperator;
            constant = right.Value;
            return true;
        }
        if (binary.Left is not SmtIntegerConstant left || binary.Right.Kind != SmtValueKind.Int)
            return false;

        term = binary.Right;
        op = Reverse(effectiveOperator);
        constant = left.Value;
        return true;
    }
    internal static bool AreComplements(SmtFormula left, SmtFormula right) {
        if (left is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } leftNot && leftNot.Operand.Equals(right) ||
            right is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } rightNot && rightNot.Operand.Equals(left))
            return true;

        if (left is not SmtBinaryFormula leftBinary || right is not SmtBinaryFormula rightBinary)
            return false;
        if (!IsComparison(leftBinary.Operator) || !IsComparison(rightBinary.Operator)) return false;

        var sameOperands = leftBinary.Left.Equals(rightBinary.Left) && leftBinary.Right.Equals(rightBinary.Right) ||
                           IsSymmetric(leftBinary.Operator) && IsSymmetric(rightBinary.Operator) &&
                           leftBinary.Left.Equals(rightBinary.Right) && leftBinary.Right.Equals(rightBinary.Left);
        return sameOperands && Negate(leftBinary.Operator) == rightBinary.Operator;
    }
    private static bool IsSymmetric(SmtBinaryOperator op) =>
        op is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual;

    internal static bool IsComparison(SmtBinaryOperator op) => op is SmtBinaryOperator.Equal or
            SmtBinaryOperator.NotEqual or
            SmtBinaryOperator.LessThan or
            SmtBinaryOperator.LessThanOrEqual or
            SmtBinaryOperator.GreaterThan or
            SmtBinaryOperator.GreaterThanOrEqual;
    internal static SmtBinaryOperator Reverse(SmtBinaryOperator op) => op switch {
        SmtBinaryOperator.LessThan => SmtBinaryOperator.GreaterThan,
        SmtBinaryOperator.LessThanOrEqual => SmtBinaryOperator.GreaterThanOrEqual,
        SmtBinaryOperator.GreaterThan => SmtBinaryOperator.LessThan,
        SmtBinaryOperator.GreaterThanOrEqual => SmtBinaryOperator.LessThanOrEqual,
        _ => op
    };
    internal static SmtBinaryOperator Negate(SmtBinaryOperator op) => op switch {
        SmtBinaryOperator.Equal => SmtBinaryOperator.NotEqual,
        SmtBinaryOperator.NotEqual => SmtBinaryOperator.Equal,
        SmtBinaryOperator.LessThan => SmtBinaryOperator.GreaterThanOrEqual,
        SmtBinaryOperator.LessThanOrEqual => SmtBinaryOperator.GreaterThan,
        SmtBinaryOperator.GreaterThan => SmtBinaryOperator.LessThanOrEqual,
        SmtBinaryOperator.GreaterThanOrEqual => SmtBinaryOperator.LessThan,
        _ => op
    };
}
