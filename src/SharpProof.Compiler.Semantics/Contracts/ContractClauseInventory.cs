namespace SharpProof.Compiler.Semantics;

internal enum ContractClausePlacement
{
    ValidPrologue,
    Conditional,
    NestedCallable,
    Unreachable,
    Late,
    Misplaced
}

internal sealed partial class ContractClauseOccurrence
{
    public Location Location => Invocation.Syntax.GetLocation();
    public bool IsValid => Placement == ContractClausePlacement.ValidPrologue;
}

internal sealed partial class ContractClauseInventory
{
    private readonly int _hasValidClause;

    internal bool HasValidClause =>
        _hasValidClause >= 0
            ? _hasValidClause != 0
            // The deferred validation failure: a null entry is reported here.
            : Clauses.Any(static clause => clause is null ? throw new ArgumentNullException(nameof(clause)) : clause.IsValid);

    private static int ComputeHasValidClause(
        ImmutableArray<ContractClauseOccurrence> clauses)
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
            clause.Placement is not (
                ContractClausePlacement.ValidPrologue or
                ContractClausePlacement.NestedCallable));
}
