namespace SharpProof.Worker;

internal static class EffectClaimResultAssembler
{
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

        // A trusted boundary needs no entry witness when entry-feasibility
        // lowering cannot represent a precondition.
        var preserveCompilerEvidence =
            entryFeasibility.IsUnknown &&
            entryFeasibility.Reason == WorkerClaimReason.UnsupportedExpression;
        if (entryFeasibility.IsUnknown && !preserveCompilerEvidence)
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
