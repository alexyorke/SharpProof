using SearchLib.Smt;

namespace SearchLib.Purity
{
    public enum PurityHazardKind
    {
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
