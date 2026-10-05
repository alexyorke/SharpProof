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
    private int _hasValidClause = -1;

    internal bool HasValidClause
    {
        get
        {
            var cached = Volatile.Read(ref _hasValidClause);
            if (cached >= 0)
            {
                return cached != 0;
            }

            var computed = Clauses.Any(static clause => clause.IsValid)
                ? 1
                : 0;
            var published = Interlocked.CompareExchange(
                ref _hasValidClause,
                computed,
                comparand: -1);
            return (published >= 0 ? published : computed) != 0;
        }
    }

    public bool HasPlacementErrors =>
        Clauses.Any(static clause =>
            clause.Placement is not (
                ContractClausePlacement.ValidPrologue or
                ContractClausePlacement.NestedCallable));
}
