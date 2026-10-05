namespace SharpProof.Worker;

internal static class CallableClaimResultAssembler
{
    internal static WorkerClaimResult FromTotal(CompilerCallablePreparation target, TotalCallableClaimCheck check)
    {
        ArgumentNullGuard.NotNull(target, nameof(target));
        ArgumentNullGuard.NotNull(check, nameof(check));
        var total = target.Total;
        if (total == null || !total.Clauses.Any(clause => clause.Kind == CompilerContractKind.Ensures && clause.ClaimId == check.ClaimId))
        { throw new ArgumentException("The check does not belong to this Total callable.", nameof(check)); }
        var record = Create(target, check.ClaimId, WorkerClaimOutcome.Unknown,
            check.Evidence.Reason == WorkerClaimReason.None ? WorkerClaimReason.SolverIncomplete : check.Evidence.Reason,
            WorkerEffectEvidenceCertainty.Unspecified, projectAssumptions: false);
        record.Assumptions = ProjectAssumptions(target, static _ => false);
        if (!check.Checked || !check.Enrolled)
        {
            if (check.Checked)
            { record.Reason = WorkerClaimReason.MalformedBackendResult; }
            return record;
        }
        if (check.Evidence.Outcome is ProvenOutcome or RefutedOutcome && check.Evidence.Reason != WorkerClaimReason.None)
        { record.Reason = WorkerClaimReason.MalformedBackendResult; return record; }
        var declarations = target.Entry.Assumptions.ToDictionary(assumption => assumption.Id, assumption => assumption.Kind, StringComparer.Ordinal);
        if (check.Assumptions.Length != declarations.Count ||
            check.Assumptions.Select(assumption => assumption.Id).Distinct(StringComparer.Ordinal).Count() != declarations.Count ||
            check.Assumptions.Any(assumption => !declarations.TryGetValue(assumption.Id, out var kind) || kind != assumption.Kind))
        { record.Reason = WorkerClaimReason.MalformedBackendResult; return record; }
        record.Assumptions = [.. check.Assumptions.Select(assumption => new WorkerAssumptionEvidence
        { Id = assumption.Id, Kind = assumption.Kind, Used = assumption.Used })];
        switch (check.Evidence.Outcome)
        {
            case ProvenOutcome:
                record.Outcome = WorkerClaimOutcome.Proven;
                record.Reason = WorkerClaimReason.None;
                record.Vacuity = check.Vacuity;
                record.ProofCore = [.. check.Evidence.Core.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
                break;
            case RefutedOutcome when check.Vacuity == WorkerVacuityKind.None:
                // These variables belong to the Total factory. Legacy variable
                // ids and integer domains cannot name or format this model.
                if (check.Evidence.EntryModel.Count != total.Parameters.Length ||
                    total.Parameters.Any(parameter => !check.Evidence.EntryModel.TryGetValue(parameter.Entry, out var value) ||
                        value.Type != total.Program.Factory.GetVariableInfo(parameter.Entry).Type))
                { record.Reason = WorkerClaimReason.MalformedBackendResult; break; }
                record.Outcome = WorkerClaimOutcome.Refuted;
                record.Reason = WorkerClaimReason.None;
                record.Model = [.. total.Parameters.Select((parameter, ordinal) =>
                {
                    var value = check.Evidence.EntryModel[parameter.Entry];
                    var formatted = WorkerProjections.FormatTotalValue(value);
                    return new WorkerModelValue
                    {
                        Variable = total.HasReceiver && ordinal == total.Parameters.Length - 1 ? "this"
                            : "parameter:" + ordinal.ToString(CultureInfo.InvariantCulture),
                        Kind = formatted.Kind,
                        Value = formatted.Value
                    };
                }).OrderBy(value => value.Variable, StringComparer.Ordinal)];
                break;
            case UnknownOutcome:
            case null when check.Evidence.QueryCompleted && check.Evidence.Reason != WorkerClaimReason.None:
                break;
            default:
                record.Reason = WorkerClaimReason.MalformedBackendResult;
                break;
        }
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
        bool projectAssumptions = true,
        IReadOnlySet<string>? effectClaimIds = null)
    {
        var claimId = target.Entry.ClaimIds[contractOrdinal];
        return CreateUnknown(
            target,
            claimId,
            reason,
            effectClaimIds?.Contains(claimId) ??
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

    internal static HashSet<string> EffectClaimIds(
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
            certainty,
            projectAssumptions: false);
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
}
