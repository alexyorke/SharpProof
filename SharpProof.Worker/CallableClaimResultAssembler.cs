namespace SharpProof.Worker;
#pragma warning disable IDE0055 // Compact result assembly preserves the fixed production-size ceiling.

internal static class CallableClaimResultAssembler {
    internal static WorkerClaimResult FromOutcome(CompilerCallablePreparation target, int contractOrdinal,
        ProofOutcome outcome, IReadOnlyList<CompilerCanonicalVariable> variables,
        IReadOnlyDictionary<ProofJustification, string> assumptionLabels,
        IReadOnlyDictionary<ProofJustification, string> userAssumptionIds,
        bool usesSpecModeledCallResult) {
        var record = CreateBaseRecord(target, contractOrdinal);
        var usedUserAssumptions = new HashSet<string>(StringComparer.Ordinal);
        switch (outcome) {
            case ProvenOutcome proven:
                record.Outcome = WorkerClaimOutcome.Proven; record.Reason = WorkerClaimReason.None;
                record.ProofCore = [.. proven.Core
                    .Select(justification =>
                        assumptionLabels.TryGetValue(justification, out var label) ? label : "hygienic")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static label => label, StringComparer.Ordinal)];
                foreach (var justification in proven.Core)
                    if (userAssumptionIds.TryGetValue(justification, out var id))
                        usedUserAssumptions.Add(id);
                break;
            case RefutedOutcome when usesSpecModeledCallResult:
                record.Outcome = WorkerClaimOutcome.Unknown;
                record.Reason = WorkerClaimReason.CounterexampleReplayFailed;
                break;
            case RefutedOutcome refuted:
                record.Outcome = WorkerClaimOutcome.Refuted; record.Reason = WorkerClaimReason.None;
                record.Model = CreateModel(refuted, variables);
                break;
            case UnknownOutcome unknown:
                record.Outcome = WorkerClaimOutcome.Unknown; record.Reason = MapAbstention(unknown.Reason);
                break;
            default:
                record.Outcome = WorkerClaimOutcome.Unknown; record.Reason = WorkerClaimReason.MalformedBackendResult;
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
        var record = CreateBaseRecord(target, contractOrdinal);
        record.Outcome = WorkerClaimOutcome.Unknown; record.Reason = reason;
        return record;
    }

    internal static ImmutableArray<WorkerClaimResult> Unknowns(
        CompilerCallablePreparation target, WorkerClaimReason reason) =>
        [.. target.Entry.ClaimIds.Select((_, index) => Unknown(target, index, reason))];

    internal static void AppendResourceLimit(ImmutableArray<WorkerClaimResult>.Builder records,
        CompilerCallablePreparation target, int start, int count) {
        for (var index = start; index < count; index++)
            records.Add(Unknown(target, index, WorkerClaimReason.ResourceLimit));
    }

    private static WorkerClaimResult CreateBaseRecord(CompilerCallablePreparation target, int contractOrdinal) =>
        new() {
            ClaimId = target.Entry.ClaimIds[contractOrdinal], Outcome = WorkerClaimOutcome.Unknown,
            Reason = WorkerClaimReason.InfrastructureFailure, Assumptions = [.. target.Entry.Assumptions]
        };

    private static WorkerModelValue[] CreateModel(
        RefutedOutcome outcome, IReadOnlyList<CompilerCanonicalVariable> variables) {
        var names = variables.ToDictionary(
            static variable => variable.Variable,
            static variable => variable.ModelLabel);
        return [.. outcome.Model.Assignments
            .Select(assignment => new WorkerModelValue {
                Variable = names.TryGetValue(assignment.Key, out var name) ? name :
                    "variable:" + assignment.Key.Value.ToString(CultureInfo.InvariantCulture),
                Kind = assignment.Value.Kind.ToString(), Value = FormatValue(assignment.Value)
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

    private static WorkerClaimReason MapAbstention(AbstentionReason reason) => reason switch {
            AbstentionReason.UnsupportedOperation => WorkerClaimReason.UnsupportedExpression,
            AbstentionReason.UnsupportedEncoding => WorkerClaimReason.UnsupportedExpression,
            AbstentionReason.ResourceLimit => WorkerClaimReason.ResourceLimit,
            AbstentionReason.Timeout => WorkerClaimReason.MethodTimeout,
            AbstentionReason.BackendUnavailable => WorkerClaimReason.BackendUnavailable,
            AbstentionReason.InfrastructureFailure => WorkerClaimReason.InfrastructureFailure,
            AbstentionReason.MalformedBackendResult => WorkerClaimReason.MalformedBackendResult,
            AbstentionReason.CounterexampleReplayFailed => WorkerClaimReason.CounterexampleReplayFailed,
            _ => WorkerClaimReason.UnsupportedExpression
        };
}
