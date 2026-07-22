namespace SharpProof.ProofCore.Smt;

internal sealed class Z3RegexTranslator {
    private const int MaxBoundedRepeat = 64;

    // Keep large Unicode category unions conservative; Z3 range-heavy regexes can get expensive
    // and smaller shorthand/category classes cover the common analyzer facts precisely.
    private const int MaxCharacterClassRangeCount = 512;

    private readonly bool _canUseIgnoreCase;
    private readonly Context _context;
    private readonly Z3RegexExpressionFactory _expressions;
    private readonly string _pattern;
    private bool _ignoreCase;
    private bool _ignorePatternWhitespace;
    private bool _isExact = true;
    private int _position;
    private bool _singleline;

    private Z3RegexTranslator(Context context, string pattern, RegexOptions options) {
        _context = context;
        _expressions = new Z3RegexExpressionFactory(context);
        _pattern = pattern;
        _ignorePatternWhitespace = (options & RegexOptions.IgnorePatternWhitespace) != 0;
        _ignoreCase = (options & RegexOptions.IgnoreCase) != 0;
        _canUseIgnoreCase = (options & RegexOptions.CultureInvariant) != 0;
        _singleline = (options & RegexOptions.Singleline) != 0;
    }

    internal static Z3RegexTranslationResult Translate(Context context, string pattern, RegexOptions options) {
        if (Z3RegexTranslationValidator.Validate(pattern, options) != RegexTranslationFallback.None)
            return Z3RegexTranslationResult.Failed();

        if (!Z3RegexPatternNormalizer.TryNormalize(pattern, options, out var normalized))
            return Z3RegexTranslationResult.Failed();

        var translator = new Z3RegexTranslator(context, normalized.Body, options);
        if (!translator.TryParseExpression(out var body))
            return Z3RegexTranslationResult.Failed();

        translator.SkipIgnoredPatternTrivia();
        if (translator._position != translator._pattern.Length)
            return Z3RegexTranslationResult.Failed();

        var regex = body;
        if (!normalized.StartAnchored) regex = context.MkConcat(translator._expressions.AnyString(), regex);

        if (normalized.DollarEndAnchored || normalized.FinalNewlineEndAnchored)
            regex = context.MkConcat(regex, translator._expressions.OptionalFinalNewline());
        else if (!normalized.StrictEndAnchored) regex = context.MkConcat(regex, translator._expressions.AnyString());

        return Z3RegexTranslationResult.Succeeded(regex, translator._isExact);
    }
    private bool TryParseExpression(out ReExpr regex) {
        SkipIgnoredPatternTrivia();
        var alternatives = new List<ReExpr>();
        if (!TryParseConcat(out var first)) {
            regex = null!;
            return false;
        }
        alternatives.Add(first);
        SkipIgnoredPatternTrivia();
        while (Peek('|')) {
            _position++;
            SkipIgnoredPatternTrivia();
            if (!TryParseConcat(out var alternative)) {
                regex = null!;
                return false;
            }
            alternatives.Add(alternative);
            SkipIgnoredPatternTrivia();
        }
        regex = alternatives.Count == 1
            ? alternatives[0]
            : _context.MkUnion([.. alternatives]);
        return true;
    }
    private bool TryParseConcat(out ReExpr regex) =>
        TryParseConcat(out regex, out _);

    private bool TryParseConcat(out ReExpr regex, out bool consumedAny) {
        var parts = new List<ReExpr>();
        consumedAny = false;
        while (true) {
            SkipIgnoredPatternTrivia();
            if (_position >= _pattern.Length ||
                Peek('|') ||
                Peek(')'))
                break;

            if (TryParseLookaroundAssertion(false, out var lookahead)) {
                if (!TryParseConcat(out var suffix, out var suffixConsumed) || !suffixConsumed) {
                    regex = null!;
                    return false;
                }
                parts.Add(ConstrainSuffixWithLookahead(lookahead, suffix));
                consumedAny = true;
                regex = parts.Count == 1 ? parts[0] : _context.MkConcat(parts.ToArray());
                return true;
            }
            if (TryParseLookaroundAssertion(true, out var lookbehind)) {
                if (parts.Count == 0) {
                    regex = null!;
                    return false;
                }
                var prefix = parts.Count == 1 ? parts[0] : _context.MkConcat(parts.ToArray());
                parts.Clear();
                parts.Add(ConstrainPrefixWithLookbehind(lookbehind, prefix));
                consumedAny = true;
                continue;
            }
            if (TryParseWordBoundaryAssertion(out var wordBoundary)) {
                var prefix = parts.Count == 0
                    ? CreateLiteralRegex(string.Empty)
                    : parts.Count == 1
                        ? parts[0]
                        : _context.MkConcat(parts.ToArray());
                if (!TryParseConcat(out var suffix, out var suffixConsumed) ||
                    !TryConstrainSplitWithWordBoundary(prefix, suffix, wordBoundary, out var constrained)) {
                    regex = null!;
                    return false;
                }
                consumedAny |= suffixConsumed;
                regex = constrained;
                return true;
            }
            if (!TryParseRepeat(out var part)) {
                regex = null!;
                return false;
            }
            parts.Add(part);
            consumedAny = true;
        }
        regex = parts.Count switch {
            0 => CreateLiteralRegex(string.Empty),
            1 => parts[0],
            _ => _context.MkConcat(parts.ToArray())
        };
        return true;
    }
    private bool TryParseLookaroundAssertion(bool lookbehind, out RegexLookaheadAssertion assertion) {
        assertion = default;
        SkipIgnoredPatternTrivia();
        var savedPosition = _position;
        var savedOptions = CaptureOptions();
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
            ApplyOptions(savedOptions);
            _isExact = savedIsExact;
            return false;
        }
        _position++;
        var lookaroundIsExact = _isExact;
        ApplyOptions(savedOptions);
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
        var lookaheadLanguage = _expressions.Concat(assertion.Regex, _expressions.AnyString());
        return assertion.IsPositive
            ? _context.MkIntersect(suffix, lookaheadLanguage)
            : _context.MkDiff(suffix, lookaheadLanguage);
    }
    private ReExpr ConstrainPrefixWithLookbehind(RegexLookaheadAssertion assertion, ReExpr prefix) {
        _isExact &= assertion.IsExact;
        var lookbehindLanguage = _expressions.Concat(_expressions.AnyString(), assertion.Regex);
        return assertion.IsPositive
            ? _context.MkIntersect(prefix, lookbehindLanguage)
            : _context.MkDiff(prefix, lookbehindLanguage);
    }
    private bool TryConstrainSplitWithWordBoundary(ReExpr prefix, ReExpr suffix, bool isBoundary, out ReExpr regex) {
        regex = null!;
        if (!TryCreateCharacterRangesRegex(Z3RegexCharacterRanges.Word, out var wordChar)) return false;

        var nonWordChar = _context.MkDiff(_expressions.AnyCharacter(), wordChar);
        var leftWord = ConstrainPrefixEnd(prefix, wordChar);
        var leftNonWord = _context.MkUnion(ConstrainPrefixEnd(prefix, nonWordChar),
            _context.MkIntersect(prefix, CreateLiteralRegex(string.Empty)));
        var rightWord = ConstrainSuffixStart(suffix, wordChar);
        var rightNonWord = _context.MkUnion(ConstrainSuffixStart(suffix, nonWordChar),
            _context.MkIntersect(suffix, CreateLiteralRegex(string.Empty)));

        var first = _expressions.Concat(leftWord, isBoundary ? rightNonWord : rightWord);
        var second = _expressions.Concat(leftNonWord, isBoundary ? rightWord : rightNonWord);
        regex = _context.MkUnion(first, second);
        return true;
    }
    private ReExpr ConstrainPrefixEnd(ReExpr prefix, ReExpr finalCharacter) =>
        _context.MkIntersect(prefix, _expressions.Concat(_expressions.AnyString(), finalCharacter));

    private ReExpr ConstrainSuffixStart(ReExpr suffix, ReExpr firstCharacter) =>
        _context.MkIntersect(suffix, _expressions.Concat(firstCharacter, _expressions.AnyString()));

    private bool TryParseRepeat(out ReExpr regex) {
        SkipIgnoredPatternTrivia();
        if (!TryParseAtom(out regex)) return false;

        SkipIgnoredPatternTrivia();
        if (_position >= _pattern.Length) return true;

        switch (_pattern[_position]) {
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
            ? _expressions.Concat(_expressions.ExactRepeat(regex, lower), _context.MkStar(regex))
            : _context.MkLoop(regex, lower, upper);
        ConsumeNonGreedyMarker();
        return true;
    }
    private bool TryParseAtom(out ReExpr regex) {
        regex = null!;
        SkipIgnoredPatternTrivia();
        if (_position >= _pattern.Length) return false;

        var current = _pattern[_position++];
        switch (current) {
            case '(':
                if (TryParseInlineOptionGroup(out var inlineOptions)) {
                    ApplyOptions(inlineOptions);
                    regex = CreateLiteralRegex(string.Empty);
                    return true;
                }
                var outerOptions = CaptureOptions();
                if (!TryParseGroupPrefix(out var groupOptions)) return false;

                ApplyOptions(groupOptions);
                if (!TryParseExpression(out regex) || !Peek(')')) {
                    ApplyOptions(outerOptions);
                    return false;
                }
                _position++;
                ApplyOptions(outerOptions);
                return true;
            case '[':
                return TryParseCharClass(out regex);
            case '.':
                regex = _expressions.Dot(_singleline);
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
    private RegexOptionScope CaptureOptions() =>
        new(_ignorePatternWhitespace, _singleline, _ignoreCase);

    private void ApplyOptions(RegexOptionScope options) {
        _ignorePatternWhitespace = options.IgnorePatternWhitespace;
        _singleline = options.Singleline;
        _ignoreCase = options.IgnoreCase;
    }
    private bool TryParseInlineOptionGroup(out RegexOptionScope groupOptions) {
        groupOptions = CaptureOptions();
        var savedPosition = _position;
        if (!Peek('?')) return false;

        _position++;
        if (TryParseRegexOptionsUntil(')', out groupOptions)) return true;

        _position = savedPosition;
        groupOptions = CaptureOptions();
        return false;
    }
    private bool TryParseGroupPrefix(out RegexOptionScope groupOptions) {
        groupOptions = CaptureOptions();
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

        groupOptions = CaptureOptions();
        return TryParseNamedCaptureGroupPrefix();
    }
    private bool TryParseOptionGroupPrefix(out RegexOptionScope groupOptions) {
        var savedPosition = _position;
        if (TryParseRegexOptionsUntil(':', out groupOptions)) return true;

        _position = savedPosition;
        groupOptions = CaptureOptions();
        return false;
    }
    private bool TryParseRegexOptionsUntil(char terminator, out RegexOptionScope groupOptions) {
        var position = _position;
        if (!Z3RegexPatternNormalizer.TryReadOptionsUntil(
                _pattern,
                ref position,
                terminator,
                CaptureOptions(),
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
    private bool TryParseEscapedAtom(out ReExpr regex) {
        regex = null!;
        if (_position >= _pattern.Length) return false;

        var escaped = _pattern[_position++];
        if (TryCreateEscapedCharacterClassRegex(escaped, out var escapedClass)) {
            _isExact &= escapedClass.IsExact;
            regex = escapedClass.Regex;
            return true;
        }
        if (!TryReadEscapedLiteralCharacter(escaped, false, out var literal)) return false;

        regex = CreateLiteralRegex(literal.ToString());
        return true;
    }
    private bool TryParseCharClass(out ReExpr regex) {
        var classStart = _position - 1;
        var savedIsExact = _isExact;
        if (_ignoreCase) return TryParseWholeCharacterClassWithDotNet(out regex);

        if (TryParseSimpleCharClass(out regex)) return true;

        _position = classStart + 1;
        _isExact = savedIsExact;
        return TryParseWholeCharacterClassWithDotNet(out regex);
    }
    private bool TryParseSimpleCharClass(out ReExpr regex) {
        regex = null!;
        var negate = false;
        if (Peek('^')) {
            negate = true;
            _position++;
        }
        var parts = new List<CharacterClassPart>();
        if (Peek(']')) {
            parts.Add(CreateClassCharacterPart(']'));
            _position++;
        }
        while (_position < _pattern.Length && !Peek(']')) {
            if (!TryReadClassPart(out var start)) return false;

            if (Peek('-') &&
                _position + 1 < _pattern.Length &&
                _pattern[_position + 1] != ']') {
                _position++;
                if (start.ExactCharacter is not { } startCharacter ||
                    !TryReadClassPart(out var end) ||
                    end.ExactCharacter is not { } endCharacter ||
                    endCharacter < startCharacter)
                    return false;

                parts.Add(new CharacterClassPart(
                    _expressions.CharacterRange(startCharacter, endCharacter),
                    null,
                    false,
                    [new CharacterRange(startCharacter, endCharacter)]));
            }
            else {
                parts.Add(start);
            }
        }
        if (!Peek(']') || parts.Count == 0) return false;

        _position++;
        regex = parts.Count == 1
            ? parts[0].Regex
            : _context.MkUnion([.. parts.Select(static part => part.Regex)]);
        if (negate) {
            if (parts.Any(static part => part.IsApproximation || part.Ranges == null)) return false;

            var complementRanges = Z3RegexCharacterRanges.Complement(Z3RegexCharacterRanges.Merge(parts.SelectMany(static part
                => part.Ranges!)));
            if (!TryCreateCharacterRangesRegex(complementRanges, out regex)) return false;
        }
        return true;
    }
    private bool TryParseWholeCharacterClassWithDotNet(out ReExpr regex) {
        regex = null!;
        if (!TryReadWholeCharacterClassPattern(out var atomPattern)) return false;

        var options = CreateCurrentCharacterClassRegexOptions();
        if (!TryCreateCharacterRangesRegex(atomPattern, options, out regex)) {
            _isExact = false;
            regex = _expressions.AnyCharacter();
        }
        return true;
    }
    private RegexOptions CreateCurrentCharacterClassRegexOptions() {
        var options = RegexOptions.None;
        if (_ignorePatternWhitespace) options |= RegexOptions.IgnorePatternWhitespace;

        if (_ignoreCase)
            options |= RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        else if (_canUseIgnoreCase) options |= RegexOptions.CultureInvariant;

        return options;
    }
    private bool TryReadWholeCharacterClassPattern(out string atomPattern) {
        atomPattern = string.Empty;
        var start = _position - 1;
        if (start < 0 || start >= _pattern.Length || _pattern[start] != '[') return false;

        var index = _position;
        if (index < _pattern.Length && _pattern[index] == '^') index++;

        if (index < _pattern.Length && _pattern[index] == ']') index++;

        var escaped = false;
        for (; index < _pattern.Length; index++) {
            var current = _pattern[index];
            if (escaped) {
                escaped = false;
                continue;
            }
            if (current == '\\') {
                escaped = true;
                continue;
            }
            if (current == ']') {
                _position = index + 1;
                atomPattern = _pattern.Substring(start, index - start + 1);
                return true;
            }
        }
        return false;
    }
    private bool TryReadClassPart(out CharacterClassPart part) {
        part = default;
        if (_position >= _pattern.Length) return false;

        var current = _pattern[_position++];
        if (current != '\\') {
            part = CreateClassCharacterPart(current);
            return true;
        }
        if (_position >= _pattern.Length) return false;

        var escaped = _pattern[_position++];
        if (TryCreateEscapedCharacterClassRegex(escaped, out var escapedClass)) {
            _isExact &= escapedClass.IsExact;
            part = new CharacterClassPart(escapedClass.Regex, null, !escapedClass.IsExact, escapedClass.Ranges);
            return true;
        }
        if (!TryReadEscapedLiteralCharacter(escaped, true, out var value)) return false;

        part = CreateClassCharacterPart(value);
        return true;
    }
    private CharacterClassPart CreateClassCharacterPart(char value)
        => new(CreateLiteralRegex(value.ToString()), value, false, [new CharacterRange(value, value)]);
    private bool TryReadEscapedLiteralCharacter(char escaped, bool inCharacterClass, out char value) {
        switch (escaped) {
            case 'x': return TryReadFixedHexChar(2, out value);
            case 'u': return TryReadFixedHexChar(4, out value);
            case 'c': return TryReadControlCharacterEscape(out value);
            case '0':
                value = ReadNullPrefixedOctalEscape();
                return true;
            default:
                return TryGetEscapedLiteralCharacter(escaped, inCharacterClass, out value);
        }
    }
    private bool TryCreateEscapedCharacterClassRegex(char escaped, out RegexClassTranslation regex) {
        regex = default;
        if (Z3RegexCharacterRanges.TryGetShorthand(escaped, out var shorthandRanges)) {
            regex = CreateCharacterClassTranslation(shorthandRanges);
            return true;
        }
        if (escaped is 'p' or 'P') {
            if (!TryReadRegexCategoryName(out var categoryName)) return false;

            if (!Z3RegexCharacterRanges.TryGet(@"\p{" + categoryName + "}", out var categoryRanges)) {
                regex = new RegexClassTranslation(_expressions.AnyCharacter(), false, null);
                return true;
            }
            var ranges = escaped == 'p' ? categoryRanges : Z3RegexCharacterRanges.Complement(categoryRanges);
            regex = CreateCharacterClassTranslation(ranges);
            return true;
        }
        return false;
    }
    private RegexClassTranslation CreateCharacterClassTranslation(CharacterRange[] ranges)
        => TryCreateCharacterRangesRegex(ranges, out var regex)
            ? new RegexClassTranslation(regex, true, ranges)
            : new RegexClassTranslation(_expressions.AnyCharacter(), false, null);
    private ReExpr CreateCharacterRangesRegex(IReadOnlyList<CharacterRange> ranges) {
        if (ranges.Count == 0 || ranges.Count > MaxCharacterClassRangeCount)
            throw new InvalidOperationException("Unsupported character class range count.");

        var regexes = new ReExpr[ranges.Count];
        for (var index = 0; index < ranges.Count; index++)
            regexes[index] = _expressions.CharacterRange(ranges[index].Start, ranges[index].End);

        return regexes.Length == 1 ? regexes[0] : _context.MkUnion(regexes);
    }
    private bool TryCreateCharacterRangesRegex(IReadOnlyList<CharacterRange> ranges, out ReExpr regex) {
        regex = null!;
        try {
            regex = CreateCharacterRangesRegex(ranges);
            return true;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }
    private bool TryCreateCharacterRangesRegex(string atomPattern, RegexOptions options, out ReExpr regex) {
        regex = null!;
        try {
            if (!Z3RegexCharacterRanges.TryGet(atomPattern, options, out var ranges)) return false;

            regex = CreateCharacterRangesRegex(ranges);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or RegexMatchTimeoutException) {
            return false;
        }
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
        => Z3RegexPatternNormalizer.SkipIgnoredTrivia(_pattern, ref _position, _ignorePatternWhitespace);
    private ReExpr CreateLiteralRegex(string value) {
        if (_ignoreCase && value.Length != 0) {
            var regexes = new ReExpr[value.Length];
            for (var index = 0; index < value.Length; index++)
                regexes[index] = CreateIgnoreCaseLiteralCharacterRegex(value[index]);

            return regexes.Length == 1 ? regexes[0] : _expressions.Concat(regexes);
        }
        return _expressions.Literal(value);
    }
    private ReExpr CreateIgnoreCaseLiteralCharacterRegex(char value) {
        if (TryCreateCharacterRangesRegex(Regex.Escape(value.ToString()),
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, out var regex)) return regex;

        _isExact = false;
        return _expressions.AnyCharacter();
    }
    private bool Peek(char value) =>
        _position < _pattern.Length && _pattern[_position] == value;

    private static bool IsRegexMetaCharacter(char value) =>
        value is '|' or '?' or '*' or '+' or ')' or '[' or ']' or '{' or '}';

    private static bool IsEscapedLiteralCharacter(char value) =>
        !char.IsLetterOrDigit(value);

    private static bool TryGetEscapedLiteralCharacter(char escaped, bool inCharacterClass, out char value) {
        value = escaped switch {
            'a' => '\a',
            'b' when inCharacterClass => '\b',
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

    readonly record struct RegexLookaheadAssertion(ReExpr Regex, bool IsPositive, bool IsExact);

    readonly record struct CharacterClassPart(ReExpr Regex, char? ExactCharacter, bool IsApproximation, CharacterRange[]? Ranges);

    readonly record struct RegexClassTranslation(ReExpr Regex, bool IsExact, CharacterRange[]? Ranges);
}
