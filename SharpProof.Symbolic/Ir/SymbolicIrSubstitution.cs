namespace SharpProof.Symbolic.Ir;

internal static class SymbolicIrSubstitution {
    internal static SymbolicTerm ReplaceTerm(
        SymbolicTerm term,
        SymbolicTerm source,
        SymbolicTerm replacement) => new TermSubstitutionRewriter(source, replacement).Rewrite(term);

    internal static SymbolicFact ReplaceTerm(
        SymbolicFact fact,
        SymbolicTerm source,
        SymbolicTerm replacement) => new TermSubstitutionRewriter(source, replacement).Rewrite(fact);

    internal static SymbolicCondition ReplaceTerm(
        SymbolicCondition condition,
        SymbolicTerm source,
        SymbolicTerm replacement) => new TermSubstitutionRewriter(source, replacement).Rewrite(condition);

    private sealed class TermSubstitutionRewriter : SymbolicIrRewriter {
        private readonly SymbolicTerm _replacement;
        private readonly string _sourceKey;

        internal TermSubstitutionRewriter(SymbolicTerm source, SymbolicTerm replacement) {
            _sourceKey = SymbolicState.CreateProofTermKey(source);
            _replacement = replacement;
        }

        protected override bool TryRewriteTerm(SymbolicTerm term, out SymbolicTerm rewritten) {
            if (string.Equals(
                    SymbolicState.CreateProofTermKey(term),
                    _sourceKey,
                    StringComparison.Ordinal)) {
                rewritten = _replacement;
                return true;
            }

            rewritten = null!;
            return false;
        }
    }
}
