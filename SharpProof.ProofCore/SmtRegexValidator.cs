namespace SharpProof.ProofCore.Smt;

internal sealed class SmtRegexValidator
{
    internal const int MaxCacheEntries = 256;
    private static readonly TimeSpan ConcreteValidationTimeout = TimeSpan.FromMilliseconds(50);
    private readonly Dictionary<RegexValidationKey, RegexValidationResult> _cache = new();

    internal int CacheCount => _cache.Count;

    internal bool TryValidate(
        string input,
        string pattern,
        RegexOptions options,
        out bool isMatch)
    {
        var key = new RegexValidationKey(input, pattern, options);
        if (_cache.TryGetValue(key, out var cached))
        {
            isMatch = cached.IsMatch;
            return cached.IsSupported;
        }

        try
        {
            isMatch = Regex.IsMatch(input, pattern, options, ConcreteValidationTimeout);
            Cache(key, new RegexValidationResult(true, isMatch));
            return true;
        }
        catch (ArgumentException)
        {
            isMatch = false;
            Cache(key, new RegexValidationResult(false, isMatch));
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            isMatch = false;
            Cache(key, new RegexValidationResult(false, isMatch));
            return false;
        }
    }

    private void Cache(RegexValidationKey key, RegexValidationResult result)
    {
        if (_cache.Count >= MaxCacheEntries) _cache.Clear();

        _cache[key] = result;
    }

    private readonly struct RegexValidationKey : IEquatable<RegexValidationKey>
    {
        private readonly string _input;
        private readonly string _pattern;
        private readonly RegexOptions _options;

        internal RegexValidationKey(string input, string pattern, RegexOptions options)
        {
            _input = input;
            _pattern = pattern;
            _options = options;
        }

        public bool Equals(RegexValidationKey other)
        {
            return string.Equals(_input, other._input, StringComparison.Ordinal) &&
                   string.Equals(_pattern, other._pattern, StringComparison.Ordinal) &&
                   _options == other._options;
        }

        public override bool Equals(object? obj)
        {
            return obj is RegexValidationKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(_input) * 397) ^
                       (StringComparer.Ordinal.GetHashCode(_pattern) * 397) ^
                       (int)_options;
            }
        }
    }

    private readonly struct RegexValidationResult(bool isSupported, bool isMatch)
    {
        internal bool IsSupported { get; } = isSupported;

        internal bool IsMatch { get; } = isMatch;
    }
}
