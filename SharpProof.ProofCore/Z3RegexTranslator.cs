using System.Diagnostics.CodeAnalysis;
namespace SharpProof.ProofCore.Smt;
internal sealed partial class Z3RegexCompiler {
    private const int MaxBoundedRepeat = 64;
    // Keep large Unicode category unions conservative; Z3 range-heavy regexes can get expensive
    // and smaller shorthand/category classes cover the common analyzer facts precisely.
    private const int MaxCharacterClassRangeCount = 512;
    private readonly bool _canUseIgnoreCase;
    private readonly Context _context;
    private readonly string _pattern;
    private bool _isExact = true;
    private RegexOptionScope _options;
    private int _position;
    internal Z3RegexCompiler(Context context) : this(context, string.Empty, RegexOptions.None) { }
    internal Z3RegexCompiler(Context context, string pattern, RegexOptions options) {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _pattern = pattern;
        _options = CreateInitialOptionScope(options);
        _canUseIgnoreCase = (options & RegexOptions.CultureInvariant) != 0;
    }
    internal static Z3RegexTranslationResult Compile(Context context, string pattern, RegexOptions options) {
        if (Validate(pattern, options) != RegexTranslationFallback.None)
            return Z3RegexTranslationResult.Failed();
        if (!TryNormalize(pattern, options, out var normalized))
            return Z3RegexTranslationResult.Failed();
        var compiler = new Z3RegexCompiler(context, normalized.Body, options);
        if (!compiler.TryParseExpression(out var body))
            return Z3RegexTranslationResult.Failed();
        compiler.SkipIgnoredPatternTrivia();
        if (compiler._position != compiler._pattern.Length)
            return Z3RegexTranslationResult.Failed();
        var regex = body;
        if (!normalized.StartAnchored) regex = context.MkConcat(compiler.AnyString(), regex);
        if (normalized.DollarEndAnchored || normalized.FinalNewlineEndAnchored)
            regex = context.MkConcat(regex, compiler.OptionalFinalNewline());
        else if (!normalized.StrictEndAnchored) regex = context.MkConcat(regex, compiler.AnyString());
        return Z3RegexTranslationResult.Succeeded(regex, compiler._isExact);
    }
    internal bool TryParseExpression([NotNullWhen(true)] out ReExpr? regex) {
        SkipIgnoredPatternTrivia();
        var alternatives = new List<ReExpr>();
        if (!TryParseConcat(out var first)) return Fail(out regex);
        alternatives.Add(first);
        SkipIgnoredPatternTrivia();
        while (Peek('|')) {
            _position++;
            SkipIgnoredPatternTrivia();
            if (!TryParseConcat(out var alternative)) return Fail(out regex);
            alternatives.Add(alternative);
            SkipIgnoredPatternTrivia();
        }
        regex = alternatives.Count == 1
            ? alternatives[0]
            : _context.MkUnion([.. alternatives]);
        return true;
    }
    internal bool TryParseConcat([NotNullWhen(true)] out ReExpr? regex) => TryParseConcat(out regex, out _);
    internal bool TryParseConcat(
        [NotNullWhen(true)] out ReExpr? regex,
        out bool consumedAny) {
        var parts = new List<ReExpr>();
        consumedAny = false;
        while (true) {
            SkipIgnoredPatternTrivia();
            if (_position >= _pattern.Length ||
                Peek('|') ||
                Peek(')'))
                break;
            if (TryParseLookaroundAssertion(false, out var lookahead)) {
                if (!TryParseConcat(out var suffix, out var suffixConsumed) || !suffixConsumed)
                    return Fail(out regex);
                parts.Add(ConstrainSuffixWithLookahead(lookahead, suffix));
                consumedAny = true;
                regex = Concat(parts);
                return true;
            }
            if (TryParseLookaroundAssertion(true, out var lookbehind)) {
                if (parts.Count == 0) return Fail(out regex);
                var prefix = Concat(parts);
                parts.Clear();
                parts.Add(ConstrainPrefixWithLookbehind(lookbehind, prefix));
                consumedAny = true;
                continue;
            }
            if (TryParseWordBoundaryAssertion(out var wordBoundary)) {
                var prefix = Concat(parts);
                if (!TryParseConcat(out var suffix, out var suffixConsumed) ||
                    !TryConstrainSplitWithWordBoundary(prefix, suffix, wordBoundary, out var constrained))
                    return Fail(out regex);
                consumedAny |= suffixConsumed;
                regex = constrained;
                return true;
            }
            if (!TryParseRepeat(out var part)) return Fail(out regex);
            parts.Add(part);
            consumedAny = true;
        }
        regex = Concat(parts);
        return true;
    }
    private ReExpr Concat(IReadOnlyList<ReExpr> parts) => parts.Count switch {
        0 => CreateLiteralRegex(string.Empty),
        1 => parts[0],
        _ => _context.MkConcat([.. parts])
    };
    private bool TryParseLookaroundAssertion(bool lookbehind, out RegexLookaheadAssertion assertion) {
        assertion = default;
        SkipIgnoredPatternTrivia();
        var savedPosition = _position;
        var savedOptions = _options;
        var savedIsExact = _isExact;
        var polarityOffset = lookbehind ? 3 : 2;
        if (_position + polarityOffset >= _pattern.Length ||
            _pattern[_position] != '(' ||
            _pattern[_position + 1] != '?' ||
            lookbehind && _pattern[_position + 2] != '<' ||
            _pattern[_position + polarityOffset] is not ('=' or '!'))
            return false;
        var positive = _pattern[_position + polarityOffset] == '=';
        _position += polarityOffset + 1;
        if (!TryParseExpression(out var lookaroundRegex) || !Peek(')')) {
            _position = savedPosition;
            _options = savedOptions;
            _isExact = savedIsExact;
            return false;
        }
        _position++;
        var lookaroundIsExact = _isExact;
        _options = savedOptions;
        _isExact = savedIsExact;
        if (!positive && !lookaroundIsExact) {
            _position = savedPosition;
            return false;
        }
        assertion = new RegexLookaheadAssertion(lookaroundRegex, positive, lookaroundIsExact);
        return true;
    }
    private bool TryParseWordBoundaryAssertion(out bool isBoundary) {
        isBoundary = false;
        SkipIgnoredPatternTrivia();
        if (_position + 1 >= _pattern.Length ||
            _pattern[_position] != '\\' ||
            _pattern[_position + 1] is not ('b' or 'B'))
            return false;
        isBoundary = _pattern[_position + 1] == 'b';
        _position += 2;
        // Word-boundary assertions are modeled well enough to prove contradictions, but SAT
        // still needs a concrete .NET witness before it can become a reachability proof.
        _isExact = false;
        return true;
    }
    private ReExpr ConstrainSuffixWithLookahead(RegexLookaheadAssertion assertion, ReExpr suffix) {
        _isExact &= assertion.IsExact;
        var lookaheadLanguage = Concat(assertion.Regex, AnyString());
        return assertion.IsPositive
            ? _context.MkIntersect(suffix, lookaheadLanguage)
            : _context.MkDiff(suffix, lookaheadLanguage);
    }
    private ReExpr ConstrainPrefixWithLookbehind(RegexLookaheadAssertion assertion, ReExpr prefix) {
        _isExact &= assertion.IsExact;
        var lookbehindLanguage = Concat(AnyString(), assertion.Regex);
        return assertion.IsPositive
            ? _context.MkIntersect(prefix, lookbehindLanguage)
            : _context.MkDiff(prefix, lookbehindLanguage);
    }
    internal bool TryConstrainSplitWithWordBoundary(
        ReExpr prefix,
        ReExpr suffix,
        bool isBoundary,
        [NotNullWhen(true)] out ReExpr? regex) {
        regex = null;
        if (!TryCreateCharacterRangesRegex(Word, out var wordChar)) return false;
        var nonWordChar = _context.MkDiff(AnyCharacter(), wordChar);
        var leftWord = ConstrainPrefixEnd(prefix, wordChar);
        var leftNonWord = _context.MkUnion(ConstrainPrefixEnd(prefix, nonWordChar),
            _context.MkIntersect(prefix, CreateLiteralRegex(string.Empty)));
        var rightWord = ConstrainSuffixStart(suffix, wordChar);
        var rightNonWord = _context.MkUnion(ConstrainSuffixStart(suffix, nonWordChar),
            _context.MkIntersect(suffix, CreateLiteralRegex(string.Empty)));
        var first = Concat(leftWord, isBoundary ? rightNonWord : rightWord);
        var second = Concat(leftNonWord, isBoundary ? rightWord : rightNonWord);
        regex = _context.MkUnion(first, second);
        return true;
    }
    private ReExpr ConstrainPrefixEnd(ReExpr prefix, ReExpr finalCharacter) =>
        _context.MkIntersect(prefix, Concat(AnyString(), finalCharacter));
    private ReExpr ConstrainSuffixStart(ReExpr suffix, ReExpr firstCharacter) =>
        _context.MkIntersect(suffix, Concat(firstCharacter, AnyString()));
    internal bool TryParseRepeat([NotNullWhen(true)] out ReExpr? regex) {
        SkipIgnoredPatternTrivia();
        if (!TryParseAtom(out regex)) return false;
        SkipIgnoredPatternTrivia();
        if (_position >= _pattern.Length) return true;
        switch (_pattern[_position]) {
            case '*' or '+' or '?':
                var quantifier = _pattern[_position];
                _position++;
                regex = quantifier switch {
                    '*' => _context.MkStar(regex),
                    '+' => _context.MkPlus(regex),
                    _ => _context.MkOption(regex)
                };
                ConsumeNonGreedyMarker();
                return true;
            case '{':
                return TryParseBoundedRepeat(ref regex);
            default:
                return true;
        }
    }
    private bool TryParseBoundedRepeat(ref ReExpr regex) {
        var savedPosition = _position;
        _position++;
        if (!TryReadNumber(out var lower)) {
            _position = savedPosition;
            return true;
        }
        var upper = lower;
        var unbounded = false;
        if (Peek(',')) {
            _position++;
            if (Peek('}'))
                unbounded = true;
            else if (!TryReadNumber(out upper)) return false;
        }
        if (!Peek('}') ||
            upper < lower ||
            lower > MaxBoundedRepeat ||
            (!unbounded && upper > MaxBoundedRepeat))
            return false;
        _position++;
        regex = unbounded
            ? Concat(ExactRepeat(regex, lower), _context.MkStar(regex))
            : _context.MkLoop(regex, lower, upper);
        ConsumeNonGreedyMarker();
        return true;
    }
    internal bool TryParseAtom([NotNullWhen(true)] out ReExpr? regex) {
        regex = null;
        SkipIgnoredPatternTrivia();
        if (_position >= _pattern.Length) return false;
        var current = _pattern[_position++];
        switch (current) {
            case '(':
                if (TryParseInlineOptionGroup(out var inlineOptions)) {
                    _options = inlineOptions;
                    regex = CreateLiteralRegex(string.Empty);
                    return true;
                }
                var outerOptions = _options;
                if (!TryParseGroupPrefix(out var groupOptions)) return false;
                _options = groupOptions;
                if (!TryParseExpression(out regex) || !Peek(')')) {
                    _options = outerOptions;
                    return false;
                }
                _position++;
                _options = outerOptions;
                return true;
            case '[':
                return TryParseCharClass(out regex);
            case '.':
                regex = Dot(_options.Singleline);
                return true;
            case '\\':
                return TryParseEscapedAtom(out regex);
            case '^':
            case '$':
                return false;
            default:
                if (IsRegexMetaCharacter(current)) return false;
                regex = CreateLiteralRegex(current.ToString());
                return true;
        }
    }
    private bool TryParseInlineOptionGroup(out RegexOptionScope groupOptions) {
        groupOptions = _options;
        var savedPosition = _position;
        if (!Peek('?')) return false;
        _position++;
        if (TryParseRegexOptionsUntil(')', out groupOptions)) return true;
        _position = savedPosition;
        groupOptions = _options;
        return false;
    }
    private bool TryParseGroupPrefix(out RegexOptionScope groupOptions) {
        groupOptions = _options;
        if (!Peek('?')) return true;
        _position++;
        if (Peek(':')) {
            _position++;
            return true;
        }
        if (Peek('>')) {
            _position++;
            // Atomic grouping can only remove matches by preventing backtracking.
            _isExact = false;
            return true;
        }
        if (TryParseOptionGroupPrefix(out groupOptions)) return true;
        groupOptions = _options;
        return TryParseNamedCaptureGroupPrefix();
    }
    private bool TryParseOptionGroupPrefix(out RegexOptionScope groupOptions) {
        var savedPosition = _position;
        if (TryParseRegexOptionsUntil(':', out groupOptions)) return true;
        _position = savedPosition;
        groupOptions = _options;
        return false;
    }
    private bool TryParseRegexOptionsUntil(char terminator, out RegexOptionScope groupOptions) {
        var position = _position;
        if (!TryReadOptionsUntil(
                _pattern,
                ref position,
                terminator,
                _options,
                _canUseIgnoreCase,
                out groupOptions))
            return false;
        _position = position;
        return true;
    }
    private bool TryParseNamedCaptureGroupPrefix() {
        if (Peek('<')) {
            if (_position + 1 >= _pattern.Length ||
                _pattern[_position + 1] is '=' or '!')
                return false;
            _position++;
            return TryReadCaptureName('>');
        }
        if (Peek('\'')) {
            _position++;
            return TryReadCaptureName('\'');
        }
        return false;
    }
    private bool TryReadCaptureName(char terminator) {
        var start = _position;
        while (_position < _pattern.Length) {
            var current = _pattern[_position];
            if (current == terminator) {
                if (_position == start) return false;
                _position++;
                return true;
            }
            if (!IsSupportedCaptureNameCharacter(current)) return false;
            _position++;
        }
        return false;
    }
    internal bool TryParseEscapedAtom([NotNullWhen(true)] out ReExpr? regex) {
        regex = null;
        if (_position >= _pattern.Length) return false;
        var escaped = _pattern[_position++];
        if (TryCreateEscapedCharacterClassRegex(escaped, out var escapedClass)) {
            _isExact &= escapedClass.IsExact;
            regex = escapedClass.Regex;
            return true;
        }
        if (!TryReadEscapedLiteralCharacter(escaped, out var literal)) return false;
        regex = CreateLiteralRegex(literal.ToString());
        return true;
    }
    internal bool TryParseCharClass([NotNullWhen(true)] out ReExpr? regex) =>
        TryParseWholeCharacterClassWithDotNet(out regex);
    internal bool TryParseSimpleCharClass([NotNullWhen(true)] out ReExpr? regex) =>
        TryParseWholeCharacterClassWithDotNet(out regex);
    internal bool TryParseWholeCharacterClassWithDotNet([NotNullWhen(true)] out ReExpr? regex) {
        regex = null;
        if (!TryReadWholeCharacterClassPattern(out var atomPattern)) return false;
        var options = CreateCurrentCharacterClassRegexOptions();
        if (!TryCreateCharacterRangesRegex(atomPattern, options, out regex)) {
            _isExact = false;
            regex = AnyCharacter();
        }
        return true;
    }
    private RegexOptions CreateCurrentCharacterClassRegexOptions() {
        var options = RegexOptions.None;
        if (_options.IgnorePatternWhitespace) options |= RegexOptions.IgnorePatternWhitespace;
        if (_options.IgnoreCase)
            options |= RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        else if (_canUseIgnoreCase) options |= RegexOptions.CultureInvariant;
        return options;
    }
    private bool TryReadWholeCharacterClassPattern(out string atomPattern) {
        atomPattern = string.Empty;
        var start = _position - 1;
        if (!TryFindCharacterClassEnd(_pattern, start, out var end)) return false;
        _position = end;
        atomPattern = _pattern.Substring(start, end - start);
        return true;
    }
    private bool TryReadEscapedLiteralCharacter(char escaped, out char value) {
        switch (escaped) {
            case 'x': return TryReadFixedHexChar(2, out value);
            case 'u': return TryReadFixedHexChar(4, out value);
            case 'c': return TryReadControlCharacterEscape(out value);
            case '0':
                value = ReadNullPrefixedOctalEscape();
                return true;
            default:
                return TryGetEscapedLiteralCharacter(escaped, out value);
        }
    }
    private bool TryCreateEscapedCharacterClassRegex(char escaped, out RegexClassTranslation regex) {
        regex = default;
        var atomPattern = escaped is 'd' or 'D' or 's' or 'S' or 'w' or 'W'
            ? "\\" + escaped
            : escaped is 'p' or 'P' && TryReadRegexCategoryName(out var category)
                ? "\\" + escaped + "{" + category + "}"
                : null;
        if (atomPattern == null) return false;
        var isExact = TryCreateCharacterRangesRegex(atomPattern, RegexOptions.None, out var expression);
        regex = new RegexClassTranslation(isExact ? expression! : AnyCharacter(), isExact);
        return true;
    }
    internal bool TryCreateCharacterRangesRegex(
        IReadOnlyList<CharacterRange> ranges,
        [NotNullWhen(true)] out ReExpr? regex) {
        if (ranges.Count == 0 || ranges.Count > MaxCharacterClassRangeCount) {
            regex = null;
            return false;
        }
        var expressions = new ReExpr[ranges.Count];
        for (var index = 0; index < ranges.Count; index++)
            expressions[index] = CharacterRange(ranges[index].Start, ranges[index].End);
        regex = expressions.Length == 1 ? expressions[0] : _context.MkUnion(expressions);
        return true;
    }
    internal bool TryCreateCharacterRangesRegex(
        string atomPattern,
        RegexOptions options,
        [NotNullWhen(true)] out ReExpr? regex) {
        if (TryGet(atomPattern, options, out var ranges))
            return TryCreateCharacterRangesRegex(ranges, out regex);
        regex = null;
        return false;
    }
    private bool TryReadRegexCategoryName(out string categoryName) {
        categoryName = string.Empty;
        if (!Peek('{')) return false;
        _position++;
        var start = _position;
        while (_position < _pattern.Length && !Peek('}')) {
            var current = _pattern[_position];
            if (!char.IsLetterOrDigit(current) && current != '_') return false;
            _position++;
        }
        if (_position == start || !Peek('}')) return false;
        _position++;
        categoryName = _pattern.Substring(start, _position - start - 1);
        return true;
    }
    private bool TryReadFixedHexChar(int digitCount, out char value) {
        value = default;
        if (_position + digitCount > _pattern.Length) return false;
        var parsed = 0;
        for (var index = 0; index < digitCount; index++) {
            var digit = HexValue(_pattern[_position + index]);
            if (digit < 0) return false;
            parsed = parsed * 16 + digit;
        }
        _position += digitCount;
        value = (char)parsed;
        return true;
    }
    private bool TryReadControlCharacterEscape(out char value) {
        value = default;
        if (_position >= _pattern.Length) return false;
        var control = _pattern[_position];
        if (control is >= 'a' and <= 'z')
            control = (char)(control - ('a' - 'A'));
        else if (control is not (>= 'A' and <= 'Z')) return false;
        _position++;
        value = (char)(control - '@');
        return true;
    }
    private char ReadNullPrefixedOctalEscape() {
        var value = 0;
        for (var digitCount = 0;
             digitCount < 2 && _position < _pattern.Length && IsOctalDigit(_pattern[_position]);
             digitCount++) {
            value = value * 8 + (_pattern[_position] - '0');
            _position++;
        }
        return (char)value;
    }
    private static bool IsOctalDigit(char value) =>
        value is >= '0' and <= '7';
    private static int HexValue(char value) {
        if (value >= '0' && value <= '9') return value - '0';
        if (value >= 'a' && value <= 'f') return value - 'a' + 10;
        if (value >= 'A' && value <= 'F') return value - 'A' + 10;
        return -1;
    }
    private bool TryReadNumber(out uint value) {
        value = 0;
        var start = _position;
        while (_position < _pattern.Length && char.IsDigit(_pattern[_position])) {
            var digit = (uint)(_pattern[_position] - '0');
            value = checked(value * 10 + digit);
            _position++;
            if (value > MaxBoundedRepeat) return false;
        }
        return _position > start;
    }
    private void ConsumeNonGreedyMarker() {
        SkipIgnoredPatternTrivia();
        if (Peek('?')) _position++;
    }
    private void SkipIgnoredPatternTrivia()
        => SkipIgnoredTrivia(_pattern, ref _position, _options.IgnorePatternWhitespace);
    private ReExpr CreateLiteralRegex(string value) {
        if (_options.IgnoreCase && value.Length != 0) {
            var regexes = new ReExpr[value.Length];
            for (var index = 0; index < value.Length; index++)
                regexes[index] = CreateIgnoreCaseLiteralCharacterRegex(value[index]);
            return regexes.Length == 1 ? regexes[0] : Concat(regexes);
        }
        return Literal(value);
    }
    private ReExpr CreateIgnoreCaseLiteralCharacterRegex(char value) {
        if (TryCreateCharacterRangesRegex(Regex.Escape(value.ToString()),
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, out var regex)) return regex;
        _isExact = false;
        return AnyCharacter();
    }
    private bool Peek(char value) =>
        _position < _pattern.Length && _pattern[_position] == value;
    private static bool IsRegexMetaCharacter(char value) =>
        value is '|' or '?' or '*' or '+' or ')' or '[' or ']' or '{' or '}';
    private static bool IsEscapedLiteralCharacter(char value) =>
        !char.IsLetterOrDigit(value);
    private static bool TryGetEscapedLiteralCharacter(char escaped, out char value) {
        value = escaped switch {
            'a' => '\a',
            'e' => '\u001b',
            'f' => '\f',
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            'v' => '\v',
            '\\' => '\\',
            '.' => '.',
            '^' => '^',
            '$' => '$',
            '|' => '|',
            '?' => '?',
            '*' => '*',
            '+' => '+',
            '(' => '(',
            ')' => ')',
            '[' => '[',
            ']' => ']',
            '{' => '{',
            '}' => '}',
            '-' => '-',
            _ => '\0'
        };
        if (value != '\0') return true;
        if (escaped is 'b' or 'B' || !IsEscapedLiteralCharacter(escaped)) return false;
        value = escaped;
        return true;
    }
    private static bool IsSupportedCaptureNameCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_';
    private static bool Fail([NotNullWhen(true)] out ReExpr? regex) {
        regex = null;
        return false;
    }
    readonly record struct RegexLookaheadAssertion(ReExpr Regex, bool IsPositive, bool IsExact);
    readonly record struct RegexClassTranslation(ReExpr Regex, bool IsExact);
}
internal sealed class Z3RegexTranslator {
    private readonly Z3RegexCompiler _compiler;
    internal Z3RegexTranslator(Context context, string pattern, RegexOptions options) =>
        _compiler = new(context, pattern, options);
    internal bool TryParseExpression([NotNullWhen(true)] out ReExpr? regex) => _compiler.TryParseExpression(out regex);
    internal bool TryParseConcat([NotNullWhen(true)] out ReExpr? regex) => _compiler.TryParseConcat(out regex);
    internal bool TryParseConcat(
        [NotNullWhen(true)] out ReExpr? regex,
        out bool consumedAny) =>
        _compiler.TryParseConcat(out regex, out consumedAny);
    internal bool TryConstrainSplitWithWordBoundary(
        ReExpr prefix,
        ReExpr suffix,
        bool isBoundary,
        [NotNullWhen(true)] out ReExpr? regex) =>
        _compiler.TryConstrainSplitWithWordBoundary(prefix, suffix, isBoundary, out regex);
    internal bool TryParseRepeat([NotNullWhen(true)] out ReExpr? regex) => _compiler.TryParseRepeat(out regex);
    internal bool TryParseAtom([NotNullWhen(true)] out ReExpr? regex) => _compiler.TryParseAtom(out regex);
    internal bool TryParseEscapedAtom([NotNullWhen(true)] out ReExpr? regex) => _compiler.TryParseEscapedAtom(out regex);
    internal bool TryParseCharClass([NotNullWhen(true)] out ReExpr? regex) => _compiler.TryParseCharClass(out regex);
    internal bool TryParseSimpleCharClass([NotNullWhen(true)] out ReExpr? regex) => _compiler.TryParseSimpleCharClass(out regex);
    internal bool TryParseWholeCharacterClassWithDotNet(
        [NotNullWhen(true)] out ReExpr? regex) =>
        _compiler.TryParseWholeCharacterClassWithDotNet(out regex);
    internal bool TryCreateCharacterRangesRegex(
        IReadOnlyList<CharacterRange> ranges,
        [NotNullWhen(true)] out ReExpr? regex) =>
        _compiler.TryCreateCharacterRangesRegex(ranges, out regex);
    internal bool TryCreateCharacterRangesRegex(
        string atomPattern,
        RegexOptions options,
        [NotNullWhen(true)] out ReExpr? regex) =>
        _compiler.TryCreateCharacterRangesRegex(atomPattern, options, out regex);
}
