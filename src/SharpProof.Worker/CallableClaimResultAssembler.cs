namespace SharpProof.Worker;

internal static class CallableClaimResultAssembler
{
    internal static WorkerClaimResult FromTotal(CompilerCallablePreparation target, TotalCallableClaimCheck check)
    {
        ArgumentNullGuard.NotNull(target, nameof(target));
        ArgumentNullGuard.NotNull(check, nameof(check));
        var total = target.Total;
        if (total == null || !total.Clauses.Any(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Ensures && clause.ClaimId == check.ClaimId)))
        { throw new ArgumentException("The check does not belong to this Total callable.", nameof(check)); }
        var record = Create(target, check.ClaimId, WorkerClaimOutcome.Unknown,
            check.Evidence.Reason == WorkerClaimReason.None ? WorkerClaimReason.SolverIncomplete : check.Evidence.Reason,
            WorkerEffectEvidenceCertainty.Unspecified, projectAssumptions: false);
        record = record with { Assumptions = ProjectAssumptions(target, static _ => false) };
        if (!check.Checked || !check.Enrolled)
        {
            if (check.Checked)
            { record = record with { Reason = WorkerClaimReason.MalformedBackendResult }; }
            return record;
        }
        if (check.Evidence.Outcome is ProvenOutcome or RefutedOutcome && check.Evidence.Reason != WorkerClaimReason.None)
        { record = record with { Reason = WorkerClaimReason.MalformedBackendResult }; return record; }
        var declarations = ImmutableDictionaryCapture.Create(target.Entry.Assumptions, assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Id), assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind), StringComparer.Ordinal);
        if (check.Assumptions.Length != declarations.Count ||
            check.Assumptions.Select(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Id)).Distinct(StringComparer.Ordinal).Count() != declarations.Count ||
            check.Assumptions.Any(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (!declarations.TryGetValue(assumption.Id, out var kind) || kind != assumption.Kind)))
        { record = record with { Reason = WorkerClaimReason.MalformedBackendResult }; return record; }
        record = record with
        {
            Assumptions = [.. check.Assumptions.Select(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (new WorkerAssumptionEvidence
        { Id = assumption.Id, Kind = assumption.Kind, Used = assumption.Used }))]
        };
        switch (check.Evidence.Outcome)
        {
            case ProvenOutcome:
                record = record with { Outcome = WorkerClaimOutcome.Proven, Diagnostic = null };
                record = record with { Reason = WorkerClaimReason.None };
                record = record with { Vacuity = check.Vacuity };
                record = record with { ProofCore = [.. check.Evidence.Core.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)] };
                break;
            case RefutedOutcome when check.Vacuity == WorkerVacuityKind.None:
                // These variables belong to the Total factory. Legacy variable
                // ids and integer domains cannot name or format this model.
                if (check.Evidence.EntryModel.Count != total.Parameters.Length ||
                    total.Parameters.Any(parameter => !check.Evidence.EntryModel.TryGetValue(parameter.Entry, out var value) ||
                        value.Type != total.Program.Factory.GetVariableInfo(parameter.Entry).Type))
                { record = record with { Reason = WorkerClaimReason.MalformedBackendResult }; break; }
                record = record with { Outcome = WorkerClaimOutcome.Refuted, Diagnostic = null };
                record = record with { Reason = WorkerClaimReason.None };
                record = record with
                {
                    Model = [.. total.Parameters.Select((parameter, ordinal) =>
                {
RepositoryParameterGuard.RequireInitialized(parameter.Entry.IsDefault || parameter.Current.IsDefault || parameter.Old.IsDefault, nameof(parameter), "Value must be initialized.");

                    var value = check.Evidence.EntryModel[parameter.Entry];
                    var formatted = WorkerProjections.FormatTotalValue(value);
                    return new WorkerModelValue
                    {
                        Variable = total.HasReceiver && ordinal == total.Parameters.Length - 1 ? "this"
                            : "parameter:" + ordinal.ToString(CultureInfo.InvariantCulture),
                        Kind = formatted.Kind,
                        Value = formatted.Value
                    };
                }).OrderBy(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Variable), StringComparer.Ordinal)]
                };
                break;
            case UnknownOutcome:
            case null when check.Evidence.QueryCompleted && check.Evidence.Reason != WorkerClaimReason.None:
                break;
        }
        return record;
    }

    private static ImmutableArray<WorkerAssumptionEvidence> ProjectAssumptions(
        CompilerCallablePreparation target,
        Func<WorkerAssumptionEvidence, bool> isUsed)
    {
        System.ArgumentNullException.ThrowIfNull(isUsed);
        System.ArgumentNullException.ThrowIfNull(target);
        return [.. target.Entry.Assumptions.Select(evidence =>
            evidence is null ? throw new System.ArgumentNullException(nameof(evidence)) : (new WorkerAssumptionEvidence
            {
                Id = evidence.Id,
                Kind = evidence.Kind,
                Used = isUsed(evidence)
            }))];
    }

    internal static WorkerClaimResult Unknown(
        CompilerCallablePreparation target,
        int contractOrdinal,
        WorkerClaimReason reason,
        bool projectAssumptions = true,
        IReadOnlySet<string>? effectClaimIds = null)
    {
        ArgumentNullGuard.NotNull(target, nameof(target));
        if ((uint)contractOrdinal >= (uint)target.Entry.ClaimIds.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(contractOrdinal));
        }
        var claimId = target.Entry.ClaimIds[contractOrdinal];
        return CreateUnknown(
            target,
            claimId,
            reason,
            effectClaimIds?.Contains(claimId) ??
                target.EffectClaims.Any(evidence => evidence is null ? throw new System.ArgumentNullException(nameof(evidence)) : (evidence.ClaimId == claimId)),
            projectAssumptions);
    }

    internal static ImmutableArray<WorkerClaimResult> Unknowns(
        CompilerCallablePreparation target, WorkerClaimReason reason)
    {
        System.ArgumentNullException.ThrowIfNull(target);
        var effectClaimIds = EffectClaimIds(target);
        return [.. target.Entry.ClaimIds.Select(claimId => claimId is null ? throw new System.ArgumentNullException(nameof(claimId)) : (CreateUnknown(
            target,
            claimId,
            reason,
            effectClaimIds.Contains(claimId))))];
    }

    internal static ImmutableHashSet<string> EffectClaimIds(
        CompilerCallablePreparation target)
    {
        ArgumentNullGuard.NotNull(target, nameof(target));
        return target.EffectClaims.Select(static evidence => evidence is null ? throw new System.ArgumentNullException(nameof(evidence)) : (evidence.ClaimId))
            .ToImmutableHashSet(StringComparer.Ordinal);
    }

    private static WorkerClaimResult CreateUnknown(
        CompilerCallablePreparation target,
        string claimId,
        WorkerClaimReason reason,
        bool hasEffectEvidence,
        bool projectAssumptions = true)
    {
        System.ArgumentNullException.ThrowIfNull(claimId);
        System.ArgumentNullException.ThrowIfNull(target);
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
        System.ArgumentNullException.ThrowIfNull(claimId);
        System.ArgumentNullException.ThrowIfNull(target);
        var record = new WorkerClaimResult
        {
            Presentation = target.Presentation,
            Diagnostic = outcome == WorkerClaimOutcome.Unknown ? target.Diagnostic : null,
            ClaimId = claimId,
            Outcome = outcome,
            Reason = reason,
            EffectCertainty = certainty
        };
        if (projectAssumptions)
        {
            record = record with
            {
                Assumptions = ProjectAssumptions(
                target,
                static evidence => evidence is null ? throw new System.ArgumentNullException(nameof(evidence)) : (evidence.Used))
            };
        }
        return record;
    }

    internal static WorkerClaimResult Contradictory(
        CompilerCallablePreparation target,
        string claimId,
        WorkerEffectEvidenceCertainty certainty,
        ImmutableArray<string> proofCore,
        ImmutableHashSet<string> usedAssumptionIds)
    {
        System.ArgumentNullException.ThrowIfNull(claimId);
        ArgumentNullGuard.NotNull(target, nameof(target));
        RepositoryParameterGuard.ThrowIfMissing(proofCore.IsDefault, nameof(proofCore));
        ArgumentNullGuard.NotNull(usedAssumptionIds, nameof(usedAssumptionIds));
        var record = Create(
            target,
            claimId,
            WorkerClaimOutcome.Proven,
            WorkerClaimReason.None,
            certainty,
            projectAssumptions: false);
        record = record with { Vacuity = WorkerVacuityKind.ContradictoryPreconditions };
        record = record with { ProofCore = proofCore };
        record = record with { Assumptions = MarkAssumptionsUsed(target, usedAssumptionIds) };
        return record;
    }

    internal static ImmutableArray<WorkerAssumptionEvidence> MarkAssumptionsUsed(
        CompilerCallablePreparation target,
        ImmutableHashSet<string> usedAssumptionIds)
    {
        ArgumentNullGuard.NotNull(target, nameof(target));
        ArgumentNullGuard.NotNull(usedAssumptionIds, nameof(usedAssumptionIds));
        return ProjectAssumptions(
            target,
            evidence => evidence is null ? throw new System.ArgumentNullException(nameof(evidence)) : (evidence.Used || usedAssumptionIds.Contains(evidence.Id)));
    }
}
