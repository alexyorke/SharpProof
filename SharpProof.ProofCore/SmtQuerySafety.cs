namespace SharpProof.ProofCore.Smt;

internal sealed class SmtQuerySafety {
    private readonly SmtRegexValidator _regexValidator = new();

    internal int RegexValidationCacheCount => _regexValidator.CacheCount;

    internal bool TryPrepare(IReadOnlyList<SmtFormula> conditions, out SmtFormula[] prepared, out bool changed) {
        var strings = CollectConcreteStrings(conditions);
        changed = false;
        prepared = new SmtFormula[conditions.Count];
        for (var index = 0; index < conditions.Count; index++) {
            var valid = true;
            prepared[index] = SmtFormulaTraversal.RewriteBottomUp(
                conditions[index],
                formula => RewriteConcreteRegex(formula, strings, ref valid),
                out var formulaChanged);
            if (!valid) {
                prepared = [];
                return false;
            }
            changed |= formulaChanged;
        }
        return true;
    }
    internal static IReadOnlyList<SmtFormula> CreateUnsafeArithmeticChecks(IEnumerable<SmtFormula> conditions) {
        var checks = new List<SmtFormula>();
        foreach (var condition in conditions)
            CollectUnsafeArithmeticChecks(condition, new SmtBooleanConstant(true), checks);
        return checks;
    }
    internal static bool ContainsUnsafeArithmetic(SmtFormula formula) =>
        SmtFormulaTraversal.Contains(formula, static item =>
            item is SmtIntegerBinaryTerm { Operator: SmtIntegerBinaryOperator.Divide or SmtIntegerBinaryOperator.Remainder });

    private SmtFormula RewriteConcreteRegex(
        SmtFormula formula,
        IReadOnlyDictionary<SmtFormula, string> strings,
        ref bool valid) {
        if (formula is not SmtRegexMatchFormula regex || !TryResolveString(regex.Value, strings, out var input))
            return formula;
        if (!_regexValidator.TryValidate(input, regex.Pattern, regex.Options, out var matches)) {
            valid = false;
            return formula;
        }
        return new SmtBooleanConstant(matches);
    }
    private static Dictionary<SmtFormula, string> CollectConcreteStrings(IEnumerable<SmtFormula> conditions) {
        var conjuncts = conditions.SelectMany(SmtFormulaTraversal.EnumerateConjuncts).ToArray();
        var equalities = conjuncts
            .OfType<SmtBinaryFormula>()
            .Where(static formula => formula.Operator == SmtBinaryOperator.Equal &&
                                     formula.Left.Kind == SmtValueKind.String &&
                                     formula.Right.Kind == SmtValueKind.String)
            .ToArray();
        var values = new Dictionary<SmtFormula, string>();
        for (var pass = 0; pass <= equalities.Length; pass++) {
            var changed = false;
            foreach (var equality in equalities) {
                if (TryResolveString(equality.Left, values, out var left))
                    changed |= TryAdd(values, equality.Right, left);
                if (TryResolveString(equality.Right, values, out var right))
                    changed |= TryAdd(values, equality.Left, right);
            }
            if (!changed) break;
        }
        var lengths = new Dictionary<SmtFormula, long>();
        foreach (var conjunct in conjuncts) {
            if (conjunct is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } equality) continue;
            if (equality.Left is SmtStringLengthTerm leftLength && equality.Right is SmtIntegerConstant rightConstant)
                lengths[leftLength.Value] = rightConstant.Value;
            else if (equality.Right is SmtStringLengthTerm rightLength && equality.Left is SmtIntegerConstant leftConstant)
                lengths[rightLength.Value] = leftConstant.Value;
        }
        foreach (var conjunct in conjuncts) {
            var (subject, argument) = conjunct switch {
                SmtStringContainsFormula value => (value.Value, value.Search),
                SmtStringStartsWithFormula value => (value.Value, value.Prefix),
                SmtStringEndsWithFormula value => (value.Value, value.Suffix),
                _ => (null, null)
            };
            if (subject != null &&
                argument != null &&
                lengths.TryGetValue(subject, out var length) &&
                TryResolveString(argument, values, out var text) &&
                length == text.Length)
                TryAdd(values, subject, text);
        }
        return values;
    }
    private static bool TryAdd(IDictionary<SmtFormula, string> values, SmtFormula formula, string value) {
        if (values.ContainsKey(formula)) return false;
        values.Add(formula, value);
        return true;
    }
    private static bool TryResolveString(
        SmtFormula formula,
        IReadOnlyDictionary<SmtFormula, string> values,
        out string value) {
        if (formula is SmtStringConstant constant) {
            value = constant.Value;
            return true;
        }
        if (values.TryGetValue(formula, out value!)) return true;
        if (formula is SmtStringConcatTerm concat &&
            TryResolveString(concat.Left, values, out var left) &&
            TryResolveString(concat.Right, values, out var right)) {
            value = left + right;
            return true;
        }
        value = string.Empty;
        return false;
    }
    private static void CollectUnsafeArithmeticChecks(
        SmtFormula formula,
        SmtFormula activation,
        ICollection<SmtFormula> checks) {
        if (formula is SmtConditionalFormula conditional) {
            CollectUnsafeArithmeticChecks(conditional.Condition, activation, checks);
            CollectUnsafeArithmeticChecks(
                conditional.WhenTrue,
                And(activation, conditional.Condition),
                checks);
            CollectUnsafeArithmeticChecks(
                conditional.WhenFalse,
                And(activation, new SmtUnaryFormula(SmtUnaryOperator.Not, conditional.Condition)),
                checks);
            return;
        }
        if (formula is SmtIntegerBinaryTerm { Operator: SmtIntegerBinaryOperator.Divide or SmtIntegerBinaryOperator.Remainder } binary) {
            checks.Add(And(
                activation,
                new SmtBinaryFormula(SmtBinaryOperator.Equal, binary.Right, new SmtIntegerConstant(0))));
        }
        foreach (var child in SmtFormulaTraversal.EnumerateChildren(formula))
            CollectUnsafeArithmeticChecks(child, activation, checks);
    }
    private static SmtFormula And(SmtFormula left, SmtFormula right) =>
        left is SmtBooleanConstant { Value: true }
            ? right
            : new SmtBinaryFormula(SmtBinaryOperator.And, left, right);
}
