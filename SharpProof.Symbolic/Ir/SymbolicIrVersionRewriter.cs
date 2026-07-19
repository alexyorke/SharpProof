using System.Globalization;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicIrVersionRewriter
{
    internal static SymbolicCondition RewriteToCurrentVersions(
        SymbolicCondition condition,
        ImmutableDictionary<string, int> symbolVersions)
    {
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        return symbolVersions.IsEmpty ? condition : new CurrentVersionRewriter(symbolVersions).Rewrite(condition);
    }

    internal static SymbolicFact RewriteToCurrentVersions(
        SymbolicFact fact,
        ImmutableDictionary<string, int> symbolVersions)
    {
        if (fact == null) throw new ArgumentNullException(nameof(fact));
        return symbolVersions.IsEmpty ? fact : new CurrentVersionRewriter(symbolVersions).Rewrite(fact);
    }

    private sealed class CurrentVersionRewriter : SymbolicIrRewriter
    {
        private readonly ImmutableDictionary<string, int> _symbolVersions;

        internal CurrentVersionRewriter(ImmutableDictionary<string, int> symbolVersions)
        {
            _symbolVersions = symbolVersions;
        }

        protected override bool TryRewriteTerm(SymbolicTerm term, out SymbolicTerm rewritten)
        {
            switch (term)
            {
                case SymbolicVariableTerm variable:
                    return TryRewriteVariableLike(
                        variable,
                        variable.Name,
                        static (source, name) => new SymbolicVariableTerm(name, source.Kind),
                        out rewritten);
                case SymbolicNullableHasValueTerm nullableHasValue:
                    return TryRewriteVariableLike(
                        nullableHasValue,
                        nullableHasValue.NullableName,
                        static (_, name) => new SymbolicNullableHasValueTerm(name),
                        out rewritten);
                case SymbolicNullableValueTerm nullableValue:
                    return TryRewriteVariableLike(
                        nullableValue,
                        nullableValue.NullableName,
                        static (source, name) => new SymbolicNullableValueTerm(name, source.Kind),
                        out rewritten);
                default:
                    rewritten = null!;
                    return false;
            }
        }

        private bool TryRewriteVariableLike<TTerm>(
            TTerm term,
            string name,
            Func<TTerm, string, SymbolicTerm> factory,
            out SymbolicTerm rewritten)
            where TTerm : SymbolicTerm
        {
            var rewrittenName = RewriteVariableLikeName(name);
            if (string.Equals(rewrittenName, name, StringComparison.Ordinal))
            {
                rewritten = term;
                return true;
            }

            rewritten = factory(term, rewrittenName);
            return true;
        }

        private string RewriteVariableLikeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var (baseName, currentVersion) = SplitVersionedName(name);
            if (!_symbolVersions.TryGetValue(baseName, out var targetVersion) ||
                currentVersion == targetVersion)
                return name;

            return targetVersion > 0
                ? baseName + "@v" + targetVersion.ToString(CultureInfo.InvariantCulture)
                : baseName;
        }
    }

    private static (string BaseName, int Version) SplitVersionedName(string name)
    {
        var markerIndex = name.LastIndexOf("@v", StringComparison.Ordinal);
        if (markerIndex < 0 ||
            markerIndex + 2 >= name.Length ||
            !int.TryParse(
                name.Substring(markerIndex + 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var version))
            return (name, 0);

        return (name.Substring(0, markerIndex), version);
    }
}
