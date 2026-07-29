namespace SharpProof.Worker;

internal static class CallableClaimResultAssembler {
    internal static WorkerClaimResult FromOutcome(CompilerCallablePreparation target, int contractOrdinal,
        ProofOutcome outcome, IReadOnlyList<CompilerCanonicalVariable> variables,
        IReadOnlyDictionary<ProofJustification, string> assumptionLabels,
        IReadOnlyDictionary<ProofJustification, string> userAssumptionIds,
        WorkerClaimReason replayFailure,
        WorkerVacuityKind vacuity) {
        var record = Unknown(target, contractOrdinal, WorkerClaimReason.InfrastructureFailure);
        var usedUserAssumptions = new HashSet<string>(StringComparer.Ordinal);
        switch (outcome) {
            case ProvenOutcome proven:
                (record.Outcome, record.Reason, record.Vacuity) =
                    (WorkerClaimOutcome.Proven, WorkerClaimReason.None, vacuity);
                record.ProofCore = [.. proven.Core
                    .Select(justification =>
                        assumptionLabels.TryGetValue(justification, out var label) ? label : "hygienic")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static label => label, StringComparer.Ordinal)];
                foreach (var justification in proven.Core)
                    if (userAssumptionIds.TryGetValue(justification, out var id))
                        usedUserAssumptions.Add(id);
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
                Used = evidence.Kind == WorkerAssumptionKind.UserAssume &&
                       usedUserAssumptions.Contains(evidence.Id)
            })];
        return record;
    }

    internal static WorkerClaimResult Unknown(
        CompilerCallablePreparation target, int contractOrdinal, WorkerClaimReason reason) {
        var claimId = target.Entry.ClaimIds[contractOrdinal];
        var certainty = target.EffectClaims.Any(evidence => evidence.ClaimId == claimId)
            ? WorkerEffectEvidenceCertainty.Unavailable
            : WorkerEffectEvidenceCertainty.Unspecified;
        return Create(target, claimId, WorkerClaimOutcome.Unknown, reason, certainty);
    }

    internal static ImmutableArray<WorkerClaimResult> Unknowns(
        CompilerCallablePreparation target, WorkerClaimReason reason) =>
        [.. target.Entry.ClaimIds.Select((_, index) => Unknown(target, index, reason))];

    internal static ImmutableArray<WorkerClaimResult> PostconditionUnknowns(
        CompilerCallablePreparation target, WorkerClaimReason reason) =>
        [.. Enumerable.Range(0, target.Clauses.Count(static clause =>
                clause.Kind == CompilerContractKind.Ensures))
            .Select(index => Unknown(target, index, reason))];

    internal static WorkerClaimResult Create(
        CompilerCallablePreparation target, string claimId,
        WorkerClaimOutcome outcome, WorkerClaimReason reason,
        WorkerEffectEvidenceCertainty certainty) =>
        new() {
            ClaimId = claimId,
            Outcome = outcome,
            Reason = reason,
            EffectCertainty = certainty,
            Assumptions = [.. target.Entry.Assumptions]
        };

    private static WorkerModelValue[] CreateModel(
        RefutedOutcome outcome, IReadOnlyList<CompilerCanonicalVariable> variables) {
        var names = variables.ToDictionary(
            static variable => variable.Variable, static variable => variable.ModelLabel);
        return [.. outcome.Model.Assignments
            .Join(names, static assignment => assignment.Key, static name => name.Key,
                static (assignment, name) => ModelValue(name.Value, assignment.Value))
            .OrderBy(static value => value.Variable, StringComparer.Ordinal)];
    }

    private static WorkerModelValue ModelValue(string variable, IrValue value) {
        var formatted = Format(value);
        return new WorkerModelValue { Variable = variable, Kind = formatted.Kind, Value = formatted.Value };
    }

    private static (string Kind, string Value) Format(IrValue value) => value.Kind switch {
        IrValueKind.Boolean => (nameof(IrValueKind.Boolean), value.Boolean ? "true" : "false"),
        IrValueKind.Integer => (nameof(IrValueKind.Integer), value.Integer.ToString(CultureInfo.InvariantCulture)),
        IrValueKind.String => (nameof(IrValueKind.String), value.String),
        IrValueKind.Null => (nameof(IrValueKind.Null), "null"),
        IrValueKind.Reference => (nameof(IrValueKind.Reference), "<opaque>"),
        IrValueKind.Sequence => (nameof(IrValueKind.Sequence), "<opaque>"),
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static WorkerClaimReason MapAbstention(AbstentionReason reason) => reason switch {
        AbstentionReason.ResourceLimit => WorkerClaimReason.ResourceLimit,
        AbstentionReason.Timeout => WorkerClaimReason.MethodTimeout,
        AbstentionReason.BackendUnavailable => WorkerClaimReason.BackendUnavailable,
        AbstentionReason.InfrastructureFailure => WorkerClaimReason.InfrastructureFailure,
        AbstentionReason.MalformedBackendResult => WorkerClaimReason.MalformedBackendResult,
        AbstentionReason.CounterexampleReplayFailed => WorkerClaimReason.CounterexampleReplayFailed,
        AbstentionReason.PostconditionMayBeUndefined => WorkerClaimReason.PostconditionMayBeUndefined,
        _ => WorkerClaimReason.UnsupportedExpression
    };
}
