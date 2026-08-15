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
            CallableEntryFeasibility.Feasible,
            CancellationToken.None);
    }

    internal static WorkerClaimResult Assemble(
        CompilerCallablePreparation target,
        CompilerEffectClaimArtifact evidence,
        CallableEntryFeasibility entryFeasibility)
    {
        return Assemble(
            target,
            evidence,
            entryFeasibility,
            CancellationToken.None);
    }

    internal static WorkerClaimResult Assemble(
        CompilerCallablePreparation target,
        CompilerEffectClaimArtifact evidence,
        CallableEntryFeasibility entryFeasibility,
        CancellationToken cancellationToken)
    {
        if (!WorkerProtocolJson.HasValidEffectCertainty(
                evidence.Outcome, evidence.Reason, evidence.Certainty))
        {
            throw new InvalidDataException(
                "Compiler effect-claim evidence has an unsupported result tuple.");
        }

        if (evidence.Outcome == WorkerClaimOutcome.Unknown &&
            evidence.Reason == WorkerClaimReason.UnsupportedContract)
        {
            return CallableClaimResultAssembler.Create(
                target,
                evidence.ClaimId,
                evidence.Outcome,
                evidence.Reason,
                evidence.Certainty);
        }

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
            var replayed = EffectCounterexampleReplayer.Replay(
                target,
                evidence,
                cancellationToken);
            if (replayed == null)
            {
                return CallableClaimResultAssembler.Create(
                    target,
                    evidence.ClaimId,
                    WorkerClaimOutcome.Unknown,
                    WorkerClaimReason.CounterexampleReplayFailed,
                    WorkerEffectEvidenceCertainty.Unavailable);
            }

            var refuted = CallableClaimResultAssembler.Create(
                target,
                evidence.ClaimId,
                WorkerClaimOutcome.Refuted,
                WorkerClaimReason.None,
                WorkerEffectEvidenceCertainty.DefiniteViolation);
            refuted.EffectWitness = replayed;
            return refuted;
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
        if (evidence.Certainty == WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)
        {
            result.Assumptions = CallableClaimResultAssembler.MarkAssumptionsUsed(
                target,
                target.Entry.Assumptions
                    .Where(static assumption =>
                        assumption.Kind == WorkerAssumptionKind.TrustedBoundary)
                    .Select(static assumption => assumption.Id)
                    .ToHashSet(StringComparer.Ordinal));
        }
        return result;
    }
}
