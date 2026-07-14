namespace SharpProof.Symbolic;

internal static class SymbolicInvariantTargetFilter
{
    internal static IReadOnlyList<SymbolicConditionProofSummary> ApplyToProofSummaries(
        IReadOnlyList<SymbolicConditionProofSummary> proofs,
        IReadOnlyList<string> invariantTargets)
    {
        return ApplyToTargets(proofs, invariantTargets, static proof => proof.Target);
    }

    internal static IReadOnlyList<SymbolicConditionProofResult> ApplyToProofResults(
        IReadOnlyList<SymbolicConditionProofResult> proofs,
        IReadOnlyList<string> invariantTargets)
    {
        return ApplyToTargets(proofs, invariantTargets, static proof => proof.Target);
    }

    internal static IReadOnlyList<SymbolicInvariantCondition> ApplyToConditions(
        IReadOnlyList<SymbolicInvariantCondition> conditions,
        IReadOnlyList<string> invariantTargets)
    {
        return ApplyToTargets(conditions, invariantTargets, static condition => condition.Target);
    }

    internal static IReadOnlyList<TTarget> ApplyToTargets<TTarget>(
        IReadOnlyList<TTarget> targets,
        IReadOnlyList<string> invariantTargets,
        Func<TTarget, string> targetSelector)
    {
        if (invariantTargets.Count == 0) return targets;

        return targets
            .Where(target => Matches(targetSelector(target), invariantTargets))
            .ToArray();
    }

    internal static IReadOnlyList<string> SelectFacts(
        IReadOnlyList<string> facts,
        IReadOnlyList<SymbolicInvariantTargetSummary> filteredTargetSummaries,
        IReadOnlyList<string> invariantTargets,
        Func<SymbolicInvariantTargetSummary, IReadOnlyList<string>> factSelector)
    {
        if (invariantTargets.Count == 0) return facts;

        return filteredTargetSummaries
            .SelectMany(factSelector)
            .Where(static fact => !string.IsNullOrWhiteSpace(fact))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<string> GetMatchedTargetFilters(
        IReadOnlyList<SymbolicInvariantTargetSummary> targetSummaries,
        IReadOnlyList<SymbolicInvariantTargetPathSummary> targetPathSummaries,
        IReadOnlyList<string> invariantTargets)
    {
        if (invariantTargets.Count == 0) return Array.Empty<string>();

        var availableTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var summary in targetSummaries) availableTargets.Add(NormalizeTarget(summary.Target));

        foreach (var summary in targetPathSummaries) availableTargets.Add(NormalizeTarget(summary.Target));

        return invariantTargets
            .Select(NormalizeTarget)
            .Where(availableTargets.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<string> GetMatchedTargetFilters(
        SymbolicInvariantQueryView query,
        IReadOnlyList<string> invariantTargets)
    {
        return GetMatchedTargetFilters(
            query.TargetSummaries,
            query.TargetPathSummaries,
            invariantTargets);
    }

    internal static IReadOnlyList<string> GetUnmatchedTargetFilters(
        IReadOnlyList<string> invariantTargets,
        IReadOnlyList<string> matchedTargetFilters)
    {
        if (invariantTargets.Count == 0) return Array.Empty<string>();

        var matched = new HashSet<string>(matchedTargetFilters, StringComparer.Ordinal);
        return invariantTargets
            .Select(NormalizeTarget)
            .Where(target => !matched.Contains(target))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool Matches(string? target, IReadOnlyList<string> invariantTargets)
    {
        var normalizedTarget = NormalizeTarget(target);
        return invariantTargets.Any(filter =>
            string.Equals(NormalizeTarget(filter), normalizedTarget, StringComparison.Ordinal));
    }

    internal static string NormalizeTarget(string? target)
    {
        return string.IsNullOrWhiteSpace(target)
            ? "path"
            : target!.Trim();
    }
}
