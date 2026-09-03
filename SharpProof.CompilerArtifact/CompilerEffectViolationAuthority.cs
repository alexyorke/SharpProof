using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilerEffectViolationAuthority
{
    private const WorkerEffectSet ImpureState =
        WorkerEffectSet.ReadsCapturedState |
        WorkerEffectSet.ReadsStaticState |
        WorkerEffectSet.ReadsAmbientState |
        WorkerEffectSet.WritesReceiverState |
        WorkerEffectSet.WritesArgumentState |
        WorkerEffectSet.WritesCapturedState |
        WorkerEffectSet.WritesStaticState |
        WorkerEffectSet.WritesAmbientState;

    internal static bool IsViolation(
        CompilerEffectClaimArtifact evidence,
        WorkerEffectViolationWitness? observed)
    {
        if (observed == null)
        {
            return false;
        }

        return evidence.ContractKind switch
        {
            WorkerEffectContractKind.EnforcePure =>
                observed.Capabilities != WorkerEffectCapabilitySet.None ||
                (observed.Effects & ImpureState) != 0,
            WorkerEffectContractKind.ZeroAllocations =>
                (observed.Effects & WorkerEffectSet.Allocates) != 0,
            WorkerEffectContractKind.AllowedCapabilities =>
                (observed.Capabilities & ~evidence.Constraint.AllowedCapabilities) !=
                WorkerEffectCapabilitySet.None,
            WorkerEffectContractKind.DoesNotThrow =>
                (observed.Effects & WorkerEffectSet.Throws) != 0,
            WorkerEffectContractKind.AllowedExceptions =>
                HasForbiddenException(evidence.Constraint, observed),
            WorkerEffectContractKind.EffectContract =>
                (observed.Effects & ~evidence.Constraint.AllowedEffects) !=
                    WorkerEffectSet.None ||
                (observed.Capabilities & ~evidence.Constraint.AllowedCapabilities) !=
                    WorkerEffectCapabilitySet.None ||
                HasForbiddenException(evidence.Constraint, observed),
            _ => false
        };
    }

    private static bool HasForbiddenException(
        CompilerEffectConstraintArtifact constraint,
        WorkerEffectViolationWitness observed)
    {
        return (observed.Effects & WorkerEffectSet.Throws) != 0 &&
            !observed.ExactExceptionTypeHierarchy.Any(type =>
                constraint.AllowedExceptionTypes.Contains(
                    type,
                    StringComparer.Ordinal));
    }
}
