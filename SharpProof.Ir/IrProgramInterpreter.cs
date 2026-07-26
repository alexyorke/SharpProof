namespace SharpProof.Ir;

public enum IrProgramExecutionStatus {
    Returned,
    AssumptionViolated,
    AssertionFailed,
    Unsupported,
    Exception,
    StepLimit
}

public sealed class IrProgramExecutionResult {
    internal IrProgramExecutionResult(
        IrProgramExecutionStatus status,
        IrValue? returnValue,
        IrInstruction? instruction,
        IrUnsupportedInfo? unsupported,
        IrExceptionInfo? exception,
        ImmutableDictionary<IrVarId, IrValue> values,
        int steps) {
        Status = status;
        ReturnValue = returnValue;
        Instruction = instruction;
        Unsupported = unsupported;
        Exception = exception;
        Values = values;
        Steps = steps;
    }

    public IrProgramExecutionStatus Status { get; }
    public IrValue? ReturnValue { get; }
    public IrInstruction? Instruction { get; }
    public IrUnsupportedInfo? Unsupported { get; }
    public IrExceptionInfo? Exception { get; }
    public ImmutableDictionary<IrVarId, IrValue> Values { get; }
    public int Steps { get; }

    public IrValue? GetCurrentValue(IrVarId variable) =>
        Values.TryGetValue(variable, out var value) ? value : null;
}

public sealed class IrProgramInterpreter(IrFactory factory) {
    private readonly IrFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly IrInterpreter _terms =
        new(factory ?? throw new ArgumentNullException(nameof(factory)));

    public IrProgramExecutionResult Execute(
        IrProgram program,
        IReadOnlyDictionary<IrVarId, IrValue>? initialValues = null,
        int maximumSteps = 10000) {
        if (program == null) throw new ArgumentNullException(nameof(program));
        if (!ReferenceEquals(program.Factory, _factory))
            throw new ArgumentException(
                "The program belongs to a different IR factory.",
                nameof(program));
        if (maximumSteps <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSteps));

        var values = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        if (initialValues != null) {
            foreach (var pair in initialValues) {
                var variable = _factory.GetVariableInfo(pair.Key);
                if (pair.Value == null || pair.Value.Type != variable.Type)
                    throw new ArgumentException(
                        "An initial value does not match its variable type.",
                        nameof(initialValues));
                values.Add(pair.Key, pair.Value);
            }
        }

        var current = program.Entry;
        var steps = 0;
        while (steps < maximumSteps) {
            var block = program.GetBlock(current);
            foreach (var instruction in block.Instructions) {
                if (++steps > maximumSteps)
                    return Result(
                        IrProgramExecutionStatus.StepLimit,
                        instruction,
                        values,
                        steps);
                switch (instruction) {
                    case IrAssignInstruction assign:
                        var assigned = _terms.Evaluate(assign.Value, values);
                        if (assigned.Status != IrEvaluationStatus.Value)
                            return FromEvaluation(
                                assigned,
                                assign,
                                values,
                                steps);
                        values[assign.Target] = assigned.Value!;
                        break;
                    case IrAssumeInstruction assume:
                        var assumed = _terms.Evaluate(assume.Condition, values);
                        if (assumed.Status != IrEvaluationStatus.Value)
                            return FromEvaluation(
                                assumed,
                                assume,
                                values,
                                steps);
                        if (!assumed.Value!.Boolean)
                            return Result(
                                IrProgramExecutionStatus.AssumptionViolated,
                                assume,
                                values,
                                steps);
                        break;
                    case IrAssertInstruction assertion:
                        var asserted = _terms.Evaluate(assertion.Condition, values);
                        if (asserted.Status != IrEvaluationStatus.Value)
                            return FromEvaluation(
                                asserted,
                                assertion,
                                values,
                                steps);
                        if (!asserted.Value!.Boolean)
                            return Result(
                                IrProgramExecutionStatus.AssertionFailed,
                                assertion,
                                values,
                                steps);
                        break;
                    case IrBranchInstruction branch:
                        var condition = _terms.Evaluate(branch.Condition, values);
                        if (condition.Status != IrEvaluationStatus.Value)
                            return FromEvaluation(
                                condition,
                                branch,
                                values,
                                steps);
                        current = condition.Value!.Boolean
                            ? branch.WhenTrue
                            : branch.WhenFalse;
                        goto NextBlock;
                    case IrGotoInstruction go:
                        current = go.Target;
                        goto NextBlock;
                    case IrReturnInstruction returned:
                        if (returned.Value == null)
                            return Result(
                                IrProgramExecutionStatus.Returned,
                                returned,
                                values,
                                steps);
                        var returnValue = _terms.Evaluate(returned.Value, values);
                        if (returnValue.Status != IrEvaluationStatus.Value)
                            return FromEvaluation(
                                returnValue,
                                returned,
                                values,
                                steps);
                        return Result(
                            IrProgramExecutionStatus.Returned,
                            returned,
                            values,
                            steps,
                            returnValue.Value);
                    case IrHavocInstruction havoc:
                        if (havoc.HavocKind is
                            IrHavocKind.Variables or
                            IrHavocKind.VariablesAndMemory) {
                            foreach (var variable in havoc.Variables)
                                values.Remove(variable);
                        }
                        return Unsupported(
                            havoc,
                            values,
                            steps,
                            "Concrete execution stopped at nondeterministic havoc.");
                    case IrLoadInstruction load:
                        var loadOperands = EvaluateLocationOperands(
                            load.Location,
                            storedValue: null,
                            values);
                        if (loadOperands != null)
                            return FromEvaluation(
                                loadOperands,
                                load,
                                values,
                                steps);
                        return Unsupported(
                            load,
                            values,
                            steps,
                            "Concrete execution requires a memory host for load.");
                    case IrStoreInstruction store:
                        var storeOperands = EvaluateLocationOperands(
                            store.Location,
                            store.Value,
                            values);
                        if (storeOperands != null)
                            return FromEvaluation(
                                storeOperands,
                                store,
                                values,
                                steps);
                        return Unsupported(
                            store,
                            values,
                            steps,
                            "Concrete execution requires a memory host for store.");
                    case IrCallInstruction call:
                        var callOperands = EvaluateCallOperands(
                            call.Receiver,
                            call.Arguments,
                            storedValue: null,
                            values,
                            "The call receiver is null.");
                        if (callOperands != null)
                            return FromEvaluation(
                                callOperands,
                                call,
                                values,
                                steps);
                        return Unsupported(
                            call,
                            values,
                            steps,
                            "Concrete execution requires a call host.");
                    default:
                        return Unsupported(
                            instruction,
                            values,
                            steps,
                            "Unknown program instruction.");
                }
            }
            return Unsupported(
                block.Instructions[block.Instructions.Length - 1],
                values,
                steps,
                "A block completed without transferring control.");

        NextBlock:
            continue;
        }

        return Result(
            IrProgramExecutionStatus.StepLimit,
            null,
            values,
            steps);
    }

    private IrEvaluationResult? EvaluateLocationOperands(
        IrLocation location,
        IrTerm? storedValue,
        IReadOnlyDictionary<IrVarId, IrValue> values) {
        switch (location) {
            case IrMemberLocation member:
                return EvaluateCallOperands(
                    member.Receiver,
                    member.Arguments,
                    storedValue,
                    values,
                    "The member access receiver is null.");
            case IrSequenceLocation sequence:
                var sequenceResult = _terms.Evaluate(sequence.Sequence, values);
                if (sequenceResult.Status != IrEvaluationStatus.Value)
                    return sequenceResult;
                var indexResult = _terms.Evaluate(sequence.Index, values);
                if (indexResult.Status != IrEvaluationStatus.Value)
                    return indexResult;
                if (storedValue != null) {
                    var storedValueResult = _terms.Evaluate(storedValue, values);
                    if (storedValueResult.Status != IrEvaluationStatus.Value)
                        return storedValueResult;
                }
                if (sequenceResult.Value!.Kind == IrValueKind.Null)
                    return IrEvaluationResult.FromException(
                        IrExceptionKind.NullReference,
                        "Sequence access used a null receiver.");
                if (sequenceResult.Value.Kind != IrValueKind.Sequence)
                    return InvalidValue(
                        "Sequence access requires a sequence value.");
                if (indexResult.Value!.Kind != IrValueKind.Integer)
                    return InvalidValue(
                        "Sequence access requires an integer index.");
                if (indexResult.Value.Integer < 0 ||
                    indexResult.Value.Integer >=
                    sequenceResult.Value.Elements.Length)
                    return IrEvaluationResult.FromException(
                        IrExceptionKind.IndexOutOfRange,
                        "The sequence index is outside the valid range.");
                return null;
            default:
                return IrEvaluationResult.FromUnsupported(
                    IrUnsupportedReason.UnsupportedOperation,
                    "Unknown IR location kind: " + location.Kind + ".");
        }
    }

    private IrEvaluationResult? EvaluateCallOperands(
        IrTerm? receiver,
        IReadOnlyList<IrTerm> arguments,
        IrTerm? storedValue,
        IReadOnlyDictionary<IrVarId, IrValue> values,
        string nullReceiverDetail) {
        IrValue? receiverValue = null;
        if (receiver != null) {
            var receiverResult = _terms.Evaluate(receiver, values);
            if (receiverResult.Status != IrEvaluationStatus.Value)
                return receiverResult;
            receiverValue = receiverResult.Value;
        }
        foreach (var argument in arguments) {
            var argumentResult = _terms.Evaluate(argument, values);
            if (argumentResult.Status != IrEvaluationStatus.Value)
                return argumentResult;
        }
        if (storedValue != null) {
            var storedValueResult = _terms.Evaluate(storedValue, values);
            if (storedValueResult.Status != IrEvaluationStatus.Value)
                return storedValueResult;
        }
        return receiverValue?.Kind == IrValueKind.Null
            ? IrEvaluationResult.FromException(
                IrExceptionKind.NullReference,
                nullReceiverDetail)
            : null;
    }

    private static IrEvaluationResult InvalidValue(string detail) =>
        IrEvaluationResult.FromUnsupported(
            IrUnsupportedReason.InvalidVariableValue,
            detail);

    private static IrProgramExecutionResult FromEvaluation(
        IrEvaluationResult evaluation,
        IrInstruction instruction,
        ImmutableDictionary<IrVarId, IrValue>.Builder values,
        int steps) =>
        evaluation.Status switch {
            IrEvaluationStatus.Exception => new IrProgramExecutionResult(
                IrProgramExecutionStatus.Exception,
                null,
                instruction,
                null,
                evaluation.Exception,
                values.ToImmutable(),
                steps),
            _ => new IrProgramExecutionResult(
                IrProgramExecutionStatus.Unsupported,
                null,
                instruction,
                evaluation.Unsupported,
                null,
                values.ToImmutable(),
                steps)
        };

    private static IrProgramExecutionResult Unsupported(
        IrInstruction instruction,
        ImmutableDictionary<IrVarId, IrValue>.Builder values,
        int steps,
        string detail) =>
        new(
            IrProgramExecutionStatus.Unsupported,
            null,
            instruction,
            new IrUnsupportedInfo(
                IrUnsupportedReason.UnsupportedOperation,
                detail),
            null,
            values.ToImmutable(),
            steps);

    private static IrProgramExecutionResult Result(
        IrProgramExecutionStatus status,
        IrInstruction? instruction,
        ImmutableDictionary<IrVarId, IrValue>.Builder values,
        int steps,
        IrValue? returnValue = null) =>
        new(
            status,
            returnValue,
            instruction,
            null,
            null,
            values.ToImmutable(),
            steps);
}
