namespace SharpProof.Worker;

internal static class EffectWitnessReplayer {
    private const WorkerEffectSet PureForbidden =
        WorkerEffectSet.ReadsCapturedState |
        WorkerEffectSet.ReadsStaticState |
        WorkerEffectSet.ReadsAmbientState |
        WorkerEffectSet.WritesReceiverState |
        WorkerEffectSet.WritesArgumentState |
        WorkerEffectSet.WritesCapturedState |
        WorkerEffectSet.WritesStaticState |
        WorkerEffectSet.WritesAmbientState;

    internal static WorkerClaimResult Assemble(
        CompilerCallablePreparation target,
        CompilerEffectClaimArtifact evidence) {
        var replayed = evidence.Outcome != WorkerClaimOutcome.Refuted || Replays(evidence);
        var result = CallableClaimResultAssembler.Create(
            target, evidence.ClaimId,
            replayed ? evidence.Outcome : WorkerClaimOutcome.Unknown,
            replayed ? evidence.Reason : WorkerClaimReason.CounterexampleReplayFailed,
            replayed ? evidence.Certainty : WorkerEffectEvidenceCertainty.Unavailable);
        if (!replayed) return result;
        result.EffectWitness = Copy(evidence.Witness);
        result.ProofCore = evidence.Outcome == WorkerClaimOutcome.Proven
            ? ["compiler-effect:" + evidence.EvidenceSha256]
            : [];
        if (evidence.Outcome == WorkerClaimOutcome.Refuted)
            result.Model = [
                new WorkerModelValue {
                    Variable = "effect-witness",
                    Kind = evidence.Witness!.Kind,
                    Value = evidence.EvidenceSha256
                }
            ];
        return result;
    }

    private static bool Replays(CompilerEffectClaimArtifact evidence) {
        var witness = evidence.Witness;
        var constraint = evidence.Constraint;
        if (witness == null || constraint == null) return false;
        return evidence.ContractKind switch {
            WorkerEffectContractKind.EnforcePure =>
                (witness.Effects & PureForbidden) != 0 ||
                witness.Capabilities != WorkerEffectCapabilitySet.None,
            WorkerEffectContractKind.ZeroAllocations =>
                (witness.Effects & WorkerEffectSet.Allocates) != 0,
            WorkerEffectContractKind.AllowedCapabilities =>
                (witness.Capabilities &
                 ~constraint.AllowedCapabilities) != 0,
            WorkerEffectContractKind.DoesNotThrow =>
                (witness.Effects & WorkerEffectSet.Throws) != 0,
            WorkerEffectContractKind.AllowedExceptions =>
                HasDisallowedException(witness, constraint),
            WorkerEffectContractKind.EffectContract =>
                ViolatesEffectContract(witness, constraint),
            _ => false
        };
    }

    private static bool ViolatesEffectContract(
        WorkerEffectViolationWitness witness,
        CompilerEffectConstraintArtifact constraint) {
        var effects = witness.Effects;
        if ((effects & WorkerEffectSet.Allocates) != 0 &&
            (constraint.AllowedEffects & WorkerEffectSet.Throws) != 0)
            effects &= ~WorkerEffectSet.Allocates;
        return (effects & ~constraint.AllowedEffects) != 0 ||
               (witness.Capabilities &
                ~constraint.AllowedCapabilities) != 0 ||
               HasDisallowedException(witness, constraint);
    }

    private static bool HasDisallowedException(
        WorkerEffectViolationWitness witness,
        CompilerEffectConstraintArtifact constraint) =>
        witness.ExactExceptionTypeHierarchy.Length != 0 &&
        !witness.ExactExceptionTypeHierarchy.Intersect(
            constraint.AllowedExceptionTypes,
            StringComparer.Ordinal).Any();

    private static WorkerEffectViolationWitness? Copy(
        WorkerEffectViolationWitness? witness) =>
        witness == null
            ? null
            : new WorkerEffectViolationWitness {
                Kind = witness.Kind,
                Detail = witness.Detail,
                Effects = witness.Effects,
                Capabilities = witness.Capabilities,
                ExactExceptionTypeHierarchy = [
                    .. witness.ExactExceptionTypeHierarchy
                ],
                Location = new WorkerSourceLocation {
                    Path = witness.Location.Path,
                    Start = witness.Location.Start,
                    Length = witness.Location.Length,
                    Line = witness.Location.Line,
                    Column = witness.Location.Column
                }
            };
}
