namespace SharpProof.ProofCore.Smt;

internal static class SmtIntegerComparisonFacts
{
    internal static bool TryEvaluate(
        SmtBinaryOperator op,
        long left,
        long right,
        out bool value)
    {
        switch (op)
        {
            case SmtBinaryOperator.Equal:
                value = left == right;
                return true;
            case SmtBinaryOperator.NotEqual:
                value = left != right;
                return true;
            case SmtBinaryOperator.LessThan:
                value = left < right;
                return true;
            case SmtBinaryOperator.LessThanOrEqual:
                value = left <= right;
                return true;
            case SmtBinaryOperator.GreaterThan:
                value = left > right;
                return true;
            case SmtBinaryOperator.GreaterThanOrEqual:
                value = left >= right;
                return true;
            default:
                value = false;
                return false;
        }
    }

    internal static bool TryEvaluateIntervals(
        SmtBinaryOperator op,
        long? leftLower,
        long? leftUpper,
        long? rightLower,
        long? rightUpper,
        out bool value)
    {
        value = false;
        if (!SmtComparisonOperatorFacts.IsComparison(op)) return false;

        if ((leftLower.HasValue && leftUpper.HasValue && leftLower.Value > leftUpper.Value) ||
            (rightLower.HasValue && rightUpper.HasValue && rightLower.Value > rightUpper.Value))
            return true;

        if (op is SmtBinaryOperator.GreaterThan or SmtBinaryOperator.GreaterThanOrEqual)
        {
            (leftLower, rightLower) = (rightLower, leftLower);
            (leftUpper, rightUpper) = (rightUpper, leftUpper);
            op = op == SmtBinaryOperator.GreaterThan
                ? SmtBinaryOperator.LessThan
                : SmtBinaryOperator.LessThanOrEqual;
        }

        if (op is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual)
        {
            if (leftLower.HasValue && leftUpper.HasValue && rightLower.HasValue && rightUpper.HasValue &&
                leftLower.Value == leftUpper.Value && rightLower.Value == rightUpper.Value)
            {
                var equal = leftLower.Value == rightLower.Value;
                value = op == SmtBinaryOperator.Equal ? equal : !equal;
                return true;
            }

            if ((leftUpper.HasValue && rightLower.HasValue && leftUpper.Value < rightLower.Value) ||
                (rightUpper.HasValue && leftLower.HasValue && rightUpper.Value < leftLower.Value))
            {
                value = op == SmtBinaryOperator.NotEqual;
                return true;
            }

            return false;
        }

        if (op == SmtBinaryOperator.LessThan)
        {
            if (leftUpper.HasValue && rightLower.HasValue && leftUpper.Value < rightLower.Value)
            {
                value = true;
                return true;
            }

            return leftLower.HasValue && rightUpper.HasValue && leftLower.Value >= rightUpper.Value;
        }

        if (op == SmtBinaryOperator.LessThanOrEqual)
        {
            if (leftUpper.HasValue && rightLower.HasValue && leftUpper.Value <= rightLower.Value)
            {
                value = true;
                return true;
            }

            return leftLower.HasValue && rightUpper.HasValue && leftLower.Value > rightUpper.Value;
        }

        return false;
    }
}
