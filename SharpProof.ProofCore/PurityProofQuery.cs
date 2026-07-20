namespace SharpProof.ProofCore.Purity;

internal enum PurityHazardKind {
    BranchReachability,
    ImpureCallReachability,
    StaticCacheRead,
    FreshOwnedObjectWrite,
    FreshOwnedArrayWrite,
    CallerVisibleMemoryWrite,
    NullDereference,
    DivideByZero
}

internal enum PurityEffectVisibility {
    CallerVisible,
    InternalOnly
}

internal sealed record PurityHazard(
    PurityHazardKind Kind,
    SmtFormula TriggerCondition,
    PurityEffectVisibility Visibility = PurityEffectVisibility.CallerVisible);

internal sealed record PurityProofQuery(
    IReadOnlyList<SmtFormula> PathConditions,
    PurityHazard Hazard);
