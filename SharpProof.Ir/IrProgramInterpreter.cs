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
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullGuard.NotNull(program, nameof(program));

        if (!ReferenceEquals(program.Factory, _factory))
        {
            throw new ArgumentException("The program belongs to a different IR factory.", nameof(program));
        }

        maximumSteps = ArgumentNullGuard.RequirePositive(
            maximumSteps, nameof(maximumSteps));

        var values = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
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
                    case IrAssignInstruction assign:
                        var assigned = _terms.Evaluate(assign.Value, values, cancellationToken);
                        if (assigned.Status != IrEvaluationStatus.Value)
                        {
                            return FromEvaluation(assigned, assign, values, steps);
                        }

                        values[assign.Target] = assigned.Value!;
                        break;
                    case IrAssumeInstruction or IrAssertInstruction:
                        var testedCondition = instruction is IrAssumeInstruction assume
                            ? assume.Condition
                            : ((IrAssertInstruction)instruction).Condition;
                        var tested = EvaluateCondition(
                            testedCondition,
                            values,
                            cancellationToken);
                        if (tested.Status != IrEvaluationStatus.Value)
                        {
                            return FromEvaluation(tested, instruction, values, steps);
                        }

                        if (!tested.Value!.Boolean)
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
                            cancellationToken);
                        if (condition.Status != IrEvaluationStatus.Value)
                        {
                            return FromEvaluation(condition, branch, values, steps);
                        }

                        current = condition.Value!.Boolean ? branch.WhenTrue : branch.WhenFalse;
                        goto NextBlock;
                    case IrGotoInstruction go:
                        current = go.Target;
                        goto NextBlock;
                    case IrReturnInstruction returned:
                        if (returned.Value == null)
                        {
                            return Result(IrProgramExecutionStatus.Returned, returned, values, steps);
                        }

                        var returnValue = _terms.Evaluate(returned.Value, values, cancellationToken);
                        if (returnValue.Status != IrEvaluationStatus.Value)
                        {
                            return FromEvaluation(returnValue, returned, values, steps);
                        }

                        return Result(IrProgramExecutionStatus.Returned, returned, values, steps, returnValue.Value);
                    case IrHavocInstruction havoc:
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
                        var callOperands = EvaluateCallOperands(
                            call.Receiver, call.Arguments, storedValue: null, values,
                            "The call receiver is null.", cancellationToken);
                        if (callOperands != null)
                        {
                            return FromEvaluation(callOperands, call, values, steps);
                        }

                        return Unsupported(call, values, steps, "Concrete execution requires a call host.");
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
        IReadOnlyDictionary<IrVarId, IrValue> values,
        CancellationToken cancellationToken)
    {
        switch (location)
        {
            case IrMemberLocation member:
                return EvaluateCallOperands(
                    member.Receiver, member.Arguments, storedValue, values,
                    "The member access receiver is null.", cancellationToken);
            case IrSequenceLocation sequence:
                var sequenceResult = _terms.Evaluate(sequence.Sequence, values, cancellationToken);
                if (sequenceResult.Status != IrEvaluationStatus.Value)
                {
                    return sequenceResult;
                }

                var indexResult = _terms.Evaluate(sequence.Index, values, cancellationToken);
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
        IReadOnlyDictionary<IrVarId, IrValue> values, string nullReceiverDetail,
        CancellationToken cancellationToken)
    {
        IrValue? receiverValue = null;
        if (receiver != null)
        {
            var receiverResult = _terms.Evaluate(receiver, values, cancellationToken);
            if (receiverResult.Status != IrEvaluationStatus.Value)
            {
                return receiverResult;
            }

            receiverValue = receiverResult.Value;
        }
        foreach (var argument in arguments)
        {
            var argumentResult = _terms.Evaluate(argument, values, cancellationToken);
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
        IReadOnlyDictionary<IrVarId, IrValue> values,
        CancellationToken cancellationToken)
    {
        if (storedValue == null)
        {
            return null;
        }

        var result = _terms.Evaluate(storedValue, values, cancellationToken);
        return result.Status == IrEvaluationStatus.Value ? null : result;
    }

    private static IrProgramExecutionResult FromEvaluation(IrEvaluationResult evaluation, IrInstruction instruction,
        ImmutableDictionary<IrVarId, IrValue>.Builder values, int steps)
    {
        return new(evaluation.Status == IrEvaluationStatus.Exception ? IrProgramExecutionStatus.Exception :
                IrProgramExecutionStatus.Unsupported, null, instruction,
            evaluation.Status == IrEvaluationStatus.Exception ? null : evaluation.Unsupported,
            evaluation.Status == IrEvaluationStatus.Exception ? evaluation.Exception : null,
            values.ToImmutable(), steps);
    }

    private IrEvaluationResult EvaluateCondition(
        IrTerm condition,
        IReadOnlyDictionary<IrVarId, IrValue> values,
        CancellationToken cancellationToken)
    {
        var result = _terms.Evaluate(condition, values, cancellationToken);
        if (result.Status != IrEvaluationStatus.Value)
        {
            return result;
        }

        return result.Value!.Kind == IrValueKind.Boolean
            ? result
            : IrEvaluationResult.FromUnsupported(
                IrUnsupportedReason.InvalidVariableValue,
                "Program conditions require boolean values.");
    }

    private static IrProgramExecutionResult Unsupported(IrInstruction instruction,
            ImmutableDictionary<IrVarId, IrValue>.Builder values, int steps, string detail)
    {
        return new(IrProgramExecutionStatus.Unsupported, null, instruction,
                new IrUnsupportedInfo(IrUnsupportedReason.UnsupportedOperation, detail),
                null, values.ToImmutable(), steps);
    }

    private static IrProgramExecutionResult Result(IrProgramExecutionStatus status, IrInstruction? instruction,
            ImmutableDictionary<IrVarId, IrValue>.Builder values,
            int steps, IrValue? returnValue = null)
    {
        return new(status, returnValue, instruction, null, null, values.ToImmutable(), steps);
    }
}
