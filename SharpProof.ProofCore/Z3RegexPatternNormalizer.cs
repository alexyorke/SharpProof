namespace SharpProof.ProofCore.Smt;

internal static class Z3RegexPatternNormalizer
{
    internal static bool TryNormalize(
        string pattern,
        RegexOptions options,
        out NormalizedRegexPattern normalized)
    {
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
        if (bodyEnd < 0 || (startAnchored && startAnchorStart + startAnchorLength > bodyEnd))
        {
            normalized = default;
            return false;
        }

        var bodyPattern = pattern.Substring(0, bodyEnd);
        if (startAnchored) bodyPattern = bodyPattern.Remove(startAnchorStart, startAnchorLength);
        normalized = new NormalizedRegexPattern(
            bodyPattern,
            startAnchored,
            strictEndAnchored,
            finalNewlineEndAnchored,
            dollarEndAnchored);
        return true;
    }

    internal static RegexOptionScope CreateInitialOptionScope(RegexOptions options) => new(
        (options & RegexOptions.IgnorePatternWhitespace) != 0,
        (options & RegexOptions.Singleline) != 0,
        (options & RegexOptions.IgnoreCase) != 0);

    internal static bool TryReadOptionsUntil(
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

    internal static void SkipIgnoredTrivia(string pattern, ref int position, bool ignorePatternWhitespace)
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
                while (position < pattern.Length && pattern[position] != '\r' && pattern[position] != '\n')
                    position++;
                continue;
            }

            return;
        }
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
            SkipIgnoredTrivia(pattern, ref index, optionScope.IgnorePatternWhitespace);
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

        if (index + 1 < pattern.Length && pattern[index] == '\\' && pattern[index + 1] is 'A' or 'G')
        {
            anchorStart = index;
            anchorLength = 2;
            return true;
        }

        return false;
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
        if (start + 2 >= pattern.Length || pattern[start] != '(' || pattern[start + 1] != '?') return false;

        var index = start + 2;
        if (!TryReadOptionsUntil(pattern, ref index, ')', currentScope, canUseIgnoreCase, out nextScope)) return false;

        nextIndex = index;
        return true;
    }

    private static bool TrySkipInlineComment(string pattern, ref int position)
    {
        if (position + 2 >= pattern.Length || pattern[position] != '(' || pattern[position + 1] != '?' ||
            pattern[position + 2] != '#')
            return false;

        var end = position + 3;
        while (end < pattern.Length && pattern[end] != ')') end++;
        if (end >= pattern.Length) return false;

        position = end + 1;
        return true;
    }

    private static bool EndsWithUnescapedAnchor(string value, string anchor) =>
        value.EndsWith(anchor, StringComparison.Ordinal) && !IsEscaped(value, value.Length - anchor.Length);

    private static bool IsEscaped(string value, int index)
    {
        var slashCount = 0;
        for (var current = index - 1; current >= 0 && value[current] == '\\'; current--) slashCount++;
        return slashCount % 2 != 0;
    }
}

internal readonly record struct NormalizedRegexPattern(
    string Body,
    bool StartAnchored,
    bool StrictEndAnchored,
    bool FinalNewlineEndAnchored,
    bool DollarEndAnchored);

internal readonly record struct RegexOptionScope(
    bool IgnorePatternWhitespace,
    bool Singleline,
    bool IgnoreCase);
