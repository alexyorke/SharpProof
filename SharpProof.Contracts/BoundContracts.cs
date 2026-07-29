namespace SharpProof.Contracts;

public enum BoundContractKind
{
    Requires,
    Ensures,
    Assume
}

public enum BoundContractEvidence
{
    CompilerBoundInvocation,
    ClosedAttribute,
    Companion
}

public enum BoundContractVariableRole
{
    Receiver,
    Parameter,
    Result,
    PreState
}

public enum ContractBindingFailure
{
    None,
    ContractApiUnavailable,
    UnsupportedExpression,
    NonBooleanCondition,
    ResultOutsideEnsures,
    OldOutsideEnsures,
    NestedOld,
    InvalidIntrinsicSignature,
    MissingCompanion,
    AmbiguousCompanion,
    CompanionSignatureMismatch,
    CompanionBodyUnavailable,
    InvalidClosedAttribute,
    InvalidClausePlacement,
    UnsupportedTarget
}

public sealed class BoundContractClause
{
    internal BoundContractClause(
        BoundContractKind kind,
        IrTerm condition,
        OperationId sourceOperation,
        BoundContractEvidence evidence)
    {
        Kind = kind;
        Condition = condition;
        SourceOperation = sourceOperation;
        Evidence = evidence;
    }

    public BoundContractKind Kind
    {
        get;
    }
    public IrTerm Condition
    {
        get;
    }
    public OperationId SourceOperation
    {
        get;
    }
    public BoundContractEvidence Evidence
    {
        get;
    }
    public bool IsAssumptionEvidence => Kind == BoundContractKind.Assume;
}

public sealed class BoundContractVariable
{
    internal BoundContractVariable(
        ISymbol? symbol,
        BoundContractVariableRole role,
        int ordinal,
        IrVarId variable,
        IrVarId? currentStateVariable)
    {
        Symbol = symbol;
        Role = role;
        Ordinal = ordinal;
        Variable = variable;
        CurrentStateVariable = currentStateVariable;
    }

    public ISymbol? Symbol
    {
        get;
    }
    public BoundContractVariableRole Role
    {
        get;
    }
    public int Ordinal
    {
        get;
    }
    public IrVarId Variable
    {
        get;
    }
    public IrVarId? CurrentStateVariable
    {
        get;
    }
}

public sealed class BoundMethodContracts
{
    internal BoundMethodContracts(
        IMethodSymbol target,
        IMethodSymbol source,
        ImmutableArray<BoundContractClause> clauses,
        ImmutableArray<BoundContractVariable> variables,
        bool usesCompanion)
    {
        Target = target;
        Source = source;
        Clauses = clauses;
        Variables = variables;
        UsesCompanion = usesCompanion;
    }

    public IMethodSymbol Target
    {
        get;
    }
    public IMethodSymbol Source
    {
        get;
    }
    public ImmutableArray<BoundContractClause> Clauses
    {
        get;
    }
    public ImmutableArray<BoundContractVariable> Variables
    {
        get;
    }
    public bool UsesCompanion
    {
        get;
    }
}

public sealed class ContractBindingResult
{
    private ContractBindingResult(
        BoundMethodContracts? contracts,
        ContractBindingFailure failure)
    {
        Contracts = contracts;
        Failure = failure;
    }

    public BoundMethodContracts? Contracts
    {
        get;
    }
    public ContractBindingFailure Failure
    {
        get;
    }
    public bool IsSuccess => Failure == ContractBindingFailure.None;

    internal static ContractBindingResult Success(BoundMethodContracts contracts)
    {
        return new(contracts, ContractBindingFailure.None);
    }

    internal static ContractBindingResult Fail(ContractBindingFailure failure)
    {
        return new(null, failure);
    }
}
