namespace SharpProof.Frontend;

public enum FrontendSubsetDecision
{
    Exact,
    ClosedAbstention
}

public enum FrontendAbstention
{
    None,
    UnsupportedOperationKind,
    UnsupportedType,
    ErrorOperation,
    InvalidOperation,
    UserDefinedOperator,
    LiftedOperator,
    UncheckedOverflowSemantics,
    ConversionMayChangeValue,
    UnsupportedMemberAccess,
    UnsupportedInvocationShape,
    UnsupportedControlFlow,
    UnsupportedStatement,
    UnsupportedMutation,
    UnknownOperationKind
}

public readonly struct FrontendSubsetClassification
{
    public FrontendSubsetClassification(
        FrontendSubsetDecision decision,
        FrontendAbstention abstention)
    {
        if (decision == FrontendSubsetDecision.Exact &&
            abstention != FrontendAbstention.None)
        {
            throw new ArgumentException(
                "An exact classification cannot carry an abstention.",
                nameof(abstention));
        }

        if (decision == FrontendSubsetDecision.ClosedAbstention &&
            abstention == FrontendAbstention.None)
        {
            throw new ArgumentException(
                "A closed abstention requires a reason.",
                nameof(abstention));
        }

        Decision = decision;
        Abstention = abstention;
    }

    public FrontendSubsetDecision Decision
    {
        get;
    }
    public FrontendAbstention Abstention
    {
        get;
    }
    public bool IsExact => Decision == FrontendSubsetDecision.Exact;

    public static FrontendSubsetClassification Exact
    {
        get;
    } =
        new(FrontendSubsetDecision.Exact, FrontendAbstention.None);

    public static FrontendSubsetClassification Abstain(FrontendAbstention reason)
    {
        return new(FrontendSubsetDecision.ClosedAbstention, reason);
    }
}

public readonly struct FrontendVariableBinding(ISymbol symbol, IrVarId variable)
{
    public ISymbol Symbol
    {
        get;
    } = ArgumentNullGuard.NotNull(symbol, nameof(symbol));
    public IrVarId Variable { get; } = variable;
}

public sealed partial class FrontendLoweringResult
{
    internal FrontendLoweringResult(
        IrTerm term,
        FrontendSubsetClassification classification,
        ImmutableArray<FrontendVariableBinding> variables)
        : this(
            ArgumentNullGuard.NotNull(term, nameof(term)),
            classification,
            variables,
            default)
    {
    }
    public bool IsExact => Classification.IsExact;
}

public readonly struct FrontendProgramAbstention
{
    public FrontendProgramAbstention(
        OperationId operation,
        FrontendAbstention reason)
    {
        if (operation.IsDefault)
        {
            throw new ArgumentException(
                "A program abstention requires an operation identity.",
                nameof(operation));
        }

        if (reason == FrontendAbstention.None)
        {
            throw new ArgumentException(
                "A program abstention requires a reason.",
                nameof(reason));
        }

        Operation = operation;
        Reason = reason;
    }

    public OperationId Operation
    {
        get;
    }
    public FrontendAbstention Reason
    {
        get;
    }
}

public sealed partial class FrontendProgramLoweringResult
{
    internal FrontendProgramLoweringResult(
        IrProgram program,
        FrontendSubsetClassification classification,
        ImmutableArray<FrontendVariableBinding> variables,
        ImmutableArray<IrVarId> captures,
        ImmutableArray<FrontendProgramAbstention> abstentions)
        : this(
            ArgumentNullGuard.NotNull(program, nameof(program)),
            classification,
            variables,
            captures,
            abstentions,
            default)
    {
    }
    public bool IsExact => Classification.IsExact;
}
