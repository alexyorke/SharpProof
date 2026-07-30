namespace SharpProof.Worker;

internal static class EffectClaimResultAssembler
{
    internal static WorkerClaimResult Assemble(
        CompilerCallablePreparation target,
        CompilerEffectClaimArtifact evidence)
    {
        return Assemble(
            target,
            evidence,
            CallableEntryFeasibility.Feasible);
    }

    internal static WorkerClaimResult Assemble(
        CompilerCallablePreparation target,
        CompilerEffectClaimArtifact evidence,
        CallableEntryFeasibility entryFeasibility)
    {
        if (entryFeasibility.IsUnknown)
        {
            return CallableClaimResultAssembler.Create(
                target,
                evidence.ClaimId,
                WorkerClaimOutcome.Unknown,
                entryFeasibility.Reason,
                WorkerEffectEvidenceCertainty.Unavailable);
        }

        if (entryFeasibility.IsContradictory)
        {
            var vacuous = CallableClaimResultAssembler.Create(
                target,
                evidence.ClaimId,
                WorkerClaimOutcome.Proven,
                WorkerClaimReason.None,
                WorkerEffectEvidenceCertainty.VacuousEntry);
            vacuous.Vacuity =
                WorkerVacuityKind.ContradictoryPreconditions;
            vacuous.ProofCore = [.. entryFeasibility.ProofCore];
            vacuous.Assumptions =
                CallableClaimResultAssembler.MarkAssumptionsUsed(
                    target,
                    entryFeasibility.UsedAssumptionIds);
            return vacuous;
        }

        if (evidence.Outcome == WorkerClaimOutcome.Refuted)
        {
            return CallableClaimResultAssembler.Create(
                target,
                evidence.ClaimId,
                WorkerClaimOutcome.Unknown,
                WorkerClaimReason.CounterexampleReplayFailed,
                WorkerEffectEvidenceCertainty.Unavailable);
        }

        var result = CallableClaimResultAssembler.Create(
            target,
            evidence.ClaimId,
            evidence.Outcome,
            evidence.Reason,
            evidence.Certainty);
        result.ProofCore = evidence.Outcome == WorkerClaimOutcome.Proven
            ? ["compiler-effect:" + evidence.EvidenceSha256]
            : [];
        return result;
    }
}
