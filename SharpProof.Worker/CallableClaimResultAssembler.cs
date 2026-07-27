namespace SharpProof.Worker;

internal static class CallableClaimResultAssembler {
    internal static WorkerClaimResult FromOutcome(
        ManifestCallableTarget target,
        int contractOrdinal,
        ProofOutcome outcome,
        BoundMethodContracts contracts,
        IReadOnlyDictionary<ProofJustification, string> assumptionLabels,
        IReadOnlyDictionary<ProofJustification, string> userAssumptionIds,
        bool usesSpecModeledCallResult) {
        var record = CreateBaseRecord(target, contractOrdinal);
        var usedUserAssumptions = new HashSet<string>(StringComparer.Ordinal);
        switch (outcome) {
            case ProvenOutcome proven:
                record.Outcome = WorkerClaimOutcome.Proven;
                record.Reason = WorkerClaimReason.None;
                record.ProofCore = [.. proven.Core
                    .Select(justification =>
                        assumptionLabels.TryGetValue(justification, out var label)
                            ? label
                            : "hygienic")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static label => label, StringComparer.Ordinal)];
                foreach (var justification in proven.Core)
                    if (userAssumptionIds.TryGetValue(justification, out var id))
                        usedUserAssumptions.Add(id);
                break;
            case RefutedOutcome when usesSpecModeledCallResult:
                record.Outcome = WorkerClaimOutcome.Unknown;
                record.Reason =
                    WorkerClaimReason.CounterexampleReplayFailed;
                break;
            case RefutedOutcome refuted:
                record.Outcome = WorkerClaimOutcome.Refuted;
                record.Reason = WorkerClaimReason.None;
                record.Model = CreateModel(refuted, contracts);
                break;
            case UnknownOutcome unknown:
                record.Outcome = WorkerClaimOutcome.Unknown;
                record.Reason = MapAbstention(unknown.Reason);
                break;
            default:
                record.Outcome = WorkerClaimOutcome.Unknown;
                record.Reason =
                    WorkerClaimReason.MalformedBackendResult;
                break;
        }
        record.Assumptions = [.. target.Assumptions.Select(evidence =>
            new WorkerAssumptionEvidence {
                Id = evidence.Id,
                Kind = evidence.Kind,
                Used = evidence.Kind == WorkerAssumptionKind.UserAssume &&
                       usedUserAssumptions.Contains(evidence.Id)
            })];
        return record;
    }

    internal static WorkerClaimResult Unknown(
        ManifestCallableTarget target,
        int contractOrdinal,
        WorkerClaimReason reason) {
        var record = CreateBaseRecord(target, contractOrdinal);
        record.Outcome = WorkerClaimOutcome.Unknown;
        record.Reason = reason;
        return record;
    }

    internal static ImmutableArray<WorkerClaimResult> Unknowns(
        ManifestCallableTarget target,
        WorkerClaimReason reason) =>
        [.. target.Claims.Select((_, index) =>
            Unknown(target, index, reason))];

    internal static void AppendResourceLimit(
        ImmutableArray<WorkerClaimResult>.Builder records,
        ManifestCallableTarget target,
        int start,
        int count) {
        for (var index = start; index < count; index++)
            records.Add(Unknown(
                target,
                index,
                WorkerClaimReason.ResourceLimit));
    }

    private static WorkerClaimResult CreateBaseRecord(
        ManifestCallableTarget target,
        int contractOrdinal) =>
        new() {
            ClaimId = target.Claims[contractOrdinal].Entry.ClaimId,
            Outcome = WorkerClaimOutcome.Unknown,
            Reason = WorkerClaimReason.InfrastructureFailure,
            Assumptions = [.. target.Assumptions]
        };

    private static WorkerModelValue[] CreateModel(
        RefutedOutcome outcome,
        BoundMethodContracts contracts) {
        var names = contracts.Variables.ToDictionary(
            static variable => variable.Variable,
            static variable => variable.Role switch {
                BoundContractVariableRole.Parameter =>
                    "parameter:" + variable.Ordinal.ToString(
                        CultureInfo.InvariantCulture),
                BoundContractVariableRole.Receiver => "receiver",
                BoundContractVariableRole.Result => "result",
                BoundContractVariableRole.PreState =>
                    "pre:" + (variable.CurrentStateVariable?.Value ?? -1)
                        .ToString(CultureInfo.InvariantCulture),
                _ => "variable:" + variable.Variable.Value.ToString(
                    CultureInfo.InvariantCulture)
            });
        return [.. outcome.Model.Assignments
            .Select(assignment => new WorkerModelValue {
                Variable = names.TryGetValue(assignment.Key, out var name)
                    ? name
                    : "variable:" + assignment.Key.Value.ToString(
                        CultureInfo.InvariantCulture),
                Kind = assignment.Value.Kind.ToString(),
                Value = FormatValue(assignment.Value)
            })
            .OrderBy(static value => value.Variable, StringComparer.Ordinal)];
    }

    private static string FormatValue(IrValue value) => value.Kind switch {
        IrValueKind.Boolean => value.Boolean ? "true" : "false",
        IrValueKind.Integer => value.Integer.ToString(CultureInfo.InvariantCulture),
        IrValueKind.String => value.String,
        IrValueKind.Null => "null",
        _ => "<opaque>"
    };

    private static WorkerClaimReason MapAbstention(
        AbstentionReason reason) => reason switch {
            AbstentionReason.UnsupportedOperation =>
                WorkerClaimReason.UnsupportedExpression,
            AbstentionReason.UnsupportedEncoding =>
                WorkerClaimReason.UnsupportedExpression,
            AbstentionReason.ResourceLimit =>
                WorkerClaimReason.ResourceLimit,
            AbstentionReason.Timeout =>
                WorkerClaimReason.MethodTimeout,
            AbstentionReason.BackendUnavailable =>
                WorkerClaimReason.BackendUnavailable,
            AbstentionReason.InfrastructureFailure =>
                WorkerClaimReason.InfrastructureFailure,
            AbstentionReason.MalformedBackendResult =>
                WorkerClaimReason.MalformedBackendResult,
            AbstentionReason.CounterexampleReplayFailed =>
                WorkerClaimReason.CounterexampleReplayFailed,
            _ => WorkerClaimReason.UnsupportedExpression
        };
}
