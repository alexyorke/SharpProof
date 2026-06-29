using Microsoft.Z3;

namespace SearchLib.Smt
{
    internal sealed class Z3FormulaEncoder : IDisposable
    {
        private readonly Context _context = new();
        private readonly Sort _referenceSort;
        private readonly Expr _nullReference;
        private readonly Dictionary<string, Expr> _variables = new(StringComparer.Ordinal);

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

            return (BoolExpr)Encode(formula);
        }

        public Solver CreateSolver(TimeSpan timeout)
        {
            var solver = _context.MkSolver();
            var parameters = _context.MkParams();
            parameters.Add("timeout", (uint)Math.Max(1, timeout.TotalMilliseconds));
            solver.Parameters = parameters;
            return solver;
        }

        public BoolExpr Negate(SmtFormula formula)
        {
            return _context.MkNot(EncodeCondition(formula));
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
                _ => throw new InvalidOperationException("Unsupported SMT integer binary operator."),
            };
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
            if (!Z3RegexTranslator.TryTranslate(_context, formula.Pattern, out var regex))
            {
                throw new InvalidOperationException("Unsupported SMT regex pattern.");
            }

            return _context.MkInRe(EncodeString(formula.Value), regex);
        }

        private Expr GetOrCreateVariable(SmtVariable variable)
        {
            if (_variables.TryGetValue(variable.Name, out var existing))
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

            _variables.Add(variable.Name, created);
            return created;
        }

        private sealed class Z3RegexTranslator
        {
            private const int MaxBoundedRepeat = 64;
            private readonly Context _context;
            private readonly string _pattern;
            private int _position;

            private Z3RegexTranslator(Context context, string pattern)
            {
                _context = context;
                _pattern = pattern;
            }

            public static bool TryTranslate(Context context, string pattern, out ReExpr regex)
            {
                regex = null!;
                if (pattern.Length > 256)
                {
                    return false;
                }

                var startAnchored = pattern.StartsWith("^", StringComparison.Ordinal);
                var strictStartAnchored = pattern.StartsWith(@"\A", StringComparison.Ordinal);
                var strictEndAnchored = pattern.EndsWith(@"\z", StringComparison.Ordinal);
                var dollarEndAnchored = !strictEndAnchored &&
                    pattern.EndsWith("$", StringComparison.Ordinal) &&
                    !IsEscaped(pattern, pattern.Length - 1);
                var bodyStart = strictStartAnchored ? 2 : startAnchored ? 1 : 0;
                var bodyEndTrim = strictEndAnchored ? 2 : dollarEndAnchored ? 1 : 0;
                var bodyLength = pattern.Length - bodyStart - bodyEndTrim;
                if (bodyLength < 0)
                {
                    return false;
                }

                var translator = new Z3RegexTranslator(context, pattern.Substring(bodyStart, bodyLength));
                if (!translator.TryParseExpression(out var body) ||
                    translator._position != translator._pattern.Length)
                {
                    return false;
                }

                regex = body;
                if (!startAnchored && !strictStartAnchored)
                {
                    regex = context.MkConcat(new[] { translator.CreateAnyStringRegex(), regex });
                }

                if (dollarEndAnchored)
                {
                    regex = context.MkConcat(new[] { regex, translator.CreateOptionalFinalNewlineRegex() });
                }
                else if (!strictEndAnchored)
                {
                    regex = context.MkConcat(new[] { regex, translator.CreateAnyStringRegex() });
                }

                return true;
            }

            private bool TryParseExpression(out ReExpr regex)
            {
                var alternatives = new List<ReExpr>();
                if (!TryParseConcat(out var first))
                {
                    regex = null!;
                    return false;
                }

                alternatives.Add(first);
                while (Peek('|'))
                {
                    _position++;
                    if (!TryParseConcat(out var alternative))
                    {
                        regex = null!;
                        return false;
                    }

                    alternatives.Add(alternative);
                }

                regex = alternatives.Count == 1
                    ? alternatives[0]
                    : _context.MkUnion(alternatives.ToArray());
                return true;
            }

            private bool TryParseConcat(out ReExpr regex)
            {
                var parts = new List<ReExpr>();
                while (_position < _pattern.Length &&
                       !Peek('|') &&
                       !Peek(')'))
                {
                    if (!TryParseRepeat(out var part))
                    {
                        regex = null!;
                        return false;
                    }

                    parts.Add(part);
                }

                regex = parts.Count switch
                {
                    0 => CreateLiteralRegex(string.Empty),
                    1 => parts[0],
                    _ => _context.MkConcat(parts.ToArray())
                };
                return true;
            }

            private bool TryParseRepeat(out ReExpr regex)
            {
                if (!TryParseAtom(out regex))
                {
                    return false;
                }

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
                if (_position >= _pattern.Length)
                {
                    return false;
                }

                var current = _pattern[_position++];
                switch (current)
                {
                    case '(':
                        if (Peek('?'))
                        {
                            _position++;
                            if (!Peek(':'))
                            {
                                return false;
                            }

                            _position++;
                        }

                        if (!TryParseExpression(out regex) || !Peek(')'))
                        {
                            return false;
                        }

                        _position++;
                        return true;
                    case '[':
                        return TryParseCharClass(out regex);
                    case '.':
                        regex = _context.MkDiff(CreateAnyCharRegex(), CreateLiteralRegex("\n"));
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

            private bool TryParseEscapedAtom(out ReExpr regex)
            {
                regex = null!;
                if (_position >= _pattern.Length)
                {
                    return false;
                }

                var escaped = _pattern[_position++];
                if (TryCreateEscapedCharacterClassRegex(escaped, out regex))
                {
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

                var literal = escaped switch
                {
                    'a' => "\a",
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
                    regex = CreateLiteralRegex(string.Empty);
                    return true;
                }

                if (literal == null)
                {
                    return false;
                }

                regex = CreateLiteralRegex(literal);
                return true;
            }

            private readonly struct CharacterClassPart
            {
                public CharacterClassPart(ReExpr regex, char? exactCharacter, bool isApproximation)
                {
                    Regex = regex;
                    ExactCharacter = exactCharacter;
                    IsApproximation = isApproximation;
                }

                public ReExpr Regex { get; }
                public char? ExactCharacter { get; }
                public bool IsApproximation { get; }
            }

            private bool TryParseCharClass(out ReExpr regex)
            {
                regex = null!;
                var negate = false;
                if (Peek('^'))
                {
                    negate = true;
                    _position++;
                }

                var parts = new List<CharacterClassPart>();
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
                            isApproximation: false));
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
                    if (parts.Any(static part => part.IsApproximation))
                    {
                        return false;
                    }

                    regex = _context.MkDiff(CreateAnyCharRegex(), regex);
                }

                return true;
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
                if (TryCreateEscapedCharacterClassRegex(escaped, out var escapedClassRegex))
                {
                    part = new CharacterClassPart(escapedClassRegex, exactCharacter: null, isApproximation: true);
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

                var value = escaped switch
                {
                    'a' => '\a',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'v' => '\v',
                    '\\' => '\\',
                    '-' => '-',
                    ']' => ']',
                    '^' => '^',
                    _ => '\0'
                };

                if (value == '\0')
                {
                    return false;
                }

                part = CreateClassCharacterPart(value);
                return true;
            }

            private CharacterClassPart CreateClassCharacterPart(char value)
            {
                return new CharacterClassPart(
                    CreateLiteralRegex(value.ToString()),
                    exactCharacter: value,
                    isApproximation: false);
            }

            private bool TryCreateEscapedCharacterClassRegex(char escaped, out ReExpr regex)
            {
                regex = null!;
                if (escaped is 'p' or 'P')
                {
                    if (!TryReadRegexCategoryName())
                    {
                        return false;
                    }

                    regex = CreateAnyCharRegex();
                    return true;
                }

                if (escaped is not ('d' or 'D' or 's' or 'S' or 'w' or 'W'))
                {
                    return false;
                }

                // .NET's shorthand classes are Unicode-aware by default. One-char over-approximation
                // preserves soundness for reachability proofs while still exposing length facts to Z3.
                regex = CreateAnyCharRegex();
                return true;
            }

            private bool TryReadRegexCategoryName()
            {
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
                if (Peek('?'))
                {
                    _position++;
                }
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
                return _context.MkToRe(_context.MkString(value));
            }

            private bool Peek(char value)
            {
                return _position < _pattern.Length && _pattern[_position] == value;
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

            private static bool IsRegexMetaCharacter(char value)
            {
                return value is '|' or '?' or '*' or '+' or ')' or '[' or ']' or '{' or '}';
            }
        }
    }
}
