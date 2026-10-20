namespace SharpProof.Frontend;

internal enum FrontendSubsetDecision
{
    Unspecified = 0,
    Exact = 1,
    ClosedAbstention = 2
}

internal enum FrontendAbstention
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
        parameterName = RepositoryParameterGuard.NotNull(parameterName, nameof(parameterName));
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

internal readonly struct FrontendSubsetClassification
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

internal readonly struct FrontendVariableBinding(ISymbol symbol, IrVarId variable)
{
    public ISymbol Symbol
    {
        get;
    } = ArgumentNullGuard.NotNull(symbol, nameof(symbol));
    public IrVarId Variable { get; } = (variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : variable);
}

internal sealed partial class FrontendLoweringResult
{
    internal FrontendLoweringResult(
        IrTerm term,
        FrontendSubsetClassification classification /* SPINT001: all values valid - every classification, including default Exact, is valid */,
        ImmutableArray<FrontendVariableBinding> variables)
        : this(
            ArgumentNullGuard.NotNull(term, nameof(term)),
            classification,
            variables.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(variables)) : variables,
            default)
    {
    }
    public bool IsExact => Classification.IsExact;
}

internal readonly struct FrontendProgramAbstention
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

internal sealed partial class FrontendProgramLoweringResult
{
    internal bool SourceInitializationEffectsComplete { get; init; } = true;
    internal bool ConstructionLimitExceeded { get; init; }
    internal bool IsShadowCallSkeleton { get; init; }
    internal ImmutableDictionary<IrAssignInstruction, TotalCallPrecondition> CallPreconditions { get; init; } =
        ImmutableDictionary<IrAssignInstruction, TotalCallPrecondition>.Empty;
    internal ImmutableDictionary<IrCallInstruction, IMethodSymbol> PreservedSourceCalls { get; init; } =
        ImmutableDictionary<IrCallInstruction, IMethodSymbol>.Empty;
    internal object? TotalOrigin { get; }
    internal FrontendProgramLoweringResult(
        IrProgram program, FrontendSubsetClassification classification /* SPINT001: all values valid - every classification, including default Exact, is valid */,
        ImmutableArray<FrontendVariableBinding> variables, ImmutableArray<IrVarId> captures,
        ImmutableArray<FrontendProgramAbstention> abstentions, object totalOrigin)
        : this(program, classification, variables, captures, abstentions)
    {



        RepositoryParameterGuard.RequireInitialized(abstentions.IsDefault, nameof(abstentions), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(captures.IsDefault, nameof(captures), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(variables.IsDefault, nameof(variables), "Array must be initialized.");
        program = RepositoryParameterGuard.NotNull(program, nameof(program));
        TotalOrigin = ArgumentNullGuard.NotNull(totalOrigin, nameof(totalOrigin));
    }

    internal FrontendProgramLoweringResult(
        IrProgram program,
        FrontendSubsetClassification classification /* SPINT001: all values valid - every classification, including default Exact, is valid */,
        ImmutableArray<FrontendVariableBinding> variables,
        ImmutableArray<IrVarId> captures,
        ImmutableArray<FrontendProgramAbstention> abstentions)
        : this(
            ArgumentNullGuard.NotNull(program, nameof(program)),
            classification,
            variables.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(variables)) : variables,
            captures.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(captures)) : captures,
            abstentions.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(abstentions)) : abstentions,
            default)
    {
    }
    public bool IsExact => Classification.IsExact && !IsShadowCallSkeleton;
}
