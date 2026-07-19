namespace SharpProof.ProofCore.Smt;

internal static class Z3RegexTranslationValidator
{
    private const int MaxPatternLength = 256;
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromMilliseconds(50);

    internal static RegexTranslationFallback Validate(string pattern, RegexOptions options)
    {
        if (pattern.Length > MaxPatternLength) return RegexTranslationFallback.PatternTooLong;

        try
        {
            _ = new Regex(pattern, options, ValidationTimeout);
            return RegexTranslationFallback.None;
        }
        catch (ArgumentException)
        {
            return RegexTranslationFallback.InvalidPattern;
        }
    }
}

internal enum RegexTranslationFallback
{
    None,
    PatternTooLong,
    InvalidPattern,
    NormalizationFailed,
    UnsupportedFragment
}
