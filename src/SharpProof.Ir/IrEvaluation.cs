namespace SharpProof.Ir;

internal enum IrValueKind
{
    Boolean = 0,
    Integer = 1,
    String = 2,
    Null = 3,
    Reference = 4,
    Sequence = 5
}

internal enum IrEvaluationStatus
{
    Value = 0,
    Unsupported = 1,
    Exception = 2
}

internal enum IrUnsupportedReason
{
    OpaqueTerm = 0,
    MissingVariable = 1,
    InvalidVariableValue = 2,
    UnsupportedCast = 3,
    UnsupportedOperation = 4
}

internal enum IrExceptionKind
{
    DivideByZero = 0,
    Overflow = 1,
    NullReference = 2,
    IndexOutOfRange = 3,
    InvalidCast = 4,
    Argument = 5,
    // Any exception of an unknown type, raised by an opaque call. Only a
    // catch of System.Exception or System.Object is known to catch it.
    Unknown = 6,
    // A `throw` statement's exception. Lowering dispatches it to handlers by
    // its static type; the site's description names that type.
    Explicit = 7,
    ArgumentOutOfRange = 8
}

internal enum IrProgramExecutionStatus
{
    Returned = 0,
    AssumptionViolated = 1,
    AssertionFailed = 2,
    Unsupported = 3,
    Exception = 4,
    StepLimit = 5
}

internal sealed partial class IrValue
{
    internal IrValue(IrTypeId type, IrValueKind kind, object? payload)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        (Type, Kind, Payload) =
            (type, kind, payload);
    }

    public IrTypeId Type { get; }

    public IrValueKind Kind { get; }

    internal object? Payload { get; }
}

internal sealed class IrUnsupportedInfo
{
    internal IrUnsupportedInfo(IrUnsupportedReason reason, string detail)
    {
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        (Reason, Detail) =
            (reason, detail);
    }

    public IrUnsupportedReason Reason { get; }

    public string Detail { get; }
}

internal sealed class IrExceptionInfo
{
    internal IrExceptionInfo(IrExceptionKind kind, string detail, OperationId? site = null)
    {
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        (Kind, Detail, Site) =
            (kind, detail, site);
    }

    public IrExceptionKind Kind { get; }

    public string Detail { get; }
    public OperationId? Site { get; }
}

internal sealed partial class IrEvaluationResult
{
    internal IrEvaluationResult(
        IrEvaluationStatus status,
        IrValue? value,
        IrUnsupportedInfo? unsupported,
        IrExceptionInfo? exception
    )
    {
        (Status, Value, Unsupported, Exception) =
            (status, value, unsupported, exception);
    }

    public IrEvaluationStatus Status { get; }

    public IrValue? Value { get; }

    public IrUnsupportedInfo? Unsupported { get; }

    public IrExceptionInfo? Exception { get; }
}

internal sealed partial class IrProgramExecutionResult
{
    internal IrProgramExecutionResult(
        IrProgramExecutionStatus status,
        IrValue? returnValue,
        IrInstruction? instruction,
        IrUnsupportedInfo? unsupported,
        IrExceptionInfo? exception,
        ImmutableDictionary<IrVarId, IrValue> values,
        int steps,
        bool consumedApproximation = false,
        ImmutableHashSet<IrVarId>? approximationVariables = null,
        IrHeap? heap = null
    )
    {
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        (Status, ReturnValue, Instruction, Unsupported, Exception, Values, Steps) =
            (status, returnValue, instruction, unsupported, exception, values, steps);
        ConsumedApproximation = consumedApproximation;
        ApproximationVariables = approximationVariables ?? ImmutableHashSet<IrVarId>.Empty;
        Heap = heap;
    }

    // What the execution stored to arrays and objects, by identity.
    internal IrHeap? Heap { get; }

    public IrProgramExecutionStatus Status { get; }

    public IrValue? ReturnValue { get; }

    public IrInstruction? Instruction { get; }

    public IrUnsupportedInfo? Unsupported { get; }

    public IrExceptionInfo? Exception { get; }

    public ImmutableDictionary<IrVarId, IrValue> Values { get; }

    public int Steps { get; }
    public bool ConsumedApproximation { get; }
    public ImmutableHashSet<IrVarId> ApproximationVariables { get; }
}
