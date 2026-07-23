namespace SharpProof.Symbolic.Ir;
internal static class SymbolicIrSubstitution {
    internal static SymbolicTerm ReplaceTerm(SymbolicTerm term, SymbolicTerm source, SymbolicTerm replacement)
        => new TermSubstitutionRewriter(source, replacement).Rewrite(term);
    internal static SymbolicFact ReplaceTerm(SymbolicFact fact, SymbolicTerm source, SymbolicTerm replacement)
        => new TermSubstitutionRewriter(source, replacement).Rewrite(fact);
    internal static SymbolicCondition ReplaceTerm(SymbolicCondition condition, SymbolicTerm source, SymbolicTerm replacement)
        => new TermSubstitutionRewriter(source, replacement).Rewrite(condition);
    internal static SymbolicCondition ReplaceVariableNames(
        SymbolicCondition condition,
        IReadOnlyDictionary<string, SymbolicTerm> replacements) =>
        replacements.Count == 0 ? condition : new VariableNameSubstitutionRewriter(replacements).Rewrite(condition);
    sealed class TermSubstitutionRewriter : SymbolicIrRewriter {
        private readonly SymbolicTerm _replacement;
        private readonly string _sourceKey;
        internal TermSubstitutionRewriter(SymbolicTerm source, SymbolicTerm replacement) {
            _sourceKey = SymbolicState.CreateProofTermKey(source);
            _replacement = replacement;
        }
        protected override bool TryRewriteTerm(SymbolicTerm term, out SymbolicTerm rewritten) {
            if (string.Equals(SymbolicState.CreateProofTermKey(term), _sourceKey, StringComparison.Ordinal)) {
                rewritten = _replacement;
                return true;
            }
            rewritten = null!;
            return false;
        }
    }
    sealed class VariableNameSubstitutionRewriter(IReadOnlyDictionary<string, SymbolicTerm> replacements)
        : SymbolicIrRewriter {
        protected override bool TryRewriteTerm(SymbolicTerm term, out SymbolicTerm rewritten) {
            if (term is SymbolicVariableTerm variable &&
                replacements.TryGetValue(variable.Name, out var replacement)) {
                rewritten = replacement;
                return true;
            }
            rewritten = null!;
            return false;
        }
    }
}
