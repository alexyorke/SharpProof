internal static class EffectSummaryGeneratedPurityRules {
    private const string ResourceName = "SharpProof.EffectSummary.GeneratedPurityRules.json";

    private static readonly GeneratedRuleRegistry Registry = LoadRegistry();
    private static readonly GeneratedImpureRule[] GeneratedImpureRules = Registry.Impure;
    private static readonly GeneratedPureRule[] GeneratedPureRules = Registry.Pure;

    internal static bool TryGetKnownGeneratedPureVisibility(string symbol, out string effectVisibilityClassification) {
        effectVisibilityClassification = "none";
        foreach (var rule in GeneratedPureRules) {
            if (!rule.Matches(symbol)) continue;

            effectVisibilityClassification = rule.Visibility;
            return true;
        }

        return false;
    }

    internal static bool TryGetKnownGeneratedImpureCategories(string symbol, out string[] categories) {
        categories = ["impure_callee"];
        foreach (var rule in GeneratedImpureRules) {
            if (!rule.Matches(symbol)) continue;

            categories = [.. rule.Categories];
            return true;
        }

        return false;
    }

    private static GeneratedRuleRegistry LoadRegistry() {
        var json = ToolEmbeddedText.Load(typeof(EffectSummaryGeneratedPurityRules).Assembly, ResourceName);
        var definitions = JsonSerializer.Deserialize<GeneratedRuleDefinitions>(json) ??
                          throw new InvalidOperationException("The generated-purity rule registry is empty.");
        if (definitions.Impure is not { Length: > 0 } || definitions.Pure is not { Length: > 0 })
            throw new InvalidOperationException("The generated-purity rule registry must contain both rule groups.");

        var impurePredicates = new HashSet<string>(StringComparer.Ordinal);
        var impure = definitions.Impure.Select((rule, index) => new GeneratedImpureRule(
            ValidateValues(rule.ExactSymbols, $"impure rule {index} exact symbols"),
            ValidateValues(rule.Prefixes, $"impure rule {index} prefixes"),
            ValidateValues(rule.Categories, $"impure rule {index} categories", requireValue: true),
            ResolveImpurePredicate(rule.Predicate, impurePredicates, index))).ToArray();

        var purePredicates = new HashSet<string>(StringComparer.Ordinal);
        var pure = definitions.Pure.Select((rule, index) => new GeneratedPureRule(
            ValidateVisibility(rule.Visibility, index),
            ValidateValues(rule.ExactSymbols, $"pure rule {index} exact symbols"),
            ValidateValues(rule.Prefixes, $"pure rule {index} prefixes"),
            ResolvePurePredicate(rule.Predicate, purePredicates, index))).ToArray();

        ValidateMatchers(impure, pure);
        return new GeneratedRuleRegistry(impure, pure);
    }

    private static string[] ValidateValues(string[]? values, string description, bool requireValue = false) {
        if (values is null || requireValue && values.Length == 0 ||
            values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidOperationException($"The generated-purity registry has invalid {description}.");
        return values;
    }

    private static string ValidateVisibility(string? visibility, int index) {
        if (visibility is not ("none" or "internal_only"))
            throw new InvalidOperationException($"Generated pure rule {index} has invalid visibility.");
        return visibility;
    }

    private static Func<string, bool>? ResolveImpurePredicate(
        string? name,
        HashSet<string> usedPredicates,
        int index) {
        if (name is null) return null;
        if (!usedPredicates.Add(name))
            throw new InvalidOperationException($"Generated impure rule {index} repeats predicate '{name}'.");
        return name switch {
            nameof(IsGeneratedArrayComparerSort) => IsGeneratedArrayComparerSort,
            _ => throw new InvalidOperationException($"Generated impure rule {index} references unknown predicate '{name}'.")
        };
    }

    private static Func<string, bool>? ResolvePurePredicate(
        string? name,
        HashSet<string> usedPredicates,
        int index) {
        if (name is null) return null;
        if (!usedPredicates.Add(name))
            throw new InvalidOperationException($"Generated pure rule {index} repeats predicate '{name}'.");
        return name switch {
            nameof(IsImmutableHashSetEnumeratorMethod) => IsImmutableHashSetEnumeratorMethod,
            _ => throw new InvalidOperationException($"Generated pure rule {index} references unknown predicate '{name}'.")
        };
    }

    private static void ValidateMatchers(GeneratedImpureRule[] impure, GeneratedPureRule[] pure) {
        if (impure.Any(rule => !rule.HasMatcher) || pure.Any(rule => !rule.HasMatcher))
            throw new InvalidOperationException("Every generated-purity rule must define a matcher.");
    }

    private sealed record GeneratedImpureRule(
        string[] ExactSymbols,
        string[] SymbolPrefixes,
        string[] Categories,
        Func<string, bool>? Predicate) {
        internal bool HasMatcher => ExactSymbols.Length != 0 || SymbolPrefixes.Length != 0 || Predicate != null;

        internal bool Matches(string symbol) =>
            ExactSymbols.Contains(symbol, StringComparer.Ordinal) ||
            SymbolPrefixes.Any(prefix => symbol.StartsWith(prefix, StringComparison.Ordinal)) ||
            Predicate?.Invoke(symbol) == true;
    }

    private sealed record GeneratedPureRule(
        string Visibility,
        string[] ExactSymbols,
        string[] SymbolPrefixes,
        Func<string, bool>? Predicate) {
        internal bool HasMatcher => ExactSymbols.Length != 0 || SymbolPrefixes.Length != 0 || Predicate != null;

        internal bool Matches(string symbol) =>
            ExactSymbols.Contains(symbol, StringComparer.Ordinal) ||
            SymbolPrefixes.Any(prefix => symbol.StartsWith(prefix, StringComparison.Ordinal)) ||
            Predicate?.Invoke(symbol) == true;
    }

    private static bool IsImmutableHashSetEnumeratorMethod(string symbol) =>
        symbol.StartsWith("System.Collections.Immutable.ImmutableHashSet`1", StringComparison.Ordinal) &&
        symbol.Contains("GetEnumerator()", StringComparison.Ordinal);

    internal static bool IsGeneratedArrayComparerSort(string symbol) =>
        symbol.StartsWith("System.Array.Sort(!!0[], System.Collections.Generic.IComparer`1<!!0>)", StringComparison.Ordinal) ||
        symbol.StartsWith("System.Array.Sort(!!0[], int, int, System.Collections.Generic.IComparer`1<!!0>)", StringComparison.Ordinal);

    private sealed record GeneratedRuleRegistry(GeneratedImpureRule[] Impure, GeneratedPureRule[] Pure);

    private sealed record GeneratedRuleDefinitions(
        GeneratedImpureRuleDefinition[] Impure,
        GeneratedPureRuleDefinition[] Pure);

    private sealed record GeneratedImpureRuleDefinition(
        string[] ExactSymbols,
        string[] Prefixes,
        string[] Categories,
        string? Predicate);

    private sealed record GeneratedPureRuleDefinition(
        string Visibility,
        string[] ExactSymbols,
        string[] Prefixes,
        string? Predicate);
}
