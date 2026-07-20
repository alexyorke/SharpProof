namespace SharpProof.ProofCore.Smt;

internal sealed class SmtRegexValidator {
    internal const int MaxCacheEntries = 256;
    private static readonly TimeSpan ConcreteValidationTimeout = TimeSpan.FromMilliseconds(50);
    private readonly Dictionary<RegexValidationKey, RegexValidationResult> _cache = new();

    internal int CacheCount => _cache.Count;

    internal bool TryValidate(
        string input,
        string pattern,
        RegexOptions options,
        out bool isMatch) {
        var key = new RegexValidationKey(input, pattern, options);
        if (_cache.TryGetValue(key, out var cached)) {
            isMatch = cached.IsMatch;
            return cached.IsSupported;
        }

        try {
            isMatch = Regex.IsMatch(input, pattern, options, ConcreteValidationTimeout);
            Cache(key, new RegexValidationResult(true, isMatch));
            return true;
        }
        catch (ArgumentException) {
            isMatch = false;
            Cache(key, new RegexValidationResult(false, isMatch));
            return false;
        }
        catch (RegexMatchTimeoutException) {
            isMatch = false;
            Cache(key, new RegexValidationResult(false, isMatch));
            return false;
        }
    }

    private void Cache(RegexValidationKey key, RegexValidationResult result) {
        if (_cache.Count >= MaxCacheEntries) _cache.Clear();

        _cache[key] = result;
    }

    private readonly record struct RegexValidationKey(string Input, string Pattern, RegexOptions Options);

    private readonly record struct RegexValidationResult(bool IsSupported, bool IsMatch);
}
