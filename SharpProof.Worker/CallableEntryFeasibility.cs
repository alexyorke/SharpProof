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
        var evidence = proofCore
            .Where(static label => !string.IsNullOrWhiteSpace(label))
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

internal readonly partial record struct CallableEntryEvidenceBuildResult
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

internal static class CallableEntryFeasibilityEvaluator
{
    internal static async Task<CallableEntryFeasibility> EvaluateAsync(
        CompilerCallablePreparation target,
        MethodResourceBudget resourceBudget,
        ProofKernel kernel,
        int maximumExpressionDepth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var build = CallableEvidenceBuilder.BuildEntry(
            target,
            maximumExpressionDepth,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!build.IsSuccess)
        {
            return CallableEntryFeasibility.Unknown(
                build.FailureReason);
        }

        var evidence = build.Evidence!;
        var literalContradiction = evidence.Assumptions.FirstOrDefault(
            static assumption =>
                assumption.Predicate is
                    IrBooleanTerm { Value: false });
        if (literalContradiction != null)
        {
            return CallableEntryFeasibility.Contradictory(
                [evidence.Labels[literalContradiction.Justification]],
                CallableProofCore.AssumptionIds(
                    [literalContradiction.Justification],
                    evidence.AssumptionIds));
        }

        if (!evidence.HasNontrivialPrecondition)
        {
            return CallableEntryFeasibility.Feasible;
        }

        if (!resourceBudget.TryStartQuery())
        {
            return CallableEntryFeasibility.Unknown(
                WorkerClaimReason.ResourceLimit);
        }

        var factory = target.Factory;
        var query = new VerificationQuery(
            factory,
            evidence.Assumptions,
            Goal.CreateInternalConsistency(factory),
            evidence.ReplayVariables);
        var outcome = await kernel.VerifyAsync(
                query,
                cancellationToken)
            .ConfigureAwait(false);
        var resourceLimitExceeded = resourceBudget.IsExceeded;
        cancellationToken.ThrowIfCancellationRequested();
        if (resourceLimitExceeded)
        {
            return CallableEntryFeasibility.Unknown(
                WorkerClaimReason.ResourceLimit);
        }

        return outcome switch
        {
            ProvenOutcome proven =>
                CallableEntryFeasibility.Contradictory(
                    CallableProofCore.Create(
                        proven,
                        evidence.Labels),
                    CallableProofCore.AssumptionIds(
                        proven.Core,
                        evidence.AssumptionIds)),
            RefutedOutcome => CallableEntryFeasibility.Feasible,
            UnknownOutcome unknown =>
                CallableEntryFeasibility.Unknown(
                    WorkerProjections.MapAbstention(
                        unknown.Reason)),
            _ => CallableEntryFeasibility.Unknown(
                WorkerClaimReason.MalformedBackendResult)
        };
    }
}

internal static class CallableProofCore
{
    internal static ImmutableArray<string> Create(
        ProvenOutcome outcome,
        IReadOnlyDictionary<ProofJustification, string> labels)
    {
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

    internal static string[] Merge(
        IEnumerable<string> left,
        IEnumerable<string> right)
    {
        return NormalizeLabels(left.Concat(right));
    }

    private static string[] NormalizeLabels(IEnumerable<string> labels)
    {
        return [.. labels
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static label => label, StringComparer.Ordinal)];
    }

    internal static IEnumerable<string> AssumptionIds(
        IEnumerable<ProofJustification> proofCore,
        IReadOnlyDictionary<ProofJustification, string>
            assumptionIds)
    {
        return proofCore
            .Select(justification =>
                assumptionIds.TryGetValue(
                    justification,
                    out var assumptionId)
                    ? assumptionId
                    : null)
            .OfType<string>();
    }
}
