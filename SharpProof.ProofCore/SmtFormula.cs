namespace SharpProof.ProofCore.Smt;

internal enum SmtValueKind {
    Bool,
    Int,
    Reference,
    String
}

internal enum SmtUnaryOperator {
    Not
}

internal enum SmtIntegerUnaryOperator {
    Negate
}

internal enum SmtBinaryOperator {
    And,
    Or,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}

internal enum SmtIntegerBinaryOperator {
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder
}

internal abstract record SmtFormula(SmtValueKind Kind);

internal sealed record SmtBooleanConstant(bool Value) : SmtFormula(SmtValueKind.Bool);

internal sealed record SmtIntegerConstant(long Value) : SmtFormula(SmtValueKind.Int);

internal sealed record SmtStringConstant(string Value) : SmtFormula(SmtValueKind.String);

internal sealed record SmtNullConstant() : SmtFormula(SmtValueKind.Reference);

internal sealed record SmtVariable(string Name, SmtValueKind Kind) : SmtFormula(Kind);

internal sealed record SmtUnaryFormula(SmtUnaryOperator Operator, SmtFormula Operand) : SmtFormula(SmtValueKind.Bool);

internal sealed record SmtBinaryFormula(SmtBinaryOperator Operator, SmtFormula Left, SmtFormula Right)
    : SmtFormula(SmtValueKind.Bool);

internal sealed record SmtIntegerUnaryTerm(SmtIntegerUnaryOperator Operator, SmtFormula Operand)
    : SmtFormula(SmtValueKind.Int);

internal sealed record SmtIntegerBinaryTerm(SmtIntegerBinaryOperator Operator, SmtFormula Left, SmtFormula Right)
    : SmtFormula(SmtValueKind.Int);

internal sealed record SmtOpaqueIntegerBinaryTerm(
    SmtIntegerBinaryOperator Operator,
    SmtFormula Left,
    SmtFormula Right) : SmtFormula(SmtValueKind.Int);

internal sealed record SmtStringLengthTerm(SmtFormula Value) : SmtFormula(SmtValueKind.Int);

internal sealed record SmtStringConcatTerm(SmtFormula Left, SmtFormula Right) : SmtFormula(SmtValueKind.String);

internal sealed record SmtStringContainsFormula(SmtFormula Value, SmtFormula Search) : SmtFormula(SmtValueKind.Bool);

internal sealed record SmtStringStartsWithFormula(SmtFormula Value, SmtFormula Prefix) : SmtFormula(SmtValueKind.Bool);

internal sealed record SmtStringEndsWithFormula(SmtFormula Value, SmtFormula Suffix) : SmtFormula(SmtValueKind.Bool);

internal sealed record SmtRegexMatchFormula(
    SmtFormula Value,
    string Pattern,
    RegexOptions Options = RegexOptions.None) : SmtFormula(SmtValueKind.Bool);

internal sealed record SmtRuntimeTypeTestFormula(SmtFormula Value, string TypeKey) : SmtFormula(SmtValueKind.Bool);

internal sealed record SmtConditionalFormula(
    SmtFormula Condition,
    SmtFormula WhenTrue,
    SmtFormula WhenFalse,
    SmtValueKind ResultKind) : SmtFormula(ResultKind);
