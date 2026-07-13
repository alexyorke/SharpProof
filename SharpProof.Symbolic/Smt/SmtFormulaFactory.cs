using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Smt;

internal static class SmtFormulaFactory
{
    internal static SmtFormula CreateVariable(string name, SmtValueKind kind)
    {
        return new SmtVariable(name, kind);
    }

    internal static SmtFormula CreateReferenceVariable(string name)
    {
        return CreateVariable(name, SmtValueKind.Reference);
    }

    internal static SmtFormula CreateIntVariable(string name)
    {
        return CreateVariable(name, SmtValueKind.Int);
    }

    internal static SmtFormula CreateBoolVariable(string name)
    {
        return CreateVariable(name, SmtValueKind.Bool);
    }

    internal static SmtFormula CreateNot(SmtFormula formula)
    {
        return new SmtUnaryFormula(SmtUnaryOperator.Not, formula);
    }

    internal static SmtFormula CreateEquality(SmtFormula left, SmtFormula right)
    {
        return new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right);
    }

    internal static SmtFormula CreateIntegerBinaryTerm(
        SmtIntegerBinaryOperator op,
        SmtFormula left,
        SmtFormula right)
    {
        return new SmtIntegerBinaryTerm(op, left, right);
    }

    internal static SmtFormula CreateIntegerUnaryTerm(SmtIntegerUnaryOperator op, SmtFormula operand)
    {
        return new SmtIntegerUnaryTerm(op, operand);
    }

    internal static SmtFormula CreateReferenceNullComparison(SmtFormula value, bool isNull)
    {
        return new SmtBinaryFormula(
            isNull ? SmtBinaryOperator.Equal : SmtBinaryOperator.NotEqual,
            value,
            new SmtNullConstant());
    }

    internal static SmtFormula CreateIntegerComparison(SmtBinaryOperator comparison, SmtFormula value, long constant)
    {
        return new SmtBinaryFormula(comparison, value, new SmtIntegerConstant(constant));
    }

    internal static SmtFormula CreateIntegerInRange(SmtFormula value, long minValue, long maxValue)
    {
        return new SmtBinaryFormula(
            SmtBinaryOperator.And,
            CreateIntegerComparison(SmtBinaryOperator.GreaterThanOrEqual, value, minValue),
            CreateIntegerComparison(SmtBinaryOperator.LessThanOrEqual, value, maxValue));
    }

    internal static SmtFormula CreateIntegerLessThanZero(SmtFormula value)
    {
        return CreateIntegerComparison(SmtBinaryOperator.LessThan, value, 0);
    }

    internal static SmtFormula CreateIntegerGreaterThanOrEqualZero(SmtFormula value)
    {
        return CreateIntegerComparison(SmtBinaryOperator.GreaterThanOrEqual, value, 0);
    }

    internal static SmtFormula CreateIntegerEqualsZero(SmtFormula value)
    {
        return CreateIntegerComparison(SmtBinaryOperator.Equal, value, 0);
    }

    internal static SmtFormula CreateIntegerOne()
    {
        return new SmtIntegerConstant(1);
    }

    internal static SmtFormula CreateSubsequenceInRangeFormula(
        SmtFormula sourceLength,
        SmtFormula start,
        SmtFormula? count,
        bool oneArgumentUpperBoundIsInclusive)
    {
        var startNonNegative = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            start,
            new SmtIntegerConstant(0));

        if (count == null)
        {
            var upperBound = new SmtBinaryFormula(
                oneArgumentUpperBoundIsInclusive
                    ? SmtBinaryOperator.LessThanOrEqual
                    : SmtBinaryOperator.LessThan,
                start,
                sourceLength);
            return new SmtBinaryFormula(SmtBinaryOperator.And, startNonNegative, upperBound);
        }

        var countNonNegative = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            count,
            new SmtIntegerConstant(0));
        var startWithinLength = new SmtBinaryFormula(
            SmtBinaryOperator.LessThanOrEqual,
            start,
            sourceLength);
        var remainingLength = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Subtract,
            sourceLength,
            start);
        var countWithinRemainingLength = new SmtBinaryFormula(
            SmtBinaryOperator.LessThanOrEqual,
            count,
            remainingLength);
        SmtFormula additionDoesNotOverflow = count is SmtIntegerConstant { Value: 0 }
            ? new SmtBooleanConstant(true)
            : new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                start,
                new SmtIntegerBinaryTerm(
                    SmtIntegerBinaryOperator.Subtract,
                    new SmtIntegerConstant(int.MaxValue),
                    count));
        return new SmtBinaryFormula(
            SmtBinaryOperator.And,
            startNonNegative,
            new SmtBinaryFormula(
                SmtBinaryOperator.And,
                countNonNegative,
                new SmtBinaryFormula(
                    SmtBinaryOperator.And,
                    startWithinLength,
                    new SmtBinaryFormula(
                        SmtBinaryOperator.And,
                        countWithinRemainingLength,
                        additionDoesNotOverflow))));
    }
}