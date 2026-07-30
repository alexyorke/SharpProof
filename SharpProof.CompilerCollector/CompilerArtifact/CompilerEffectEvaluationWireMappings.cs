using SharpProof.Analyzer;

namespace SharpProof.CompilerArtifact;

internal static class CompilerEffectEvaluationWireMappings
{
    internal static WorkerEffectContractKind ToWorker(
        EffectEvaluationContractKind value)
    {
        return value switch
        {
            EffectEvaluationContractKind.EnforcePure =>
                WorkerEffectContractKind.EnforcePure,
            EffectEvaluationContractKind.ZeroAllocations =>
                WorkerEffectContractKind.ZeroAllocations,
            EffectEvaluationContractKind.AllowedCapabilities =>
                WorkerEffectContractKind.AllowedCapabilities,
            EffectEvaluationContractKind.DoesNotThrow =>
                WorkerEffectContractKind.DoesNotThrow,
            EffectEvaluationContractKind.AllowedExceptions =>
                WorkerEffectContractKind.AllowedExceptions,
            EffectEvaluationContractKind.EffectContract =>
                WorkerEffectContractKind.EffectContract,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    internal static WorkerClaimOutcome ToWorker(
        EffectEvaluationOutcome value)
    {
        return value switch
        {
            EffectEvaluationOutcome.Proven => WorkerClaimOutcome.Proven,
            EffectEvaluationOutcome.Refuted => WorkerClaimOutcome.Refuted,
            EffectEvaluationOutcome.Unknown => WorkerClaimOutcome.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    internal static WorkerClaimReason ToWorker(
        EffectEvaluationReason value)
    {
        return value switch
        {
            EffectEvaluationReason.None => WorkerClaimReason.None,
            EffectEvaluationReason.UnsupportedContract =>
                WorkerClaimReason.UnsupportedContract,
            EffectEvaluationReason.EffectContractNotEstablished =>
                WorkerClaimReason.EffectContractNotEstablished,
            EffectEvaluationReason.EffectSummaryIncomplete =>
                WorkerClaimReason.EffectSummaryIncomplete,
            EffectEvaluationReason.ResourceLimit =>
                WorkerClaimReason.ResourceLimit,
            EffectEvaluationReason.UnsupportedBody =>
                WorkerClaimReason.UnsupportedBody,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    internal static WorkerEffectEvidenceCertainty ToWorker(
        EffectEvaluationCertainty value)
    {
        return value switch
        {
            EffectEvaluationCertainty.IncompleteMayEffectSummary =>
                WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary,
            EffectEvaluationCertainty.CompleteMayEffectSummary =>
                WorkerEffectEvidenceCertainty.CompleteMayEffectSummary,
            EffectEvaluationCertainty.TrustedCompleteBoundary =>
                WorkerEffectEvidenceCertainty.TrustedCompleteBoundary,
            EffectEvaluationCertainty.DefiniteViolation =>
                WorkerEffectEvidenceCertainty.DefiniteViolation,
            EffectEvaluationCertainty.Unavailable =>
                WorkerEffectEvidenceCertainty.Unavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }
}
