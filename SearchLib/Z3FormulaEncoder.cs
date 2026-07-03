using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Z3;

namespace SearchLib.Smt
{
    internal sealed class Z3FormulaEncoder : IDisposable
    {
        private readonly Context _context = new();
        private readonly Sort _referenceSort;
        private readonly Expr _nullReference;
        private readonly Dictionary<(string Name, SmtValueKind Kind), Expr> _variables = new();
        private readonly Dictionary<string, FuncDecl> _runtimeTypeTests = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Pattern, RegexOptions Options), RegexTranslationPrecision> _regexPrecisionCache = new();
        private const RegexOptions Z3SupportedRegexOptions =
            RegexOptions.ExplicitCapture |
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.Singleline |
            RegexOptions.Multiline |
            RegexOptions.IgnorePatternWhitespace |
            RegexOptions.IgnoreCase;

        public Z3FormulaEncoder()
        {
            _referenceSort = _context.MkUninterpretedSort("Reference");
            _nullReference = _context.MkConst("null_reference", _referenceSort);
        }

        public BoolExpr EncodeCondition(SmtFormula formula)
        {
            if (formula.Kind != SmtValueKind.Bool)
            {
                throw new InvalidOperationException("Only boolean SMT formulas can be used as conditions.");
            }

            EnsureSafeRegexPolarity(formula, isNegativeContext: false);
            return (BoolExpr)Encode(formula);
        }

        public Solver CreateSolver(TimeSpan timeout)
        {
            var solver = _context.MkSolver();
            var parameters = _context.MkParams();
            parameters.Add("timeout", GetTimeoutMilliseconds(timeout));
            solver.Parameters = parameters;
            return solver;
        }

        public BoolExpr Negate(SmtFormula formula)
        {
            return _context.MkNot(EncodeCondition(formula));
        }

        public bool ContainsApproximateRegex(SmtFormula formula)
        {
            return formula switch
            {
                SmtRegexMatchFormula regexMatch => GetRegexTranslationPrecision(regexMatch.Pattern, regexMatch.Options) == RegexTranslationPrecision.Approximate ||
                    ContainsApproximateRegex(regexMatch.Value),
                SmtRuntimeTypeTestFormula runtimeTypeTestFormula => ContainsApproximateRegex(runtimeTypeTestFormula.Value),
                SmtUnaryFormula unaryFormula => ContainsApproximateRegex(unaryFormula.Operand),
                SmtBinaryFormula binaryFormula => ContainsApproximateRegex(binaryFormula.Left) ||
                    ContainsApproximateRegex(binaryFormula.Right),
                SmtIntegerUnaryTerm integerUnaryTerm => ContainsApproximateRegex(integerUnaryTerm.Operand),
                SmtIntegerBinaryTerm integerBinaryTerm => ContainsApproximateRegex(integerBinaryTerm.Left) ||
                    ContainsApproximateRegex(integerBinaryTerm.Right),
                SmtStringLengthTerm stringLengthTerm => ContainsApproximateRegex(stringLengthTerm.Value),
                SmtStringConcatTerm stringConcatTerm => ContainsApproximateRegex(stringConcatTerm.Left) ||
                    ContainsApproximateRegex(stringConcatTerm.Right),
                SmtStringContainsFormula stringContainsFormula => ContainsApproximateRegex(stringContainsFormula.Value) ||
                    ContainsApproximateRegex(stringContainsFormula.Search),
                SmtStringStartsWithFormula stringStartsWithFormula => ContainsApproximateRegex(stringStartsWithFormula.Value) ||
                    ContainsApproximateRegex(stringStartsWithFormula.Prefix),
                SmtStringEndsWithFormula stringEndsWithFormula => ContainsApproximateRegex(stringEndsWithFormula.Value) ||
                    ContainsApproximateRegex(stringEndsWithFormula.Suffix),
                SmtConditionalFormula conditionalFormula => ContainsApproximateRegex(conditionalFormula.Condition) ||
                    ContainsApproximateRegex(conditionalFormula.WhenTrue) ||
                    ContainsApproximateRegex(conditionalFormula.WhenFalse),
                _ => false,
            };
        }

        public void Dispose()
        {
            _context.Dispose();
        }

        private Expr Encode(SmtFormula formula)
        {
            return formula switch
            {
                SmtBooleanConstant booleanConstant => booleanConstant.Value ? _context.MkTrue() : _context.MkFalse(),
                SmtIntegerConstant integerConstant => _context.MkInt(integerConstant.Value),
                SmtStringConstant stringConstant => _context.MkString(stringConstant.Value),
                SmtNullConstant => _nullReference,
                SmtVariable variable => GetOrCreateVariable(variable),
                SmtUnaryFormula unaryFormula => EncodeUnary(unaryFormula),
                SmtBinaryFormula binaryFormula => EncodeBinary(binaryFormula),
                SmtIntegerUnaryTerm integerUnaryTerm => EncodeIntegerUnary(integerUnaryTerm),
                SmtIntegerBinaryTerm integerBinaryTerm => EncodeIntegerBinary(integerBinaryTerm),
                SmtStringLengthTerm stringLengthTerm => _context.MkLength(EncodeString(stringLengthTerm.Value)),
                SmtStringConcatTerm stringConcatTerm => _context.MkConcat(
                    EncodeString(stringConcatTerm.Left),
                    EncodeString(stringConcatTerm.Right)),
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
                _ => throw new InvalidOperationException("Unsupported SMT formula node."),
            };
        }

        private Expr EncodeUnary(SmtUnaryFormula formula)
        {
            return formula.Operator switch
            {
                SmtUnaryOperator.Not => _context.MkNot(EncodeCondition(formula.Operand)),
                _ => throw new InvalidOperationException("Unsupported SMT unary operator."),
            };
        }

        private Expr EncodeIntegerUnary(SmtIntegerUnaryTerm term)
        {
            return term.Operator switch
            {
                SmtIntegerUnaryOperator.Negate => _context.MkUnaryMinus(EncodeInteger(term.Operand)),
                _ => throw new InvalidOperationException("Unsupported SMT integer unary operator."),
            };
        }

        private Expr EncodeBinary(SmtBinaryFormula formula)
        {
            return formula.Operator switch
            {
                SmtBinaryOperator.And => _context.MkAnd(EncodeCondition(formula.Left), EncodeCondition(formula.Right)),
                SmtBinaryOperator.Or => _context.MkOr(EncodeCondition(formula.Left), EncodeCondition(formula.Right)),
                SmtBinaryOperator.Equal => _context.MkEq(Encode(formula.Left), Encode(formula.Right)),
                SmtBinaryOperator.NotEqual => _context.MkNot(_context.MkEq(Encode(formula.Left), Encode(formula.Right))),
                SmtBinaryOperator.LessThan => _context.MkLt(EncodeInteger(formula.Left), EncodeInteger(formula.Right)),
                SmtBinaryOperator.LessThanOrEqual => _context.MkLe(EncodeInteger(formula.Left), EncodeInteger(formula.Right)),
                SmtBinaryOperator.GreaterThan => _context.MkGt(EncodeInteger(formula.Left), EncodeInteger(formula.Right)),
                SmtBinaryOperator.GreaterThanOrEqual => _context.MkGe(EncodeInteger(formula.Left), EncodeInteger(formula.Right)),
                _ => throw new InvalidOperationException("Unsupported SMT binary operator."),
            };
        }

        private Expr EncodeIntegerBinary(SmtIntegerBinaryTerm term)
        {
            return term.Operator switch
            {
                SmtIntegerBinaryOperator.Add => _context.MkAdd(EncodeInteger(term.Left), EncodeInteger(term.Right)),
                SmtIntegerBinaryOperator.Subtract => _context.MkSub(EncodeInteger(term.Left), EncodeInteger(term.Right)),
                SmtIntegerBinaryOperator.Multiply => _context.MkMul(EncodeInteger(term.Left), EncodeInteger(term.Right)),
                SmtIntegerBinaryOperator.Divide => EncodeCSharpIntegerDivide(term),
                SmtIntegerBinaryOperator.Remainder => EncodeCSharpIntegerRemainder(term),
                _ => throw new InvalidOperationException("Unsupported SMT integer binary operator."),
            };
        }

        private ArithExpr EncodeCSharpIntegerDivide(SmtIntegerBinaryTerm term)
        {
            var left = EncodeInteger(term.Left);
            var right = EncodeInteger(term.Right);
            return EncodeCSharpIntegerDivide(left, right);
        }

        private ArithExpr EncodeCSharpIntegerRemainder(SmtIntegerBinaryTerm term)
        {
            var left = EncodeInteger(term.Left);
            var right = EncodeInteger(term.Right);
            var quotient = EncodeCSharpIntegerDivide(left, right);
            return _context.MkSub(left, _context.MkMul(quotient, right));
        }

        private ArithExpr EncodeCSharpIntegerDivide(ArithExpr left, ArithExpr right)
        {
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

        private Expr EncodeConditional(SmtConditionalFormula formula)
        {
            return _context.MkITE(
                EncodeCondition(formula.Condition),
                Encode(formula.WhenTrue),
                Encode(formula.WhenFalse));
        }

        private ArithExpr EncodeInteger(SmtFormula formula)
        {
            if (formula.Kind != SmtValueKind.Int)
            {
                throw new InvalidOperationException("Only integer SMT formulas can be encoded as arithmetic expressions.");
            }

            return (ArithExpr)Encode(formula);
        }

        private SeqExpr EncodeString(SmtFormula formula)
        {
            if (formula.Kind != SmtValueKind.String)
            {
                throw new InvalidOperationException("Only string SMT formulas can be encoded as string expressions.");
            }

            return (SeqExpr)Encode(formula);
        }

        private BoolExpr EncodeRegexMatch(SmtRegexMatchFormula formula)
        {
            if (!CanEncodeRegexOptions(formula.Options))
            {
                throw new InvalidOperationException("Unsupported SMT regex options.");
            }

            if (!Z3RegexTranslator.TryTranslate(_context, formula.Pattern, formula.Options, out var regex, out _))
            {
                throw new InvalidOperationException("Unsupported SMT regex pattern.");
            }

            return _context.MkInRe(EncodeString(formula.Value), regex);
        }

        private BoolExpr EncodeRuntimeTypeTest(SmtRuntimeTypeTestFormula formula)
        {
            if (formula.Value.Kind != SmtValueKind.Reference)
            {
                throw new InvalidOperationException("Only reference SMT formulas can be used in runtime type tests.");
            }

            if (!_runtimeTypeTests.TryGetValue(formula.TypeKey, out var predicate))
            {
                predicate = _context.MkFuncDecl(
                    "runtime_type_test:" + SanitizeSymbolName(formula.TypeKey),
                    new[] { _referenceSort },
                    _context.BoolSort);
                _runtimeTypeTests.Add(formula.TypeKey, predicate);
            }

            return (BoolExpr)_context.MkApp(predicate, EncodeReference(formula.Value));
        }

        private Expr EncodeReference(SmtFormula formula)
        {
            if (formula.Kind != SmtValueKind.Reference)
            {
                throw new InvalidOperationException("Only reference SMT formulas can be encoded as reference expressions.");
            }

            return Encode(formula);
        }

        private void EnsureSafeRegexPolarity(SmtFormula formula, bool isNegativeContext)
        {
            switch (formula)
            {
                case SmtRegexMatchFormula regexMatch:
                    if (!CanEncodeRegexOptions(regexMatch.Options))
                    {
                        throw new InvalidOperationException("Unsupported SMT regex options.");
                    }

                    if (isNegativeContext && IsApproximateRegexPattern(regexMatch.Pattern, regexMatch.Options))
                    {
                        throw new InvalidOperationException("Approximate SMT regex patterns cannot be safely negated.");
                    }

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

        private void EnsureSafeRegexPolarity(SmtBinaryFormula formula, bool isNegativeContext)
        {
            if (formula.Operator is SmtBinaryOperator.And or SmtBinaryOperator.Or)
            {
                EnsureSafeRegexPolarity(formula.Left, isNegativeContext);
                EnsureSafeRegexPolarity(formula.Right, isNegativeContext);
                return;
            }

            if ((formula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual) &&
                formula.Left.Kind == SmtValueKind.Bool &&
                formula.Right.Kind == SmtValueKind.Bool)
            {
                EnsureSafeBooleanComparisonRegexPolarity(formula, isNegativeContext);
                return;
            }

            EnsureSafeRegexInTerm(formula.Left);
            EnsureSafeRegexInTerm(formula.Right);
        }

        private void EnsureSafeBooleanComparisonRegexPolarity(SmtBinaryFormula formula, bool isNegativeContext)
        {
            if (formula.Left is SmtBooleanConstant leftConstant)
            {
                EnsureSafeRegexPolarity(
                    formula.Right,
                    GetBooleanComparisonOperandPolarity(formula.Operator, leftConstant.Value, isNegativeContext));
                return;
            }

            if (formula.Right is SmtBooleanConstant rightConstant)
            {
                EnsureSafeRegexPolarity(
                    formula.Left,
                    GetBooleanComparisonOperandPolarity(formula.Operator, rightConstant.Value, isNegativeContext));
                return;
            }

            EnsureExactRegexUse(formula.Left);
            EnsureExactRegexUse(formula.Right);
        }

        private void EnsureSafeRegexInTerm(SmtFormula formula)
        {
            switch (formula)
            {
                case SmtIntegerUnaryTerm integerUnaryTerm:
                    EnsureSafeRegexInTerm(integerUnaryTerm.Operand);
                    return;
                case SmtIntegerBinaryTerm integerBinaryTerm:
                    EnsureSafeRegexInTerm(integerBinaryTerm.Left);
                    EnsureSafeRegexInTerm(integerBinaryTerm.Right);
                    return;
                case SmtStringLengthTerm stringLengthTerm:
                    EnsureSafeRegexInTerm(stringLengthTerm.Value);
                    return;
                case SmtStringConcatTerm stringConcatTerm:
                    EnsureSafeRegexInTerm(stringConcatTerm.Left);
                    EnsureSafeRegexInTerm(stringConcatTerm.Right);
                    return;
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    EnsureSafeRegexInTerm(runtimeTypeTest.Value);
                    return;
                case SmtConditionalFormula conditionalFormula:
                    EnsureExactRegexUse(conditionalFormula.Condition);
                    EnsureSafeRegexInTerm(conditionalFormula.WhenTrue);
                    EnsureSafeRegexInTerm(conditionalFormula.WhenFalse);
                    return;
            }
        }

        private void EnsureExactRegexUse(SmtFormula formula)
        {
            switch (formula)
            {
                case SmtRegexMatchFormula regexMatch:
                    if (!CanEncodeRegexOptions(regexMatch.Options))
                    {
                        throw new InvalidOperationException("Unsupported SMT regex options.");
                    }

                    if (IsApproximateRegexPattern(regexMatch.Pattern, regexMatch.Options))
                    {
                        throw new InvalidOperationException("Approximate SMT regex patterns require positive polarity.");
                    }

                    EnsureSafeRegexInTerm(regexMatch.Value);
                    return;
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    EnsureExactRegexUse(runtimeTypeTest.Value);
                    return;
                case SmtUnaryFormula unaryFormula:
                    EnsureExactRegexUse(unaryFormula.Operand);
                    return;
                case SmtBinaryFormula binaryFormula:
                    EnsureExactRegexUse(binaryFormula.Left);
                    EnsureExactRegexUse(binaryFormula.Right);
                    return;
                case SmtIntegerUnaryTerm integerUnaryTerm:
                    EnsureExactRegexUse(integerUnaryTerm.Operand);
                    return;
                case SmtIntegerBinaryTerm integerBinaryTerm:
                    EnsureExactRegexUse(integerBinaryTerm.Left);
                    EnsureExactRegexUse(integerBinaryTerm.Right);
                    return;
                case SmtStringLengthTerm stringLengthTerm:
                    EnsureExactRegexUse(stringLengthTerm.Value);
                    return;
                case SmtStringConcatTerm stringConcatTerm:
                    EnsureExactRegexUse(stringConcatTerm.Left);
                    EnsureExactRegexUse(stringConcatTerm.Right);
                    return;
                case SmtStringContainsFormula stringContainsFormula:
                    EnsureExactRegexUse(stringContainsFormula.Value);
                    EnsureExactRegexUse(stringContainsFormula.Search);
                    return;
                case SmtStringStartsWithFormula stringStartsWithFormula:
                    EnsureExactRegexUse(stringStartsWithFormula.Value);
                    EnsureExactRegexUse(stringStartsWithFormula.Prefix);
                    return;
                case SmtStringEndsWithFormula stringEndsWithFormula:
                    EnsureExactRegexUse(stringEndsWithFormula.Value);
                    EnsureExactRegexUse(stringEndsWithFormula.Suffix);
                    return;
                case SmtConditionalFormula conditionalFormula:
                    EnsureExactRegexUse(conditionalFormula.Condition);
                    EnsureExactRegexUse(conditionalFormula.WhenTrue);
                    EnsureExactRegexUse(conditionalFormula.WhenFalse);
                    return;
            }
        }

        private bool IsApproximateRegexPattern(string pattern, RegexOptions options)
        {
            return GetRegexTranslationPrecision(pattern, options) == RegexTranslationPrecision.Approximate;
        }

        private RegexTranslationPrecision GetRegexTranslationPrecision(string pattern, RegexOptions options)
        {
            var key = (pattern, options);
            if (_regexPrecisionCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var precision = Z3RegexTranslator.TryTranslate(_context, pattern, options, out _, out var isExact)
                ? isExact
                    ? RegexTranslationPrecision.Exact
                    : RegexTranslationPrecision.Approximate
                : RegexTranslationPrecision.Unsupported;
            _regexPrecisionCache.Add(key, precision);
            return precision;
        }

        private static bool CanEncodeRegexOptions(RegexOptions options)
        {
            if ((options & ~Z3SupportedRegexOptions) != 0)
            {
                return false;
            }

            return (options & RegexOptions.IgnoreCase) == 0 ||
                (options & RegexOptions.CultureInvariant) != 0;
        }

        private static bool GetBooleanComparisonOperandPolarity(
            SmtBinaryOperator op,
            bool constantValue,
            bool isNegativeContext)
        {
            var preservesPolarity =
                (op == SmtBinaryOperator.Equal && constantValue) ||
                (op == SmtBinaryOperator.NotEqual && !constantValue);
            return preservesPolarity ? isNegativeContext : !isNegativeContext;
        }

        private static uint GetTimeoutMilliseconds(TimeSpan timeout)
        {
            var totalMilliseconds = timeout.TotalMilliseconds;
            if (totalMilliseconds >= uint.MaxValue)
            {
                return uint.MaxValue;
            }

            return (uint)Math.Max(1, totalMilliseconds);
        }

        private static string SanitizeSymbolName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            var buffer = new char[value.Length];
            for (var index = 0; index < value.Length; index++)
            {
                var ch = value[index];
                buffer[index] = char.IsLetterOrDigit(ch) || ch == '_' || ch == '.'
                    ? ch
                    : '_';
            }

            return new string(buffer);
        }

        private Expr GetOrCreateVariable(SmtVariable variable)
        {
            var key = (variable.Name, variable.Kind);
            if (_variables.TryGetValue(key, out var existing))
            {
                return existing;
            }

            Expr created = variable.Kind switch
            {
                SmtValueKind.Bool => _context.MkBoolConst(variable.Name),
                SmtValueKind.Int => _context.MkIntConst(variable.Name),
                SmtValueKind.Reference => _context.MkConst(variable.Name, _referenceSort),
                SmtValueKind.String => _context.MkConst(variable.Name, _context.StringSort),
                _ => throw new InvalidOperationException("Unsupported SMT variable kind."),
            };

            _variables.Add(key, created);
            return created;
        }

        private enum RegexTranslationPrecision
        {
            Unsupported,
            Exact,
            Approximate,
        }

        private sealed class Z3RegexTranslator
        {
            private const int MaxBoundedRepeat = 64;
            // Keep large Unicode category unions conservative; Z3 range-heavy regexes can get expensive
            // and smaller shorthand/category classes cover the common analyzer facts precisely.
            private const int MaxCharacterClassRangeCount = 512;
            private static readonly TimeSpan RegexSyntaxValidationTimeout = TimeSpan.FromMilliseconds(50);
            private static readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), CharacterRange[]> RegexCharacterRangeCache = new();
            private static readonly Lazy<CharacterRange[]> DecimalDigitRanges = new(() => CreateRegexCharacterRanges((@"\d", RegexOptions.None)));
            private static readonly Lazy<CharacterRange[]> WhitespaceRanges = new(() => CreateRegexCharacterRanges((@"\s", RegexOptions.None)));
            private static readonly Lazy<CharacterRange[]> WordRanges = new(() => CreateRegexCharacterRanges((@"\w", RegexOptions.None)));
            private readonly Context _context;
            private readonly string _pattern;
            private readonly Dictionary<string, RegexClassTranslation> _characterClassCache = new(StringComparer.Ordinal);
            private bool _isExact = true;
            private bool _ignorePatternWhitespace;
            private bool _ignoreCase;
            private readonly bool _canUseIgnoreCase;
            private bool _singleline;
            private int _position;

            private Z3RegexTranslator(Context context, string pattern, RegexOptions options)
            {
                _context = context;
                _pattern = pattern;
                _ignorePatternWhitespace = (options & RegexOptions.IgnorePatternWhitespace) != 0;
                _ignoreCase = (options & RegexOptions.IgnoreCase) != 0;
                _canUseIgnoreCase = (options & RegexOptions.CultureInvariant) != 0;
                _singleline = (options & RegexOptions.Singleline) != 0;
            }

            public static bool TryTranslate(Context context, string pattern, out ReExpr regex, out bool isExact)
            {
                return TryTranslate(context, pattern, RegexOptions.None, out regex, out isExact);
            }

            public static bool TryTranslate(Context context, string pattern, RegexOptions options, out ReExpr regex, out bool isExact)
            {
                regex = null!;
                isExact = true;
                if (pattern.Length > 256)
                {
                    return false;
                }

                if (!IsValidDotNetRegexPattern(pattern, options))
                {
                    return false;
                }

                var multiline = (options & RegexOptions.Multiline) != 0;
                var startAnchored = TryFindLeadingStartAnchor(
                    pattern,
                    options,
                    allowCaretAnchor: !multiline,
                    out var startAnchorStart,
                    out var startAnchorLength);
                var strictEndAnchored = EndsWithUnescapedAnchor(pattern, @"\z");
                var finalNewlineEndAnchored = !strictEndAnchored && EndsWithUnescapedAnchor(pattern, @"\Z");
                var dollarEndAnchored = !strictEndAnchored &&
                    !finalNewlineEndAnchored &&
                    !multiline &&
                    pattern.EndsWith("$", StringComparison.Ordinal) &&
                    !IsEscaped(pattern, pattern.Length - 1);
                var bodyEndTrim = strictEndAnchored || finalNewlineEndAnchored ? 2 : dollarEndAnchored ? 1 : 0;
                var bodyEnd = pattern.Length - bodyEndTrim;
                if (bodyEnd < 0 ||
                    (startAnchored && startAnchorStart + startAnchorLength > bodyEnd))
                {
                    return false;
                }

                var bodyPattern = pattern.Substring(0, bodyEnd);
                if (startAnchored)
                {
                    bodyPattern = bodyPattern.Remove(startAnchorStart, startAnchorLength);
                }

                var translator = new Z3RegexTranslator(context, bodyPattern, options);
                if (!translator.TryParseExpression(out var body))
                {
                    return false;
                }

                translator.SkipIgnoredPatternTrivia();
                if (translator._position != translator._pattern.Length)
                {
                    return false;
                }

                regex = body;
                isExact = translator._isExact;
                if (!startAnchored)
                {
                    regex = context.MkConcat(new[] { translator.CreateAnyStringRegex(), regex });
                }

                if (dollarEndAnchored || finalNewlineEndAnchored)
                {
                    regex = context.MkConcat(new[] { regex, translator.CreateOptionalFinalNewlineRegex() });
                }
                else if (!strictEndAnchored)
                {
                    regex = context.MkConcat(new[] { regex, translator.CreateAnyStringRegex() });
                }

                return true;
            }

            private static bool TryFindLeadingStartAnchor(
                string pattern,
                RegexOptions options,
                bool allowCaretAnchor,
                out int anchorStart,
                out int anchorLength)
            {
                anchorStart = -1;
                anchorLength = 0;
                var index = 0;
                var optionScope = CreateInitialOptionScope(options);
                var canUseIgnoreCase = (options & RegexOptions.CultureInvariant) != 0;
                while (true)
                {
                    SkipIgnoredPatternTrivia(pattern, ref index, optionScope.IgnorePatternWhitespace);
                    if (!TryReadInlineOptionGroup(pattern, index, optionScope, canUseIgnoreCase, out var nextScope, out var nextIndex))
                    {
                        break;
                    }

                    optionScope = nextScope;
                    index = nextIndex;
                }

                if (index >= pattern.Length)
                {
                    return false;
                }

                if (pattern[index] == '^' && allowCaretAnchor)
                {
                    anchorStart = index;
                    anchorLength = 1;
                    return true;
                }

                if (index + 1 < pattern.Length &&
                    pattern[index] == '\\' &&
                    pattern[index + 1] is 'A' or 'G')
                {
                    anchorStart = index;
                    anchorLength = 2;
                    return true;
                }

                return false;
            }

            private static RegexOptionScope CreateInitialOptionScope(RegexOptions options)
            {
                return new RegexOptionScope(
                    (options & RegexOptions.IgnorePatternWhitespace) != 0,
                    (options & RegexOptions.Singleline) != 0,
                    (options & RegexOptions.IgnoreCase) != 0);
            }

            private static bool TryReadInlineOptionGroup(
                string pattern,
                int start,
                RegexOptionScope currentScope,
                bool canUseIgnoreCase,
                out RegexOptionScope nextScope,
                out int nextIndex)
            {
                nextScope = currentScope;
                nextIndex = start;
                if (start + 2 >= pattern.Length ||
                    pattern[start] != '(' ||
                    pattern[start + 1] != '?')
                {
                    return false;
                }

                var index = start + 2;
                var nextIgnorePatternWhitespace = currentScope.IgnorePatternWhitespace;
                var nextSingleline = currentScope.Singleline;
                var nextIgnoreCase = currentScope.IgnoreCase;
                var sawOption = false;
                var sawDisableSeparator = false;
                while (index < pattern.Length && pattern[index] != ')')
                {
                    var current = pattern[index];
                    if (current == '-')
                    {
                        if (sawDisableSeparator)
                        {
                            return false;
                        }

                        sawDisableSeparator = true;
                        index++;
                        continue;
                    }

                    if (current == 'n')
                    {
                        sawOption = true;
                        index++;
                        continue;
                    }

                    if (current == 'x')
                    {
                        sawOption = true;
                        nextIgnorePatternWhitespace = !sawDisableSeparator;
                        index++;
                        continue;
                    }

                    if (current == 's')
                    {
                        sawOption = true;
                        nextSingleline = !sawDisableSeparator;
                        index++;
                        continue;
                    }

                    if (current == 'i' && canUseIgnoreCase)
                    {
                        sawOption = true;
                        nextIgnoreCase = !sawDisableSeparator;
                        index++;
                        continue;
                    }

                    return false;
                }

                if (!sawOption ||
                    index >= pattern.Length ||
                    pattern[index] != ')')
                {
                    return false;
                }

                nextScope = new RegexOptionScope(nextIgnorePatternWhitespace, nextSingleline, nextIgnoreCase);
                nextIndex = index + 1;
                return true;
            }

            private bool TryParseExpression(out ReExpr regex)
            {
                SkipIgnoredPatternTrivia();
                var alternatives = new List<ReExpr>();
                if (!TryParseConcat(out var first))
                {
                    regex = null!;
                    return false;
                }

                alternatives.Add(first);
                SkipIgnoredPatternTrivia();
                while (Peek('|'))
                {
                    _position++;
                    SkipIgnoredPatternTrivia();
                    if (!TryParseConcat(out var alternative))
                    {
                        regex = null!;
                        return false;
                    }

                    alternatives.Add(alternative);
                    SkipIgnoredPatternTrivia();
                }

                regex = alternatives.Count == 1
                    ? alternatives[0]
                    : _context.MkUnion(alternatives.ToArray());
                return true;
            }

            private bool TryParseConcat(out ReExpr regex)
            {
                return TryParseConcat(out regex, out _);
            }

            private bool TryParseConcat(out ReExpr regex, out bool consumedAny)
            {
                var parts = new List<ReExpr>();
                consumedAny = false;
                while (true)
                {
                    SkipIgnoredPatternTrivia();
                    if (_position >= _pattern.Length ||
                        Peek('|') ||
                        Peek(')'))
                    {
                        break;
                    }

                    if (TryParseLookaheadAssertion(out var lookahead))
                    {
                        if (!TryParseConcat(out var suffix, out var suffixConsumed) || !suffixConsumed)
                        {
                            regex = null!;
                            return false;
                        }

                        parts.Add(ConstrainSuffixWithLookahead(lookahead, suffix));
                        consumedAny = true;
                        regex = parts.Count == 1 ? parts[0] : _context.MkConcat(parts.ToArray());
                        return true;
                    }

                    if (TryParseLookbehindAssertion(out var lookbehind))
                    {
                        if (parts.Count == 0)
                        {
                            regex = null!;
                            return false;
                        }

                        var prefix = parts.Count == 1 ? parts[0] : _context.MkConcat(parts.ToArray());
                        parts.Clear();
                        parts.Add(ConstrainPrefixWithLookbehind(lookbehind, prefix));
                        consumedAny = true;
                        continue;
                    }

                    if (TryParseWordBoundaryAssertion(out var wordBoundary))
                    {
                        var prefix = parts.Count == 0
                            ? CreateLiteralRegex(string.Empty)
                            : parts.Count == 1
                                ? parts[0]
                                : _context.MkConcat(parts.ToArray());
                        if (!TryParseConcat(out var suffix, out var suffixConsumed) ||
                            !TryConstrainSplitWithWordBoundary(prefix, suffix, wordBoundary, out var constrained))
                        {
                            regex = null!;
                            return false;
                        }

                        consumedAny |= suffixConsumed;
                        regex = constrained;
                        return true;
                    }

                    if (!TryParseRepeat(out var part))
                    {
                        regex = null!;
                        return false;
                    }

                    parts.Add(part);
                    consumedAny = true;
                }

                regex = parts.Count switch
                {
                    0 => CreateLiteralRegex(string.Empty),
                    1 => parts[0],
                    _ => _context.MkConcat(parts.ToArray())
                };
                return true;
            }

            private bool TryParseLookaheadAssertion(out RegexLookaheadAssertion assertion)
            {
                assertion = default;
                SkipIgnoredPatternTrivia();
                var savedPosition = _position;
                var savedOptions = CaptureOptions();
                var savedIsExact = _isExact;
                if (_position + 2 >= _pattern.Length ||
                    _pattern[_position] != '(' ||
                    _pattern[_position + 1] != '?' ||
                    _pattern[_position + 2] is not ('=' or '!'))
                {
                    return false;
                }

                var positive = _pattern[_position + 2] == '=';
                _position += 3;
                if (!TryParseExpression(out var lookaheadRegex) || !Peek(')'))
                {
                    _position = savedPosition;
                    ApplyOptions(savedOptions);
                    _isExact = savedIsExact;
                    return false;
                }

                _position++;
                var lookaheadIsExact = _isExact;
                ApplyOptions(savedOptions);
                _isExact = savedIsExact;
                if (!positive && !lookaheadIsExact)
                {
                    _position = savedPosition;
                    return false;
                }

                assertion = new RegexLookaheadAssertion(lookaheadRegex, positive, lookaheadIsExact);
                return true;
            }

            private bool TryParseLookbehindAssertion(out RegexLookaheadAssertion assertion)
            {
                assertion = default;
                SkipIgnoredPatternTrivia();
                var savedPosition = _position;
                var savedOptions = CaptureOptions();
                var savedIsExact = _isExact;
                if (_position + 3 >= _pattern.Length ||
                    _pattern[_position] != '(' ||
                    _pattern[_position + 1] != '?' ||
                    _pattern[_position + 2] != '<' ||
                    _pattern[_position + 3] is not ('=' or '!'))
                {
                    return false;
                }

                var positive = _pattern[_position + 3] == '=';
                _position += 4;
                if (!TryParseExpression(out var lookbehindRegex) || !Peek(')'))
                {
                    _position = savedPosition;
                    ApplyOptions(savedOptions);
                    _isExact = savedIsExact;
                    return false;
                }

                _position++;
                var lookbehindIsExact = _isExact;
                ApplyOptions(savedOptions);
                _isExact = savedIsExact;
                if (!positive && !lookbehindIsExact)
                {
                    _position = savedPosition;
                    return false;
                }

                assertion = new RegexLookaheadAssertion(lookbehindRegex, positive, lookbehindIsExact);
                return true;
            }

            private bool TryParseWordBoundaryAssertion(out bool isBoundary)
            {
                isBoundary = false;
                SkipIgnoredPatternTrivia();
                if (_position + 1 >= _pattern.Length ||
                    _pattern[_position] != '\\' ||
                    _pattern[_position + 1] is not ('b' or 'B'))
                {
                    return false;
                }

                isBoundary = _pattern[_position + 1] == 'b';
                _position += 2;
                // Word-boundary assertions are modeled well enough to prove contradictions, but SAT
                // still needs a concrete .NET witness before it can become a reachability proof.
                _isExact = false;
                return true;
            }

            private ReExpr ConstrainSuffixWithLookahead(RegexLookaheadAssertion assertion, ReExpr suffix)
            {
                _isExact &= assertion.IsExact;
                var lookaheadLanguage = CreateConcat(assertion.Regex, CreateAnyStringRegex());
                return assertion.IsPositive
                    ? _context.MkIntersect(new[] { suffix, lookaheadLanguage })
                    : _context.MkDiff(suffix, lookaheadLanguage);
            }

            private ReExpr ConstrainPrefixWithLookbehind(RegexLookaheadAssertion assertion, ReExpr prefix)
            {
                _isExact &= assertion.IsExact;
                var lookbehindLanguage = CreateConcat(CreateAnyStringRegex(), assertion.Regex);
                return assertion.IsPositive
                    ? _context.MkIntersect(new[] { prefix, lookbehindLanguage })
                    : _context.MkDiff(prefix, lookbehindLanguage);
            }

            private bool TryConstrainSplitWithWordBoundary(
                ReExpr prefix,
                ReExpr suffix,
                bool isBoundary,
                out ReExpr regex)
            {
                regex = null!;
                if (!TryCreateCharacterRangesRegex(WordRanges.Value, out var wordChar))
                {
                    return false;
                }

                var nonWordChar = _context.MkDiff(CreateAnyCharRegex(), wordChar);
                var leftWord = ConstrainPrefixEnd(prefix, wordChar);
                var leftNonWord = _context.MkUnion(new[]
                {
                    ConstrainPrefixEnd(prefix, nonWordChar),
                    _context.MkIntersect(new[] { prefix, CreateLiteralRegex(string.Empty) }),
                });
                var rightWord = ConstrainSuffixStart(suffix, wordChar);
                var rightNonWord = _context.MkUnion(new[]
                {
                    ConstrainSuffixStart(suffix, nonWordChar),
                    _context.MkIntersect(new[] { suffix, CreateLiteralRegex(string.Empty) }),
                });

                var first = CreateConcat(leftWord, isBoundary ? rightNonWord : rightWord);
                var second = CreateConcat(leftNonWord, isBoundary ? rightWord : rightNonWord);
                regex = _context.MkUnion(new[] { first, second });
                return true;
            }

            private ReExpr ConstrainPrefixEnd(ReExpr prefix, ReExpr finalCharacter)
            {
                return _context.MkIntersect(new[]
                {
                    prefix,
                    CreateConcat(CreateAnyStringRegex(), finalCharacter),
                });
            }

            private ReExpr ConstrainSuffixStart(ReExpr suffix, ReExpr firstCharacter)
            {
                return _context.MkIntersect(new[]
                {
                    suffix,
                    CreateConcat(firstCharacter, CreateAnyStringRegex()),
                });
            }

            private readonly struct RegexLookaheadAssertion
            {
                public RegexLookaheadAssertion(ReExpr regex, bool isPositive, bool isExact)
                {
                    Regex = regex;
                    IsPositive = isPositive;
                    IsExact = isExact;
                }

                public ReExpr Regex { get; }

                public bool IsPositive { get; }

                public bool IsExact { get; }
            }

            private bool TryParseRepeat(out ReExpr regex)
            {
                SkipIgnoredPatternTrivia();
                if (!TryParseAtom(out regex))
                {
                    return false;
                }

                SkipIgnoredPatternTrivia();
                if (_position >= _pattern.Length)
                {
                    return true;
                }

                switch (_pattern[_position])
                {
                    case '*':
                        _position++;
                        regex = _context.MkStar(regex);
                        ConsumeNonGreedyMarker();
                        return true;
                    case '+':
                        _position++;
                        regex = _context.MkPlus(regex);
                        ConsumeNonGreedyMarker();
                        return true;
                    case '?':
                        _position++;
                        regex = _context.MkOption(regex);
                        ConsumeNonGreedyMarker();
                        return true;
                    case '{':
                        return TryParseBoundedRepeat(ref regex);
                    default:
                        return true;
                }
            }

            private bool TryParseBoundedRepeat(ref ReExpr regex)
            {
                var savedPosition = _position;
                _position++;
                if (!TryReadNumber(out var lower))
                {
                    _position = savedPosition;
                    return true;
                }

                uint upper = lower;
                var unbounded = false;
                if (Peek(','))
                {
                    _position++;
                    if (Peek('}'))
                    {
                        unbounded = true;
                    }
                    else if (!TryReadNumber(out upper))
                    {
                        return false;
                    }
                }

                if (!Peek('}') ||
                    upper < lower ||
                    lower > MaxBoundedRepeat ||
                    (!unbounded && upper > MaxBoundedRepeat))
                {
                    return false;
                }

                _position++;
                regex = unbounded
                    ? CreateConcat(CreateExactRepeat(regex, lower), _context.MkStar(regex))
                    : _context.MkLoop(regex, lower, upper);
                ConsumeNonGreedyMarker();
                return true;
            }

            private bool TryParseAtom(out ReExpr regex)
            {
                regex = null!;
                SkipIgnoredPatternTrivia();
                if (_position >= _pattern.Length)
                {
                    return false;
                }

                var current = _pattern[_position++];
                switch (current)
                {
                    case '(':
                        if (TryParseInlineOptionGroup(out var inlineOptions))
                        {
                            ApplyOptions(inlineOptions);
                            regex = CreateLiteralRegex(string.Empty);
                            return true;
                        }

                        var outerOptions = CaptureOptions();
                        if (!TryParseGroupPrefix(out var groupOptions))
                        {
                            return false;
                        }

                        ApplyOptions(groupOptions);
                        if (!TryParseExpression(out regex) || !Peek(')'))
                        {
                            ApplyOptions(outerOptions);
                            return false;
                        }

                        _position++;
                        ApplyOptions(outerOptions);
                        return true;
                    case '[':
                        return TryParseCharClass(out regex);
                    case '.':
                        regex = CreateDotRegex();
                        return true;
                    case '\\':
                        return TryParseEscapedAtom(out regex);
                    case '^':
                    case '$':
                        return false;
                    default:
                        if (IsRegexMetaCharacter(current))
                        {
                            return false;
                        }

                        regex = CreateLiteralRegex(current.ToString());
                        return true;
                }
            }

            private RegexOptionScope CaptureOptions()
            {
                return new RegexOptionScope(_ignorePatternWhitespace, _singleline, _ignoreCase);
            }

            private void ApplyOptions(RegexOptionScope options)
            {
                _ignorePatternWhitespace = options.IgnorePatternWhitespace;
                _singleline = options.Singleline;
                _ignoreCase = options.IgnoreCase;
            }

            private bool TryParseInlineOptionGroup(out RegexOptionScope groupOptions)
            {
                groupOptions = CaptureOptions();
                var savedPosition = _position;
                if (!Peek('?'))
                {
                    return false;
                }

                _position++;
                if (TryParseRegexOptionsUntil(')', out groupOptions))
                {
                    return true;
                }

                _position = savedPosition;
                groupOptions = CaptureOptions();
                return false;
            }

            private bool TryParseGroupPrefix(out RegexOptionScope groupOptions)
            {
                groupOptions = CaptureOptions();
                if (!Peek('?'))
                {
                    return true;
                }

                _position++;
                if (Peek(':'))
                {
                    _position++;
                    return true;
                }

                if (Peek('>'))
                {
                    _position++;
                    // Atomic grouping can only remove matches by preventing backtracking.
                    _isExact = false;
                    return true;
                }

                if (TryParseOptionGroupPrefix(out groupOptions))
                {
                    return true;
                }

                groupOptions = CaptureOptions();
                return TryParseNamedCaptureGroupPrefix();
            }

            private bool TryParseOptionGroupPrefix(out RegexOptionScope groupOptions)
            {
                var savedPosition = _position;
                if (TryParseRegexOptionsUntil(':', out groupOptions))
                {
                    return true;
                }

                _position = savedPosition;
                groupOptions = CaptureOptions();
                return false;
            }

            private bool TryParseRegexOptionsUntil(char terminator, out RegexOptionScope groupOptions)
            {
                groupOptions = CaptureOptions();
                var nextIgnorePatternWhitespace = groupOptions.IgnorePatternWhitespace;
                var nextSingleline = groupOptions.Singleline;
                var nextIgnoreCase = groupOptions.IgnoreCase;
                var sawOption = false;
                var sawDisableSeparator = false;
                while (_position < _pattern.Length && !Peek(terminator))
                {
                    var current = _pattern[_position];
                    if (current == '-')
                    {
                        if (sawDisableSeparator)
                        {
                            return false;
                        }

                        sawDisableSeparator = true;
                        _position++;
                        continue;
                    }

                    if (current == 'n')
                    {
                        sawOption = true;
                        _position++;
                        continue;
                    }

                    if (current == 'x')
                    {
                        sawOption = true;
                        nextIgnorePatternWhitespace = !sawDisableSeparator;
                        _position++;
                        continue;
                    }

                    if (current == 's')
                    {
                        sawOption = true;
                        nextSingleline = !sawDisableSeparator;
                        _position++;
                        continue;
                    }

                    if (current == 'i' && _canUseIgnoreCase)
                    {
                        sawOption = true;
                        nextIgnoreCase = !sawDisableSeparator;
                        _position++;
                        continue;
                    }

                    return false;
                }

                if (!sawOption || !Peek(terminator))
                {
                    return false;
                }

                _position++;
                groupOptions = new RegexOptionScope(nextIgnorePatternWhitespace, nextSingleline, nextIgnoreCase);
                return true;
            }

            private bool TryParseNamedCaptureGroupPrefix()
            {
                if (Peek('<'))
                {
                    if (_position + 1 >= _pattern.Length ||
                        _pattern[_position + 1] is '=' or '!')
                    {
                        return false;
                    }

                    _position++;
                    return TryReadCaptureName('>');
                }

                if (Peek('\''))
                {
                    _position++;
                    return TryReadCaptureName('\'');
                }

                return false;
            }

            private bool TryReadCaptureName(char terminator)
            {
                var start = _position;
                while (_position < _pattern.Length)
                {
                    var current = _pattern[_position];
                    if (current == terminator)
                    {
                        if (_position == start)
                        {
                            return false;
                        }

                        _position++;
                        return true;
                    }

                    if (!IsSupportedCaptureNameCharacter(current))
                    {
                        return false;
                    }

                    _position++;
                }

                return false;
            }

            private bool TryParseEscapedAtom(out ReExpr regex)
            {
                regex = null!;
                if (_position >= _pattern.Length)
                {
                    return false;
                }

                var escaped = _pattern[_position++];
                if (TryCreateEscapedCharacterClassRegex(escaped, out var escapedClass))
                {
                    _isExact &= escapedClass.IsExact;
                    regex = escapedClass.Regex;
                    return true;
                }

                if (escaped == 'x')
                {
                    if (!TryReadFixedHexChar(2, out var hexChar))
                    {
                        return false;
                    }

                    regex = CreateLiteralRegex(hexChar.ToString());
                    return true;
                }

                if (escaped == 'u')
                {
                    if (!TryReadFixedHexChar(4, out var unicodeChar))
                    {
                        return false;
                    }

                    regex = CreateLiteralRegex(unicodeChar.ToString());
                    return true;
                }

                if (escaped == 'c')
                {
                    if (!TryReadControlCharacterEscape(out var controlChar))
                    {
                        return false;
                    }

                    regex = CreateLiteralRegex(controlChar.ToString());
                    return true;
                }

                if (escaped == '0')
                {
                    regex = CreateLiteralRegex(ReadNullPrefixedOctalEscape().ToString());
                    return true;
                }

                var literal = escaped switch
                {
                    'a' => "\a",
                    'e' => "\u001b",
                    'f' => "\f",
                    'n' => "\n",
                    'r' => "\r",
                    't' => "\t",
                    'v' => "\v",
                    '\\' => "\\",
                    '.' => ".",
                    '^' => "^",
                    '$' => "$",
                    '|' => "|",
                    '?' => "?",
                    '*' => "*",
                    '+' => "+",
                    '(' => "(",
                    ')' => ")",
                    '[' => "[",
                    ']' => "]",
                    '{' => "{",
                    '}' => "}",
                    '-' => "-",
                    _ => null
                };

                if (escaped is 'b' or 'B')
                {
                    return false;
                }

                if (literal == null)
                {
                    if (!IsEscapedLiteralCharacter(escaped))
                    {
                        return false;
                    }

                    literal = escaped.ToString();
                }

                regex = CreateLiteralRegex(literal);
                return true;
            }

            private readonly struct CharacterClassPart
            {
                public CharacterClassPart(
                    ReExpr regex,
                    char? exactCharacter,
                    bool isApproximation,
                    CharacterRange[]? ranges)
                {
                    Regex = regex;
                    ExactCharacter = exactCharacter;
                    IsApproximation = isApproximation;
                    Ranges = ranges;
                }

                public ReExpr Regex { get; }
                public char? ExactCharacter { get; }
                public bool IsApproximation { get; }
                public CharacterRange[]? Ranges { get; }
            }

            private bool TryParseCharClass(out ReExpr regex)
            {
                regex = null!;
                var classStart = _position - 1;
                var savedIsExact = _isExact;
                if (_ignoreCase)
                {
                    return TryParseWholeCharacterClassWithDotNet(out regex);
                }

                if (TryParseSimpleCharClass(out regex))
                {
                    return true;
                }

                _position = classStart + 1;
                _isExact = savedIsExact;
                return TryParseWholeCharacterClassWithDotNet(out regex);
            }

            private bool TryParseSimpleCharClass(out ReExpr regex)
            {
                regex = null!;
                var negate = false;
                if (Peek('^'))
                {
                    negate = true;
                    _position++;
                }

                var parts = new List<CharacterClassPart>();
                if (Peek(']'))
                {
                    parts.Add(CreateClassCharacterPart(']'));
                    _position++;
                }

                while (_position < _pattern.Length && !Peek(']'))
                {
                    if (!TryReadClassPart(out var start))
                    {
                        return false;
                    }

                    if (Peek('-') &&
                        _position + 1 < _pattern.Length &&
                        _pattern[_position + 1] != ']')
                    {
                        _position++;
                        if (start.ExactCharacter is not { } startCharacter ||
                            !TryReadClassPart(out var end) ||
                            end.ExactCharacter is not { } endCharacter ||
                            endCharacter < startCharacter)
                        {
                            return false;
                        }

                        parts.Add(new CharacterClassPart(
                            _context.MkRange(
                                _context.MkString(startCharacter.ToString()),
                                _context.MkString(endCharacter.ToString())),
                            exactCharacter: null,
                            isApproximation: false,
                            ranges: new[] { new CharacterRange(startCharacter, endCharacter) }));
                    }
                    else
                    {
                        parts.Add(start);
                    }
                }

                if (!Peek(']') || parts.Count == 0)
                {
                    return false;
                }

                _position++;
                regex = parts.Count == 1 ? parts[0].Regex : _context.MkUnion(parts.Select(static part => part.Regex).ToArray());
                if (negate)
                {
                    if (parts.Any(static part => part.IsApproximation || part.Ranges == null))
                    {
                        return false;
                    }

                    var complementRanges = ComplementRanges(MergeRanges(parts.SelectMany(static part => part.Ranges!)));
                    if (!TryCreateCharacterRangesRegex(complementRanges, out regex))
                    {
                        return false;
                    }
                }

                return true;
            }

            private bool TryParseWholeCharacterClassWithDotNet(out ReExpr regex)
            {
                regex = null!;
                if (!TryReadWholeCharacterClassPattern(out var atomPattern))
                {
                    return false;
                }

                var options = CreateCurrentCharacterClassRegexOptions();
                if (!TryCreateCharacterRangesRegex(atomPattern, options, out regex))
                {
                    _isExact = false;
                    regex = CreateAnyCharRegex();
                }

                return true;
            }

            private RegexOptions CreateCurrentCharacterClassRegexOptions()
            {
                var options = RegexOptions.None;
                if (_ignorePatternWhitespace)
                {
                    options |= RegexOptions.IgnorePatternWhitespace;
                }

                if (_ignoreCase)
                {
                    options |= RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
                }
                else if (_canUseIgnoreCase)
                {
                    options |= RegexOptions.CultureInvariant;
                }

                return options;
            }

            private bool TryReadWholeCharacterClassPattern(out string atomPattern)
            {
                atomPattern = string.Empty;
                var start = _position - 1;
                if (start < 0 || start >= _pattern.Length || _pattern[start] != '[')
                {
                    return false;
                }

                var index = _position;
                if (index < _pattern.Length && _pattern[index] == '^')
                {
                    index++;
                }

                if (index < _pattern.Length && _pattern[index] == ']')
                {
                    index++;
                }

                var escaped = false;
                for (; index < _pattern.Length; index++)
                {
                    var current = _pattern[index];
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == ']')
                    {
                        _position = index + 1;
                        atomPattern = _pattern.Substring(start, index - start + 1);
                        return true;
                    }
                }

                return false;
            }

            private bool TryReadClassPart(out CharacterClassPart part)
            {
                part = default;
                if (_position >= _pattern.Length)
                {
                    return false;
                }

                var current = _pattern[_position++];
                if (current != '\\')
                {
                    part = CreateClassCharacterPart(current);
                    return true;
                }

                if (_position >= _pattern.Length)
                {
                    return false;
                }

                var escaped = _pattern[_position++];
                if (TryCreateEscapedCharacterClassRegex(escaped, out var escapedClass))
                {
                    _isExact &= escapedClass.IsExact;
                    part = new CharacterClassPart(
                        escapedClass.Regex,
                        exactCharacter: null,
                        isApproximation: !escapedClass.IsExact,
                        ranges: escapedClass.Ranges);
                    return true;
                }

                if (escaped == 'x')
                {
                    if (!TryReadFixedHexChar(2, out var hexChar))
                    {
                        return false;
                    }

                    part = CreateClassCharacterPart(hexChar);
                    return true;
                }

                if (escaped == 'u')
                {
                    if (!TryReadFixedHexChar(4, out var unicodeChar))
                    {
                        return false;
                    }

                    part = CreateClassCharacterPart(unicodeChar);
                    return true;
                }

                if (escaped == 'c')
                {
                    if (!TryReadControlCharacterEscape(out var controlChar))
                    {
                        return false;
                    }

                    part = CreateClassCharacterPart(controlChar);
                    return true;
                }

                if (escaped == '0')
                {
                    part = CreateClassCharacterPart(ReadNullPrefixedOctalEscape());
                    return true;
                }

                var value = escaped switch
                {
                    'a' => '\a',
                    'b' => '\b',
                    'e' => '\u001b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'v' => '\v',
                    '\\' => '\\',
                    '.' => '.',
                    '$' => '$',
                    '|' => '|',
                    '?' => '?',
                    '*' => '*',
                    '+' => '+',
                    '(' => '(',
                    ')' => ')',
                    '[' => '[',
                    '{' => '{',
                    '}' => '}',
                    '-' => '-',
                    ']' => ']',
                    '^' => '^',
                    _ => '\0'
                };

                if (value == '\0')
                {
                    if (!IsEscapedLiteralCharacter(escaped))
                    {
                        return false;
                    }

                    value = escaped;
                }

                part = CreateClassCharacterPart(value);
                return true;
            }

            private CharacterClassPart CreateClassCharacterPart(char value)
            {
                return new CharacterClassPart(
                    CreateLiteralRegex(value.ToString()),
                    exactCharacter: value,
                    isApproximation: false,
                    ranges: new[] { new CharacterRange(value, value) });
            }

            private readonly struct RegexClassTranslation
            {
                public RegexClassTranslation(ReExpr regex, bool isExact, CharacterRange[]? ranges)
                {
                    Regex = regex;
                    IsExact = isExact;
                    Ranges = ranges;
                }

                public ReExpr Regex { get; }

                public bool IsExact { get; }

                public CharacterRange[]? Ranges { get; }
            }

            private bool TryCreateEscapedCharacterClassRegex(char escaped, out RegexClassTranslation regex)
            {
                regex = default;
                if (escaped is 'd' or 'D')
                {
                    var digitRanges = DecimalDigitRanges.Value;
                    var ranges = escaped == 'd' ? digitRanges : ComplementRanges(digitRanges);
                    if (!TryCreateCharacterRangesRegex(ranges, out var digitRegex))
                    {
                        regex = new RegexClassTranslation(CreateAnyCharRegex(), isExact: false, ranges: null);
                        return true;
                    }

                    regex = new RegexClassTranslation(digitRegex, isExact: true, ranges);
                    return true;
                }

                if (escaped is 's' or 'S')
                {
                    var whitespaceRanges = WhitespaceRanges.Value;
                    var ranges = escaped == 's' ? whitespaceRanges : ComplementRanges(whitespaceRanges);
                    if (!TryCreateCharacterRangesRegex(ranges, out var whitespaceRegex))
                    {
                        regex = new RegexClassTranslation(CreateAnyCharRegex(), isExact: false, ranges: null);
                        return true;
                    }

                    regex = new RegexClassTranslation(whitespaceRegex, isExact: true, ranges);
                    return true;
                }

                if (escaped is 'w' or 'W')
                {
                    var wordRanges = WordRanges.Value;
                    var ranges = escaped == 'w' ? wordRanges : ComplementRanges(wordRanges);
                    if (!TryCreateCharacterRangesRegex(ranges, out var wordRegex))
                    {
                        regex = new RegexClassTranslation(CreateAnyCharRegex(), isExact: false, ranges: null);
                        return true;
                    }

                    regex = new RegexClassTranslation(wordRegex, isExact: true, ranges);
                    return true;
                }

                if (escaped is 'p' or 'P')
                {
                    if (!TryReadRegexCategoryName(out var categoryName))
                    {
                        return false;
                    }

                    if (!TryGetCharacterRanges(@"\p{" + categoryName + "}", out var categoryRanges))
                    {
                        regex = new RegexClassTranslation(CreateAnyCharRegex(), isExact: false, ranges: null);
                        return true;
                    }

                    var ranges = escaped == 'p' ? categoryRanges : ComplementRanges(categoryRanges);
                    if (!TryCreateCharacterRangesRegex(ranges, out var categoryRegex))
                    {
                        regex = new RegexClassTranslation(CreateAnyCharRegex(), isExact: false, ranges: null);
                        return true;
                    }

                    regex = new RegexClassTranslation(categoryRegex, isExact: true, ranges);
                    return true;
                }

                return false;
            }

            private ReExpr CreateCharacterRangesRegex(IReadOnlyList<CharacterRange> ranges)
            {
                if (ranges.Count == 0 || ranges.Count > MaxCharacterClassRangeCount)
                {
                    throw new InvalidOperationException("Unsupported character class range count.");
                }

                var regexes = new ReExpr[ranges.Count];
                for (var index = 0; index < ranges.Count; index++)
                {
                    regexes[index] = _context.MkRange(
                        _context.MkString(ranges[index].Start.ToString()),
                        _context.MkString(ranges[index].End.ToString()));
                }

                return regexes.Length == 1 ? regexes[0] : _context.MkUnion(regexes);
            }

            private bool TryCreateCharacterRangesRegex(IReadOnlyList<CharacterRange> ranges, out ReExpr regex)
            {
                regex = null!;
                try
                {
                    regex = CreateCharacterRangesRegex(ranges);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }

            private bool TryCreateCharacterRangesRegex(string atomPattern, out ReExpr regex)
            {
                return TryCreateCharacterRangesRegex(atomPattern, RegexOptions.None, out regex);
            }

            private bool TryCreateCharacterRangesRegex(string atomPattern, RegexOptions options, out ReExpr regex)
            {
                regex = null!;
                try
                {
                    if (!TryGetCharacterRanges(atomPattern, options, out var ranges))
                    {
                        return false;
                    }

                    regex = CreateCharacterRangesRegex(ranges);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
                catch (RegexMatchTimeoutException)
                {
                    return false;
                }
            }

            private static bool TryGetCharacterRanges(string atomPattern, out CharacterRange[] ranges)
            {
                return TryGetCharacterRanges(atomPattern, RegexOptions.None, out ranges);
            }

            private static bool TryGetCharacterRanges(string atomPattern, RegexOptions options, out CharacterRange[] ranges)
            {
                ranges = Array.Empty<CharacterRange>();
                try
                {
                    ranges = RegexCharacterRangeCache.GetOrAdd((atomPattern, options), CreateRegexCharacterRanges);
                    return ranges.Length is > 0 and <= MaxCharacterClassRangeCount;
                }
                catch (ArgumentException)
                {
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
                catch (RegexMatchTimeoutException)
                {
                    return false;
                }
            }

            private static CharacterRange[] CreateRegexCharacterRanges((string Pattern, RegexOptions Options) key)
            {
                var ranges = new List<CharacterRange>();
                char? rangeStart = null;
                var previous = '\0';
                var regex = new Regex(@"\A(?:" + key.Pattern + @")\z", key.Options, RegexSyntaxValidationTimeout);
                for (var codePoint = 0; codePoint <= char.MaxValue; codePoint++)
                {
                    var current = (char)codePoint;
                    if (regex.IsMatch(current.ToString()))
                    {
                        rangeStart ??= current;
                        previous = current;
                        continue;
                    }

                    if (rangeStart is { } start)
                    {
                        ranges.Add(new CharacterRange(start, previous));
                        rangeStart = null;
                    }
                }

                if (rangeStart is { } finalStart)
                {
                    ranges.Add(new CharacterRange(finalStart, previous));
                }

                return ranges.ToArray();
            }

            private static CharacterRange[] MergeRanges(IEnumerable<CharacterRange> ranges)
            {
                var ordered = ranges
                    .OrderBy(static range => range.Start)
                    .ThenBy(static range => range.End)
                    .ToArray();
                if (ordered.Length == 0)
                {
                    return Array.Empty<CharacterRange>();
                }

                var merged = new List<CharacterRange>();
                var currentStart = ordered[0].Start;
                var currentEnd = ordered[0].End;
                for (var index = 1; index < ordered.Length; index++)
                {
                    var range = ordered[index];
                    if (range.Start <= currentEnd ||
                        currentEnd != char.MaxValue && range.Start == currentEnd + 1)
                    {
                        if (range.End > currentEnd)
                        {
                            currentEnd = range.End;
                        }

                        continue;
                    }

                    merged.Add(new CharacterRange(currentStart, currentEnd));
                    currentStart = range.Start;
                    currentEnd = range.End;
                }

                merged.Add(new CharacterRange(currentStart, currentEnd));
                return merged.ToArray();
            }

            private static CharacterRange[] ComplementRanges(IEnumerable<CharacterRange> ranges)
            {
                var merged = MergeRanges(ranges);
                var complement = new List<CharacterRange>();
                var nextStart = 0;
                foreach (var range in merged)
                {
                    if (nextStart < range.Start)
                    {
                        complement.Add(new CharacterRange((char)nextStart, (char)(range.Start - 1)));
                    }

                    if (range.End == char.MaxValue)
                    {
                        nextStart = char.MaxValue + 1;
                        break;
                    }

                    nextStart = range.End + 1;
                }

                if (nextStart <= char.MaxValue)
                {
                    complement.Add(new CharacterRange((char)nextStart, char.MaxValue));
                }

                return complement.ToArray();
            }

            private bool TryReadRegexCategoryName(out string categoryName)
            {
                categoryName = string.Empty;
                if (!Peek('{'))
                {
                    return false;
                }

                _position++;
                var start = _position;
                while (_position < _pattern.Length && !Peek('}'))
                {
                    var current = _pattern[_position];
                    if (!char.IsLetterOrDigit(current) && current != '_')
                    {
                        return false;
                    }

                    _position++;
                }

                if (_position == start || !Peek('}'))
                {
                    return false;
                }

                _position++;
                categoryName = _pattern.Substring(start, _position - start - 1);
                return true;
            }

            private bool TryReadFixedHexChar(int digitCount, out char value)
            {
                value = default;
                if (_position + digitCount > _pattern.Length)
                {
                    return false;
                }

                var parsed = 0;
                for (var index = 0; index < digitCount; index++)
                {
                    var digit = HexValue(_pattern[_position + index]);
                    if (digit < 0)
                    {
                        return false;
                    }

                    parsed = (parsed * 16) + digit;
                }

                _position += digitCount;
                value = (char)parsed;
                return true;
            }

            private bool TryReadControlCharacterEscape(out char value)
            {
                value = default;
                if (_position >= _pattern.Length)
                {
                    return false;
                }

                var control = _pattern[_position];
                if (control is >= 'a' and <= 'z')
                {
                    control = (char)(control - ('a' - 'A'));
                }
                else if (control is not (>= 'A' and <= 'Z'))
                {
                    return false;
                }

                _position++;
                value = (char)(control - '@');
                return true;
            }

            private char ReadNullPrefixedOctalEscape()
            {
                var value = 0;
                for (var digitCount = 0;
                     digitCount < 2 && _position < _pattern.Length && IsOctalDigit(_pattern[_position]);
                     digitCount++)
                {
                    value = (value * 8) + (_pattern[_position] - '0');
                    _position++;
                }

                return (char)value;
            }

            private static bool IsOctalDigit(char value)
            {
                return value is >= '0' and <= '7';
            }

            private static int HexValue(char value)
            {
                if (value >= '0' && value <= '9')
                {
                    return value - '0';
                }

                if (value >= 'a' && value <= 'f')
                {
                    return value - 'a' + 10;
                }

                if (value >= 'A' && value <= 'F')
                {
                    return value - 'A' + 10;
                }

                return -1;
            }

            private readonly struct CharacterRange
            {
                public CharacterRange(char start, char end)
                {
                    Start = start;
                    End = end;
                }

                public char Start { get; }

                public char End { get; }
            }

            private bool TryReadNumber(out uint value)
            {
                value = 0;
                var start = _position;
                while (_position < _pattern.Length && char.IsDigit(_pattern[_position]))
                {
                    var digit = (uint)(_pattern[_position] - '0');
                    value = checked((value * 10) + digit);
                    _position++;
                    if (value > MaxBoundedRepeat)
                    {
                        return false;
                    }
                }

                return _position > start;
            }

            private void ConsumeNonGreedyMarker()
            {
                SkipIgnoredPatternTrivia();
                if (Peek('?'))
                {
                    _position++;
                }
            }

            private void SkipIgnoredPatternTrivia()
            {
                while (_position < _pattern.Length)
                {
                    if (TrySkipInlineComment())
                    {
                        continue;
                    }

                    if (!_ignorePatternWhitespace)
                    {
                        return;
                    }

                    var current = _pattern[_position];
                    if (char.IsWhiteSpace(current))
                    {
                        _position++;
                        continue;
                    }

                    if (current == '#')
                    {
                        _position++;
                        while (_position < _pattern.Length &&
                               _pattern[_position] != '\r' &&
                               _pattern[_position] != '\n')
                        {
                            _position++;
                        }

                        continue;
                    }

                    return;
                }
            }

            private static void SkipIgnoredPatternTrivia(string pattern, ref int position, bool ignorePatternWhitespace)
            {
                while (position < pattern.Length)
                {
                    if (TrySkipInlineComment(pattern, ref position))
                    {
                        continue;
                    }

                    if (!ignorePatternWhitespace)
                    {
                        return;
                    }

                    var current = pattern[position];
                    if (char.IsWhiteSpace(current))
                    {
                        position++;
                        continue;
                    }

                    if (current == '#')
                    {
                        position++;
                        while (position < pattern.Length &&
                               pattern[position] != '\r' &&
                               pattern[position] != '\n')
                        {
                            position++;
                        }

                        continue;
                    }

                    return;
                }
            }

            private bool TrySkipInlineComment()
            {
                if (_position + 2 >= _pattern.Length ||
                    _pattern[_position] != '(' ||
                    _pattern[_position + 1] != '?' ||
                    _pattern[_position + 2] != '#')
                {
                    return false;
                }

                var end = _position + 3;
                while (end < _pattern.Length && _pattern[end] != ')')
                {
                    end++;
                }

                if (end >= _pattern.Length)
                {
                    return false;
                }

                _position = end + 1;
                return true;
            }

            private static bool TrySkipInlineComment(string pattern, ref int position)
            {
                if (position + 2 >= pattern.Length ||
                    pattern[position] != '(' ||
                    pattern[position + 1] != '?' ||
                    pattern[position + 2] != '#')
                {
                    return false;
                }

                var end = position + 3;
                while (end < pattern.Length && pattern[end] != ')')
                {
                    end++;
                }

                if (end >= pattern.Length)
                {
                    return false;
                }

                position = end + 1;
                return true;
            }

            private ReExpr CreateAnyStringRegex()
            {
                return _context.MkStar(CreateAnyCharRegex());
            }

            private ReExpr CreateOptionalFinalNewlineRegex()
            {
                return _context.MkOption(CreateLiteralRegex("\n"));
            }

            private ReExpr CreateAnyCharRegex()
            {
                return _context.MkRange(_context.MkString("\u0000"), _context.MkString("\uffff"));
            }

            private ReExpr CreateDotRegex()
            {
                return _singleline
                    ? CreateAnyCharRegex()
                    : _context.MkDiff(CreateAnyCharRegex(), CreateLiteralRegex("\n"));
            }

            private ReExpr CreateExactRepeat(ReExpr regex, uint count)
            {
                if (count == 0)
                {
                    return CreateLiteralRegex(string.Empty);
                }

                return _context.MkLoop(regex, count, count);
            }

            private ReExpr CreateConcat(ReExpr left, ReExpr right)
            {
                return _context.MkConcat(new[] { left, right });
            }

            private ReExpr CreateLiteralRegex(string value)
            {
                if (_ignoreCase && value.Length != 0)
                {
                    var regexes = new ReExpr[value.Length];
                    for (var index = 0; index < value.Length; index++)
                    {
                        regexes[index] = CreateIgnoreCaseLiteralCharacterRegex(value[index]);
                    }

                    return regexes.Length == 1 ? regexes[0] : _context.MkConcat(regexes);
                }

                return _context.MkToRe(_context.MkString(value));
            }

            private ReExpr CreateIgnoreCaseLiteralCharacterRegex(char value)
            {
                if (TryCreateCharacterRangesRegex(Regex.Escape(value.ToString()), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, out var regex))
                {
                    return regex;
                }

                _isExact = false;
                return CreateAnyCharRegex();
            }

            private bool Peek(char value)
            {
                return _position < _pattern.Length && _pattern[_position] == value;
            }

            private readonly struct RegexOptionScope
            {
                public RegexOptionScope(bool ignorePatternWhitespace, bool singleline, bool ignoreCase)
                {
                    IgnorePatternWhitespace = ignorePatternWhitespace;
                    Singleline = singleline;
                    IgnoreCase = ignoreCase;
                }

                public bool IgnorePatternWhitespace { get; }

                public bool Singleline { get; }

                public bool IgnoreCase { get; }
            }

            private static bool IsEscaped(string value, int index)
            {
                var slashCount = 0;
                for (var current = index - 1; current >= 0 && value[current] == '\\'; current--)
                {
                    slashCount++;
                }

                return slashCount % 2 == 1;
            }

            private static bool EndsWithUnescapedAnchor(string value, string anchor)
            {
                return value.EndsWith(anchor, StringComparison.Ordinal) &&
                    !IsEscaped(value, value.Length - anchor.Length);
            }

            private static bool IsValidDotNetRegexPattern(string pattern, RegexOptions options)
            {
                try
                {
                    _ = new Regex(pattern, options, RegexSyntaxValidationTimeout);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            private static bool IsRegexMetaCharacter(char value)
            {
                return value is '|' or '?' or '*' or '+' or ')' or '[' or ']' or '{' or '}';
            }

            private static bool IsEscapedLiteralCharacter(char value)
            {
                return !char.IsLetterOrDigit(value);
            }

            private static bool IsSupportedCaptureNameCharacter(char value)
            {
                return char.IsLetterOrDigit(value) || value == '_';
            }
        }
    }
}
