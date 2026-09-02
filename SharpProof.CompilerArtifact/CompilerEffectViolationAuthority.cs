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

        var unexpectedEffects =
            observed.Effects & ~evidence.Constraint.AllowedEffects;
        var unexpectedCapabilities =
            observed.Capabilities & ~evidence.Constraint.AllowedCapabilities;
        var forbiddenException = HasForbiddenException(
            evidence.Constraint,
            observed);

        return evidence.ContractKind switch
        {
            WorkerEffectContractKind.EnforcePure =>
                observed.Capabilities != WorkerEffectCapabilitySet.None ||
                (observed.Effects & ImpureState) != 0,
            WorkerEffectContractKind.ZeroAllocations =>
                (observed.Effects & WorkerEffectSet.Allocates) != 0,
            WorkerEffectContractKind.AllowedCapabilities =>
                unexpectedCapabilities != WorkerEffectCapabilitySet.None,
            WorkerEffectContractKind.DoesNotThrow =>
                (observed.Effects & WorkerEffectSet.Throws) != 0,
            WorkerEffectContractKind.AllowedExceptions => forbiddenException,
            WorkerEffectContractKind.EffectContract =>
                unexpectedEffects != WorkerEffectSet.None ||
                unexpectedCapabilities != WorkerEffectCapabilitySet.None ||
                forbiddenException,
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
