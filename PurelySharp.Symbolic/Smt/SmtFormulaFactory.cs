using SearchLib.Smt;

namespace PurelySharp.Symbolic.Smt
{
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

        internal static SmtFormula CreateFalse()
        {
            return new SmtBooleanConstant(false);
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
    }
}
