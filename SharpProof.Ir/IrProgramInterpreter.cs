namespace SharpProof.Ir;

public sealed partial class IrProgramExecutionResult
{
    public IrValue? GetCurrentValue(IrVarId variable)
    {
        if (variable.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(variable));
        }
        return Values.TryGetValue(variable, out var value) ? value : null;
    }
}

public sealed class IrProgramInterpreter(IrFactory factory)
{
    internal readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));
    private readonly IrInterpreter _terms =
        new(factory);
    public IrProgramExecutionResult Execute(
        IrProgram program, IReadOnlyDictionary<IrVarId, IrValue>? initialValues = null, int maximumSteps = 10000,
        CancellationToken cancellationToken = default)
    {
        if (program is null)
        {
            throw new System.ArgumentNullException(nameof(program));
        }
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
        if (program is null)
        {
            throw new System.ArgumentNullException(nameof(program));
        }
        return Execute(program, initialValues, maximumSteps, callHost, replayOptions: null, cancellationToken);
    }

    public IrProgramExecutionResult Execute(
        IrProgram program, IReadOnlyDictionary<IrVarId, IrValue>? initialValues,
        int maximumSteps, IrProgramReplayOptions replayOptions,
        CancellationToken cancellationToken = default)
    {
        if (program is null)
        {
            throw new System.ArgumentNullException(nameof(program));
        }
        if (replayOptions is null)
        {
            throw new System.ArgumentNullException(nameof(replayOptions));
        }
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

    // Original cases 4
    internal static IrProgramExecutionResult? ExecuteCondition(IrInstruction instruction, ref ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        if (instruction is null)
        {
            throw new System.ArgumentNullException(nameof(instruction));
        }
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

    // Original cases 11
    internal static IrProgramExecutionResult? ExecuteMemory(IrInstruction instruction, ref ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        if (instruction is null)
        {
            throw new System.ArgumentNullException(nameof(instruction));
        }
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

    private IrEvaluationResult? EvaluateLocationOperands(
        IrLocation location, IrTerm? storedValue,
        ReplayValues values,
        CancellationToken cancellationToken)
    {
        if (location is null)
        {
            throw new System.ArgumentNullException(nameof(location));
        }
        if (values is null)
        {
            throw new System.ArgumentNullException(nameof(values));
        }
        return location.Accept(LocationOperandVisitor.Instance, (this, storedValue, values, cancellationToken));
    }

    private sealed class LocationOperandVisitor : IIrLocationVisitor<(IrProgramInterpreter Interpreter, IrTerm? StoredValue, ReplayValues Values, CancellationToken CancellationToken), IrEvaluationResult?>
    {
        internal static LocationOperandVisitor Instance { get; } = new();

        public IrEvaluationResult? Visit(IrMemberLocation location, (IrProgramInterpreter Interpreter, IrTerm? StoredValue, ReplayValues Values, CancellationToken CancellationToken) state)
        {
            if (location is null)
            {
                throw new System.ArgumentNullException(nameof(location));
            }
            return state.Interpreter.EvaluateCallOperands(
                location.Receiver, location.Arguments, state.StoredValue, state.Values,
                "The member access receiver is null.", state.CancellationToken);
        }

        public IrEvaluationResult? Visit(IrSequenceLocation location, (IrProgramInterpreter Interpreter, IrTerm? StoredValue, ReplayValues Values, CancellationToken CancellationToken) state)
        {
            if (location is null)
            {
                throw new System.ArgumentNullException(nameof(location));
            }
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
    internal IrEvaluationResult? EvaluateCallOperands(
        IrTerm? receiver, IReadOnlyList<IrTerm> arguments, IrTerm? storedValue,
        ReplayValues values, string nullReceiverDetail,
        CancellationToken cancellationToken)
    {
        if (arguments is null)
        {
            throw new System.ArgumentNullException(nameof(arguments));
        }
        if (nullReceiverDetail is null)
        {
            throw new System.ArgumentNullException(nameof(nullReceiverDetail));
        }
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
        if (values is null)
        {
            throw new System.ArgumentNullException(nameof(values));
        }
        if (storedValue == null)
        {
            return null;
        }

        var result = Evaluate(storedValue, values, cancellationToken);
        return result.Status == IrEvaluationStatus.Value ? null : result;
    }

    internal static IrProgramExecutionResult FromEvaluation(IrEvaluationResult evaluation, IrInstruction instruction,
        ReplayValues values, int steps)
    {
        if (evaluation is null)
        {
            throw new System.ArgumentNullException(nameof(evaluation));
        }
        if (instruction is null)
        {
            throw new System.ArgumentNullException(nameof(instruction));
        }
        if (values is null)
        {
            throw new System.ArgumentNullException(nameof(values));
        }
        return new(evaluation.Status == IrEvaluationStatus.Exception ? IrProgramExecutionStatus.Exception :
                IrProgramExecutionStatus.Unsupported, null, instruction,
            evaluation.Status == IrEvaluationStatus.Exception ? null : evaluation.Unsupported,
            evaluation.Status == IrEvaluationStatus.Exception ? evaluation.Exception : null,
            values.ToImmutable(), steps, values.ConsumedApproximation, values.ApproximationVariables, values.Heap);
    }

    internal IrEvaluationResult EvaluateCondition(
        IrTerm condition,
        ReplayValues values,
        CancellationToken cancellationToken,
        out bool value)
    {
        if (condition is null)
        {
            throw new System.ArgumentNullException(nameof(condition));
        }
        if (values is null)
        {
            throw new System.ArgumentNullException(nameof(values));
        }
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

    internal static IrProgramExecutionResult Unsupported(IrInstruction instruction,
            ReplayValues values, int steps, string detail)
    {
        if (detail is null)
        {
            throw new System.ArgumentNullException(nameof(detail));
        }
        if (instruction is null)
        {
            throw new System.ArgumentNullException(nameof(instruction));
        }
        if (values is null)
        {
            throw new System.ArgumentNullException(nameof(values));
        }
        return new(IrProgramExecutionStatus.Unsupported, null, instruction,
                new IrUnsupportedInfo(IrUnsupportedReason.UnsupportedOperation, detail),
                null, values.ToImmutable(), steps, values.ConsumedApproximation, values.ApproximationVariables, values.Heap);
    }

    internal static IrProgramExecutionResult Result(IrProgramExecutionStatus status, IrInstruction? instruction,
            ReplayValues values,
            int steps, IrValue? returnValue = null)
    {
        if (values is null)
        {
            throw new System.ArgumentNullException(nameof(values));
        }
        return new(status, returnValue, instruction, null, null, values.ToImmutable(), steps, values.ConsumedApproximation, values.ApproximationVariables, values.Heap);
    }

    internal IrEvaluationResult Evaluate(IrTerm term, ReplayValues values, CancellationToken cancellationToken)
    {
        if (term is null)
        {
            throw new System.ArgumentNullException(nameof(term));
        }
        if (values is null)
        {
            throw new System.ArgumentNullException(nameof(values));
        }
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
        internal ImmutableHashSet<IrVarId> Snapshots { get; } = (snapshots ?? throw new System.ArgumentNullException(nameof(snapshots)));
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
            if (key.IsDefault)
            {
                throw new System.ArgumentOutOfRangeException(nameof(key));
            }
            if (_approximations.Contains(key))
            {
                _consumedApproximation = true;
            }
        }
        internal void Add(IrVarId key, IrValue value)
        {
            if (key.IsDefault)
            {
                throw new System.ArgumentOutOfRangeException(nameof(key));
            }
            if (value is null)
            {
                throw new System.ArgumentNullException(nameof(value));
            }
            _values.Add(key, value);
        }
        internal void Remove(IrVarId key)
        {
            if (key.IsDefault)
            {
                throw new System.ArgumentOutOfRangeException(nameof(key));
            }
            _values.Remove(key);
            _approximations.Remove(key);
        }
        internal void SetHavocValue(IrVarId key, IrValue value, IrHavocOrigin origin)
        {
            if (key.IsDefault)
            {
                throw new System.ArgumentOutOfRangeException(nameof(key));
            }
            if (value is null)
            {
                throw new System.ArgumentNullException(nameof(value));
            }
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
        if (this.Length is { } length)
        {
            var size = state.Interpreter.Evaluate(length, state.Values, state.CancellationToken);
            if (size.Status != IrEvaluationStatus.Value)
            { return IrProgramInterpreter.FromEvaluation(size, this, state.Values, state.Steps); }
            // Compare as BigInteger: a narrowing cast would wrap or throw on huge lengths.
            var requestedLength = size.Value!.IntegerNumericValue;
            if (requestedLength < 0)
            { return IrProgramInterpreter.FromEvaluation(IrEvaluationResult.FromException(IrExceptionKind.Overflow, "An array length was negative."), this, state.Values, state.Steps); }
            // This bounds concrete replay work, never the symbolic input domain.
            if (requestedLength > state.MaximumSteps - state.Steps)
            { return IrProgramInterpreter.Result(IrProgramExecutionStatus.StepLimit, null, state.Values, state.Steps); }
            var count = (int)requestedLength;
            state.Steps += count;
            var element = state.Interpreter._factory.GetTypeInfo(this.AllocatedType).ElementType
                ?? throw new InvalidOperationException("An allocation of a non-array type was replayed.");
            var info = state.Interpreter._factory.GetTypeInfo(element);
            var initial = info.CreateDefaultValue(state.Interpreter._factory);
            var elements = Enumerable.Repeat(initial, count);
            if (!this.InitialValues.IsEmpty)
            {
                var evaluatedElements = new IrValue[this.InitialValues.Length];
                for (var initialIndex = 0; initialIndex < evaluatedElements.Length; initialIndex++)
                {
                    var initialValue = state.Interpreter.Evaluate(this.InitialValues[initialIndex], state.Values, state.CancellationToken);
                    evaluatedElements[initialIndex] = initialValue.Value!;
                }
                elements = evaluatedElements;
            }
            var createdArray = state.Interpreter._factory.CreateSequenceValue(this.AllocatedType, elements);
            state.Values[this.Target!.Value] = createdArray;
            state.Values.Heap.RegisterFreshArray(createdArray);
        }
        else if (this.Target is { } allocatedTarget)
        {
            var created = state.Interpreter._factory.CreateReferenceValue(this.AllocatedType, new object());
            state.Values[allocatedTarget] = created;
            state.Values.Heap.RegisterFreshObject(created);
        }
        state.ReplayOptions?.AllocationObserver?.Invoke(this);
        state.ReplayOptions?.AllocationPrefixObserver?.Invoke(this, state.Values.ConsumedApproximation);
        return null;
    }
}

public sealed partial class IrLockInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        var receiver = state.Interpreter.Evaluate(this.Receiver, state.Values, state.CancellationToken);
        if (receiver.Status != IrEvaluationStatus.Value)
        { return IrProgramInterpreter.FromEvaluation(receiver, this, state.Values, state.Steps); }
        state.ReplayOptions?.LockObserver?.Invoke(this);
        state.ReplayOptions?.LockPrefixObserver?.Invoke(this, state.Values.ConsumedApproximation);
        return IrProgramInterpreter.Unsupported(this, state.Values, state.Steps,
            "Concrete execution stopped at a synchronization attempt.");
    }
}

public sealed partial class IrWriteInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        var freshWriteReceiver = false;
        if (this is { Target: { } storedTarget, Value: { } storedElement })
        {
            var target = state.Interpreter.Evaluate(storedTarget, state.Values, state.CancellationToken);
            if (target.Status != IrEvaluationStatus.Value)
            { return IrProgramInterpreter.FromEvaluation(target, this, state.Values, state.Steps); }
            freshWriteReceiver = state.Values.Heap.IsFreshReceiver(target.Value!);
            IrEvaluationResult? position = null;
            if (this.Index is { } storedIndex)
            {
                position = state.Interpreter.Evaluate(storedIndex, state.Values, state.CancellationToken);
                if (position.Status != IrEvaluationStatus.Value)
                { return IrProgramInterpreter.FromEvaluation(position, this, state.Values, state.Steps); }
            }
            var element = state.Interpreter.Evaluate(storedElement, state.Values, state.CancellationToken);
            if (element.Status != IrEvaluationStatus.Value)
            { return IrProgramInterpreter.FromEvaluation(element, this, state.Values, state.Steps); }
            // The lowering guards the store, so an invalid one is not C#.
            if (position != null)
            {
                if (IrInterpreter.ValidateSequenceAccess(target.Value!, position.Value!) != null)
                { return IrProgramInterpreter.Unsupported(this, state.Values, state.Steps, "An element store was not in bounds."); }
                state.Values.Heap.StoreElement(target.Value!, (int)position.Value!.Integer, element.Value!);
            }
            else if (this.Field is { } field && target.Value!.Kind == IrValueKind.Reference)
            { state.Values.Heap.Fields[(target.Value.Reference, field)] = element.Value!; }
            else
            { return IrProgramInterpreter.Unsupported(this, state.Values, state.Steps, "A field store needs an object receiver."); }
        }
        else if (this.Region is IrWriteRegion.Field or IrWriteRegion.Element or IrWriteRegion.Parameter or IrWriteRegion.Unknown)
        { state.Values.Heap.Invalidate(); }
        state.ReplayOptions?.WriteObserver?.Invoke(this);
        state.ReplayOptions?.WritePrefixObserver?.Invoke(this, state.Values.ConsumedApproximation);
        if (!freshWriteReceiver)
        { state.ReplayOptions?.NonFreshWritePrefixObserver?.Invoke(this, state.Values.ConsumedApproximation); }
        return null;
    }
}

public sealed partial class IrAssignInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        var assigned = state.Interpreter.Evaluate(this.Value, state.Values, state.CancellationToken);
        if (assigned.Status != IrEvaluationStatus.Value)
        {
            return IrProgramInterpreter.FromEvaluation(assigned, this, state.Values, state.Steps);
        }

        state.Values[this.Target] = assigned.Value!;
        state.ReplayOptions?.AssignmentObserver?.Invoke(this, assigned.Value!, state.Values.ConsumedApproximation);
        return null;
    }
}

public sealed partial class IrLoadInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        return IrProgramInterpreter.ExecuteMemory(this, ref state);
    }
}

public sealed partial class IrStoreInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        return IrProgramInterpreter.ExecuteMemory(this, ref state);
    }
}

public sealed partial class IrCallInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        var interpreter = state.Interpreter;
        var values = state.Values;
        var cancellationToken = state.CancellationToken;
        var call = this;
        if (this is IrCallInstruction { Target: null } opaqueCall &&
            state.CallHost == null && state.ReplayOptions != null &&
            state.Interpreter._factory.Semantics == IrExecutionSemantics.Total)
        {
            // Results and exceptions have separate approximation
            // havocs. Operands still run, and a writer forgets current
            // contents until an exact store supplies a demanded cell.
            if (interpreter.EvaluateCallOperands(opaqueCall.Receiver, opaqueCall.Arguments, null, values,
                    "The call receiver is null.", cancellationToken) is { } operandFailure)
            { return IrProgramInterpreter.FromEvaluation(operandFailure, opaqueCall, values, state.Steps); }
            if ((IrOpaqueCallSite.Effects(state.Interpreter._factory, opaqueCall.Operation) & IrOpaqueCallEffects.Writes) != 0)
            { values.Heap.Invalidate(); }
            return null;
        }
        {
            var callResult = IrProgramCallHostExecution.Execute(
                state.Interpreter._factory, call, term => term is null ? throw new System.ArgumentNullException(nameof(term)) : (interpreter.Evaluate(term, values, cancellationToken)), state.CallHost);
            if (callResult.Status != IrEvaluationStatus.Value)
            {
                return IrProgramInterpreter.FromEvaluation(callResult, call, values, state.Steps);
            }

            values[call.Target!.Value] = callResult.Value!;
            return null;
        }
    }
}

public sealed partial class IrAssumeInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        return IrProgramInterpreter.ExecuteCondition(this, ref state);
    }
}

public sealed partial class IrAssertInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        return IrProgramInterpreter.ExecuteCondition(this, ref state);
    }
}

public sealed partial class IrHavocInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.ReplayOptions != null && this.HavocKind == IrHavocKind.Variables)
        {
            state.Occurrences.TryGetValue(this.Id, out var occurrence);
            state.Occurrences[this.Id] = occurrence + 1;
            foreach (var variable in this.Variables)
            {
                var modeled = state.ReplayOptions.HavocValueProvider(new IrHavocRequest(
                    this.Id, this.Operation, variable, occurrence, this.Origin));
                if (modeled == null || modeled.Type != state.Interpreter._factory.GetVariableInfo(variable).Type)
                {
                    return IrProgramInterpreter.Unsupported(this, state.Values, state.Steps, "The havoc model is missing or has the wrong type.");
                }
                state.Values.SetHavocValue(variable, modeled, this.Origin);
            }
            return null;
        }
        if (this.HavocKind is IrHavocKind.Variables or IrHavocKind.VariablesAndMemory)
        {
            foreach (var variable in this.Variables)
            {
                state.Values.Remove(variable);
            }
        }
        return IrProgramInterpreter.Unsupported(this, state.Values, state.Steps,
            "Concrete execution stopped at nondeterministic havoc.");
    }
}

public sealed partial class IrThrowInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        state.PendingException = this;
        state.Current = this.Target;
        state.Transfer = true;
        return null;
    }
}

public sealed partial class IrExceptionalExitInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        return state.PendingException == null
            ? IrProgramInterpreter.Unsupported(this, state.Values, state.Steps, "Exceptional exit has no pending exception.")
            : new IrProgramExecutionResult(IrProgramExecutionStatus.Exception, null,
                state.PendingException, null, new IrExceptionInfo(state.PendingException.ExceptionKind,
                    "The program followed an explicit exception edge.", state.PendingException.Operation),
                state.Values.ToImmutable(), state.Steps, state.Values.ConsumedApproximation, state.Values.ApproximationVariables, state.Values.Heap);
    }
}

public sealed partial class IrBranchInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        var condition = state.Interpreter.EvaluateCondition(
            this.Condition,
            state.Values,
            state.CancellationToken,
            out var conditionValue);
        if (condition.Status != IrEvaluationStatus.Value)
        {
            return IrProgramInterpreter.FromEvaluation(condition, this, state.Values, state.Steps);
        }

        state.Current = conditionValue ? this.WhenTrue : this.WhenFalse;
        state.Transfer = true;
        return null;
    }
}

public sealed partial class IrGotoInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        state.Current = this.Target;
        state.Transfer = true;
        return null;
    }
}

public sealed partial class IrReturnInstruction
{
    internal override IrProgramExecutionResult? Execute(ref IrProgramInterpreter.ExecutionState state)
    {
        if (state.Interpreter is null || state.Values is null || state.Occurrences is null || state.Current.IsDefault)
        { throw new System.ArgumentException("Value must be initialized.", nameof(state)); }

        if (this.Value == null)
        {
            return IrProgramInterpreter.Result(IrProgramExecutionStatus.Returned, this, state.Values, state.Steps);
        }

        var returnValue = state.Interpreter.Evaluate(this.Value, state.Values, state.CancellationToken);
        if (returnValue.Status != IrEvaluationStatus.Value)
        {
            return IrProgramInterpreter.FromEvaluation(returnValue, this, state.Values, state.Steps);
        }

        return IrProgramInterpreter.Result(IrProgramExecutionStatus.Returned, this, state.Values, state.Steps, returnValue.Value);
    }
}
