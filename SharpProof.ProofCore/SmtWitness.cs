namespace SharpProof.ProofCore.Smt;

public enum SmtWitnessStatus
{
    None,
    Exact,
    Approximate,
    Unsupported
}

public sealed record SmtModelAssignment(
    string Name,
    SmtValueKind Kind,
    string Value,
    bool? BooleanValue = null,
    long? IntegerValue = null,
    string? StringValue = null,
    bool? IsNull = null,
    SmtWitnessStatus Status = SmtWitnessStatus.Exact);

public sealed record SmtSatisfyingWitness(
    SmtWitnessStatus Status,
    string Reason,
    IReadOnlyList<SmtModelAssignment> Assignments)
{
    public bool IsAvailable => Status is SmtWitnessStatus.Exact or SmtWitnessStatus.Approximate;

    internal static SmtSatisfyingWitness None(string reason)
    {
        return Absent(SmtWitnessStatus.None, reason);
    }

    internal static SmtSatisfyingWitness Unsupported(string reason)
    {
        return Absent(SmtWitnessStatus.Unsupported, reason);
    }

    private static SmtSatisfyingWitness Absent(SmtWitnessStatus status, string reason)
    {
        return new SmtSatisfyingWitness(status, reason, Array.Empty<SmtModelAssignment>());
    }
}

public sealed record SmtFeasibilityResult(
    Feasibility Feasibility,
    SmtSatisfyingWitness Witness);

public sealed record SmtPathAndImpurityCheckResult(
    SmtFeasibilityResult Path,
    SmtFeasibilityResult Impurity);
