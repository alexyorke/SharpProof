namespace SharpProof.Symbolic;

internal static class SymbolicInvariantTargetFilter {
    internal static IReadOnlyList<TTarget> ApplyToTargets<TTarget>(
        IReadOnlyList<TTarget> targets,
        IReadOnlyList<string> invariantTargets,
        Func<TTarget, string> targetSelector) {
        if (invariantTargets.Count == 0) return targets;

        return targets
            .Where(target => Matches(targetSelector(target), invariantTargets))
            .ToArray();
    }

    internal static IReadOnlyList<string> GetUnmatchedTargetFilters(
        IReadOnlyList<string> invariantTargets,
        IReadOnlyList<string> matchedTargetFilters) {
        if (invariantTargets.Count == 0) return Array.Empty<string>();

        var matched = new HashSet<string>(matchedTargetFilters, StringComparer.Ordinal);
        return invariantTargets
            .Select(NormalizeTarget)
            .Where(target => !matched.Contains(target))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool Matches(string? target, IReadOnlyList<string> invariantTargets) {
        var normalizedTarget = NormalizeTarget(target);
        return invariantTargets.Any(filter =>
            string.Equals(NormalizeTarget(filter), normalizedTarget, StringComparison.Ordinal));
    }

    internal static string NormalizeTarget(string? target) {
        return string.IsNullOrWhiteSpace(target)
            ? "path"
            : target!.Trim();
    }
}
