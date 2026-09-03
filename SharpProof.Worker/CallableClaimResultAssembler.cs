namespace SharpProof.Worker;

internal static class CallableClaimResultAssembler
{
    internal static WorkerClaimResult FromOutcome(CompilerCallablePreparation target, int contractOrdinal,
        ProofOutcome outcome, IReadOnlyList<CompilerCanonicalVariable> variables,
        IReadOnlyDictionary<ProofJustification, string> assumptionLabels,
        IReadOnlyDictionary<ProofJustification, string> userAssumptionIds,
        WorkerClaimReason replayFailure,
        WorkerVacuityKind vacuity)
    {
        var record = Unknown(
            target,
            contractOrdinal,
            WorkerClaimReason.InfrastructureFailure,
            projectAssumptions: false);
        var usedUserAssumptions = new HashSet<string>(StringComparer.Ordinal);
        switch (outcome)
        {
            case ProvenOutcome proven:
                var proofCore = new SortedSet<string>(StringComparer.Ordinal);
                var hasMalformedEvidence = false;
                foreach (var justification in proven.Core)
                {
                    if (!assumptionLabels.TryGetValue(justification, out var label))
                    {
                        hasMalformedEvidence = true;
                        break;
                    }

                    proofCore.Add(label);
                    if (userAssumptionIds.TryGetValue(justification, out var id))
                    {
                        usedUserAssumptions.Add(id);
                    }
                }

                if (hasMalformedEvidence)
                {
                    usedUserAssumptions.Clear();
                    record.Reason = WorkerClaimReason.MalformedBackendResult;
                    break;
                }

                (record.Outcome, record.Reason, record.Vacuity) =
                    (WorkerClaimOutcome.Proven, WorkerClaimReason.None, vacuity);
                record.ProofCore = [.. proofCore];
                break;
            case RefutedOutcome when replayFailure != WorkerClaimReason.None:
                record.Reason = replayFailure;
                break;
            case RefutedOutcome refuted:
                (record.Outcome, record.Reason) =
                    (WorkerClaimOutcome.Refuted, WorkerClaimReason.None);
                record.Model = CreateModel(refuted, variables);
                break;
            case UnknownOutcome unknown:
                (record.Outcome, record.Reason) =
                    (WorkerClaimOutcome.Unknown,
                        WorkerProjections.MapAbstention(unknown.Reason));
                break;
            default:
                (record.Outcome, record.Reason) =
                    (WorkerClaimOutcome.Unknown, WorkerClaimReason.MalformedBackendResult);
                break;
        }
        record.Assumptions = ProjectAssumptions(
            target,
            evidence => evidence.Kind == WorkerAssumptionKind.UserAssume &&
                usedUserAssumptions.Contains(evidence.Id));
        return record;
    }

    private static WorkerAssumptionEvidence[] ProjectAssumptions(
        CompilerCallablePreparation target,
        Func<WorkerAssumptionEvidence, bool> isUsed)
    {
        return [.. target.Entry.Assumptions.Select(evidence =>
            new WorkerAssumptionEvidence
            {
                Id = evidence.Id,
                Kind = evidence.Kind,
                Used = isUsed(evidence)
            })];
    }

    internal static WorkerClaimResult Unknown(
        CompilerCallablePreparation target,
        int contractOrdinal,
        WorkerClaimReason reason,
        bool projectAssumptions = true)
    {
        var claimId = target.Entry.ClaimIds[contractOrdinal];
        return CreateUnknown(
            target,
            claimId,
            reason,
            target.EffectClaims.Any(evidence => evidence.ClaimId == claimId),
            projectAssumptions);
    }

    internal static ImmutableArray<WorkerClaimResult> Unknowns(
        CompilerCallablePreparation target, WorkerClaimReason reason)
    {
        var effectClaimIds = EffectClaimIds(target);
        return [.. target.Entry.ClaimIds.Select(claimId => CreateUnknown(
            target,
            claimId,
            reason,
            effectClaimIds.Contains(claimId)))];
    }

    internal static ImmutableArray<WorkerClaimResult> PostconditionUnknowns(
        CompilerCallablePreparation target, WorkerClaimReason reason)
    {
        // One caller reaches here precisely because the Ensures clauses outnumber
        // the declared claim ids, so the clause count cannot be used to index
        // ClaimIds without clamping.
        var ensures = target.Clauses.Count(static clause =>
            clause.Kind == CompilerContractKind.Ensures);
        var effectClaimIds = EffectClaimIds(target);
        return [.. Enumerable.Range(0, Math.Min(ensures, target.Entry.ClaimIds.Length))
            .Select(index =>
                CreateUnknown(
                    target,
                    target.Entry.ClaimIds[index],
                    reason,
                    effectClaimIds.Contains(target.Entry.ClaimIds[index])))];
    }

    private static HashSet<string> EffectClaimIds(
        CompilerCallablePreparation target)
    {
        return new HashSet<string>(
            target.EffectClaims.Select(static evidence => evidence.ClaimId),
            StringComparer.Ordinal);
    }

    private static WorkerClaimResult CreateUnknown(
        CompilerCallablePreparation target,
        string claimId,
        WorkerClaimReason reason,
        bool hasEffectEvidence,
        bool projectAssumptions = true)
    {
        return Create(
            target,
            claimId,
            WorkerClaimOutcome.Unknown,
            reason,
            hasEffectEvidence
                ? WorkerEffectEvidenceCertainty.Unavailable
                : WorkerEffectEvidenceCertainty.Unspecified,
            projectAssumptions);
    }

    internal static WorkerClaimResult Create(
        CompilerCallablePreparation target, string claimId,
        WorkerClaimOutcome outcome, WorkerClaimReason reason,
        WorkerEffectEvidenceCertainty certainty,
        bool projectAssumptions = true)
    {
        var record = new WorkerClaimResult
        {
            ClaimId = claimId,
            Outcome = outcome,
            Reason = reason,
            EffectCertainty = certainty
        };
        if (projectAssumptions)
        {
            record.Assumptions = ProjectAssumptions(
                target,
                static evidence => evidence.Used);
        }
        return record;
    }

    internal static WorkerClaimResult Contradictory(
        CompilerCallablePreparation target,
        string claimId,
        WorkerEffectEvidenceCertainty certainty,
        IReadOnlyList<string> proofCore,
        IReadOnlySet<string> usedAssumptionIds)
    {
        var record = Create(
            target,
            claimId,
            WorkerClaimOutcome.Proven,
            WorkerClaimReason.None,
            certainty);
        record.Vacuity = WorkerVacuityKind.ContradictoryPreconditions;
        record.ProofCore = [.. proofCore];
        record.Assumptions = MarkAssumptionsUsed(target, usedAssumptionIds);
        return record;
    }

    internal static WorkerAssumptionEvidence[] MarkAssumptionsUsed(
        CompilerCallablePreparation target,
        IReadOnlySet<string> usedAssumptionIds)
    {
        return ProjectAssumptions(
            target,
            evidence => evidence.Used || usedAssumptionIds.Contains(evidence.Id));
    }

    private static WorkerModelValue[] CreateModel(
        RefutedOutcome outcome, IReadOnlyList<CompilerCanonicalVariable> variables)
    {
        var names = variables.ToDictionary(
            static variable => variable.Variable, static variable => variable.ModelLabel);
        return [.. outcome.Model.Assignments
            .Join(names, static assignment => assignment.Key, static name => name.Key,
                static (assignment, name) => ModelValue(name.Value, assignment.Value))
            .OrderBy(static value => value.Variable, StringComparer.Ordinal)];
    }

    private static WorkerModelValue ModelValue(string variable, IrValue value)
    {
        var formatted = WorkerProjections.FormatValue(value);
        return new WorkerModelValue { Variable = variable, Kind = formatted.Kind, Value = formatted.Value };
    }
}
