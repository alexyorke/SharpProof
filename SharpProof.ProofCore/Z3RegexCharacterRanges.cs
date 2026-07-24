using SharpProof.ProofCore.Collections;
namespace SharpProof.ProofCore.Smt;
internal sealed partial class Z3RegexCompiler {
    private const int CacheLimit = 1024;
    private static readonly BoundedConcurrentCache<(string Pattern, RegexOptions Options), CharacterRange[]> Cache =
        new(CacheLimit);
    internal static CharacterRange[] Word => GetOrEmpty(@"\w");
    internal static bool TryGetShorthand(char escaped, out CharacterRange[] ranges) {
        if (escaped is not ('d' or 'D' or 's' or 'S' or 'w' or 'W')) {
            ranges = [];
            return false;
        }
        var baseRanges = GetOrEmpty("\\" + char.ToLowerInvariant(escaped));
        ranges = escaped is 'D' or 'S' or 'W' ? Complement(baseRanges) : baseRanges;
        return true;
    }
    internal static bool TryGet(string atomPattern, out CharacterRange[] ranges) =>
        TryGet(atomPattern, RegexOptions.None, out ranges);
    internal static bool TryGet(string atomPattern, RegexOptions options, out CharacterRange[] ranges) =>
        TryCreate(() => Cache.GetOrAdd((atomPattern, options), Create), out ranges) &&
               ranges.Length is > 0 and <= MaxCharacterClassRangeCount;
    internal static CharacterRange[] Merge(IEnumerable<CharacterRange> ranges) {
        var ordered = ranges
            .OrderBy(static range => range.Start)
            .ThenBy(static range => range.End)
            .ToArray();
        if (ordered.Length == 0) return [];
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
        return [.. merged];
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
        return [.. complement];
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
        return [.. ranges];
    }
    private static CharacterRange[] GetOrEmpty(string pattern) =>
        TryCreate(() => Cache.GetOrAdd((pattern, RegexOptions.None), Create), out var ranges) ? ranges : [];
    private static bool TryCreate(Func<CharacterRange[]> create, out CharacterRange[] ranges) {
        try {
            ranges = create();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                               RegexMatchTimeoutException) {
            ranges = [];
            return false;
        }
    }
}
internal readonly record struct CharacterRange(char Start, char End);
