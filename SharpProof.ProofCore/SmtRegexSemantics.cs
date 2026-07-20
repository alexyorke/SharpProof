namespace SharpProof.ProofCore.Smt;

internal static class SmtRegexSemantics
{
    private const RegexOptions PreservedOptions =
        RegexOptions.ExplicitCapture |
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Singleline |
        RegexOptions.Multiline |
        RegexOptions.IgnorePatternWhitespace |
        RegexOptions.IgnoreCase;

    internal static bool CanPreserveOptions(RegexOptions options) =>
        (options & ~PreservedOptions) == 0;

    internal static bool CanEncodeOptions(RegexOptions options)
    {
        return CanPreserveOptions(options) &&
               ((options & RegexOptions.IgnoreCase) == 0 ||
                (options & RegexOptions.CultureInvariant) != 0);
    }
}
