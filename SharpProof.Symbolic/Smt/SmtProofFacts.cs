using SharpProof.ProofCore.Purity;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Smt;

internal static class SmtComparisonOperatorFacts
{
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

internal static class PurityProofResultFactory
{
    internal static PurityProofResult Unknown(string reason)
    {
        return new PurityProofResult(
            PurityProofOutcome.Unknown,
            new ProofCheckInfo(false, Feasibility.Unknown),
            new ProofCheckInfo(false, Feasibility.Unknown),
            reason);
    }
}
