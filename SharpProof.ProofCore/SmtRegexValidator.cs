namespace SharpProof.ProofCore.Smt;
internal sealed partial class Z3RegexCompiler {
    internal const int MaxConcreteCacheEntries = 256;
    internal static bool TryValidateConcrete(
        string input, string pattern, RegexOptions options, out bool isMatch) {
        try {
            isMatch = Regex.IsMatch(input, pattern, options, ValidationTimeout);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or RegexMatchTimeoutException) {
            isMatch = false;
            return false;
        }
    }
}
internal sealed class SmtRegexValidator {
    internal const int MaxCacheEntries = Z3RegexCompiler.MaxConcreteCacheEntries;
    private readonly Dictionary<(string Input, string Pattern, RegexOptions Options), (bool Supported, bool IsMatch)> _cache = [];
    internal bool TryValidate(string input, string pattern, RegexOptions options, out bool isMatch) {
        var key = (input, pattern, options);
        if (_cache.TryGetValue(key, out var cached)) {
            isMatch = cached.IsMatch;
            return cached.Supported;
        }
        var supported = Z3RegexCompiler.TryValidateConcrete(input, pattern, options, out isMatch);
        if (_cache.Count >= MaxCacheEntries) _cache.Clear();
        _cache[key] = (supported, isMatch);
        return supported;
    }
}
