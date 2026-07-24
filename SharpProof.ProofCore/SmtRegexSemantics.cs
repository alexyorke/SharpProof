namespace SharpProof.ProofCore.Smt;
internal sealed partial class Z3RegexCompiler {
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
    internal static bool CanEncodeOptions(RegexOptions options) => CanPreserveOptions(options) &&
               ((options & RegexOptions.IgnoreCase) == 0 ||
                (options & RegexOptions.CultureInvariant) != 0);
}
internal static class SmtRegexSemantics {
    internal static bool CanPreserveOptions(RegexOptions options) =>
        Z3RegexCompiler.CanPreserveOptions(options);
    internal static bool CanEncodeOptions(RegexOptions options) =>
        Z3RegexCompiler.CanEncodeOptions(options);
}
