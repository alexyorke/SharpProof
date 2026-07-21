namespace SharpProof.ProofCore.Analysis;

internal enum AnalysisHazardKind {
    BranchReachability,
    EffectViolationReachability,
    StaticCacheRead,
    FreshOwnedObjectWrite,
    FreshOwnedArrayWrite,
    CallerVisibleMemoryWrite,
    NullDereference,
    DivideByZero
}

internal enum AnalysisEffectVisibility {
    CallerVisible,
    InternalOnly
}

internal sealed record AnalysisHazard(
    AnalysisHazardKind Kind,
    SmtFormula TriggerCondition,
    AnalysisEffectVisibility Visibility = AnalysisEffectVisibility.CallerVisible);

internal sealed record AnalysisProofQuery(
    IReadOnlyList<SmtFormula> PathConditions,
    AnalysisHazard Hazard);
