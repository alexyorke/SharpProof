namespace SharpProof.Analyzer;

internal enum EffectEvaluationContractKind
{
    EnforcePure,
    ZeroAllocations,
    AllowedCapabilities,
    DoesNotThrow,
    AllowedExceptions,
    EffectContract
}

internal enum EffectEvaluationOutcome
{
    Proven,
    Refuted,
    Unknown
}

internal enum EffectEvaluationReason
{
    None,
    UnsupportedContract,
    EffectContractNotEstablished,
    EffectSummaryIncomplete,
    ResourceLimit,
    UnsupportedBody
}

internal enum EffectEvaluationCertainty
{
    IncompleteMayEffectSummary,
    CompleteMayEffectSummary,
    TrustedCompleteBoundary,
    DefiniteViolation,
    Unavailable
}
