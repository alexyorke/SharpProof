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

public sealed class ContractClauseOccurrence
{
    internal ContractClauseOccurrence(
        BoundContractKind kind,
        ContractClausePlacement placement,
        int ordinal,
        int sourceOrdinal,
        IInvocationOperation invocation)
    {
        Kind = kind;
        Placement = placement;
        Ordinal = ordinal;
        SourceOrdinal = sourceOrdinal;
        Invocation = invocation;
    }

    public BoundContractKind Kind
    {
        get;
    }
    public ContractClausePlacement Placement
    {
        get;
    }
    public int Ordinal
    {
        get;
    }
    public int SourceOrdinal
    {
        get;
    }
    public IInvocationOperation Invocation
    {
        get;
    }
    public Location Location => Invocation.Syntax.GetLocation();
    public bool IsValid => Placement == ContractClausePlacement.ValidPrologue;
}

public sealed class ContractClauseInventory
{
    internal ContractClauseInventory(
        IMethodSymbol callable,
        bool contractApiAvailable,
        IOperation? implementationBody,
        ImmutableArray<ContractClauseOccurrence> clauses)
    {
        Callable = callable;
        ContractApiAvailable = contractApiAvailable;
        ImplementationBody = implementationBody;
        Clauses = clauses;
    }

    public IMethodSymbol Callable
    {
        get;
    }
    public bool ContractApiAvailable
    {
        get;
    }
    public IOperation? ImplementationBody
    {
        get;
    }
    public ImmutableArray<ContractClauseOccurrence> Clauses
    {
        get;
    }
    public bool HasPlacementErrors =>
        Clauses.Any(static clause =>
            !clause.IsValid &&
            clause.Placement != ContractClausePlacement.NestedCallable);
}
