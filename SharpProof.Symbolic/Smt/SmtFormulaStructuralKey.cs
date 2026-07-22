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
            SmtBinaryFormula binary => "binary:" + (int)binary.Operator + "(" + Create(binary.Left) + "," +
                                       Create(binary.Right) + ")",
            SmtIntegerUnaryTerm unary =>
                "integer-unary:" + (int)unary.Operator + "(" + Create(unary.Operand) + ")",
            SmtIntegerBinaryTerm binary =>
                "integer-binary:" + (int)binary.Operator + "(" + Create(binary.Left) + "," +
                Create(binary.Right) + ")",
            SmtOpaqueIntegerBinaryTerm binary =>
                "opaque-integer-binary:" + (int)binary.Operator + "(" + Create(binary.Left) + "," +
                Create(binary.Right) + ")",
            SmtStringLengthTerm length => "string-length(" + Create(length.Value) + ")",
            SmtStringConcatTerm concat => "string-concat(" + Create(concat.Left) + "," + Create(concat.Right) +
                                           ")",
            SmtStringSubstringTerm substring => "string-substring(" + Create(substring.Value) + "," +
                                           Create(substring.Offset) + "," + Create(substring.Length) + ")",
            SmtStringContainsFormula contains =>
                "string-contains(" + Create(contains.Value) + "," + Create(contains.Search) + ")",
            SmtStringStartsWithFormula startsWith =>
                "string-starts-with(" + Create(startsWith.Value) + "," + Create(startsWith.Prefix) + ")",
            SmtStringEndsWithFormula endsWith =>
                "string-ends-with(" + Create(endsWith.Value) + "," + Create(endsWith.Suffix) + ")",
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
    private static string Encode(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}
