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
        var values = new ReplayValues();
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
        var (current, steps) = (program.Entry, 0);
        var occurrences = new Dictionary<IrInstructionId, int>();
        IrThrowInstruction? pendingException = null;
        while (steps < maximumSteps)
        {
            var block = program.GetBlock(current);
            foreach (var instruction in block.Instructions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (steps == maximumSteps)
                {
                    return Result(IrProgramExecutionStatus.StepLimit, null, values, steps);
                }
                steps++;

                switch (instruction)
                {
                    case IrAllocationInstruction allocation:
                        if (allocation.Length is { } length)
                        {
                            var size = _terms.Evaluate(length, values.Current, values.ObserveRead, cancellationToken);
                            if (size.Status != IrEvaluationStatus.Value)
                            { return FromEvaluation(size, allocation, values, steps); }
                            var count = (int)size.Value!.IntegerNumericValue;
                            if (count < 0)
                            { return FromEvaluation(IrEvaluationResult.FromException(IrExceptionKind.Overflow, "An array length was negative."), allocation, values, steps); }
                            // This bounds concrete replay work, never the symbolic input domain.
                            if (count > maximumSteps - steps)
                            { return Result(IrProgramExecutionStatus.StepLimit, null, values, steps); }
                            steps += count;
                            var element = _factory.GetTypeInfo(allocation.AllocatedType).ElementType!.Value;
                            var info = _factory.GetTypeInfo(element);
                            var initial = info.Kind == IrTypeKind.Boolean ? _factory.CreateBooleanValue(false)
                                : info.Kind == IrTypeKind.Integer ? _factory.CreateIntegerValue(element, 0L) : _factory.CreateNullValue(element);
                            var elements = allocation.InitialValues.IsEmpty ? Enumerable.Repeat(initial, count)
                                : allocation.InitialValues.Select(value => _terms.Evaluate(value, values.Current, values.ObserveRead, cancellationToken).Value!);
                            values[allocation.Target!.Value] = _factory.CreateSequenceValue(allocation.AllocatedType, elements);
                        }
                        else if (allocation.Target is { } allocatedTarget)
                        { values[allocatedTarget] = _factory.CreateReferenceValue(allocation.AllocatedType, new object()); }
                        replayOptions?.AllocationObserver?.Invoke(allocation);
                        replayOptions?.AllocationPrefixObserver?.Invoke(allocation, values.ConsumedApproximation);
                        break;
                    case IrLockInstruction synchronization:
                        var receiver = _terms.Evaluate(synchronization.Receiver, values.Current, values.ObserveRead, cancellationToken);
                        if (receiver.Status != IrEvaluationStatus.Value)
                        { return FromEvaluation(receiver, synchronization, values, steps); }
                        replayOptions?.LockObserver?.Invoke(synchronization);
                        replayOptions?.LockPrefixObserver?.Invoke(synchronization, values.ConsumedApproximation);
                        return Unsupported(synchronization, values, steps,
                            "Concrete execution stopped at a synchronization attempt.");
                    case IrWriteInstruction write:
                        replayOptions?.WriteObserver?.Invoke(write);
                        replayOptions?.WritePrefixObserver?.Invoke(write, values.ConsumedApproximation);
                        break;
                    case IrAssignInstruction assign:
                        var assigned = _terms.Evaluate(assign.Value, values.Current, values.ObserveRead, cancellationToken);
                        if (assigned.Status != IrEvaluationStatus.Value)
                        {
                            return FromEvaluation(assigned, assign, values, steps);
                        }

                        values[assign.Target] = assigned.Value!;
                        replayOptions?.AssignmentObserver?.Invoke(assign, assigned.Value!, values.ConsumedApproximation);
                        break;
                    case IrAssumeInstruction or IrAssertInstruction:
                        var testedCondition = instruction is IrAssumeInstruction assume
                            ? assume.Condition
                            : ((IrAssertInstruction)instruction).Condition;
                        var tested = EvaluateCondition(
                            testedCondition,
                            values,
                            cancellationToken,
                            out var testedValue);
                        if (tested.Status != IrEvaluationStatus.Value)
                        {
                            return FromEvaluation(tested, instruction, values, steps);
                        }

                        if (!testedValue)
                        {
                            return Result(instruction is IrAssumeInstruction
                                ? IrProgramExecutionStatus.AssumptionViolated
                                : IrProgramExecutionStatus.AssertionFailed, instruction, values, steps);
                        }

                        break;
                    case IrBranchInstruction branch:
                        var condition = EvaluateCondition(
                            branch.Condition,
                            values,
                            cancellationToken,
                            out var conditionValue);
                        if (condition.Status != IrEvaluationStatus.Value)
                        {
                            return FromEvaluation(condition, branch, values, steps);
                        }

                        current = conditionValue ? branch.WhenTrue : branch.WhenFalse;
                        goto NextBlock;
                    case IrGotoInstruction go:
                        current = go.Target;
                        goto NextBlock;
                    case IrThrowInstruction thrown:
                        pendingException = thrown;
                        current = thrown.Target;
                        goto NextBlock;
                    case IrExceptionalExitInstruction exited:
                        return pendingException == null
                            ? Unsupported(exited, values, steps, "Exceptional exit has no pending exception.")
                            : new IrProgramExecutionResult(IrProgramExecutionStatus.Exception, null,
                                pendingException, null, new IrExceptionInfo(pendingException.ExceptionKind,
                                    "The program followed an explicit exception edge.", pendingException.Operation),
                                values.ToImmutable(), steps, values.ConsumedApproximation, values.ApproximationVariables);
                    case IrReturnInstruction returned:
                        if (returned.Value == null)
                        {
                            return Result(IrProgramExecutionStatus.Returned, returned, values, steps);
                        }

                        var returnValue = _terms.Evaluate(returned.Value, values.Current, values.ObserveRead, cancellationToken);
                        if (returnValue.Status != IrEvaluationStatus.Value)
                        {
                            return FromEvaluation(returnValue, returned, values, steps);
                        }

                        return Result(IrProgramExecutionStatus.Returned, returned, values, steps, returnValue.Value);
                    case IrHavocInstruction havoc:
                        if (replayOptions != null && havoc.HavocKind == IrHavocKind.Variables)
                        {
                            occurrences.TryGetValue(havoc.Id, out var occurrence);
                            occurrences[havoc.Id] = occurrence + 1;
                            foreach (var variable in havoc.Variables)
                            {
                                var modeled = replayOptions.HavocValueProvider(new IrHavocRequest(
                                    havoc.Id, havoc.Operation, variable, occurrence, havoc.Origin));
                                if (modeled == null || modeled.Type != _factory.GetVariableInfo(variable).Type)
                                {
                                    return Unsupported(havoc, values, steps, "The havoc model is missing or has the wrong type.");
                                }
                                values.SetHavocValue(variable, modeled, havoc.Origin);
                            }
                            break;
                        }
                        if (havoc.HavocKind is IrHavocKind.Variables or IrHavocKind.VariablesAndMemory)
                        {
                            foreach (var variable in havoc.Variables)
                            {
                                values.Remove(variable);
                            }
                        }
                        return Unsupported(havoc, values, steps,
                            "Concrete execution stopped at nondeterministic havoc.");
                    case IrLoadInstruction or IrStoreInstruction:
                        var location = instruction is IrLoadInstruction load
                            ? load.Location
                            : ((IrStoreInstruction)instruction).Location;
                        var storedValue = (instruction as IrStoreInstruction)?.Value;
                        var locationOperands = EvaluateLocationOperands(
                            location, storedValue, values, cancellationToken);
                        if (locationOperands != null)
                        {
                            return FromEvaluation(locationOperands, instruction, values, steps);
                        }

                        return Unsupported(instruction, values, steps, instruction is IrLoadInstruction
                            ? "Concrete execution requires a memory host for load."
                            : "Concrete execution requires a memory host for store.");
                    case IrCallInstruction call:
                        {
                            var callResult = IrProgramCallHostExecution.Execute(
                                _factory, _terms, call, values.Current, callHost,
                                values.ObserveRead, cancellationToken);
                            if (callResult.Status != IrEvaluationStatus.Value)
                            {
                                return FromEvaluation(callResult, call, values, steps);
                            }

                            values[call.Target!.Value] = callResult.Value!;
                            break;
                        }
                    default:
                        return Unsupported(instruction, values, steps, "Unknown program instruction.");
                }
            }
            return Unsupported(block.Instructions[block.Instructions.Length - 1], values, steps,
                "A block completed without transferring control.");
        NextBlock:
            continue;
        }
        return Result(IrProgramExecutionStatus.StepLimit, null, values, steps);
    }
    private IrEvaluationResult? EvaluateLocationOperands(
        IrLocation location, IrTerm? storedValue,
        ReplayValues values,
        CancellationToken cancellationToken)
    {
        switch (location)
        {
            case IrMemberLocation member:
                return EvaluateCallOperands(
                    member.Receiver, member.Arguments, storedValue, values,
                    "The member access receiver is null.", cancellationToken);
            case IrSequenceLocation sequence:
                var sequenceResult = _terms.Evaluate(sequence.Sequence, values.Current, values.ObserveRead, cancellationToken);
                if (sequenceResult.Status != IrEvaluationStatus.Value)
                {
                    return sequenceResult;
                }

                var indexResult = _terms.Evaluate(sequence.Index, values.Current, values.ObserveRead, cancellationToken);
                if (indexResult.Status != IrEvaluationStatus.Value)
                {
                    return indexResult;
                }

                if (EvaluateOptionalStoredValue(
                        storedValue, values, cancellationToken) is { } storedValueResult)
                {
                    return storedValueResult;
                }
                return IrInterpreter.ValidateSequenceAccess(sequenceResult.Value!, indexResult.Value!);
            default:
                return IrEvaluationResult.FromUnsupported(IrUnsupportedReason.UnsupportedOperation,
                    "Unknown IR location kind: " + location.Kind + ".");
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
            var receiverResult = _terms.Evaluate(receiver, values.Current, values.ObserveRead, cancellationToken);
            if (receiverResult.Status != IrEvaluationStatus.Value)
            {
                return receiverResult;
            }

            receiverValue = receiverResult.Value;
        }
        foreach (var argument in arguments)
        {
            var argumentResult = _terms.Evaluate(argument, values.Current, values.ObserveRead, cancellationToken);
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

        var result = _terms.Evaluate(storedValue, values.Current, values.ObserveRead, cancellationToken);
        return result.Status == IrEvaluationStatus.Value ? null : result;
    }

    private static IrProgramExecutionResult FromEvaluation(IrEvaluationResult evaluation, IrInstruction instruction,
        ReplayValues values, int steps)
    {
        return new(evaluation.Status == IrEvaluationStatus.Exception ? IrProgramExecutionStatus.Exception :
                IrProgramExecutionStatus.Unsupported, null, instruction,
            evaluation.Status == IrEvaluationStatus.Exception ? null : evaluation.Unsupported,
            evaluation.Status == IrEvaluationStatus.Exception ? evaluation.Exception : null,
            values.ToImmutable(), steps, values.ConsumedApproximation, values.ApproximationVariables);
    }

    private IrEvaluationResult EvaluateCondition(
        IrTerm condition,
        ReplayValues values,
        CancellationToken cancellationToken,
        out bool value)
    {
        value = false;
        var result = _terms.Evaluate(condition, values.Current, values.ObserveRead, cancellationToken);
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
                null, values.ToImmutable(), steps, values.ConsumedApproximation, values.ApproximationVariables);
    }

    private static IrProgramExecutionResult Result(IrProgramExecutionStatus status, IrInstruction? instruction,
            ReplayValues values,
            int steps, IrValue? returnValue = null)
    {
        return new(status, returnValue, instruction, null, null, values.ToImmutable(), steps, values.ConsumedApproximation, values.ApproximationVariables);
    }

    private sealed class ReplayValues
    {
        private readonly ImmutableDictionary<IrVarId, IrValue>.Builder _values = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        private readonly HashSet<IrVarId> _approximations = [];
        internal bool ConsumedApproximation { get; private set; }
        internal ImmutableHashSet<IrVarId> ApproximationVariables => _approximations.ToImmutableHashSet();
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
                ConsumedApproximation = true;
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
