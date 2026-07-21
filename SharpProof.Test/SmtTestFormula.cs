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
}
