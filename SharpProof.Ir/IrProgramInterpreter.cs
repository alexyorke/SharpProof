namespace SharpProof.Ir;

public sealed partial class IrProgramExecutionResult
{
    public IrValue? GetCurrentValue(IrVarId variable)
    {
        return Values.TryGetValue(variable, out var value) ? value : null;
    }
}

public sealed class IrProgramInterpreter(IrFactory factory)
{
    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));
    private readonly IrInterpreter _terms =
        new(factory);
    public IrProgramExecutionResult Execute(
        IrProgram program, IReadOnlyDictionary<IrVarId, IrValue>? initialValues = null, int maximumSteps = 10000,
        CancellationToken cancellationToken = default)
    {
        return Execute(
            program, initialValues, maximumSteps, callHost: null,
            cancellationToken);
    }

    internal IrProgramExecutionResult Execute(
        IrProgram program, IReadOnlyDictionary<IrVarId, IrValue>? initialValues,
        int maximumSteps,
        Func<IrCallInstruction, IrValue?, ImmutableArray<IrValue>, IrValue?>? callHost,
        CancellationToken cancellationToken)
    {
        return Execute(program, initialValues, maximumSteps, callHost, replayOptions: null, cancellationToken);
    }

    public IrProgramExecutionResult Execute(
        IrProgram program, IReadOnlyDictionary<IrVarId, IrValue>? initialValues,
        int maximumSteps, IrProgramReplayOptions replayOptions,
        CancellationToken cancellationToken = default)
    {
        return Execute(program, initialValues, maximumSteps, callHost: null, replayOptions, cancellationToken);
    }

    internal IrProgramExecutionResult Execute(
        IrProgram program, IReadOnlyDictionary<IrVarId, IrValue>? initialValues,
        int maximumSteps,
        Func<IrCallInstruction, IrValue?, ImmutableArray<IrValue>, IrValue?>? callHost,
        IrProgramReplayOptions? replayOptions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullGuard.NotNull(program, nameof(program));

        if (!ReferenceEquals(program.Factory, _factory))
        {
            throw new ArgumentException("The program belongs to a different IR factory.", nameof(program));
        }

        maximumSteps = ArgumentNullGuard.RequirePositive(
            maximumSteps, nameof(maximumSteps));

        if (replayOptions != null && _factory.Semantics != IrExecutionSemantics.Total)
        {
            throw new ArgumentException("Modeled havoc requires total program semantics.", nameof(replayOptions));
        }
        var snapshots = replayOptions?.SnapshotVariables ?? ImmutableHashSet<IrVarId>.Empty;
        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _factory.GetVariableInfo(snapshot);
        }
        var values = new ReplayValues(snapshots);
        if (initialValues != null)
        {
            foreach (var pair in initialValues)
            {
                var variable = _factory.GetVariableInfo(pair.Key);
                if (pair.Value == null || pair.Value.Type != variable.Type)
                {
                    throw new ArgumentException("An initial value does not match its variable type.", nameof(initialValues));
                }

                values.Add(pair.Key, pair.Value);
            }
        }
        var entry = program.Entry;
        var occurrences = new Dictionary<IrInstructionId, int>();
        var state = new ExecutionState
        {
            Interpreter = this,
            Values = values,
            MaximumSteps = maximumSteps,
            CancellationToken = cancellationToken,
            CallHost = callHost,
            ReplayOptions = replayOptions,
            Occurrences = occurrences,
            Current = entry,
        };
        ref var steps = ref state.Steps;
        ref var current = ref state.Current;
        while (steps < maximumSteps)
        {
            var block = program.GetBlock(current);
            // A block's final instruction always returns or transfers control.
            for (var instructionIndex = 0; ; instructionIndex++)
            {
                var instruction = block.Instructions[instructionIndex];
                cancellationToken.ThrowIfCancellationRequested();
                if (steps == maximumSteps)
                {
                    return Result(IrProgramExecutionStatus.StepLimit, null, values, steps);
                }
                steps++;

                state.Transfer = false;
                var result = instruction.Execute(ref state);
                if (result != null)
                {
                    return result;
                }
                if (state.Transfer)
                {
                    goto NextBlock;
                }
            }
        NextBlock:
            continue;
        }
        return Result(IrProgramExecutionStatus.StepLimit, null, values, steps);
    }
    internal struct ExecutionState
    {
        internal IrProgramInterpreter Interpreter;
        internal ReplayValues Values;
        internal int MaximumSteps;
        internal CancellationToken CancellationToken;
        internal Func<IrCallInstruction, IrValue?, ImmutableArray<IrValue>, IrValue?>? CallHost;
        internal IrProgramReplayOptions? ReplayOptions;
        internal Dictionary<IrInstructionId, int> Occurrences;
        internal IrBlockId Current;
        internal int Steps;
        internal IrThrowInstruction? PendingException;
        internal bool Transfer;
    }

    // Original cases 0
    internal static IrProgramExecutionResult? ExecuteInstruction(IrAllocationInstruction allocation, ref ExecutionState state)
    {
        if (allocation.Length is { } length)
        {
            var size = state.Interpreter.Evaluate(length, state.Values, state.CancellationToken);
            if (size.Status != IrEvaluationStatus.Value)
            { return FromEvaluation(size, allocation, state.Values, state.Steps); }
            // Compare as BigInteger: a narrowing cast would wrap or throw on huge lengths.
            var requestedLength = size.Value!.IntegerNumericValue;
            if (requestedLength < 0)
            { return FromEvaluation(IrEvaluationResult.FromException(IrExceptionKind.Overflow, "An array length was negative."), allocation, state.Values, state.Steps); }
            // This bounds concrete replay work, never the symbolic input domain.
            if (requestedLength > state.MaximumSteps - state.Steps)
            { return Result(IrProgramExecutionStatus.StepLimit, null, state.Values, state.Steps); }
            var count = (int)requestedLength;
            state.Steps += count;
            var element = state.Interpreter._factory.GetTypeInfo(allocation.AllocatedType).ElementType
                ?? throw new InvalidOperationException("An allocation of a non-array type was replayed.");
            var info = state.Interpreter._factory.GetTypeInfo(element);
            var initial = info.CreateDefaultValue(state.Interpreter._factory);
            var elements = Enumerable.Repeat(initial, count);
            if (!allocation.InitialValues.IsEmpty)
            {
                var evaluatedElements = new IrValue[allocation.InitialValues.Length];
                for (var initialIndex = 0; initialIndex < evaluatedElements.Length; initialIndex++)
                {
                    var initialValue = state.Interpreter.Evaluate(allocation.InitialValues[initialIndex], state.Values, state.CancellationToken);
                    evaluatedElements[initialIndex] = initialValue.Value!;
                }
                elements = evaluatedElements;
            }
            var createdArray = state.Interpreter._factory.CreateSequenceValue(allocation.AllocatedType, elements);
            state.Values[allocation.Target!.Value] = createdArray;
            state.Values.Heap.RegisterFreshArray(createdArray);
        }
        else if (allocation.Target is { } allocatedTarget)
        {
            var created = state.Interpreter._factory.CreateReferenceValue(allocation.AllocatedType, new object());
            state.Values[allocatedTarget] = created;
            state.Values.Heap.RegisterFreshObject(created);
        }
        state.ReplayOptions?.AllocationObserver?.Invoke(allocation);
        state.ReplayOptions?.AllocationPrefixObserver?.Invoke(allocation, state.Values.ConsumedApproximation);
        return null;
    }

    // Original cases 1
    internal static IrProgramExecutionResult? ExecuteInstruction(IrLockInstruction synchronization, ref ExecutionState state)
    {
        var receiver = state.Interpreter.Evaluate(synchronization.Receiver, state.Values, state.CancellationToken);
        if (receiver.Status != IrEvaluationStatus.Value)
        { return FromEvaluation(receiver, synchronization, state.Values, state.Steps); }
        state.ReplayOptions?.LockObserver?.Invoke(synchronization);
        state.ReplayOptions?.LockPrefixObserver?.Invoke(synchronization, state.Values.ConsumedApproximation);
        return Unsupported(synchronization, state.Values, state.Steps,
            "Concrete execution stopped at a synchronization attempt.");
    }

    // Original cases 2
    internal static IrProgramExecutionResult? ExecuteInstruction(IrWriteInstruction write, ref ExecutionState state)
    {
        var freshWriteReceiver = false;
        if (write is { Target: { } storedTarget, Value: { } storedElement })
        {
            var target = state.Interpreter.Evaluate(storedTarget, state.Values, state.CancellationToken);
            if (target.Status != IrEvaluationStatus.Value)
            { return FromEvaluation(target, write, state.Values, state.Steps); }
            freshWriteReceiver = state.Values.Heap.IsFreshReceiver(target.Value!);
            IrEvaluationResult? position = null;
            if (write.Index is { } storedIndex)
            {
                position = state.Interpreter.Evaluate(storedIndex, state.Values, state.CancellationToken);
                if (position.Status != IrEvaluationStatus.Value)
                { return FromEvaluation(position, write, state.Values, state.Steps); }
            }
            var element = state.Interpreter.Evaluate(storedElement, state.Values, state.CancellationToken);
            if (element.Status != IrEvaluationStatus.Value)
            { return FromEvaluation(element, write, state.Values, state.Steps); }
            // The lowering guards the store, so an invalid one is not C#.
            if (position != null)
            {
                if (IrInterpreter.ValidateSequenceAccess(target.Value!, position.Value!) != null)
                { return Unsupported(write, state.Values, state.Steps, "An element store was not in bounds."); }
                state.Values.Heap.StoreElement(target.Value!, (int)position.Value!.Integer, element.Value!);
            }
            else if (write.Field is { } field && target.Value!.Kind == IrValueKind.Reference)
            { state.Values.Heap.Fields[(target.Value.Reference, field)] = element.Value!; }
            else
            { return Unsupported(write, state.Values, state.Steps, "A field store needs an object receiver."); }
        }
        else if (write.Region is IrWriteRegion.Field or IrWriteRegion.Element or IrWriteRegion.Parameter or IrWriteRegion.Unknown)
        { state.Values.Heap.Invalidate(); }
        state.ReplayOptions?.WriteObserver?.Invoke(write);
        state.ReplayOptions?.WritePrefixObserver?.Invoke(write, state.Values.ConsumedApproximation);
        if (!freshWriteReceiver)
        { state.ReplayOptions?.NonFreshWritePrefixObserver?.Invoke(write, state.Values.ConsumedApproximation); }
        return null;
    }

    // Original cases 3
    internal static IrProgramExecutionResult? ExecuteInstruction(IrAssignInstruction assign, ref ExecutionState state)
    {
        var assigned = state.Interpreter.Evaluate(assign.Value, state.Values, state.CancellationToken);
        if (assigned.Status != IrEvaluationStatus.Value)
        {
            return FromEvaluation(assigned, assign, state.Values, state.Steps);
        }

        state.Values[assign.Target] = assigned.Value!;
        state.ReplayOptions?.AssignmentObserver?.Invoke(assign, assigned.Value!, state.Values.ConsumedApproximation);
        return null;
    }

    internal static IrProgramExecutionResult? ExecuteInstruction(IrAssumeInstruction instruction, ref ExecutionState state)
    {
        return ExecuteCondition(instruction, ref state);
    }

    internal static IrProgramExecutionResult? ExecuteInstruction(IrAssertInstruction instruction, ref ExecutionState state)
    {
        return ExecuteCondition(instruction, ref state);
    }

    // Original cases 4
    private static IrProgramExecutionResult? ExecuteCondition(IrInstruction instruction, ref ExecutionState state)
    {
        var testedCondition = instruction is IrAssumeInstruction assume
            ? assume.Condition
            : ((IrAssertInstruction)instruction).Condition;
        var tested = state.Interpreter.EvaluateCondition(
            testedCondition,
            state.Values,
            state.CancellationToken,
            out var testedValue);
        if (tested.Status != IrEvaluationStatus.Value)
        {
            return FromEvaluation(tested, instruction, state.Values, state.Steps);
        }

        if (!testedValue)
        {
            return Result(instruction is IrAssumeInstruction
                ? IrProgramExecutionStatus.AssumptionViolated
                : IrProgramExecutionStatus.AssertionFailed, instruction, state.Values, state.Steps);
        }

        return null;
    }

    // Original cases 5
    internal static IrProgramExecutionResult? ExecuteInstruction(IrBranchInstruction branch, ref ExecutionState state)
    {
        var condition = state.Interpreter.EvaluateCondition(
            branch.Condition,
            state.Values,
            state.CancellationToken,
            out var conditionValue);
        if (condition.Status != IrEvaluationStatus.Value)
        {
            return FromEvaluation(condition, branch, state.Values, state.Steps);
        }

        state.Current = conditionValue ? branch.WhenTrue : branch.WhenFalse;
        state.Transfer = true;
        return null;
    }

    // Original cases 6
    internal static IrProgramExecutionResult? ExecuteInstruction(IrGotoInstruction go, ref ExecutionState state)
    {
        state.Current = go.Target;
        state.Transfer = true;
        return null;
    }

    // Original cases 7
    internal static IrProgramExecutionResult? ExecuteInstruction(IrThrowInstruction thrown, ref ExecutionState state)
    {
        state.PendingException = thrown;
        state.Current = thrown.Target;
        state.Transfer = true;
        return null;
    }

    // Original cases 8
    internal static IrProgramExecutionResult? ExecuteInstruction(IrExceptionalExitInstruction exited, ref ExecutionState state)
    {
        return state.PendingException == null
            ? Unsupported(exited, state.Values, state.Steps, "Exceptional exit has no pending exception.")
            : new IrProgramExecutionResult(IrProgramExecutionStatus.Exception, null,
                state.PendingException, null, new IrExceptionInfo(state.PendingException.ExceptionKind,
                    "The program followed an explicit exception edge.", state.PendingException.Operation),
                state.Values.ToImmutable(), state.Steps, state.Values.ConsumedApproximation, state.Values.ApproximationVariables, state.Values.Heap);
    }

    // Original cases 9
    internal static IrProgramExecutionResult? ExecuteInstruction(IrReturnInstruction returned, ref ExecutionState state)
    {
        if (returned.Value == null)
        {
            return Result(IrProgramExecutionStatus.Returned, returned, state.Values, state.Steps);
        }

        var returnValue = state.Interpreter.Evaluate(returned.Value, state.Values, state.CancellationToken);
        if (returnValue.Status != IrEvaluationStatus.Value)
        {
            return FromEvaluation(returnValue, returned, state.Values, state.Steps);
        }

        return Result(IrProgramExecutionStatus.Returned, returned, state.Values, state.Steps, returnValue.Value);
    }

    // Original cases 10
    internal static IrProgramExecutionResult? ExecuteInstruction(IrHavocInstruction havoc, ref ExecutionState state)
    {
        if (state.ReplayOptions != null && havoc.HavocKind == IrHavocKind.Variables)
        {
            state.Occurrences.TryGetValue(havoc.Id, out var occurrence);
            state.Occurrences[havoc.Id] = occurrence + 1;
            foreach (var variable in havoc.Variables)
            {
                var modeled = state.ReplayOptions.HavocValueProvider(new IrHavocRequest(
                    havoc.Id, havoc.Operation, variable, occurrence, havoc.Origin));
                if (modeled == null || modeled.Type != state.Interpreter._factory.GetVariableInfo(variable).Type)
                {
                    return Unsupported(havoc, state.Values, state.Steps, "The havoc model is missing or has the wrong type.");
                }
                state.Values.SetHavocValue(variable, modeled, havoc.Origin);
            }
            return null;
        }
        if (havoc.HavocKind is IrHavocKind.Variables or IrHavocKind.VariablesAndMemory)
        {
            foreach (var variable in havoc.Variables)
            {
                state.Values.Remove(variable);
            }
        }
        return Unsupported(havoc, state.Values, state.Steps,
            "Concrete execution stopped at nondeterministic havoc.");
    }

    internal static IrProgramExecutionResult? ExecuteInstruction(IrLoadInstruction instruction, ref ExecutionState state)
    {
        return ExecuteMemory(instruction, ref state);
    }

    internal static IrProgramExecutionResult? ExecuteInstruction(IrStoreInstruction instruction, ref ExecutionState state)
    {
        return ExecuteMemory(instruction, ref state);
    }

    // Original cases 11
    private static IrProgramExecutionResult? ExecuteMemory(IrInstruction instruction, ref ExecutionState state)
    {
        var location = instruction is IrLoadInstruction load
            ? load.Location
            : ((IrStoreInstruction)instruction).Location;
        var storedValue = (instruction as IrStoreInstruction)?.Value;
        var locationOperands = state.Interpreter.EvaluateLocationOperands(
            location, storedValue, state.Values, state.CancellationToken);
        if (locationOperands != null)
        {
            return FromEvaluation(locationOperands, instruction, state.Values, state.Steps);
        }

        return Unsupported(instruction, state.Values, state.Steps, instruction is IrLoadInstruction
            ? "Concrete execution requires a memory host for load."
            : "Concrete execution requires a memory host for store.");
    }

    // Original cases 12,13
    internal static IrProgramExecutionResult? ExecuteInstruction(IrCallInstruction instruction, ref ExecutionState state)
    {
        var interpreter = state.Interpreter;
        var values = state.Values;
        var cancellationToken = state.CancellationToken;
        var call = instruction;
        if (instruction is IrCallInstruction { Target: null } opaqueCall &&
            state.CallHost == null && state.ReplayOptions != null &&
            state.Interpreter._factory.Semantics == IrExecutionSemantics.Total)
        {
            // Results and exceptions have separate approximation
            // havocs. Operands still run, and a writer forgets current
            // contents until an exact store supplies a demanded cell.
            if (interpreter.EvaluateCallOperands(opaqueCall.Receiver, opaqueCall.Arguments, null, values,
                    "The call receiver is null.", cancellationToken) is { } operandFailure)
            { return FromEvaluation(operandFailure, opaqueCall, values, state.Steps); }
            if ((IrOpaqueCallSite.Effects(state.Interpreter._factory, opaqueCall.Operation) & IrOpaqueCallEffects.Writes) != 0)
            { values.Heap.Invalidate(); }
            return null;
        }
        {
            var callResult = IrProgramCallHostExecution.Execute(
                state.Interpreter._factory, call, term => interpreter.Evaluate(term, values, cancellationToken), state.CallHost);
            if (callResult.Status != IrEvaluationStatus.Value)
            {
                return FromEvaluation(callResult, call, values, state.Steps);
            }

            values[call.Target!.Value] = callResult.Value!;
            return null;
        }
    }

    private IrEvaluationResult? EvaluateLocationOperands(
        IrLocation location, IrTerm? storedValue,
        ReplayValues values,
        CancellationToken cancellationToken)
    {
        return location.Accept(LocationOperandVisitor.Instance, (this, storedValue, values, cancellationToken));
    }

    private sealed class LocationOperandVisitor : IIrLocationVisitor<(IrProgramInterpreter Interpreter, IrTerm? StoredValue, ReplayValues Values, CancellationToken CancellationToken), IrEvaluationResult?>
    {
        internal static LocationOperandVisitor Instance { get; } = new();

        public IrEvaluationResult? Visit(IrMemberLocation location, (IrProgramInterpreter Interpreter, IrTerm? StoredValue, ReplayValues Values, CancellationToken CancellationToken) state)
        {
            return state.Interpreter.EvaluateCallOperands(
                location.Receiver, location.Arguments, state.StoredValue, state.Values,
                "The member access receiver is null.", state.CancellationToken);
        }

        public IrEvaluationResult? Visit(IrSequenceLocation location, (IrProgramInterpreter Interpreter, IrTerm? StoredValue, ReplayValues Values, CancellationToken CancellationToken) state)
        {
            var sequenceResult = state.Interpreter.Evaluate(location.Sequence, state.Values, state.CancellationToken);
            if (sequenceResult.Status != IrEvaluationStatus.Value)
            {
                return sequenceResult;
            }

            var indexResult = state.Interpreter.Evaluate(location.Index, state.Values, state.CancellationToken);
            if (indexResult.Status != IrEvaluationStatus.Value)
            {
                return indexResult;
            }

            if (state.Interpreter.EvaluateOptionalStoredValue(
                    state.StoredValue, state.Values, state.CancellationToken) is { } storedValueResult)
            {
                return storedValueResult;
            }
            return IrInterpreter.ValidateSequenceAccess(sequenceResult.Value!, indexResult.Value!);
        }
    }
    private IrEvaluationResult? EvaluateCallOperands(
        IrTerm? receiver, IReadOnlyList<IrTerm> arguments, IrTerm? storedValue,
        ReplayValues values, string nullReceiverDetail,
        CancellationToken cancellationToken)
    {
        IrValue? receiverValue = null;
        if (receiver != null)
        {
            var receiverResult = Evaluate(receiver, values, cancellationToken);
            if (receiverResult.Status != IrEvaluationStatus.Value)
            {
                return receiverResult;
            }

            receiverValue = receiverResult.Value;
        }
        foreach (var argument in arguments)
        {
            var argumentResult = Evaluate(argument, values, cancellationToken);
            if (argumentResult.Status != IrEvaluationStatus.Value)
            {
                return argumentResult;
            }
        }
        if (EvaluateOptionalStoredValue(
                storedValue, values, cancellationToken) is { } storedValueResult)
        {
            return storedValueResult;
        }
        return receiverValue?.Kind == IrValueKind.Null
            ? IrEvaluationResult.FromException(IrExceptionKind.NullReference, nullReceiverDetail)
            : null;
    }

    private IrEvaluationResult? EvaluateOptionalStoredValue(
        IrTerm? storedValue,
        ReplayValues values,
        CancellationToken cancellationToken)
    {
        if (storedValue == null)
        {
            return null;
        }

        var result = Evaluate(storedValue, values, cancellationToken);
        return result.Status == IrEvaluationStatus.Value ? null : result;
    }

    private static IrProgramExecutionResult FromEvaluation(IrEvaluationResult evaluation, IrInstruction instruction,
        ReplayValues values, int steps)
    {
        return new(evaluation.Status == IrEvaluationStatus.Exception ? IrProgramExecutionStatus.Exception :
                IrProgramExecutionStatus.Unsupported, null, instruction,
            evaluation.Status == IrEvaluationStatus.Exception ? null : evaluation.Unsupported,
            evaluation.Status == IrEvaluationStatus.Exception ? evaluation.Exception : null,
            values.ToImmutable(), steps, values.ConsumedApproximation, values.ApproximationVariables, values.Heap);
    }

    private IrEvaluationResult EvaluateCondition(
        IrTerm condition,
        ReplayValues values,
        CancellationToken cancellationToken,
        out bool value)
    {
        value = false;
        var result = Evaluate(condition, values, cancellationToken);
        if (result.Status != IrEvaluationStatus.Value)
        {
            return result;
        }

        if (result.Value!.Kind != IrValueKind.Boolean)
        {
            return IrEvaluationResult.FromUnsupported(
                IrUnsupportedReason.InvalidVariableValue,
                "Program conditions require boolean values.");
        }

        value = result.Value.Boolean;
        return result;
    }

    private static IrProgramExecutionResult Unsupported(IrInstruction instruction,
            ReplayValues values, int steps, string detail)
    {
        return new(IrProgramExecutionStatus.Unsupported, null, instruction,
                new IrUnsupportedInfo(IrUnsupportedReason.UnsupportedOperation, detail),
                null, values.ToImmutable(), steps, values.ConsumedApproximation, values.ApproximationVariables, values.Heap);
    }

    private static IrProgramExecutionResult Result(IrProgramExecutionStatus status, IrInstruction? instruction,
            ReplayValues values,
            int steps, IrValue? returnValue = null)
    {
        return new(status, returnValue, instruction, null, null, values.ToImmutable(), steps, values.ConsumedApproximation, values.ApproximationVariables, values.Heap);
    }

    private IrEvaluationResult Evaluate(IrTerm term, ReplayValues values, CancellationToken cancellationToken)
    {
        return _terms.Evaluate(term, values.Current, values.ObserveRead, values.Heap,
            cancellationToken, values.Snapshots);
    }

    internal sealed class ReplayValues(ImmutableHashSet<IrVarId> snapshots)
    {
        private readonly ImmutableDictionary<IrVarId, IrValue>.Builder _values = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        private readonly HashSet<IrVarId> _approximations = [];
        private bool _consumedApproximation;
        internal bool ConsumedApproximation => _consumedApproximation || Heap.ConsumedApproximation;
        internal ImmutableHashSet<IrVarId> ApproximationVariables => _approximations.ToImmutableHashSet();
        internal IrHeap Heap { get; } = new();
        internal ImmutableHashSet<IrVarId> Snapshots { get; } = snapshots;
        internal IReadOnlyDictionary<IrVarId, IrValue> Current => _values;
        internal IrValue this[IrVarId key]
        {
            set
            {
                _values[key] = value;
                _approximations.Remove(key);
            }
        }
        internal void ObserveRead(IrVarId key)
        {
            if (_approximations.Contains(key))
            {
                _consumedApproximation = true;
            }
        }
        internal void Add(IrVarId key, IrValue value)
        {
            _values.Add(key, value);
        }
        internal void Remove(IrVarId key)
        {
            _values.Remove(key);
            _approximations.Remove(key);
        }
        internal void SetHavocValue(IrVarId key, IrValue value, IrHavocOrigin origin)
        {
            this[key] = value;
            if (origin == IrHavocOrigin.Approximation)
            {
                _approximations.Add(key);
            }
        }
        internal ImmutableDictionary<IrVarId, IrValue> ToImmutable()
        {
            return _values.ToImmutable();
        }
    }
}

public abstract partial class IrInstruction
{
    internal abstract IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state);
}

public sealed partial class IrAllocationInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrLockInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrWriteInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrAssignInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrLoadInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrStoreInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrCallInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrAssumeInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrAssertInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrHavocInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrThrowInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrExceptionalExitInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrBranchInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrGotoInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}

public sealed partial class IrReturnInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        return IrProgramInterpreter.ExecuteInstruction(this, ref state);
    }
}
