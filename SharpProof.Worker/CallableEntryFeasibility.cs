namespace SharpProof.Worker;

internal enum CallableEntryFeasibilityKind
{
    Feasible,
    Contradictory,
    Unknown
}

internal sealed record CallableEntryFeasibility(
    CallableEntryFeasibilityKind Kind,
    WorkerClaimReason Reason,
    ImmutableArray<string> ProofCore,
    ImmutableHashSet<string> UsedAssumptionIds)
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
        var evidence = proofCore
            .Where(static label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static label => label, StringComparer.Ordinal)
            .ToImmutableArray();
        return evidence.IsDefaultOrEmpty
            ? Unknown(WorkerClaimReason.MalformedBackendResult)
            : new(
                CallableEntryFeasibilityKind.Contradictory,
                WorkerClaimReason.None,
                evidence,
                usedAssumptionIds
                    .Where(static id =>
                        !string.IsNullOrWhiteSpace(id))
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

internal sealed record CallableEntryEvidence(
    ImmutableArray<Assumption> Assumptions,
    IReadOnlyDictionary<ProofJustification, string> Labels,
    IReadOnlyDictionary<ProofJustification, string> AssumptionIds,
    ImmutableArray<IrVarId> ReplayVariables,
    bool HasNontrivialPrecondition);

internal readonly record struct CallableEntryEvidenceBuildResult(
    CallableEntryEvidence? Evidence,
    WorkerClaimReason FailureReason)
{
    internal bool IsSuccess => Evidence != null;

    internal static CallableEntryEvidenceBuildResult Success(
        CallableEntryEvidence evidence)
    {
        return new(evidence, WorkerClaimReason.None);
    }

    internal static CallableEntryEvidenceBuildResult Fail(
        WorkerClaimReason reason)
    {
        return new(null, reason);
    }
}

internal sealed record CallableProofVerification(
    ImmutableArray<WorkerClaimResult> Postconditions,
    CallableEntryFeasibility EntryFeasibility);
