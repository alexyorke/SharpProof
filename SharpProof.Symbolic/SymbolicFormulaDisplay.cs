namespace SharpProof.Symbolic;
internal static class SymbolicFormulaDisplay {
    internal static string Format(SymbolicCondition condition) {
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        return SymbolicIrFormulaEncoder.TryEncode(condition, out var formula)
            ? Format(formula)
            : condition.ToString() ?? string.Empty;
    }
    internal static string Format(SmtFormula formula) {
        if (formula == null) throw new ArgumentNullException(nameof(formula));
        switch (formula) {
            case SmtBooleanConstant boolean:
                return boolean.Value ? "true" : "false";
            case SmtIntegerConstant integer:
                return integer.Value.ToString(CultureInfo.InvariantCulture);
            case SmtStringConstant text:
                return "\"" + EscapeString(text.Value) + "\"";
            case SmtNullConstant:
                return "null";
            case SmtVariable variable:
                return FormatVariableName(variable.Name);
            case SmtUnaryFormula unary:
                return "!(" + Format(unary.Operand) + ")";
            case SmtBinaryFormula binary:
                return FormatBinary(binary);
            case SmtIntegerUnaryTerm unary:
                return "-" + FormatTerm(unary.Operand);
            case SmtIntegerBinaryTerm binary:
                return FormatIntegerBinary(binary);
            case SmtOpaqueIntegerBinaryTerm binary:
                return FormatIntegerBinary(binary.Operator, binary.Left, binary.Right);
            case SmtStringLengthTerm length:
                return FormatTerm(length.Value) + ".Length";
            case SmtStringConcatTerm concat:
                return FormatTerm(concat.Left) + " + " + FormatTerm(concat.Right);
            case SmtStringSubstringTerm substring:
                return FormatTerm(substring.Value) + ".Substring(" + Format(substring.Offset) + ", " +
                       Format(substring.Length) + ")";
            case SmtStringContainsFormula contains:
                return FormatTerm(contains.Value) + ".Contains(" + Format(contains.Search) + ")";
            case SmtStringStartsWithFormula startsWith:
                return FormatTerm(startsWith.Value) + ".StartsWith(" + Format(startsWith.Prefix) + ")";
            case SmtStringEndsWithFormula endsWith:
                return FormatTerm(endsWith.Value) + ".EndsWith(" + Format(endsWith.Suffix) + ")";
            case SmtRegexMatchFormula regex:
                return "Regex.IsMatch(" + FormatTerm(regex.Value) + ", \"" + EscapeString(regex.Pattern) + "\")";
            case SmtRuntimeTypeTestFormula runtimeTypeTest:
                return FormatTerm(runtimeTypeTest.Value) + " is " + runtimeTypeTest.TypeKey;
            case SmtConditionalFormula conditional:
                return "(" +
                       Format(conditional.Condition) +
                       " ? " +
                       Format(conditional.WhenTrue) +
                       " : " +
                       Format(conditional.WhenFalse) +
                       ")";
            default:
                return "?";
        }
    }
    internal static string GetMergeTarget(SmtFormula formula) {
        if (formula == null) throw new ArgumentNullException(nameof(formula));
        switch (formula) {
            case SmtUnaryFormula unary:
                return GetMergeTarget(unary.Operand);
            case SmtBinaryFormula binary when IsComparison(binary.Operator): {
                    var leftTarget = TryGetTermTarget(binary.Left);
                    var rightTarget = TryGetTermTarget(binary.Right);
                    if (leftTarget != null && IsConstant(binary.Right)) return leftTarget;
                    if (rightTarget != null && IsConstant(binary.Left)) return rightTarget;
                    if (leftTarget != null && rightTarget != null) return leftTarget + "," + rightTarget;
                    return Format(binary);
                }
            case SmtStringContainsFormula contains:
                return FormatTerm(contains.Value);
            case SmtStringStartsWithFormula startsWith:
                return FormatTerm(startsWith.Value);
            case SmtStringEndsWithFormula endsWith:
                return FormatTerm(endsWith.Value);
            case SmtRegexMatchFormula regex:
                return FormatTerm(regex.Value);
            case SmtRuntimeTypeTestFormula runtimeTypeTest:
                return FormatTerm(runtimeTypeTest.Value);
            case SmtVariable variable:
                return FormatVariableName(variable.Name);
            default:
                return Format(formula);
        }
    }
    private static string FormatBinary(SmtBinaryFormula binary) {
        var op = binary.Operator switch {
            SmtBinaryOperator.And => "&&",
            SmtBinaryOperator.Or => "||",
            SmtBinaryOperator.Equal => "==",
            SmtBinaryOperator.NotEqual => "!=",
            SmtBinaryOperator.LessThan => "<",
            SmtBinaryOperator.LessThanOrEqual => "<=",
            SmtBinaryOperator.GreaterThan => ">",
            SmtBinaryOperator.GreaterThanOrEqual => ">=",
            _ => binary.Operator.ToString()
        };
        if (binary.Operator == SmtBinaryOperator.And ||
            binary.Operator == SmtBinaryOperator.Or)
            return FormatConditionTerm(binary.Left) + " " + op + " " + FormatConditionTerm(binary.Right);
        return FormatTerm(binary.Left) + " " + op + " " + FormatTerm(binary.Right);
    }
    private static string FormatIntegerBinary(SmtIntegerBinaryTerm binary) =>
        FormatIntegerBinary(binary.Operator, binary.Left, binary.Right);
    private static string FormatIntegerBinary(SmtIntegerBinaryOperator binaryOperator, SmtFormula left, SmtFormula right) {
        var op = binaryOperator switch {
            SmtIntegerBinaryOperator.Add => "+",
            SmtIntegerBinaryOperator.Subtract => "-",
            SmtIntegerBinaryOperator.Multiply => "*",
            SmtIntegerBinaryOperator.Divide => "/",
            SmtIntegerBinaryOperator.Remainder => "%",
            _ => binaryOperator.ToString()
        };
        return FormatTerm(left) + " " + op + " " + FormatTerm(right);
    }
    private static string FormatConditionTerm(SmtFormula formula) => formula is SmtBinaryFormula or SmtConditionalFormula
            ? "(" + Format(formula) + ")"
            : Format(formula);
    private static string FormatTerm(SmtFormula formula)
        => formula is SmtBinaryFormula or SmtIntegerBinaryTerm or SmtOpaqueIntegerBinaryTerm or
            SmtConditionalFormula
            ? "(" + Format(formula) + ")"
            : Format(formula);
    private static string? TryGetTermTarget(SmtFormula formula) {
        switch (formula) {
            case SmtVariable variable:
                return FormatVariableName(variable.Name);
            case SmtStringLengthTerm length:
                return FormatTerm(length.Value) + ".Length";
            case SmtStringConcatTerm:
            case SmtStringSubstringTerm:
            case SmtIntegerBinaryTerm:
            case SmtOpaqueIntegerBinaryTerm:
            case SmtIntegerUnaryTerm:
                return Format(formula);
            default:
                return null;
        }
    }
    private static bool IsComparison(SmtBinaryOperator op) => op == SmtBinaryOperator.Equal ||
               op == SmtBinaryOperator.NotEqual ||
               op == SmtBinaryOperator.LessThan ||
               op == SmtBinaryOperator.LessThanOrEqual ||
               op == SmtBinaryOperator.GreaterThan ||
               op == SmtBinaryOperator.GreaterThanOrEqual;
    private static bool IsConstant(SmtFormula formula) =>
        formula is SmtBooleanConstant or SmtIntegerConstant or SmtStringConstant or SmtNullConstant;
    private static string FormatVariableName(string name) {
        if (string.IsNullOrWhiteSpace(name)) return name ?? string.Empty;
        const string recordPrefix = "SmtVariable {";
        if (name.StartsWith(recordPrefix, StringComparison.Ordinal)) {
            var nameMarker = "Name = ";
            var nameIndex = name.IndexOf(nameMarker, StringComparison.Ordinal);
            var closeIndex = name.IndexOf(" }", StringComparison.Ordinal);
            if (nameIndex >= 0 && closeIndex > nameIndex) {
                var innerNameStart = nameIndex + nameMarker.Length;
                var innerName = name.Substring(innerNameStart, closeIndex - innerNameStart).Trim();
                var suffix = closeIndex + 2 < name.Length
                    ? name.Substring(closeIndex + 2)
                    : string.Empty;
                return FormatVariableName(innerName) + suffix;
            }
        }
        name = name.Replace(".String", string.Empty);
        var hashIndex = name.LastIndexOf('#');
        if (hashIndex > 0 && hashIndex + 1 < name.Length) {
            var index = hashIndex + 1;
            while (index < name.Length && char.IsDigit(name[index])) index++;
            if (index > hashIndex + 1) return name.Substring(0, hashIndex) + name.Substring(index);
        }
        return name;
    }
    private static string EscapeString(string value) => (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
}
