using System.Text.RegularExpressions;
using Microsoft.Z3;

namespace SharpProof.ProofCore.Smt;

internal sealed class Z3RegexTranslator
{
    private const int MaxBoundedRepeat = 64;

    // Keep large Unicode category unions conservative; Z3 range-heavy regexes can get expensive
    // and smaller shorthand/category classes cover the common analyzer facts precisely.
    private const int MaxCharacterClassRangeCount = 512;
    private static readonly TimeSpan RegexSyntaxValidationTimeout = TimeSpan.FromMilliseconds(50);

    private readonly bool _canUseIgnoreCase;
    private readonly Context _context;
    private readonly string _pattern;
    private bool _ignoreCase;
    private bool _ignorePatternWhitespace;
    private bool _isExact = true;
    private ReExpr? _anyCharRegex;
    private int _position;
    private bool _singleline;

    private Z3RegexTranslator(Context context, string pattern, RegexOptions options)
    {
        _context = context;
        _pattern = pattern;
        _ignorePatternWhitespace = (options & RegexOptions.IgnorePatternWhitespace) != 0;
        _ignoreCase = (options & RegexOptions.IgnoreCase) != 0;
        _canUseIgnoreCase = (options & RegexOptions.CultureInvariant) != 0;
        _singleline = (options & RegexOptions.Singleline) != 0;
    }


    public static bool TryTranslate(Context context, string pattern, RegexOptions options, out ReExpr regex,
        out bool isExact)
    {
        regex = null!;
        isExact = true;
        if (pattern.Length > 256) return false;

        if (!IsValidDotNetRegexPattern(pattern, options)) return false;

        var multiline = (options & RegexOptions.Multiline) != 0;
        var startAnchored = TryFindLeadingStartAnchor(
            pattern,
            options,
            !multiline,
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
            return false;

        var bodyPattern = pattern.Substring(0, bodyEnd);
        if (startAnchored) bodyPattern = bodyPattern.Remove(startAnchorStart, startAnchorLength);

        var translator = new Z3RegexTranslator(context, bodyPattern, options);
        if (!translator.TryParseExpression(out var body)) return false;

        translator.SkipIgnoredPatternTrivia();
        if (translator._position != translator._pattern.Length) return false;

        regex = body;
        isExact = translator._isExact;
        if (!startAnchored) regex = context.MkConcat(translator.CreateAnyStringRegex(), regex);

        if (dollarEndAnchored || finalNewlineEndAnchored)
            regex = context.MkConcat(regex, translator.CreateOptionalFinalNewlineRegex());
        else if (!strictEndAnchored) regex = context.MkConcat(regex, translator.CreateAnyStringRegex());

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
            if (!TryReadInlineOptionGroup(pattern, index, optionScope, canUseIgnoreCase, out var nextScope,
                    out var nextIndex)) break;

            optionScope = nextScope;
            index = nextIndex;
        }

        if (index >= pattern.Length) return false;

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
            return false;

        var index = start + 2;
        if (!TryReadRegexOptionsUntil(
                pattern,
                ref index,
                ')',
                currentScope,
                canUseIgnoreCase,
                out nextScope))
            return false;

        nextIndex = index;
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
                break;

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
        return TryParseLookaroundAssertion(false, out assertion);
    }

    private bool TryParseLookbehindAssertion(out RegexLookaheadAssertion assertion)
    {
        return TryParseLookaroundAssertion(true, out assertion);
    }

    private bool TryParseLookaroundAssertion(bool lookbehind, out RegexLookaheadAssertion assertion)
    {
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
        if (!TryParseExpression(out var lookaroundRegex) || !Peek(')'))
        {
            _position = savedPosition;
            ApplyOptions(savedOptions);
            _isExact = savedIsExact;
            return false;
        }

        _position++;
        var lookaroundIsExact = _isExact;
        ApplyOptions(savedOptions);
        _isExact = savedIsExact;
        if (!positive && !lookaroundIsExact)
        {
            _position = savedPosition;
            return false;
        }

        assertion = new RegexLookaheadAssertion(lookaroundRegex, positive, lookaroundIsExact);
        return true;
    }

    private bool TryParseWordBoundaryAssertion(out bool isBoundary)
    {
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

    private ReExpr ConstrainSuffixWithLookahead(RegexLookaheadAssertion assertion, ReExpr suffix)
    {
        _isExact &= assertion.IsExact;
        var lookaheadLanguage = CreateConcat(assertion.Regex, CreateAnyStringRegex());
        return assertion.IsPositive
            ? _context.MkIntersect(suffix, lookaheadLanguage)
            : _context.MkDiff(suffix, lookaheadLanguage);
    }

    private ReExpr ConstrainPrefixWithLookbehind(RegexLookaheadAssertion assertion, ReExpr prefix)
    {
        _isExact &= assertion.IsExact;
        var lookbehindLanguage = CreateConcat(CreateAnyStringRegex(), assertion.Regex);
        return assertion.IsPositive
            ? _context.MkIntersect(prefix, lookbehindLanguage)
            : _context.MkDiff(prefix, lookbehindLanguage);
    }

    private bool TryConstrainSplitWithWordBoundary(
        ReExpr prefix,
        ReExpr suffix,
        bool isBoundary,
        out ReExpr regex)
    {
        regex = null!;
        if (!TryCreateCharacterRangesRegex(Z3RegexCharacterRanges.Word, out var wordChar)) return false;

        var nonWordChar = _context.MkDiff(CreateAnyCharRegex(), wordChar);
        var leftWord = ConstrainPrefixEnd(prefix, wordChar);
        var leftNonWord = _context.MkUnion(ConstrainPrefixEnd(prefix, nonWordChar),
            _context.MkIntersect(prefix, CreateLiteralRegex(string.Empty)));
        var rightWord = ConstrainSuffixStart(suffix, wordChar);
        var rightNonWord = _context.MkUnion(ConstrainSuffixStart(suffix, nonWordChar),
            _context.MkIntersect(suffix, CreateLiteralRegex(string.Empty)));

        var first = CreateConcat(leftWord, isBoundary ? rightNonWord : rightWord);
        var second = CreateConcat(leftNonWord, isBoundary ? rightWord : rightNonWord);
        regex = _context.MkUnion(first, second);
        return true;
    }

    private ReExpr ConstrainPrefixEnd(ReExpr prefix, ReExpr finalCharacter)
    {
        return _context.MkIntersect(prefix, CreateConcat(CreateAnyStringRegex(), finalCharacter));
    }

    private ReExpr ConstrainSuffixStart(ReExpr suffix, ReExpr firstCharacter)
    {
        return _context.MkIntersect(suffix, CreateConcat(firstCharacter, CreateAnyStringRegex()));
    }

    private bool TryParseRepeat(out ReExpr regex)
    {
        SkipIgnoredPatternTrivia();
        if (!TryParseAtom(out regex)) return false;

        SkipIgnoredPatternTrivia();
        if (_position >= _pattern.Length) return true;

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

        var upper = lower;
        var unbounded = false;
        if (Peek(','))
        {
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
            ? CreateConcat(CreateExactRepeat(regex, lower), _context.MkStar(regex))
            : _context.MkLoop(regex, lower, upper);
        ConsumeNonGreedyMarker();
        return true;
    }

    private bool TryParseAtom(out ReExpr regex)
    {
        regex = null!;
        SkipIgnoredPatternTrivia();
        if (_position >= _pattern.Length) return false;

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
                if (!TryParseGroupPrefix(out var groupOptions)) return false;

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
                if (IsRegexMetaCharacter(current)) return false;

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
        if (!Peek('?')) return false;

        _position++;
        if (TryParseRegexOptionsUntil(')', out groupOptions)) return true;

        _position = savedPosition;
        groupOptions = CaptureOptions();
        return false;
    }

    private bool TryParseGroupPrefix(out RegexOptionScope groupOptions)
    {
        groupOptions = CaptureOptions();
        if (!Peek('?')) return true;

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

        if (TryParseOptionGroupPrefix(out groupOptions)) return true;

        groupOptions = CaptureOptions();
        return TryParseNamedCaptureGroupPrefix();
    }

    private bool TryParseOptionGroupPrefix(out RegexOptionScope groupOptions)
    {
        var savedPosition = _position;
        if (TryParseRegexOptionsUntil(':', out groupOptions)) return true;

        _position = savedPosition;
        groupOptions = CaptureOptions();
        return false;
    }

    private bool TryParseRegexOptionsUntil(char terminator, out RegexOptionScope groupOptions)
    {
        var position = _position;
        if (!TryReadRegexOptionsUntil(
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

    private static bool TryReadRegexOptionsUntil(
        string pattern,
        ref int position,
        char terminator,
        RegexOptionScope currentScope,
        bool canUseIgnoreCase,
        out RegexOptionScope nextScope)
    {
        nextScope = currentScope;
        var nextIgnorePatternWhitespace = currentScope.IgnorePatternWhitespace;
        var nextSingleline = currentScope.Singleline;
        var nextIgnoreCase = currentScope.IgnoreCase;
        var sawOption = false;
        var sawDisableSeparator = false;
        while (position < pattern.Length && pattern[position] != terminator)
        {
            var current = pattern[position];
            if (current == '-')
            {
                if (sawDisableSeparator) return false;

                sawDisableSeparator = true;
                position++;
                continue;
            }

            if (current == 'n')
            {
                sawOption = true;
                position++;
                continue;
            }

            if (current == 'x')
            {
                sawOption = true;
                nextIgnorePatternWhitespace = !sawDisableSeparator;
                position++;
                continue;
            }

            if (current == 's')
            {
                sawOption = true;
                nextSingleline = !sawDisableSeparator;
                position++;
                continue;
            }

            if (current == 'i' && canUseIgnoreCase)
            {
                sawOption = true;
                nextIgnoreCase = !sawDisableSeparator;
                position++;
                continue;
            }

            return false;
        }

        if (!sawOption || position >= pattern.Length || pattern[position] != terminator) return false;

        position++;
        nextScope = new RegexOptionScope(nextIgnorePatternWhitespace, nextSingleline, nextIgnoreCase);
        return true;
    }

    private bool TryParseNamedCaptureGroupPrefix()
    {
        if (Peek('<'))
        {
            if (_position + 1 >= _pattern.Length ||
                _pattern[_position + 1] is '=' or '!')
                return false;

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
                if (_position == start) return false;

                _position++;
                return true;
            }

            if (!IsSupportedCaptureNameCharacter(current)) return false;

            _position++;
        }

        return false;
    }

    private bool TryParseEscapedAtom(out ReExpr regex)
    {
        regex = null!;
        if (_position >= _pattern.Length) return false;

        var escaped = _pattern[_position++];
        if (TryCreateEscapedCharacterClassRegex(escaped, out var escapedClass))
        {
            _isExact &= escapedClass.IsExact;
            regex = escapedClass.Regex;
            return true;
        }

        if (!TryReadEscapedLiteralCharacter(escaped, false, out var literal)) return false;

        regex = CreateLiteralRegex(literal.ToString());
        return true;
    }

    private bool TryParseCharClass(out ReExpr regex)
    {
        var classStart = _position - 1;
        var savedIsExact = _isExact;
        if (_ignoreCase) return TryParseWholeCharacterClassWithDotNet(out regex);

        if (TryParseSimpleCharClass(out regex)) return true;

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
            if (!TryReadClassPart(out var start)) return false;

            if (Peek('-') &&
                _position + 1 < _pattern.Length &&
                _pattern[_position + 1] != ']')
            {
                _position++;
                if (start.ExactCharacter is not { } startCharacter ||
                    !TryReadClassPart(out var end) ||
                    end.ExactCharacter is not { } endCharacter ||
                    endCharacter < startCharacter)
                    return false;

                parts.Add(new CharacterClassPart(
                    CreateCharacterRangeRegex(startCharacter, endCharacter),
                    null,
                    false,
                    new[] { new CharacterRange(startCharacter, endCharacter) }));
            }
            else
            {
                parts.Add(start);
            }
        }

        if (!Peek(']') || parts.Count == 0) return false;

        _position++;
        regex = parts.Count == 1
            ? parts[0].Regex
            : _context.MkUnion(parts.Select(static part => part.Regex).ToArray());
        if (negate)
        {
            if (parts.Any(static part => part.IsApproximation || part.Ranges == null)) return false;

            var complementRanges = Z3RegexCharacterRanges.Complement(Z3RegexCharacterRanges.Merge(parts.SelectMany(static part => part.Ranges!)));
            if (!TryCreateCharacterRangesRegex(complementRanges, out regex)) return false;
        }

        return true;
    }

    private bool TryParseWholeCharacterClassWithDotNet(out ReExpr regex)
    {
        regex = null!;
        if (!TryReadWholeCharacterClassPattern(out var atomPattern)) return false;

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
        if (_ignorePatternWhitespace) options |= RegexOptions.IgnorePatternWhitespace;

        if (_ignoreCase)
            options |= RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        else if (_canUseIgnoreCase) options |= RegexOptions.CultureInvariant;

        return options;
    }

    private bool TryReadWholeCharacterClassPattern(out string atomPattern)
    {
        atomPattern = string.Empty;
        var start = _position - 1;
        if (start < 0 || start >= _pattern.Length || _pattern[start] != '[') return false;

        var index = _position;
        if (index < _pattern.Length && _pattern[index] == '^') index++;

        if (index < _pattern.Length && _pattern[index] == ']') index++;

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
        if (_position >= _pattern.Length) return false;

        var current = _pattern[_position++];
        if (current != '\\')
        {
            part = CreateClassCharacterPart(current);
            return true;
        }

        if (_position >= _pattern.Length) return false;

        var escaped = _pattern[_position++];
        if (TryCreateEscapedCharacterClassRegex(escaped, out var escapedClass))
        {
            _isExact &= escapedClass.IsExact;
            part = new CharacterClassPart(
                escapedClass.Regex,
                null,
                !escapedClass.IsExact,
                escapedClass.Ranges);
            return true;
        }

        if (!TryReadEscapedLiteralCharacter(escaped, true, out var value)) return false;

        part = CreateClassCharacterPart(value);
        return true;
    }

    private CharacterClassPart CreateClassCharacterPart(char value)
    {
        return new CharacterClassPart(
            CreateLiteralRegex(value.ToString()),
            value,
            false,
            new[] { new CharacterRange(value, value) });
    }

    private bool TryReadEscapedLiteralCharacter(char escaped, bool inCharacterClass, out char value)
    {
        switch (escaped)
        {
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

    private bool TryCreateEscapedCharacterClassRegex(char escaped, out RegexClassTranslation regex)
    {
        regex = default;
        if (Z3RegexCharacterRanges.TryGetShorthand(escaped, out var shorthandRanges))
        {
            regex = CreateCharacterClassTranslation(shorthandRanges);
            return true;
        }

        if (escaped is 'p' or 'P')
        {
            if (!TryReadRegexCategoryName(out var categoryName)) return false;

            if (!Z3RegexCharacterRanges.TryGet(@"\p{" + categoryName + "}", out var categoryRanges))
            {
                regex = new RegexClassTranslation(CreateAnyCharRegex(), false, null);
                return true;
            }

            var ranges = escaped == 'p' ? categoryRanges : Z3RegexCharacterRanges.Complement(categoryRanges);
            regex = CreateCharacterClassTranslation(ranges);
            return true;
        }

        return false;
    }

    private RegexClassTranslation CreateCharacterClassTranslation(CharacterRange[] ranges)
    {
        return TryCreateCharacterRangesRegex(ranges, out var regex)
            ? new RegexClassTranslation(regex, true, ranges)
            : new RegexClassTranslation(CreateAnyCharRegex(), false, null);
    }

    private ReExpr CreateCharacterRangesRegex(IReadOnlyList<CharacterRange> ranges)
    {
        if (ranges.Count == 0 || ranges.Count > MaxCharacterClassRangeCount)
            throw new InvalidOperationException("Unsupported character class range count.");

        var regexes = new ReExpr[ranges.Count];
        for (var index = 0; index < ranges.Count; index++)
            regexes[index] = CreateCharacterRangeRegex(ranges[index].Start, ranges[index].End);

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

    private bool TryCreateCharacterRangesRegex(string atomPattern, RegexOptions options, out ReExpr regex)
    {
        regex = null!;
        try
        {
            if (!Z3RegexCharacterRanges.TryGet(atomPattern, options, out var ranges)) return false;

            regex = CreateCharacterRangesRegex(ranges);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private bool TryReadRegexCategoryName(out string categoryName)
    {
        categoryName = string.Empty;
        if (!Peek('{')) return false;

        _position++;
        var start = _position;
        while (_position < _pattern.Length && !Peek('}'))
        {
            var current = _pattern[_position];
            if (!char.IsLetterOrDigit(current) && current != '_') return false;

            _position++;
        }

        if (_position == start || !Peek('}')) return false;

        _position++;
        categoryName = _pattern.Substring(start, _position - start - 1);
        return true;
    }

    private bool TryReadFixedHexChar(int digitCount, out char value)
    {
        value = default;
        if (_position + digitCount > _pattern.Length) return false;

        var parsed = 0;
        for (var index = 0; index < digitCount; index++)
        {
            var digit = HexValue(_pattern[_position + index]);
            if (digit < 0) return false;

            parsed = parsed * 16 + digit;
        }

        _position += digitCount;
        value = (char)parsed;
        return true;
    }

    private bool TryReadControlCharacterEscape(out char value)
    {
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

    private char ReadNullPrefixedOctalEscape()
    {
        var value = 0;
        for (var digitCount = 0;
             digitCount < 2 && _position < _pattern.Length && IsOctalDigit(_pattern[_position]);
             digitCount++)
        {
            value = value * 8 + (_pattern[_position] - '0');
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
        if (value >= '0' && value <= '9') return value - '0';

        if (value >= 'a' && value <= 'f') return value - 'a' + 10;

        if (value >= 'A' && value <= 'F') return value - 'A' + 10;

        return -1;
    }

    private bool TryReadNumber(out uint value)
    {
        value = 0;
        var start = _position;
        while (_position < _pattern.Length && char.IsDigit(_pattern[_position]))
        {
            var digit = (uint)(_pattern[_position] - '0');
            value = checked(value * 10 + digit);
            _position++;
            if (value > MaxBoundedRepeat) return false;
        }

        return _position > start;
    }

    private void ConsumeNonGreedyMarker()
    {
        SkipIgnoredPatternTrivia();
        if (Peek('?')) _position++;
    }

    private void SkipIgnoredPatternTrivia()
    {
        SkipIgnoredPatternTrivia(_pattern, ref _position, _ignorePatternWhitespace);
    }

    private static void SkipIgnoredPatternTrivia(string pattern, ref int position, bool ignorePatternWhitespace)
    {
        while (position < pattern.Length)
        {
            if (TrySkipInlineComment(pattern, ref position)) continue;

            if (!ignorePatternWhitespace) return;

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
                    position++;

                continue;
            }

            return;
        }
    }

    private static bool TrySkipInlineComment(string pattern, ref int position)
    {
        if (position + 2 >= pattern.Length ||
            pattern[position] != '(' ||
            pattern[position + 1] != '?' ||
            pattern[position + 2] != '#')
            return false;

        var end = position + 3;
        while (end < pattern.Length && pattern[end] != ')') end++;

        if (end >= pattern.Length) return false;

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
        if (_anyCharRegex != null) return _anyCharRegex;

        const string marker = "__sharpproof_allchar";
        var regexSort = _context.MkReSort(_context.StringSort);
        var declaration = _context.MkConstDecl(marker, regexSort);
        var assertions = _context.ParseSMTLIB2String(
            "(assert (= " + marker + " re.allchar))",
            Array.Empty<Symbol>(),
            Array.Empty<Sort>(),
            new[] { _context.MkSymbol(marker) },
            new[] { declaration });
        if (assertions.Length != 1 ||
            assertions[0].Args.Length != 2 ||
            assertions[0].Args[1] is not ReExpr allChar)
            throw new InvalidOperationException("Unable to create the Z3 all-character regular expression.");

        _anyCharRegex = allChar;
        return allChar;
    }

    private ReExpr CreateCharacterRangeRegex(char start, char end)
    {
        if (start > end) throw new ArgumentOutOfRangeException(nameof(start));

        if (start == char.MinValue)
        {
            if (end == char.MaxValue) return CreateAnyCharRegex();

            return _context.MkDiff(
                CreateAnyCharRegex(),
                CreateCharacterRangeRegex((char)(end + 1), char.MaxValue));
        }

        return _context.MkRange(
            _context.MkString(start.ToString()),
            _context.MkString(end.ToString()));
    }

    private ReExpr CreateDotRegex()
    {
        return _singleline
            ? CreateAnyCharRegex()
            : _context.MkDiff(CreateAnyCharRegex(), CreateLiteralRegex("\n"));
    }

    private ReExpr CreateExactRepeat(ReExpr regex, uint count)
    {
        if (count == 0) return CreateLiteralRegex(string.Empty);

        return _context.MkLoop(regex, count, count);
    }

    private ReExpr CreateConcat(ReExpr left, ReExpr right)
    {
        return _context.MkConcat(left, right);
    }

    private ReExpr CreateLiteralRegex(string value)
    {
        if (_ignoreCase && value.Length != 0)
        {
            var regexes = new ReExpr[value.Length];
            for (var index = 0; index < value.Length; index++)
                regexes[index] = CreateIgnoreCaseLiteralCharacterRegex(value[index]);

            return regexes.Length == 1 ? regexes[0] : _context.MkConcat(regexes);
        }

        return _context.MkToRe(_context.MkString(value));
    }

    private ReExpr CreateIgnoreCaseLiteralCharacterRegex(char value)
    {
        if (TryCreateCharacterRangesRegex(Regex.Escape(value.ToString()),
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, out var regex)) return regex;

        _isExact = false;
        return CreateAnyCharRegex();
    }

    private bool Peek(char value)
    {
        return _position < _pattern.Length && _pattern[_position] == value;
    }

    private static bool IsEscaped(string value, int index)
    {
        var slashCount = 0;
        for (var current = index - 1; current >= 0 && value[current] == '\\'; current--) slashCount++;

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

    private static bool TryGetEscapedLiteralCharacter(char escaped, bool inCharacterClass, out char value)
    {
        value = escaped switch
        {
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

    private static bool IsSupportedCaptureNameCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }

    private readonly struct RegexLookaheadAssertion(ReExpr regex, bool isPositive, bool isExact)
    {
        public ReExpr Regex { get; } = regex;

        public bool IsPositive { get; } = isPositive;

        public bool IsExact { get; } = isExact;
    }

    private readonly struct CharacterClassPart(
        ReExpr regex,
        char? exactCharacter,
        bool isApproximation,
        CharacterRange[]? ranges)
    {
        public ReExpr Regex { get; } = regex;
        public char? ExactCharacter { get; } = exactCharacter;
        public bool IsApproximation { get; } = isApproximation;
        public CharacterRange[]? Ranges { get; } = ranges;
    }

    private readonly struct RegexClassTranslation(ReExpr regex, bool isExact, CharacterRange[]? ranges)
    {
        public ReExpr Regex { get; } = regex;

        public bool IsExact { get; } = isExact;

        public CharacterRange[]? Ranges { get; } = ranges;
    }

    private readonly struct RegexOptionScope(bool ignorePatternWhitespace, bool singleline, bool ignoreCase)
    {
        public bool IgnorePatternWhitespace { get; } = ignorePatternWhitespace;

        public bool Singleline { get; } = singleline;

        public bool IgnoreCase { get; } = ignoreCase;
    }
}
