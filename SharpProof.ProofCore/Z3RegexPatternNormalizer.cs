namespace SharpProof.ProofCore.Smt;
internal sealed partial class Z3RegexCompiler {
    internal static bool TryNormalize(string pattern, RegexOptions options, out NormalizedRegexPattern normalized) {
        var multiline = (options & RegexOptions.Multiline) != 0;
        var effectivePatternLength = FindEffectivePatternLength(pattern, options);
        var effectivePattern = pattern.Substring(0, effectivePatternLength);
        var startAnchored = TryFindLeadingStartAnchor(pattern, options, !multiline, out var startAnchorStart, out var startAnchorLength);
        var strictEndAnchored = EndsWithUnescapedAnchor(effectivePattern, @"\z");
        var finalNewlineEndAnchored = !strictEndAnchored && EndsWithUnescapedAnchor(effectivePattern, @"\Z");
        var dollarEndAnchored = !strictEndAnchored &&
                                !finalNewlineEndAnchored &&
                                !multiline &&
                                effectivePattern.EndsWith("$", StringComparison.Ordinal) &&
                                !IsEscaped(effectivePattern, effectivePattern.Length - 1);
        if ((startAnchored ||
             strictEndAnchored ||
             finalNewlineEndAnchored ||
             dollarEndAnchored) &&
            ContainsTopLevelAlternation(effectivePattern, options)) {
            normalized = default;
            return false;
        }
        var bodyEndTrim = strictEndAnchored || finalNewlineEndAnchored ? 2 : dollarEndAnchored ? 1 : 0;
        var bodyEnd = effectivePatternLength - bodyEndTrim;
        if (bodyEnd < 0 || (startAnchored && startAnchorStart + startAnchorLength > bodyEnd)) {
            normalized = default;
            return false;
        }
        var bodyPattern = pattern.Substring(0, bodyEnd);
        if (startAnchored) bodyPattern = bodyPattern.Remove(startAnchorStart, startAnchorLength);
        normalized = new NormalizedRegexPattern(bodyPattern, startAnchored, strictEndAnchored, finalNewlineEndAnchored, dollarEndAnchored);
        return true;
    }
    private static int FindEffectivePatternLength(string pattern, RegexOptions options) {
        var currentScope = CreateInitialOptionScope(options);
        var scopes = new Stack<RegexOptionScope>();
        var lastSignificantEnd = 0;
        var position = 0;
        while (position < pattern.Length) {
            if (currentScope.IgnorePatternWhitespace) {
                if (char.IsWhiteSpace(pattern[position])) {
                    position++;
                    continue;
                }
                if (pattern[position] == '#') {
                    position++;
                    while (position < pattern.Length && pattern[position] is not ('\r' or '\n')) position++;
                    continue;
                }
            }
            if (pattern[position] == '\\') {
                position = Math.Min(position + 2, pattern.Length);
                lastSignificantEnd = position;
                continue;
            }
            if (pattern[position] == '[') {
                SkipCharacterClass(pattern, ref position);
                lastSignificantEnd = position;
                continue;
            }
            if (TrySkipInlineComment(pattern, ref position)) continue;
            if (pattern[position] == '(') {
                if (TryReadOptionGroup(pattern, position, currentScope, true,
                        out var nextScope, out var nextPosition, out var scoped)) {
                    if (scoped) scopes.Push(currentScope);
                    currentScope = nextScope;
                    position = nextPosition;
                    lastSignificantEnd = position;
                    continue;
                }
                scopes.Push(currentScope);
            }
            else if (pattern[position] == ')' && scopes.Count > 0) {
                currentScope = scopes.Pop();
            }
            position++;
            lastSignificantEnd = position;
        }
        return lastSignificantEnd;
    }
    private static bool ContainsTopLevelAlternation(
        string pattern,
        RegexOptions options) {
        var currentScope = CreateInitialOptionScope(options);
        var scopes = new Stack<RegexOptionScope>();
        var position = 0;
        while (position < pattern.Length) {
            if (currentScope.IgnorePatternWhitespace) {
                if (char.IsWhiteSpace(pattern[position])) {
                    position++;
                    continue;
                }
                if (pattern[position] == '#') {
                    position++;
                    while (position < pattern.Length &&
                           pattern[position] is not ('\r' or '\n'))
                        position++;
                    continue;
                }
            }
            if (pattern[position] == '\\') {
                position = Math.Min(position + 2, pattern.Length);
                continue;
            }
            if (pattern[position] == '[') {
                SkipCharacterClass(pattern, ref position);
                continue;
            }
            if (TrySkipInlineComment(pattern, ref position))
                continue;
            if (pattern[position] == '(') {
                if (TryReadOptionGroup(
                        pattern,
                        position,
                        currentScope,
                        true,
                        out var nextScope,
                        out var nextPosition,
                        out var scoped)) {
                    if (scoped)
                        scopes.Push(currentScope);
                    currentScope = nextScope;
                    position = nextPosition;
                    continue;
                }
                scopes.Push(currentScope);
            }
            else if (pattern[position] == ')' && scopes.Count > 0) {
                currentScope = scopes.Pop();
            }
            else if (pattern[position] == '|' && scopes.Count == 0) {
                return true;
            }
            position++;
        }
        return false;
    }
    private static void SkipCharacterClass(string pattern, ref int position) =>
        position = TryFindCharacterClassEnd(pattern, position, out var end) ? end : pattern.Length;
    internal static bool TryFindCharacterClassEnd(string pattern, int start, out int end) {
        end = -1;
        if (start < 0 || start >= pattern.Length || pattern[start] != '[') return false;
        var depth = 1;
        var firstCharacter = true;
        var escaped = false;
        var previousWasUnescapedHyphen = false;
        for (var index = start + 1; index < pattern.Length; index++) {
            var current = pattern[index];
            if (escaped) {
                escaped = false;
                firstCharacter = false;
                previousWasUnescapedHyphen = false;
                continue;
            }
            if (current == '\\') {
                escaped = true;
                previousWasUnescapedHyphen = false;
                continue;
            }
            if (firstCharacter && current == '^') {
                previousWasUnescapedHyphen = false;
                continue;
            }
            if (firstCharacter && current == ']') {
                firstCharacter = false;
                previousWasUnescapedHyphen = false;
                continue;
            }
            if (current == '[' && previousWasUnescapedHyphen) {
                depth++;
                firstCharacter = true;
                previousWasUnescapedHyphen = false;
                continue;
            }
            if (current == ']' && --depth == 0) {
                end = index + 1;
                return true;
            }
            firstCharacter = false;
            previousWasUnescapedHyphen = current == '-';
        }
        return false;
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
        out RegexOptionScope nextScope) {
        nextScope = currentScope;
        var sawOption = false;
        var sawDisableSeparator = false;
        while (position < pattern.Length && pattern[position] != terminator) {
            var current = pattern[position];
            if (current == '-') {
                if (sawDisableSeparator) return false;
                sawDisableSeparator = true;
                position++;
                continue;
            }
            if (current is not ('n' or 'x' or 's') && (current != 'i' || !canUseIgnoreCase)) return false;
            sawOption = true;
            var enabled = !sawDisableSeparator;
            nextScope = current switch {
                'x' => nextScope with { IgnorePatternWhitespace = enabled },
                's' => nextScope with { Singleline = enabled },
                'i' => nextScope with { IgnoreCase = enabled },
                _ => nextScope
            };
            position++;
        }
        if (!sawOption || position >= pattern.Length || pattern[position] != terminator) return false;
        position++;
        return true;
    }
    private static bool TryReadOptionGroup(
        string pattern,
        int start,
        RegexOptionScope currentScope,
        bool canUseIgnoreCase,
        out RegexOptionScope nextScope,
        out int nextPosition,
        out bool scoped) {
        nextScope = currentScope;
        nextPosition = start;
        scoped = false;
        if (start + 2 >= pattern.Length || pattern[start] != '(' || pattern[start + 1] != '?') return false;
        var position = start + 2;
        if (TryReadOptionsUntil(pattern, ref position, ')', currentScope, canUseIgnoreCase, out nextScope)) {
            nextPosition = position;
            return true;
        }
        position = start + 2;
        if (!TryReadOptionsUntil(pattern, ref position, ':', currentScope, canUseIgnoreCase, out nextScope))
            return false;
        nextPosition = position;
        scoped = true;
        return true;
    }
    internal static void SkipIgnoredTrivia(string pattern, ref int position, bool ignorePatternWhitespace) {
        while (position < pattern.Length) {
            if (TrySkipInlineComment(pattern, ref position)) continue;
            if (!ignorePatternWhitespace) return;
            var current = pattern[position];
            if (char.IsWhiteSpace(current)) {
                position++;
                continue;
            }
            if (current == '#') {
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
        out int anchorLength) {
        anchorStart = -1;
        anchorLength = 0;
        var index = 0;
        var optionScope = CreateInitialOptionScope(options);
        var canUseIgnoreCase = (options & RegexOptions.CultureInvariant) != 0;
        while (true) {
            SkipIgnoredTrivia(pattern, ref index, optionScope.IgnorePatternWhitespace);
            if (TryReadOptionGroup(pattern, index, optionScope, canUseIgnoreCase,
                    out var nextScope, out var nextIndex, out var scoped) && !scoped) {
                optionScope = nextScope;
                index = nextIndex;
                continue;
            }
            if (TrySkipEmptyGroup(pattern, ref index)) continue;
            break;
        }
        if (index >= pattern.Length) return false;
        if (pattern[index] == '^' && allowCaretAnchor) {
            anchorStart = index;
            anchorLength = 1;
            return true;
        }
        if (index + 1 < pattern.Length && pattern[index] == '\\' && pattern[index + 1] is 'A' or 'G') {
            anchorStart = index;
            anchorLength = 2;
            return true;
        }
        return false;
    }
    private static bool TrySkipEmptyGroup(string pattern, ref int position) {
        var length = position + 1 < pattern.Length &&
                     pattern[position] == '(' &&
                     pattern[position + 1] == ')' ? 2 :
            position + 3 < pattern.Length &&
            pattern[position] == '(' &&
            pattern[position + 1] == '?' &&
            pattern[position + 2] is ':' or '>' &&
            pattern[position + 3] == ')' ? 4 : 0;
        position += length;
        return length != 0;
    }
    private static bool TrySkipInlineComment(string pattern, ref int position) {
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
    private static bool IsEscaped(string value, int index) {
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
internal readonly record struct RegexOptionScope(bool IgnorePatternWhitespace, bool Singleline, bool IgnoreCase);
