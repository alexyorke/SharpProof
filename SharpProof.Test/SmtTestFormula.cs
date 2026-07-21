using SharpProof.ProofCore.Smt;

namespace SharpProof.Test;

internal static class SmtTestFormula {
    internal static SmtVariable Int(string name) => new(name, SmtValueKind.Int);
    internal static SmtVariable Bool(string name) => new(name, SmtValueKind.Bool);
    internal static SmtVariable String(string name) => new(name, SmtValueKind.String);
    internal static SmtVariable Reference(string name) => new(name, SmtValueKind.Reference);
    internal static SmtIntegerConstant Integer(long value) => new(value);
    internal static SmtStringConstant Text(string value) => new(value);
    internal static SmtBooleanConstant Boolean(bool value) => new(value);
    internal static SmtNullConstant Null() => new();
    internal static SmtBinaryFormula Equal(SmtFormula left, SmtFormula right) =>
        new(SmtBinaryOperator.Equal, left, right);
    internal static SmtBinaryFormula NotEqual(SmtFormula left, SmtFormula right) =>
        new(SmtBinaryOperator.NotEqual, left, right);
    internal static SmtBinaryFormula GreaterThan(SmtFormula left, SmtFormula right) =>
        new(SmtBinaryOperator.GreaterThan, left, right);
    internal static SmtBinaryFormula GreaterThanOrEqual(SmtFormula left, SmtFormula right) =>
        new(SmtBinaryOperator.GreaterThanOrEqual, left, right);
    internal static SmtBinaryFormula LessThan(SmtFormula left, SmtFormula right) =>
        new(SmtBinaryOperator.LessThan, left, right);
    internal static SmtBinaryFormula LessThanOrEqual(SmtFormula left, SmtFormula right) =>
        new(SmtBinaryOperator.LessThanOrEqual, left, right);
    internal static SmtBinaryFormula And(SmtFormula left, SmtFormula right) =>
        new(SmtBinaryOperator.And, left, right);
    internal static SmtBinaryFormula Or(SmtFormula left, SmtFormula right) =>
        new(SmtBinaryOperator.Or, left, right);
    internal static SmtUnaryFormula Not(SmtFormula operand) => new(SmtUnaryOperator.Not, operand);
    internal static SmtIntegerBinaryTerm Add(SmtFormula left, SmtFormula right) =>
        new(SmtIntegerBinaryOperator.Add, left, right);
    internal static SmtIntegerBinaryTerm Subtract(SmtFormula left, SmtFormula right) =>
        new(SmtIntegerBinaryOperator.Subtract, left, right);
    internal static SmtIntegerBinaryTerm Multiply(SmtFormula left, SmtFormula right) =>
        new(SmtIntegerBinaryOperator.Multiply, left, right);
    internal static SmtIntegerBinaryTerm Divide(SmtFormula left, SmtFormula right) =>
        new(SmtIntegerBinaryOperator.Divide, left, right);
    internal static SmtIntegerBinaryTerm Remainder(SmtFormula left, SmtFormula right) =>
        new(SmtIntegerBinaryOperator.Remainder, left, right);
    internal static SmtConditionalFormula Conditional(
        SmtFormula condition, SmtFormula whenTrue, SmtFormula whenFalse, SmtValueKind kind) =>
        new(condition, whenTrue, whenFalse, kind);
    internal static SmtStringLengthTerm Length(SmtFormula value) => new(value);
    internal static SmtStringConcatTerm Concat(SmtFormula left, SmtFormula right) => new(left, right);
    internal static SmtStringStartsWithFormula StartsWith(SmtFormula value, SmtFormula prefix) => new(value, prefix);
    internal static SmtStringEndsWithFormula EndsWith(SmtFormula value, SmtFormula suffix) => new(value, suffix);
    internal static SmtStringContainsFormula Contains(SmtFormula value, SmtFormula fragment) => new(value, fragment);
}
