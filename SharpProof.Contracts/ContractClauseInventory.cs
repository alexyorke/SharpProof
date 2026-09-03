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
    public bool HasPlacementErrors =>
        Clauses.Any(static clause =>
            clause.Placement is not (
                ContractClausePlacement.ValidPrologue or
                ContractClausePlacement.NestedCallable));
}
