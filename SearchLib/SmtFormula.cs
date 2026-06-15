namespace SearchLib.Smt
{
    public enum SmtValueKind
    {
        Bool,
        Int,
        Reference,
    }

    public enum SmtUnaryOperator
    {
        Not,
    }

    public enum SmtBinaryOperator
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

    public abstract record SmtFormula(SmtValueKind Kind);

    public sealed record SmtBooleanConstant(bool Value) : SmtFormula(SmtValueKind.Bool);

    public sealed record SmtIntegerConstant(long Value) : SmtFormula(SmtValueKind.Int);

    public sealed record SmtNullConstant() : SmtFormula(SmtValueKind.Reference);

    public sealed record SmtVariable(string Name, SmtValueKind Kind) : SmtFormula(Kind);

    public sealed record SmtUnaryFormula(SmtUnaryOperator Operator, SmtFormula Operand) : SmtFormula(SmtValueKind.Bool);

    public sealed record SmtBinaryFormula(SmtBinaryOperator Operator, SmtFormula Left, SmtFormula Right) : SmtFormula(SmtValueKind.Bool);
}
