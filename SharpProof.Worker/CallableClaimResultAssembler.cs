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
                        Variable = "parameter:" + ordinal.ToString(CultureInfo.InvariantCulture),
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

    internal static WorkerClaimResult FromOutcome(CompilerCallablePreparation target, int contractOrdinal,
        ProofOutcome outcome,
        IReadOnlyDictionary<ProofJustification, string> assumptionLabels,
        IReadOnlyDictionary<ProofJustification, string> userAssumptionIds,
        WorkerVacuityKind vacuity,
        IReadOnlySet<string>? effectClaimIds = null)
    {
        var claimId = target.Entry.ClaimIds[contractOrdinal];
        var effectCertainty = (effectClaimIds?.Contains(claimId) ??
            target.EffectClaims.Any(evidence => evidence.ClaimId == claimId))
            ? WorkerEffectEvidenceCertainty.Unavailable
            : WorkerEffectEvidenceCertainty.Unspecified;
        WorkerClaimResult record;
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
                    record = Create(
                        target,
                        claimId,
                        WorkerClaimOutcome.Unknown,
                        WorkerClaimReason.MalformedBackendResult,
                        effectCertainty,
                        projectAssumptions: false);
                    break;
                }

                record = Create(
                    target,
                    claimId,
                    WorkerClaimOutcome.Proven,
                    WorkerClaimReason.None,
                    effectCertainty,
                    projectAssumptions: false);
                record.Vacuity = vacuity;
                record.ProofCore = [.. proofCore];
                break;
            case RefutedOutcome refuted:
                record = Create(
                    target,
                    claimId,
                    WorkerClaimOutcome.Refuted,
                    WorkerClaimReason.None,
                    effectCertainty,
                    projectAssumptions: false);
                record.Model = CreateModel(refuted, target.Variables);
                break;
            case UnknownOutcome unknown:
                record = Create(
                    target,
                    claimId,
                    WorkerClaimOutcome.Unknown,
                    WorkerProjections.MapAbstention(unknown.Reason),
                    effectCertainty,
                    projectAssumptions: false);
                break;
            default:
                record = Create(
                    target,
                    claimId,
                    WorkerClaimOutcome.Unknown,
                    WorkerClaimReason.MalformedBackendResult,
                    effectCertainty,
                    projectAssumptions: false);
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

    internal static ImmutableArray<WorkerClaimResult> PostconditionUnknowns(
        CompilerCallablePreparation target,
        WorkerClaimReason reason,
        int startIndex = 0,
        IReadOnlySet<string>? effectClaimIds = null)
    {
        // One caller reaches here precisely because the Ensures clauses outnumber
        // the declared claim ids, so the clause count cannot be used to index
        // ClaimIds without clamping.
        var ensures = target.Clauses.Count(static clause =>
            clause.Kind == CompilerContractKind.Ensures);
        var count = Math.Min(ensures, target.Entry.ClaimIds.Length);
        startIndex = Math.Clamp(startIndex, 0, count);
        effectClaimIds ??= EffectClaimIds(target);
        var results = ImmutableArray.CreateBuilder<WorkerClaimResult>(
            count - startIndex);
        for (var index = startIndex; index < count; index++)
        {
            var claimId = target.Entry.ClaimIds[index];
            results.Add(CreateUnknown(
                target,
                claimId,
                reason,
                effectClaimIds.Contains(claimId)));
        }
        return results.MoveToImmutable();
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

    private static WorkerModelValue[] CreateModel(
        RefutedOutcome outcome, IReadOnlyList<CompilerCanonicalVariable> variables)
    {
        var names = variables.ToDictionary(
            static variable => variable.Variable, static variable => variable.ModelLabel);
        var model = new List<WorkerModelValue>();
        foreach (var assignment in outcome.Model.Assignments)
        {
            if (names.TryGetValue(assignment.Key, out var name))
            {
                model.Add(ModelValue(name, assignment.Value));
            }
        }
        return [.. model.OrderBy(
            static value => value.Variable, StringComparer.Ordinal)];
    }

    private static WorkerModelValue ModelValue(string variable, IrValue value)
    {
        var formatted = WorkerProjections.FormatValue(value);
        return new WorkerModelValue { Variable = variable, Kind = formatted.Kind, Value = formatted.Value };
    }
}
