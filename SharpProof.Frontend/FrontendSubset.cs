namespace SharpProof.Frontend;

public enum FrontendSubsetDecision {
    Exact,
    ClosedAbstention
}

public enum FrontendAbstention {
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

public readonly struct FrontendSubsetClassification {
    public FrontendSubsetClassification(
        FrontendSubsetDecision decision,
        FrontendAbstention abstention) {
        if (decision == FrontendSubsetDecision.Exact &&
            abstention != FrontendAbstention.None)
            throw new ArgumentException(
                "An exact classification cannot carry an abstention.",
                nameof(abstention));
        if (decision == FrontendSubsetDecision.ClosedAbstention &&
            abstention == FrontendAbstention.None)
            throw new ArgumentException(
                "A closed abstention requires a reason.",
                nameof(abstention));
        Decision = decision;
        Abstention = abstention;
    }

    public FrontendSubsetDecision Decision { get; }
    public FrontendAbstention Abstention { get; }
    public bool IsExact => Decision == FrontendSubsetDecision.Exact;

    public static FrontendSubsetClassification Exact { get; } =
        new(FrontendSubsetDecision.Exact, FrontendAbstention.None);

    public static FrontendSubsetClassification Abstain(FrontendAbstention reason) =>
        new(FrontendSubsetDecision.ClosedAbstention, reason);
}

public readonly struct FrontendVariableBinding(ISymbol symbol, IrVarId variable) {
    public ISymbol Symbol { get; } =
        symbol ?? throw new ArgumentNullException(nameof(symbol));
    public IrVarId Variable { get; } = variable;
}

public sealed class FrontendLoweringResult {
    internal FrontendLoweringResult(
        IrTerm term,
        FrontendSubsetClassification classification,
        ImmutableArray<FrontendVariableBinding> variables) {
        Term = term ?? throw new ArgumentNullException(nameof(term));
        Classification = classification;
        Variables = variables;
    }

    public IrTerm Term { get; }
    public FrontendSubsetClassification Classification { get; }
    public ImmutableArray<FrontendVariableBinding> Variables { get; }
    public bool IsExact => Classification.IsExact;
}

public readonly struct FrontendProgramAbstention {
    public FrontendProgramAbstention(
        OperationId operation,
        FrontendAbstention reason) {
        if (operation.IsDefault)
            throw new ArgumentException(
                "A program abstention requires an operation identity.",
                nameof(operation));
        if (reason == FrontendAbstention.None)
            throw new ArgumentException(
                "A program abstention requires a reason.",
                nameof(reason));
        Operation = operation;
        Reason = reason;
    }

    public OperationId Operation { get; }
    public FrontendAbstention Reason { get; }
}

public sealed class FrontendProgramLoweringResult {
    internal FrontendProgramLoweringResult(
        IrProgram program,
        FrontendSubsetClassification classification,
        ImmutableArray<FrontendVariableBinding> variables,
        ImmutableArray<IrVarId> captures,
        ImmutableArray<FrontendProgramAbstention> abstentions) {
        Program = program ?? throw new ArgumentNullException(nameof(program));
        Classification = classification;
        Variables = variables;
        Captures = captures;
        Abstentions = abstentions;
    }

    public IrProgram Program { get; }
    public FrontendSubsetClassification Classification { get; }
    public ImmutableArray<FrontendVariableBinding> Variables { get; }
    public ImmutableArray<IrVarId> Captures { get; }
    public ImmutableArray<FrontendProgramAbstention> Abstentions { get; }
    public bool IsExact => Classification.IsExact;
}
