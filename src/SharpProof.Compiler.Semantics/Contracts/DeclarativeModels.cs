#nullable enable
namespace SharpProof.Contracts;

internal sealed partial class ContractClauseOccurrence
{
    internal ContractClauseOccurrence(
        BoundContractKind kind,
        ContractClausePlacement placement,
        int ordinal,
        int sourceOrdinal,
        IInvocationOperation invocation
    )
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        Kind = kind;
        Placement = placement;
        Ordinal = ordinal;
        SourceOrdinal = sourceOrdinal;
        Invocation = invocation;
    }
    public BoundContractKind Kind { get; }
    public ContractClausePlacement Placement { get; }
    public int Ordinal { get; }
    public int SourceOrdinal { get; }
    public IInvocationOperation Invocation { get; }
}

internal sealed partial class ContractClauseInventory
{
    internal ContractClauseInventory(
        IMethodSymbol callable,
        bool contractApiAvailable,
        bool hasRejectedContractApiUsage,
        IOperation? implementationBody,
        ImmutableArray<ContractClauseOccurrence> clauses
    )
    {
        callable = RepositoryParameterGuard.NotNull(callable, nameof(callable));
        RepositoryParameterGuard.RequireInitialized(clauses.IsDefault, nameof(clauses), "Array must be initialized.");
        Callable = callable;
        ContractApiAvailable = contractApiAvailable;
        HasRejectedContractApiUsage = hasRejectedContractApiUsage;
        ImplementationBody = implementationBody;
        Clauses = clauses;
        _hasValidClause = ComputeHasValidClause(clauses);
    }
    public IMethodSymbol Callable { get; }
    public bool ContractApiAvailable { get; }
    public bool HasRejectedContractApiUsage { get; }
    public IOperation? ImplementationBody { get; }
    public ImmutableArray<ContractClauseOccurrence> Clauses { get; }
}

internal sealed partial class ContractBindingResult
{
    private ContractBindingResult(BoundMethodContracts? contracts, ContractBindingFailure failure)
    {
        Contracts = contracts;
        Failure = failure;
    }
    public BoundMethodContracts? Contracts { get; }
    public ContractBindingFailure Failure { get; }
}
