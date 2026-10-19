namespace SharpProof.Worker;

internal enum CallableEntryFeasibilityKind
{
    Feasible,
    Contradictory,
    Unknown
}

internal sealed partial record CallableEntryFeasibility
{
    internal static CallableEntryFeasibility Feasible { get; } =
        new(
            CallableEntryFeasibilityKind.Feasible,
            WorkerClaimReason.None,
            [],
            ImmutableHashSet<string>.Empty);

    internal bool IsContradictory =>
        Kind == CallableEntryFeasibilityKind.Contradictory;

    internal bool IsUnknown =>
        Kind == CallableEntryFeasibilityKind.Unknown;

    internal static CallableEntryFeasibility Contradictory(
        IEnumerable<string> proofCore,
        IEnumerable<string> usedAssumptionIds)
    {
        System.ArgumentNullException.ThrowIfNull(proofCore);
        System.ArgumentNullException.ThrowIfNull(usedAssumptionIds);
        var evidence = proofCore
            .Where(static label => label is null ? throw new System.ArgumentNullException(nameof(label)) : (!string.IsNullOrWhiteSpace(label)))
            .ToImmutableArray();
        return evidence.IsDefaultOrEmpty
            ? Unknown(WorkerClaimReason.MalformedBackendResult)
            : new(
                CallableEntryFeasibilityKind.Contradictory,
                WorkerClaimReason.None,
                evidence,
                usedAssumptionIds
                    .Where(static id =>
                        id is null ? throw new System.ArgumentNullException(nameof(id)) : (!string.IsNullOrWhiteSpace(id)))
                    .ToImmutableHashSet(
                        StringComparer.Ordinal));
    }

    internal static CallableEntryFeasibility Unknown(
        WorkerClaimReason reason)
    {
        return new(
            CallableEntryFeasibilityKind.Unknown,
            reason,
            [],
            ImmutableHashSet<string>.Empty);
    }
}

internal static class CallableProofCore
{
    internal static ImmutableArray<string> Create(
        ProvenOutcome outcome,
        ImmutableDictionary<ProofJustification, string> labels)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(labels);
        var result = ImmutableArray.CreateBuilder<string>(
            outcome.Core.Length);
        foreach (var justification in outcome.Core)
        {
            if (!labels.TryGetValue(justification, out var label))
            {
                return [];
            }

            result.Add(label);
        }

        return [.. NormalizeLabels(result)];
    }

    private static IEnumerable<string> NormalizeLabels(
        IEnumerable<string> labels)
    {
        System.ArgumentNullException.ThrowIfNull(labels);
        return labels
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static label => label is null ? throw new System.ArgumentNullException(nameof(label)) : (label), StringComparer.Ordinal);
    }

    internal static IEnumerable<string> AssumptionIds(
        IEnumerable<ProofJustification> proofCore,
        ImmutableDictionary<ProofJustification, string>
            assumptionIds)
    {
        System.ArgumentNullException.ThrowIfNull(proofCore);
        ArgumentNullException.ThrowIfNull(assumptionIds);
        return proofCore
            .Select(justification =>
                justification is null ? throw new System.ArgumentNullException(nameof(justification)) : (assumptionIds.TryGetValue(
                    justification,
                    out var assumptionId)
                    ? assumptionId
                    : null))
            .OfType<string>();
    }
}
