namespace SharpProof.Symbolic.Ir;
internal static class SymbolicIrSubstitution {
    internal static SymbolicTerm ReplaceTerm(SymbolicTerm term, SymbolicTerm source, SymbolicTerm replacement) =>
        SymbolicAlgebra.Rewrite(term, Replacer(source, replacement));
    internal static SymbolicFact ReplaceTerm(SymbolicFact fact, SymbolicTerm source, SymbolicTerm replacement) =>
        SymbolicAlgebra.Rewrite(fact, Replacer(source, replacement));
    internal static SymbolicCondition ReplaceTerm(
        SymbolicCondition condition,
        SymbolicTerm source,
        SymbolicTerm replacement) =>
        SymbolicAlgebra.Rewrite(condition, Replacer(source, replacement));
    internal static SymbolicCondition ReplaceVariableNames(
        SymbolicCondition condition,
        IReadOnlyDictionary<string, SymbolicTerm> replacements) =>
        replacements.Count == 0
            ? condition
            : SymbolicAlgebra.Rewrite(condition, term =>
                term is SymbolicVariableTerm variable &&
                replacements.TryGetValue(variable.Name, out var replacement)
                    ? replacement
                    : null);
    private static Func<SymbolicTerm, SymbolicTerm?> Replacer(SymbolicTerm source, SymbolicTerm replacement) {
        var key = SymbolicState.CreateProofTermIndexKey(source);
        return candidate => SymbolicState.CreateProofTermIndexKey(candidate).Equals(key) ? replacement : null;
    }
}
