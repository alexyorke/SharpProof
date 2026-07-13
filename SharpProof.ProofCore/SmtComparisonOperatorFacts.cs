namespace SharpProof.ProofCore.Smt;

internal static class SmtComparisonOperatorFacts
{
    internal static bool IsComparison(SmtBinaryOperator op)
    {
        return op is SmtBinaryOperator.Equal or
            SmtBinaryOperator.NotEqual or
            SmtBinaryOperator.LessThan or
            SmtBinaryOperator.LessThanOrEqual or
            SmtBinaryOperator.GreaterThan or
            SmtBinaryOperator.GreaterThanOrEqual;
    }

    internal static SmtBinaryOperator Reverse(SmtBinaryOperator op)
    {
        return op switch
        {
            SmtBinaryOperator.LessThan => SmtBinaryOperator.GreaterThan,
            SmtBinaryOperator.LessThanOrEqual => SmtBinaryOperator.GreaterThanOrEqual,
            SmtBinaryOperator.GreaterThan => SmtBinaryOperator.LessThan,
            SmtBinaryOperator.GreaterThanOrEqual => SmtBinaryOperator.LessThanOrEqual,
            _ => op
        };
    }

    internal static SmtBinaryOperator Negate(SmtBinaryOperator op)
    {
        return op switch
        {
            SmtBinaryOperator.Equal => SmtBinaryOperator.NotEqual,
            SmtBinaryOperator.NotEqual => SmtBinaryOperator.Equal,
            SmtBinaryOperator.LessThan => SmtBinaryOperator.GreaterThanOrEqual,
            SmtBinaryOperator.LessThanOrEqual => SmtBinaryOperator.GreaterThan,
            SmtBinaryOperator.GreaterThan => SmtBinaryOperator.LessThanOrEqual,
            SmtBinaryOperator.GreaterThanOrEqual => SmtBinaryOperator.LessThan,
            _ => op
        };
    }
}
