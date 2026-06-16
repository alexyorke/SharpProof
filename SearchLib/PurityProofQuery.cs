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

    public sealed record PurityHazard(
        PurityHazardKind Kind,
        SmtFormula TriggerCondition);

    public sealed record PurityProofQuery(
        IReadOnlyList<SmtFormula> PathConditions,
        PurityHazard Hazard);
}
