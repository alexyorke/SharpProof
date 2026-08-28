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
        var record = Unknown(target, contractOrdinal, WorkerClaimReason.InfrastructureFailure);
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
                }

                if (hasMalformedEvidence)
                {
                    record.Reason = WorkerClaimReason.MalformedBackendResult;
                    break;
                }

                (record.Outcome, record.Reason, record.Vacuity) =
                    (WorkerClaimOutcome.Proven, WorkerClaimReason.None, vacuity);
                record.ProofCore = [.. proofCore];
                foreach (var justification in proven.Core)
                {
                    if (userAssumptionIds.TryGetValue(justification, out var id))
                    {
                        usedUserAssumptions.Add(id);
                    }
                }

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
                    (WorkerClaimOutcome.Unknown, MapAbstention(unknown.Reason));
                break;
            default:
                (record.Outcome, record.Reason) =
                    (WorkerClaimOutcome.Unknown, WorkerClaimReason.MalformedBackendResult);
                break;
        }
        record.Assumptions = [.. target.Entry.Assumptions.Select(evidence =>
            new WorkerAssumptionEvidence {
                Id = evidence.Id, Kind = evidence.Kind,
                Used = evidence.Kind is
                           (WorkerAssumptionKind.Precondition or
                            WorkerAssumptionKind.UserAssume) &&
                       usedUserAssumptions.Contains(evidence.Id)
            })];
        return record;
    }

    internal static WorkerClaimResult Unknown(
        CompilerCallablePreparation target, int contractOrdinal, WorkerClaimReason reason)
    {
        var claimId = target.Entry.ClaimIds[contractOrdinal];
        var certainty = target.EffectClaims.Any(evidence => evidence.ClaimId == claimId)
            ? WorkerEffectEvidenceCertainty.Unavailable
            : WorkerEffectEvidenceCertainty.Unspecified;
        return Create(target, claimId, WorkerClaimOutcome.Unknown, reason, certainty);
    }

    internal static ImmutableArray<WorkerClaimResult> Unknowns(
        CompilerCallablePreparation target, WorkerClaimReason reason)
    {
        return [.. target.Entry.ClaimIds.Select((_, index) => Unknown(target, index, reason))];
    }

    internal static ImmutableArray<WorkerClaimResult> PostconditionUnknowns(
        CompilerCallablePreparation target, WorkerClaimReason reason)
    {
        // One caller reaches here precisely because the Ensures clauses outnumber
        // the declared claim ids, so the clause count cannot be used to index
        // ClaimIds without clamping.
        var ensures = target.Clauses.Count(static clause =>
            clause.Kind == CompilerContractKind.Ensures);
        return [.. Enumerable.Range(0, Math.Min(ensures, target.Entry.ClaimIds.Length))
            .Select(index => Unknown(target, index, reason))];
    }

    internal static WorkerClaimResult Create(
        CompilerCallablePreparation target, string claimId,
        WorkerClaimOutcome outcome, WorkerClaimReason reason,
        WorkerEffectEvidenceCertainty certainty)
    {
        return new()
        {
            ClaimId = claimId,
            Outcome = outcome,
            Reason = reason,
            EffectCertainty = certainty,
            Assumptions = [.. target.Entry.Assumptions]
        };
    }

    internal static WorkerAssumptionEvidence[] MarkAssumptionsUsed(
        CompilerCallablePreparation target,
        IReadOnlySet<string> usedAssumptionIds)
    {
        return [.. target.Entry.Assumptions.Select(evidence =>
            new WorkerAssumptionEvidence
            {
                Id = evidence.Id,
                Kind = evidence.Kind,
                Used = evidence.Used ||
                    usedAssumptionIds.Contains(evidence.Id)
            })];
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
        var formatted = Format(value);
        return new WorkerModelValue { Variable = variable, Kind = formatted.Kind, Value = formatted.Value };
    }

    private static (string Kind, string Value) Format(IrValue value)
    {
        return WorkerProjections.FormatValue(value);
    }

    internal static WorkerClaimReason MapAbstention(
        AbstentionReason reason)
    {
        return WorkerProjections.MapAbstention(reason);
    }
}
