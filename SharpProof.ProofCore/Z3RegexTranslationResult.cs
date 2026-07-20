namespace SharpProof.ProofCore.Smt;

internal readonly record struct Z3RegexTranslationResult(
    bool Success,
    ReExpr? Regex,
    bool IsExact,
    RegexTranslationFallback Fallback) {
    internal static Z3RegexTranslationResult Succeeded(ReExpr regex, bool isExact) =>
        new(true, regex, isExact, RegexTranslationFallback.None);

    internal static Z3RegexTranslationResult Failed(RegexTranslationFallback fallback) =>
        new(false, null, false, fallback);
}
