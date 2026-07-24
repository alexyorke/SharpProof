namespace SharpProof.ProofCore.Smt;
internal sealed partial class Z3RegexCompiler {
    internal const int MaxPatternLength = 256;
    internal static readonly TimeSpan ValidationTimeout = TimeSpan.FromMilliseconds(50);
    internal static RegexTranslationFallback Validate(string pattern, RegexOptions options) {
        if (pattern.Length > MaxPatternLength) return RegexTranslationFallback.PatternTooLong;
        try {
            _ = new Regex(pattern, options, ValidationTimeout);
            return RegexTranslationFallback.None;
        }
        catch (ArgumentException) {
            return RegexTranslationFallback.InvalidPattern;
        }
    }
}
internal static class Z3RegexTranslationValidator {
    internal static RegexTranslationFallback Validate(string pattern, RegexOptions options) =>
        Z3RegexCompiler.Validate(pattern, options);
}
internal enum RegexTranslationFallback {
    None,
    PatternTooLong,
    InvalidPattern,
}
