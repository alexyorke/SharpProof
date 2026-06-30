using System.Text.RegularExpressions;

namespace SearchLib.Smt
{
    public enum SmtValueKind
    {
        Bool,
        Int,
        Reference,
        String,
    }

    public enum SmtUnaryOperator
    {
        Not,
    }

    public enum SmtIntegerUnaryOperator
    {
        Negate,
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

    public enum SmtIntegerBinaryOperator
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        Remainder,
    }

    public abstract record SmtFormula(SmtValueKind Kind);

    public sealed record SmtBooleanConstant(bool Value) : SmtFormula(SmtValueKind.Bool);

    public sealed record SmtIntegerConstant(long Value) : SmtFormula(SmtValueKind.Int);

    public sealed record SmtStringConstant(string Value) : SmtFormula(SmtValueKind.String);

    public sealed record SmtNullConstant() : SmtFormula(SmtValueKind.Reference);

    public sealed record SmtVariable(string Name, SmtValueKind Kind) : SmtFormula(Kind);

    public sealed record SmtUnaryFormula(SmtUnaryOperator Operator, SmtFormula Operand) : SmtFormula(SmtValueKind.Bool);

    public sealed record SmtBinaryFormula(SmtBinaryOperator Operator, SmtFormula Left, SmtFormula Right) : SmtFormula(SmtValueKind.Bool);

    public sealed record SmtIntegerUnaryTerm(SmtIntegerUnaryOperator Operator, SmtFormula Operand) : SmtFormula(SmtValueKind.Int);

    public sealed record SmtIntegerBinaryTerm(SmtIntegerBinaryOperator Operator, SmtFormula Left, SmtFormula Right) : SmtFormula(SmtValueKind.Int);

    public sealed record SmtStringLengthTerm(SmtFormula Value) : SmtFormula(SmtValueKind.Int);

    public sealed record SmtStringConcatTerm(SmtFormula Left, SmtFormula Right) : SmtFormula(SmtValueKind.String);

    public sealed record SmtStringContainsFormula(SmtFormula Value, SmtFormula Search) : SmtFormula(SmtValueKind.Bool);

    public sealed record SmtStringStartsWithFormula(SmtFormula Value, SmtFormula Prefix) : SmtFormula(SmtValueKind.Bool);

    public sealed record SmtStringEndsWithFormula(SmtFormula Value, SmtFormula Suffix) : SmtFormula(SmtValueKind.Bool);

    public sealed record SmtRegexMatchFormula(
        SmtFormula Value,
        string Pattern,
        RegexOptions Options = RegexOptions.None) : SmtFormula(SmtValueKind.Bool);

    public sealed record SmtConditionalFormula(SmtFormula Condition, SmtFormula WhenTrue, SmtFormula WhenFalse, SmtValueKind ResultKind) : SmtFormula(ResultKind);
}
