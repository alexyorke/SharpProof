namespace SharpProof.ProofCore.Smt;

internal sealed class Z3FormulaEncoder : IDisposable {
    private readonly Context _context = new();
    private readonly Expr _nullReference;
    private readonly Sort _referenceSort;

    private readonly Dictionary<(string Pattern, RegexOptions Options), RegexTranslationPrecision>
        _regexPrecisionCache = new();

    private readonly Dictionary<string, FuncDecl> _runtimeTypeTests = new(StringComparer.Ordinal);
    private readonly Dictionary<SmtIntegerBinaryOperator, FuncDecl> _opaqueIntegerOperations = new();
    private readonly Dictionary<(string Name, SmtValueKind Kind), Expr> _variables = new();

    public Z3FormulaEncoder() {
        _referenceSort = _context.MkUninterpretedSort("Reference");
        _nullReference = _context.MkConst("null_reference", _referenceSort);
    }

    public void Dispose() {
        foreach (var variable in _variables.Values) variable.Dispose();
        foreach (var runtimeTypeTest in _runtimeTypeTests.Values) runtimeTypeTest.Dispose();
        foreach (var opaqueIntegerOperation in _opaqueIntegerOperations.Values) opaqueIntegerOperation.Dispose();
        _nullReference.Dispose();
        _referenceSort.Dispose();
        _context.Dispose();
    }

    public BoolExpr EncodeCondition(SmtFormula formula) {
        if (formula.Kind != SmtValueKind.Bool)
            throw new InvalidOperationException("Only boolean SMT formulas can be used as conditions.");

        EnsureSafeRegexPolarity(formula, false);
        return (BoolExpr)Encode(formula);
    }

    public Solver CreateSolver(TimeSpan timeout) {
        var solver = _context.MkSolver();
        var parameters = _context.MkParams();
        // rlimit is the binding budget: it counts solver work, so outcomes are
        // deterministic under CPU load. The wall-clock timeout is a scaled-up
        // safety net only.
        parameters.Add("rlimit", SmtResourceBudget.GetRlimit(timeout));
        parameters.Add("timeout", GetTimeoutMilliseconds(SmtResourceBudget.GetWallClockSafetyNet(timeout)));
        solver.Parameters = parameters;
        return solver;
    }

    public SmtSatisfyingWitness CreateWitness(
        Model model,
        IEnumerable<SmtVariable> variables,
        SmtWitnessStatus status = SmtWitnessStatus.Exact,
        string reason = "satisfying_model") {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var assignments = variables
            .Distinct()
            .OrderBy(static variable => variable.Name, StringComparer.Ordinal)
            .ThenBy(static variable => variable.Kind)
            .Select(variable => CreateModelAssignment(model, variable))
            .ToArray();
        if (status == SmtWitnessStatus.Exact &&
            assignments.Any(static assignment => assignment.Status == SmtWitnessStatus.Approximate)) {
            status = SmtWitnessStatus.Approximate;
            reason = "satisfying_model_contains_opaque_values";
        }

        return new SmtSatisfyingWitness(status, reason, assignments);
    }


    public bool ContainsApproximateRegex(SmtFormula formula) {
        foreach (var candidate in SmtFormulaTraversal.Enumerate(formula))
            if (candidate is SmtRegexMatchFormula regexMatch &&
                GetRegexTranslationPrecision(regexMatch.Pattern, regexMatch.Options) ==
                RegexTranslationPrecision.Approximate)
                return true;

        return false;
    }

    private Expr Encode(SmtFormula formula) {
        return formula switch {
            SmtBooleanConstant booleanConstant => booleanConstant.Value ? _context.MkTrue() : _context.MkFalse(),
            SmtIntegerConstant integerConstant => _context.MkInt(integerConstant.Value),
            SmtStringConstant stringConstant => _context.MkString(stringConstant.Value),
            SmtNullConstant => _nullReference,
            SmtVariable variable => GetOrCreateVariable(variable),
            SmtUnaryFormula unaryFormula => EncodeUnary(unaryFormula),
            SmtBinaryFormula binaryFormula => EncodeBinary(binaryFormula),
            SmtIntegerUnaryTerm integerUnaryTerm => EncodeIntegerUnary(integerUnaryTerm),
            SmtIntegerBinaryTerm integerBinaryTerm => EncodeIntegerBinary(integerBinaryTerm),
            SmtOpaqueIntegerBinaryTerm opaqueIntegerTerm => EncodeOpaqueIntegerBinary(opaqueIntegerTerm),
            SmtStringLengthTerm stringLengthTerm => _context.MkLength(EncodeString(stringLengthTerm.Value)),
            SmtStringConcatTerm stringConcatTerm => _context.MkConcat(
                EncodeString(stringConcatTerm.Left),
                EncodeString(stringConcatTerm.Right)),
            // Offset and length are cast to IntExpr so this binds to the sequence
            // overload of MkExtract rather than the bit-vector one.
            SmtStringSubstringTerm stringSubstringTerm => _context.MkExtract(
                EncodeString(stringSubstringTerm.Value),
                (IntExpr)EncodeInteger(stringSubstringTerm.Offset),
                (IntExpr)EncodeInteger(stringSubstringTerm.Length)),
            SmtStringContainsFormula stringContainsFormula => _context.MkContains(
                EncodeString(stringContainsFormula.Value),
                EncodeString(stringContainsFormula.Search)),
            SmtStringStartsWithFormula stringStartsWithFormula => _context.MkPrefixOf(
                EncodeString(stringStartsWithFormula.Prefix),
                EncodeString(stringStartsWithFormula.Value)),
            SmtStringEndsWithFormula stringEndsWithFormula => _context.MkSuffixOf(
                EncodeString(stringEndsWithFormula.Suffix),
                EncodeString(stringEndsWithFormula.Value)),
            SmtRegexMatchFormula regexMatchFormula => EncodeRegexMatch(regexMatchFormula),
            SmtRuntimeTypeTestFormula runtimeTypeTestFormula => EncodeRuntimeTypeTest(runtimeTypeTestFormula),
            SmtConditionalFormula conditionalFormula => EncodeConditional(conditionalFormula),
            _ => throw new InvalidOperationException("Unsupported SMT formula node.")
        };
    }

    private Expr EncodeUnary(SmtUnaryFormula formula) {
        return formula.Operator switch {
            SmtUnaryOperator.Not => _context.MkNot(EncodeCondition(formula.Operand)),
            _ => throw new InvalidOperationException("Unsupported SMT unary operator.")
        };
    }

    private Expr EncodeIntegerUnary(SmtIntegerUnaryTerm term) {
        return term.Operator switch {
            SmtIntegerUnaryOperator.Negate => _context.MkUnaryMinus(EncodeInteger(term.Operand)),
            _ => throw new InvalidOperationException("Unsupported SMT integer unary operator.")
        };
    }

    private Expr EncodeBinary(SmtBinaryFormula formula) {
        return formula.Operator switch {
            SmtBinaryOperator.And => _context.MkAnd(EncodeCondition(formula.Left), EncodeCondition(formula.Right)),
            SmtBinaryOperator.Or => _context.MkOr(EncodeCondition(formula.Left), EncodeCondition(formula.Right)),
            SmtBinaryOperator.Equal => _context.MkEq(Encode(formula.Left), Encode(formula.Right)),
            SmtBinaryOperator.NotEqual => _context.MkNot(_context.MkEq(Encode(formula.Left), Encode(formula.Right))),
            SmtBinaryOperator.LessThan => _context.MkLt(EncodeInteger(formula.Left), EncodeInteger(formula.Right)),
            SmtBinaryOperator.LessThanOrEqual => _context.MkLe(EncodeInteger(formula.Left),
                EncodeInteger(formula.Right)),
            SmtBinaryOperator.GreaterThan => _context.MkGt(EncodeInteger(formula.Left), EncodeInteger(formula.Right)),
            SmtBinaryOperator.GreaterThanOrEqual => _context.MkGe(EncodeInteger(formula.Left),
                EncodeInteger(formula.Right)),
            _ => throw new InvalidOperationException("Unsupported SMT binary operator.")
        };
    }

    private Expr EncodeIntegerBinary(SmtIntegerBinaryTerm term) {
        return term.Operator switch {
            SmtIntegerBinaryOperator.Add => _context.MkAdd(EncodeInteger(term.Left), EncodeInteger(term.Right)),
            SmtIntegerBinaryOperator.Subtract => _context.MkSub(EncodeInteger(term.Left), EncodeInteger(term.Right)),
            SmtIntegerBinaryOperator.Multiply => _context.MkMul(EncodeInteger(term.Left), EncodeInteger(term.Right)),
            SmtIntegerBinaryOperator.Divide => EncodeCSharpIntegerDivide(term),
            SmtIntegerBinaryOperator.Remainder => EncodeCSharpIntegerRemainder(term),
            _ => throw new InvalidOperationException("Unsupported SMT integer binary operator.")
        };
    }

    private Expr EncodeOpaqueIntegerBinary(SmtOpaqueIntegerBinaryTerm term) {
        if (!_opaqueIntegerOperations.TryGetValue(term.Operator, out var operation)) {
            operation = _context.MkFuncDecl(
                "csharp_overflow_sensitive_" + term.Operator.ToString().ToLowerInvariant(),
                new Sort[] { _context.IntSort, _context.IntSort },
                _context.IntSort);
            _opaqueIntegerOperations.Add(term.Operator, operation);
        }

        return _context.MkApp(operation, EncodeInteger(term.Left), EncodeInteger(term.Right));
    }

    private ArithExpr EncodeCSharpIntegerDivide(SmtIntegerBinaryTerm term) {
        var left = EncodeInteger(term.Left);
        var right = EncodeInteger(term.Right);
        return EncodeCSharpIntegerDivide(left, right);
    }

    private ArithExpr EncodeCSharpIntegerRemainder(SmtIntegerBinaryTerm term) {
        var left = EncodeInteger(term.Left);
        var right = EncodeInteger(term.Right);
        var quotient = EncodeCSharpIntegerDivide(left, right);
        return _context.MkSub(left, _context.MkMul(quotient, right));
    }

    private ArithExpr EncodeCSharpIntegerDivide(ArithExpr left, ArithExpr right) {
        var zero = _context.MkInt(0);
        var leftAbs = (ArithExpr)_context.MkITE(
            _context.MkGe(left, zero),
            left,
            _context.MkUnaryMinus(left));
        var rightAbs = (ArithExpr)_context.MkITE(
            _context.MkGe(right, zero),
            right,
            _context.MkUnaryMinus(right));
        var magnitude = _context.MkDiv(leftAbs, rightAbs);
        var signsDiffer = _context.MkXor(_context.MkLt(left, zero), _context.MkLt(right, zero));
        return (ArithExpr)_context.MkITE(signsDiffer, _context.MkUnaryMinus(magnitude), magnitude);
    }

    private Expr EncodeConditional(SmtConditionalFormula formula) {
        return _context.MkITE(
            EncodeCondition(formula.Condition),
            Encode(formula.WhenTrue),
            Encode(formula.WhenFalse));
    }

    private ArithExpr EncodeInteger(SmtFormula formula) {
        if (formula.Kind != SmtValueKind.Int)
            throw new InvalidOperationException("Only integer SMT formulas can be encoded as arithmetic expressions.");

        return (ArithExpr)Encode(formula);
    }

    private SeqExpr EncodeString(SmtFormula formula) {
        if (formula.Kind != SmtValueKind.String)
            throw new InvalidOperationException("Only string SMT formulas can be encoded as string expressions.");

        return (SeqExpr)Encode(formula);
    }

    private BoolExpr EncodeRegexMatch(SmtRegexMatchFormula formula) {
        if (!CanEncodeRegexOptions(formula.Options))
            throw new InvalidOperationException("Unsupported SMT regex options.");

        var translation = Z3RegexTranslator.Translate(_context, formula.Pattern, formula.Options);
        if (!translation.Success)
            throw new InvalidOperationException("Unsupported SMT regex pattern.");

        return _context.MkInRe(EncodeString(formula.Value), translation.Regex!);
    }

    private BoolExpr EncodeRuntimeTypeTest(SmtRuntimeTypeTestFormula formula) {
        if (formula.Value.Kind != SmtValueKind.Reference)
            throw new InvalidOperationException("Only reference SMT formulas can be used in runtime type tests.");

        if (!_runtimeTypeTests.TryGetValue(formula.TypeKey, out var predicate)) {
            predicate = _context.MkFuncDecl(
                "runtime_type_test:" + SanitizeSymbolName(formula.TypeKey),
                new[] { _referenceSort },
                _context.BoolSort);
            _runtimeTypeTests.Add(formula.TypeKey, predicate);
        }

        return (BoolExpr)_context.MkApp(predicate, EncodeReference(formula.Value));
    }

    private Expr EncodeReference(SmtFormula formula) {
        if (formula.Kind != SmtValueKind.Reference)
            throw new InvalidOperationException("Only reference SMT formulas can be encoded as reference expressions.");

        return Encode(formula);
    }

    private void EnsureSafeRegexPolarity(SmtFormula formula, bool isNegativeContext) {
        switch (formula) {
            case SmtRegexMatchFormula regexMatch:
                if (!CanEncodeRegexOptions(regexMatch.Options))
                    throw new InvalidOperationException("Unsupported SMT regex options.");

                if (isNegativeContext && IsApproximateRegexPattern(regexMatch.Pattern, regexMatch.Options))
                    throw new InvalidOperationException("Approximate SMT regex patterns cannot be safely negated.");

                EnsureSafeRegexInTerm(regexMatch.Value);
                return;
            case SmtRuntimeTypeTestFormula runtimeTypeTest:
                EnsureSafeRegexInTerm(runtimeTypeTest.Value);
                return;
            case SmtUnaryFormula { Operator: SmtUnaryOperator.Not } unaryFormula:
                EnsureSafeRegexPolarity(unaryFormula.Operand, !isNegativeContext);
                return;
            case SmtBinaryFormula binaryFormula:
                EnsureSafeRegexPolarity(binaryFormula, isNegativeContext);
                return;
            case SmtStringContainsFormula stringContainsFormula:
                EnsureSafeRegexInTerm(stringContainsFormula.Value);
                EnsureSafeRegexInTerm(stringContainsFormula.Search);
                return;
            case SmtStringStartsWithFormula stringStartsWithFormula:
                EnsureSafeRegexInTerm(stringStartsWithFormula.Value);
                EnsureSafeRegexInTerm(stringStartsWithFormula.Prefix);
                return;
            case SmtStringEndsWithFormula stringEndsWithFormula:
                EnsureSafeRegexInTerm(stringEndsWithFormula.Value);
                EnsureSafeRegexInTerm(stringEndsWithFormula.Suffix);
                return;
            case SmtConditionalFormula { Kind: SmtValueKind.Bool } conditionalFormula:
                EnsureExactRegexUse(conditionalFormula.Condition);
                EnsureSafeRegexPolarity(conditionalFormula.WhenTrue, isNegativeContext);
                EnsureSafeRegexPolarity(conditionalFormula.WhenFalse, isNegativeContext);
                return;
        }

        EnsureSafeRegexInTerm(formula);
    }

    private void EnsureSafeRegexPolarity(SmtBinaryFormula formula, bool isNegativeContext) {
        if (formula.Operator is SmtBinaryOperator.And or SmtBinaryOperator.Or) {
            EnsureSafeRegexPolarity(formula.Left, isNegativeContext);
            EnsureSafeRegexPolarity(formula.Right, isNegativeContext);
            return;
        }

        if (formula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual &&
            formula.Left.Kind == SmtValueKind.Bool &&
            formula.Right.Kind == SmtValueKind.Bool) {
            EnsureSafeBooleanComparisonRegexPolarity(formula, isNegativeContext);
            return;
        }

        EnsureSafeRegexInTerm(formula.Left);
        EnsureSafeRegexInTerm(formula.Right);
    }

    private void EnsureSafeBooleanComparisonRegexPolarity(SmtBinaryFormula formula, bool isNegativeContext) {
        if (formula.Left is SmtBooleanConstant leftConstant) {
            EnsureSafeRegexPolarity(
                formula.Right,
                GetBooleanComparisonOperandPolarity(formula.Operator, leftConstant.Value, isNegativeContext));
            return;
        }

        if (formula.Right is SmtBooleanConstant rightConstant) {
            EnsureSafeRegexPolarity(
                formula.Left,
                GetBooleanComparisonOperandPolarity(formula.Operator, rightConstant.Value, isNegativeContext));
            return;
        }

        EnsureExactRegexUse(formula.Left);
        EnsureExactRegexUse(formula.Right);
    }

    private void EnsureSafeRegexInTerm(SmtFormula formula) {
        foreach (var conditional in SmtFormulaTraversal.Enumerate(formula).OfType<SmtConditionalFormula>())
            EnsureExactRegexUse(conditional.Condition);
    }

    private void EnsureExactRegexUse(SmtFormula formula) {
        foreach (var regexMatch in SmtFormulaTraversal.Enumerate(formula).OfType<SmtRegexMatchFormula>()) {
            if (!CanEncodeRegexOptions(regexMatch.Options))
                throw new InvalidOperationException("Unsupported SMT regex options.");

            if (IsApproximateRegexPattern(regexMatch.Pattern, regexMatch.Options))
                throw new InvalidOperationException("Approximate SMT regex patterns require positive polarity.");
        }
    }

    private bool IsApproximateRegexPattern(string pattern, RegexOptions options) =>
        GetRegexTranslationPrecision(pattern, options) == RegexTranslationPrecision.Approximate;

    private RegexTranslationPrecision GetRegexTranslationPrecision(string pattern, RegexOptions options) {
        var key = (pattern, options);
        if (_regexPrecisionCache.TryGetValue(key, out var cached)) return cached;

        var translation = Z3RegexTranslator.Translate(_context, pattern, options);
        var precision = translation.Success
            ? translation.IsExact
                ? RegexTranslationPrecision.Exact
                : RegexTranslationPrecision.Approximate
            : RegexTranslationPrecision.Unsupported;
        _regexPrecisionCache.Add(key, precision);
        return precision;
    }

    private static bool CanEncodeRegexOptions(RegexOptions options) =>
        SmtRegexSemantics.CanEncodeOptions(options);

    private static bool GetBooleanComparisonOperandPolarity(
        SmtBinaryOperator op,
        bool constantValue,
        bool isNegativeContext) {
        var preservesPolarity =
            (op == SmtBinaryOperator.Equal && constantValue) ||
            (op == SmtBinaryOperator.NotEqual && !constantValue);
        return preservesPolarity ? isNegativeContext : !isNegativeContext;
    }

    private static uint GetTimeoutMilliseconds(TimeSpan timeout) {
        var totalMilliseconds = timeout.TotalMilliseconds;
        if (totalMilliseconds >= uint.MaxValue) return uint.MaxValue;

        return (uint)Math.Max(1, totalMilliseconds);
    }

    private static string SanitizeSymbolName(string value) {
        if (string.IsNullOrEmpty(value)) return "unknown";

        var buffer = new char[value.Length];
        for (var index = 0; index < value.Length; index++) {
            var ch = value[index];
            buffer[index] = char.IsLetterOrDigit(ch) || ch == '_' || ch == '.'
                ? ch
                : '_';
        }

        return new string(buffer);
    }

    private Expr GetOrCreateVariable(SmtVariable variable) {
        var key = (variable.Name, variable.Kind);
        if (_variables.TryGetValue(key, out var existing)) return existing;

        var created = variable.Kind switch {
            SmtValueKind.Bool => _context.MkBoolConst(variable.Name),
            SmtValueKind.Int => _context.MkIntConst(variable.Name),
            SmtValueKind.Reference => _context.MkConst(variable.Name, _referenceSort),
            SmtValueKind.String => _context.MkConst(variable.Name, _context.StringSort),
            _ => throw new InvalidOperationException("Unsupported SMT variable kind.")
        };

        _variables.Add(key, created);
        return created;
    }

    private SmtModelAssignment CreateModelAssignment(Model model, SmtVariable variable) {
        using var evaluated = model.Evaluate(GetOrCreateVariable(variable), true);
        switch (variable.Kind) {
            case SmtValueKind.Bool:
                if (evaluated.IsTrue)
                    return new SmtModelAssignment(variable.Name, variable.Kind, "true", BooleanValue: true);

                if (evaluated.IsFalse)
                    return new SmtModelAssignment(variable.Name, variable.Kind, "false", BooleanValue: false);

                break;
            case SmtValueKind.Int:
                if (evaluated is IntNum integer) {
                    var text = integer.ToString();
                    return long.TryParse(text, out var value)
                        ? new SmtModelAssignment(variable.Name, variable.Kind, text, IntegerValue: value)
                        : new SmtModelAssignment(variable.Name, variable.Kind, text);
                }

                break;
            case SmtValueKind.String:
                if (evaluated.IsString)
                    return new SmtModelAssignment(
                        variable.Name,
                        variable.Kind,
                        evaluated.String,
                        StringValue: evaluated.String);

                break;
            case SmtValueKind.Reference: {
                using var nullValue = model.Evaluate(_nullReference, true);
                var isNull = evaluated.Equals(nullValue);
                return new SmtModelAssignment(
                    variable.Name,
                    variable.Kind,
                    isNull ? "null" : evaluated.ToString(),
                    IsNull: isNull,
                    Status: isNull ? SmtWitnessStatus.Exact : SmtWitnessStatus.Approximate);
            }
        }

        return new SmtModelAssignment(
            variable.Name,
            variable.Kind,
            evaluated.ToString(),
            Status: SmtWitnessStatus.Approximate);
    }

    private enum RegexTranslationPrecision {
        Unsupported,
        Exact,
        Approximate
    }

}
