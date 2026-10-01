namespace SharpProof.Frontend;

public enum FrontendSubsetDecision
{
    Unspecified = 0,
    Exact = 1,
    ClosedAbstention = 2
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
    UnknownOperationKind,
    ExpressionDepthLimit
}

internal static class FrontendAbstentionValidation
{
    internal static FrontendAbstention RequireDefined(
        FrontendAbstention value,
        string parameterName)
    {
        var isDefined = value switch
        {
            FrontendAbstention.None or
            FrontendAbstention.UnsupportedOperationKind or
            FrontendAbstention.UnsupportedType or
            FrontendAbstention.ErrorOperation or
            FrontendAbstention.InvalidOperation or
            FrontendAbstention.UserDefinedOperator or
            FrontendAbstention.LiftedOperator or
            FrontendAbstention.UncheckedOverflowSemantics or
            FrontendAbstention.ConversionMayChangeValue or
            FrontendAbstention.UnsupportedMemberAccess or
            FrontendAbstention.UnsupportedInvocationShape or
            FrontendAbstention.UnsupportedControlFlow or
            FrontendAbstention.UnsupportedStatement or
            FrontendAbstention.UnsupportedMutation or
            FrontendAbstention.UnknownOperationKind or
            FrontendAbstention.ExpressionDepthLimit => true,
            _ => false
        };
        if (!isDefined)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }
}

public readonly struct FrontendSubsetClassification
{
    public FrontendSubsetClassification(
        FrontendSubsetDecision decision,
        FrontendAbstention abstention)
    {
        FrontendAbstentionValidation.RequireDefined(
            abstention,
            nameof(abstention));

        var valid = decision switch
        {
            FrontendSubsetDecision.Exact =>
                abstention == FrontendAbstention.None,
            FrontendSubsetDecision.ClosedAbstention =>
                abstention != FrontendAbstention.None,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException(
                "The subset decision and abstention must form a valid classification.",
                nameof(decision));
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

        FrontendAbstentionValidation.RequireDefined(reason, nameof(reason));

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
    internal bool ConstructionLimitExceeded { get; set; }
    internal object? TotalOrigin { get; }
    internal FrontendProgramLoweringResult(
        IrProgram program, FrontendSubsetClassification classification,
        ImmutableArray<FrontendVariableBinding> variables, ImmutableArray<IrVarId> captures,
        ImmutableArray<FrontendProgramAbstention> abstentions, object totalOrigin)
        : this(program, classification, variables, captures, abstentions)
    {
        TotalOrigin = ArgumentNullGuard.NotNull(totalOrigin, nameof(totalOrigin));
    }

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
