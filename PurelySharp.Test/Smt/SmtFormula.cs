namespace PurelySharp.Test.Smt
{
    internal enum SmtValueKind
    {
        Bool,
        Int,
        Reference,
    }

    internal enum SmtUnaryOperator
    {
        Not,
    }

    internal enum SmtBinaryOperator
    {
        And,
        Or,
        Equal,
        NotEqual,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
    }

    internal abstract record SmtFormula(SmtValueKind Kind);

    internal sealed record SmtBooleanConstant(bool Value) : SmtFormula(SmtValueKind.Bool);

    internal sealed record SmtIntegerConstant(long Value) : SmtFormula(SmtValueKind.Int);

    internal sealed record SmtNullConstant() : SmtFormula(SmtValueKind.Reference);

    internal sealed record SmtVariable(string Name, SmtValueKind Kind) : SmtFormula(Kind);

    internal sealed record SmtUnaryFormula(SmtUnaryOperator Operator, SmtFormula Operand) : SmtFormula(SmtValueKind.Bool);

    internal sealed record SmtBinaryFormula(SmtBinaryOperator Operator, SmtFormula Left, SmtFormula Right) : SmtFormula(SmtValueKind.Bool);
}
