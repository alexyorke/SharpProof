using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Smt;

internal static class SmtFormulaVersionRewriter
{
    internal static SmtFormula RewriteSymbolVersions(
        SmtFormula formula,
        ImmutableDictionary<ISymbol, int> sourceVersions,
        ImmutableDictionary<ISymbol, int> targetVersions)
    {
        var rewrites = CreateRewrites(sourceVersions, targetVersions);
        return rewrites.Length == 0
            ? formula
            : RewriteSymbolVersions(formula, rewrites);
    }

    internal static ImmutableArray<SmtFormula> RewriteSymbolVersions(
        ImmutableArray<SmtFormula> formulas,
        ImmutableDictionary<ISymbol, int> sourceVersions,
        ImmutableDictionary<ISymbol, int> targetVersions)
    {
        if (formulas.IsDefaultOrEmpty) return formulas;

        var rewrites = CreateRewrites(sourceVersions, targetVersions);
        if (rewrites.Length == 0) return formulas;

        var builder = ImmutableArray.CreateBuilder<SmtFormula>(formulas.Length);
        foreach (var formula in formulas) builder.Add(RewriteSymbolVersions(formula, rewrites));

        return builder.ToImmutable();
    }

    private static ImmutableArray<SmtVersionRewrite> CreateRewrites(
        ImmutableDictionary<ISymbol, int> sourceVersions,
        ImmutableDictionary<ISymbol, int> targetVersions)
    {
        var symbols = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
        symbols.UnionWith(sourceVersions.Keys);
        symbols.UnionWith(targetVersions.Keys);

        var builder = ImmutableArray.CreateBuilder<SmtVersionRewrite>();
        foreach (var symbol in symbols)
        {
            var originalDefinition = symbol.OriginalDefinition;
            var sourceVersion = sourceVersions.TryGetValue(originalDefinition, out var currentVersion)
                ? currentVersion
                : 0;
            var targetVersion = targetVersions.TryGetValue(originalDefinition, out var mergedVersion)
                ? mergedVersion
                : 0;
            if (sourceVersion == targetVersion) continue;

            builder.Add(new SmtVersionRewrite(
                SymbolicFactFactory.GetSmtVariableName(originalDefinition),
                sourceVersion,
                targetVersion));
        }

        return builder.ToImmutable();
    }

    private static SmtFormula RewriteSymbolVersions(
        SmtFormula formula,
        ImmutableArray<SmtVersionRewrite> rewrites)
    {
        return SmtFormulaTraversal.RewriteBottomUp(
            formula,
            candidate =>
            {
                if (candidate is not SmtVariable variable) return candidate;

                var rewrittenName = RewriteVariableName(variable.Name, rewrites);
                return string.Equals(rewrittenName, variable.Name, StringComparison.Ordinal)
                    ? candidate
                    : new SmtVariable(rewrittenName, variable.Kind);
            },
            out _);
    }

    private static string RewriteVariableName(
        string name,
        ImmutableArray<SmtVersionRewrite> rewrites)
    {
        var rewritten = name;
        foreach (var rewrite in rewrites) rewritten = RewriteVariableName(rewritten, rewrite);

        return rewritten;
    }

    private static string RewriteVariableName(string name, SmtVersionRewrite rewrite)
    {
        var fromBase = CreateVersionedBaseName(rewrite.Prefix, rewrite.FromVersion);
        var toBase = CreateVersionedBaseName(rewrite.Prefix, rewrite.ToVersion);
        if (string.Equals(fromBase, toBase, StringComparison.Ordinal)) return name;

        var rewritten = name;
        var searchIndex = 0;
        while (searchIndex < rewritten.Length)
        {
            var matchIndex = rewritten.IndexOf(fromBase, searchIndex, StringComparison.Ordinal);
            if (matchIndex < 0) break;

            var endIndex = matchIndex + fromBase.Length;
            if (IsVariableNameStartBoundary(rewritten, matchIndex) &&
                IsVariableNameEndBoundary(rewritten, endIndex))
            {
                rewritten = rewritten.Substring(0, matchIndex) + toBase + rewritten.Substring(endIndex);
                searchIndex = matchIndex + toBase.Length;
                continue;
            }

            searchIndex = matchIndex + 1;
        }

        return rewritten;
    }

    private static string CreateVersionedBaseName(string prefix, int version)
    {
        return version > 0
            ? prefix + "@v" + version.ToString(CultureInfo.InvariantCulture)
            : prefix;
    }

    private static bool IsVariableNameStartBoundary(string name, int index)
    {
        return index <= 0 || IsVariableNameDelimiter(name[index - 1]);
    }

    private static bool IsVariableNameEndBoundary(string name, int index)
    {
        return index >= name.Length || IsVariableNameDelimiter(name[index]);
    }

    private static bool IsVariableNameDelimiter(char value)
    {
        return value is '.' or '[' or ']';
    }

    private readonly struct SmtVersionRewrite
    {
        public SmtVersionRewrite(string prefix, int fromVersion, int toVersion)
        {
            Prefix = prefix;
            FromVersion = fromVersion;
            ToVersion = toVersion;
        }

        public string Prefix { get; }

        public int FromVersion { get; }

        public int ToVersion { get; }
    }
}
