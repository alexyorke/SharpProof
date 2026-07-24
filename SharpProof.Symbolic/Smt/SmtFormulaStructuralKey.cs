namespace SharpProof.Symbolic.Smt;
internal static class SmtFormulaStructuralKey {
    internal static string Create(SmtFormula formula) {
        if (formula == null) throw new ArgumentNullException(nameof(formula));
        return formula switch {
            SmtBooleanConstant boolean => "bool:" + (boolean.Value ? "true" : "false"),
            SmtIntegerConstant integer => "int:" + integer.Value.ToString(CultureInfo.InvariantCulture),
            SmtStringConstant text => "string:" + Encode(text.Value),
            SmtNullConstant => "null",
            SmtVariable variable => "variable:" + (int)variable.Kind + ":" + Encode(variable.Name),
            SmtUnaryFormula unary => "unary:" + (int)unary.Operator + "(" + Create(unary.Operand) + ")",
            SmtBinaryFormula binary => Binary("binary:" + (int)binary.Operator, binary.Left, binary.Right),
            SmtIntegerUnaryTerm unary =>
                "integer-unary:" + (int)unary.Operator + "(" + Create(unary.Operand) + ")",
            SmtIntegerBinaryTerm binary =>
                Binary("integer-binary:" + (int)binary.Operator, binary.Left, binary.Right),
            SmtOpaqueIntegerBinaryTerm binary =>
                Binary("opaque-integer-binary:" + (int)binary.Operator, binary.Left, binary.Right),
            SmtStringLengthTerm length => "string-length(" + Create(length.Value) + ")",
            SmtStringConcatTerm concat => Binary("string-concat", concat.Left, concat.Right),
            SmtStringSubstringTerm substring => "string-substring(" + Create(substring.Value) + "," +
                                            Create(substring.Offset) + "," + Create(substring.Length) + ")",
            SmtStringContainsFormula contains =>
                Binary("string-contains", contains.Value, contains.Search),
            SmtStringStartsWithFormula startsWith =>
                Binary("string-starts-with", startsWith.Value, startsWith.Prefix),
            SmtStringEndsWithFormula endsWith =>
                Binary("string-ends-with", endsWith.Value, endsWith.Suffix),
            SmtRegexMatchFormula regex => "regex(" + Create(regex.Value) + "," + Encode(regex.Pattern) + "," +
                                          (int)regex.Options + ")",
            SmtRuntimeTypeTestFormula typeTest =>
                "runtime-type(" + Create(typeTest.Value) + "," + Encode(typeTest.TypeKey) + ")",
            SmtConditionalFormula conditional =>
                "conditional:" + (int)conditional.ResultKind + "(" + Create(conditional.Condition) + "," +
                Create(conditional.WhenTrue) + "," + Create(conditional.WhenFalse) + ")",
            _ => throw new NotSupportedException("Unsupported SMT formula type: " + formula.GetType().FullName)
        };
    }
    private static string Binary(string kind, SmtFormula left, SmtFormula right) =>
        kind + "(" + Create(left) + "," + Create(right) + ")";
    private static string Encode(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}
