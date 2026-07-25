namespace SharpProof.Symbolic.Ir;
internal static class SymbolicIrVersionRewriter {
    internal static SymbolicCondition RewriteToCurrentVersions(
        SymbolicCondition condition,
        ImmutableDictionary<string, int> symbolVersions) {
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        return symbolVersions.IsEmpty ? condition : SymbolicAlgebra.Rewrite(condition, Rewrite);
        SymbolicTerm? Rewrite(SymbolicTerm term) => RewriteTerm(term, symbolVersions);
    }
    internal static SymbolicFact RewriteToCurrentVersions(
        SymbolicFact fact,
        ImmutableDictionary<string, int> symbolVersions) {
        if (fact == null) throw new ArgumentNullException(nameof(fact));
        return symbolVersions.IsEmpty ? fact : SymbolicAlgebra.Rewrite(fact, Rewrite);
        SymbolicTerm? Rewrite(SymbolicTerm term) => RewriteTerm(term, symbolVersions);
    }
    internal static SymbolicTerm RewriteToCurrentVersions(
        SymbolicTerm term,
        ImmutableDictionary<string, int> symbolVersions) =>
        symbolVersions.IsEmpty
            ? term
            : SymbolicAlgebra.Rewrite(term, candidate => RewriteTerm(candidate, symbolVersions));
    private static SymbolicTerm? RewriteTerm(SymbolicTerm term, ImmutableDictionary<string, int> versions) {
        var name = term switch {
            SymbolicVariableTerm variable => variable.Name,
            SymbolicNullableHasValueTerm nullable => nullable.NullableName,
            SymbolicNullableValueTerm nullable => nullable.NullableName,
            _ => null
        };
        if (name == null || !TryGetCurrentName(name, versions, out var current)) return null;
        return term switch {
            SymbolicVariableTerm variable => variable with { Name = current },
            SymbolicNullableHasValueTerm nullable => nullable with { NullableName = current },
            SymbolicNullableValueTerm nullable => nullable with { NullableName = current },
            _ => null
        };
    }
    private static bool TryGetCurrentName(
        string name,
        ImmutableDictionary<string, int> versions,
        out string current) {
        var marker = name.LastIndexOf("@v", StringComparison.Ordinal);
        var baseName = name;
        var version = 0;
        if (marker >= 0 && marker + 2 < name.Length &&
            int.TryParse(name.Substring(marker + 2), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) {
            baseName = name.Substring(0, marker);
            version = parsed;
        }
        if (!versions.TryGetValue(baseName, out var target) || target == version) {
            current = name;
            return false;
        }
        current = target > 0 ? baseName + "@v" + target.ToString(CultureInfo.InvariantCulture) : baseName;
        return true;
    }
}
