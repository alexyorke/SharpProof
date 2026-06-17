using SearchLib.Smt;

namespace SearchLib.Purity
{
    public enum PurityHazardKind
    {
        BranchReachability,
        ImpureCallReachability,
        NullDereference,
        DivideByZero,
    }

    public enum PurityEffectVisibility
    {
        CallerVisible,
        InternalOnly,
    }

    public sealed record PurityHazard(
        PurityHazardKind Kind,
        SmtFormula TriggerCondition,
        PurityEffectVisibility Visibility = PurityEffectVisibility.CallerVisible);

    public sealed record PurityProofQuery(
        IReadOnlyList<SmtFormula> PathConditions,
        PurityHazard Hazard);
}
