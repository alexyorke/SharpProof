namespace SharpProof.Contracts;

public enum ContractClausePlacement
{
    ValidPrologue,
    Conditional,
    NestedCallable,
    Unreachable,
    Late,
    Misplaced
}

public sealed partial class ContractClauseOccurrence
{
    public Location Location => Invocation.Syntax.GetLocation();
    public bool IsValid => Placement == ContractClausePlacement.ValidPrologue;
}

public sealed partial class ContractClauseInventory
{
    private readonly int _hasValidClause;

    internal bool HasValidClause =>
        _hasValidClause >= 0
            ? _hasValidClause != 0
            : Clauses.Any(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.IsValid));

    private static int ComputeHasValidClause(
        ImmutableArray<ContractClauseOccurrence> clauses /* SPINT001: all values valid - default arrays and null entries defer the original validation failure until the property is read */)
    {
        if (clauses.IsDefault)
        {
            return -1;
        }
        foreach (var clause in clauses)
        {
            if (clause is null)
            {
                return -1;
            }
            if (clause.IsValid)
            {
                return 1;
            }
        }
        return 0;
    }

    public bool HasPlacementErrors =>
        Clauses.Any(static clause =>
            clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Placement is not (
                ContractClausePlacement.ValidPrologue or
                ContractClausePlacement.NestedCallable)));
}
