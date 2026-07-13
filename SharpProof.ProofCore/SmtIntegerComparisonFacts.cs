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
}
