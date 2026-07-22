namespace SharpProof.ProofCore.Smt;
internal readonly record struct Z3RegexTranslationResult(bool Success, ReExpr? Regex, bool IsExact) {
    internal static Z3RegexTranslationResult Succeeded(ReExpr regex, bool isExact) =>
        new(true, regex, isExact);
    internal static Z3RegexTranslationResult Failed() =>
        new(false, null, false);
}
