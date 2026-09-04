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
        CallableEntryFeasibility entryFeasibility,
        CancellationToken cancellationToken)
    {
        if (!WorkerProtocolJson.HasValidEffectCertainty(
                evidence.Outcome, evidence.Reason, evidence.Certainty))
        {
            throw new InvalidDataException(
                "Compiler effect-claim evidence has an unsupported result tuple.");
        }
        cancellationToken.ThrowIfCancellationRequested();

        WorkerClaimResult CreateResult(
            WorkerClaimOutcome outcome,
            WorkerClaimReason reason,
            WorkerEffectEvidenceCertainty certainty,
            bool projectAssumptions = true)
        {
            return CallableClaimResultAssembler.Create(
                target,
                evidence.ClaimId,
                outcome,
                reason,
                certainty,
                projectAssumptions);
        }

        if (evidence.Outcome == WorkerClaimOutcome.Unknown &&
            evidence.Reason == WorkerClaimReason.UnsupportedContract)
        {
            return CreateResult(
                evidence.Outcome,
                evidence.Reason,
                evidence.Certainty);
        }

        if (entryFeasibility.IsUnknown)
        {
            return CreateResult(
                WorkerClaimOutcome.Unknown,
                entryFeasibility.Reason,
                WorkerEffectEvidenceCertainty.Unavailable);
        }

        if (entryFeasibility.IsContradictory)
        {
            return CallableClaimResultAssembler.Contradictory(
                target,
                evidence.ClaimId,
                WorkerEffectEvidenceCertainty.VacuousEntry,
                entryFeasibility.ProofCore,
                entryFeasibility.UsedAssumptionIds);
        }

        if (evidence.Outcome == WorkerClaimOutcome.Refuted)
        {
            var replayed = EffectCounterexampleReplayer.Replay(
                target,
                evidence,
                cancellationToken);
            if (replayed == null)
            {
                return CreateResult(
                    WorkerClaimOutcome.Unknown,
                    WorkerClaimReason.CounterexampleReplayFailed,
                    WorkerEffectEvidenceCertainty.Unavailable);
            }

            var refuted = CreateResult(
                WorkerClaimOutcome.Refuted,
                WorkerClaimReason.None,
                WorkerEffectEvidenceCertainty.DefiniteViolation);
            refuted.EffectWitness = replayed;
            return refuted;
        }

        var result = CreateResult(
            evidence.Outcome,
            evidence.Reason,
            evidence.Certainty,
            projectAssumptions: evidence.Certainty !=
                WorkerEffectEvidenceCertainty.TrustedCompleteBoundary);
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
