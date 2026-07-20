using SharpProof.ProofCore.Collections;

namespace SharpProof.ProofCore.Smt;

internal static class Z3RegexCharacterRanges {
    private const int CacheLimit = 1024;
    private const int MaxRangeCount = 512;
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly BoundedConcurrentCache<(string Pattern, RegexOptions Options), CharacterRange[]> Cache =
        new(CacheLimit);

    private static readonly Lazy<CharacterRange[]> DecimalDigits =
        new(() => CreateOrEmpty((@"\d", RegexOptions.None)));

    private static readonly Lazy<CharacterRange[]> Whitespace =
        new(() => CreateOrEmpty((@"\s", RegexOptions.None)));

    private static readonly Lazy<CharacterRange[]> Words =
        new(() => CreateOrEmpty((@"\w", RegexOptions.None)));

    internal static CharacterRange[] Word => Words.Value;

    internal static bool TryGetShorthand(char escaped, out CharacterRange[] ranges) {
        var baseRanges = escaped switch {
            'd' or 'D' => DecimalDigits.Value,
            's' or 'S' => Whitespace.Value,
            'w' or 'W' => Words.Value,
            _ => null,
        };
        if (baseRanges is null) {
            ranges = Array.Empty<CharacterRange>();
            return false;
        }

        ranges = escaped is 'D' or 'S' or 'W' ? Complement(baseRanges) : baseRanges;
        return true;
    }

    internal static bool TryGet(string atomPattern, out CharacterRange[] ranges) =>
        TryGet(atomPattern, RegexOptions.None, out ranges);

    internal static bool TryGet(string atomPattern, RegexOptions options, out CharacterRange[] ranges) {
        ranges = Array.Empty<CharacterRange>();
        try {
            ranges = Cache.GetOrAdd((atomPattern, options), Create);
            return ranges.Length is > 0 and <= MaxRangeCount;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                               RegexMatchTimeoutException) {
            return false;
        }
    }

    internal static CharacterRange[] Merge(IEnumerable<CharacterRange> ranges) {
        var ordered = ranges
            .OrderBy(static range => range.Start)
            .ThenBy(static range => range.End)
            .ToArray();
        if (ordered.Length == 0) return Array.Empty<CharacterRange>();

        var merged = new List<CharacterRange>();
        var currentStart = ordered[0].Start;
        var currentEnd = ordered[0].End;
        for (var index = 1; index < ordered.Length; index++) {
            var range = ordered[index];
            if (range.Start <= currentEnd ||
                (currentEnd != char.MaxValue && range.Start == currentEnd + 1)) {
                if (range.End > currentEnd) currentEnd = range.End;
                continue;
            }

            merged.Add(new CharacterRange(currentStart, currentEnd));
            currentStart = range.Start;
            currentEnd = range.End;
        }

        merged.Add(new CharacterRange(currentStart, currentEnd));
        return merged.ToArray();
    }

    internal static CharacterRange[] Complement(IEnumerable<CharacterRange> ranges) {
        var merged = Merge(ranges);
        var complement = new List<CharacterRange>();
        var nextStart = 0;
        foreach (var range in merged) {
            if (nextStart < range.Start)
                complement.Add(new CharacterRange((char)nextStart, (char)(range.Start - 1)));

            if (range.End == char.MaxValue) {
                nextStart = char.MaxValue + 1;
                break;
            }

            nextStart = range.End + 1;
        }

        if (nextStart <= char.MaxValue) complement.Add(new CharacterRange((char)nextStart, char.MaxValue));
        return complement.ToArray();
    }

    private static CharacterRange[] Create((string Pattern, RegexOptions Options) key) {
        var ranges = new List<CharacterRange>();
        char? rangeStart = null;
        var previous = '\0';
        var regex = new Regex(@"\A(?:" + key.Pattern + @")\z", key.Options, ValidationTimeout);
        for (var codePoint = 0; codePoint <= char.MaxValue; codePoint++) {
            var current = (char)codePoint;
            if (regex.IsMatch(current.ToString())) {
                rangeStart ??= current;
                previous = current;
                continue;
            }

            if (rangeStart is { } start) {
                ranges.Add(new CharacterRange(start, previous));
                rangeStart = null;
            }
        }

        if (rangeStart is { } finalStart) ranges.Add(new CharacterRange(finalStart, previous));
        return ranges.ToArray();
    }

    private static CharacterRange[] CreateOrEmpty((string Pattern, RegexOptions Options) key) {
        try {
            return Create(key);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                               RegexMatchTimeoutException) {
            return Array.Empty<CharacterRange>();
        }
    }
}

internal readonly record struct CharacterRange(char Start, char End);
