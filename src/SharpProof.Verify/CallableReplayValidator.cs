namespace SharpProof.Verify;

internal static class CallableReplayValidator
{
    internal static bool EntryConstraintsHold(IrFactory factory, ImmutableArray<IrTerm> constraints,
        ImmutableDictionary<IrVarId, IrValue> inputs, CancellationToken cancellationToken)
    {

        RepositoryParameterGuard.RequireInitialized(constraints.IsDefault, nameof(constraints), "Array must be initialized.");
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        inputs = ArgumentNullGuard.NotNull(inputs, nameof(inputs));
        var interpreter = new IrInterpreter(factory);
        foreach (var constraint in constraints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evaluated = interpreter.Evaluate(constraint, inputs, cancellationToken: cancellationToken);
            if (evaluated.Status != IrEvaluationStatus.Value || evaluated.Value is not { Kind: IrValueKind.Boolean, Boolean: true })
            { return false; }
        }
        return true;
    }
    internal static AbstentionReason? Validate(
        IrFactory factory, CallableReplayContext context,
        ImmutableDictionary<IrVarId, IrValue> model, CancellationToken cancellationToken)
    {
        try
        {
            return ValidateCore(factory, context, model, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return AbstentionReason.CounterexampleReplayFailed;
        }
    }

    private static AbstentionReason? ValidateCore(
        IrFactory factory, CallableReplayContext context,
        ImmutableDictionary<IrVarId, IrValue> model, CancellationToken cancellationToken)
    {
        if (!EntryConstraintsHold(factory, context.EntryConstraints, model, cancellationToken))
        { return AbstentionReason.CounterexampleReplayFailed; }
        var finalBuilder = model.ToBuilder();
        var approximationBuilder = ImmutableHashSet.CreateBuilder<IrVarId>();
        IrHeap? heap = null;
        if (!context.IsTrivial)
        {
            if (context.Program is not { } program || !ReferenceEquals(program.Factory, factory) ||
                context.MaximumSteps is < 1 or > 4096)
            {
                return AbstentionReason.CounterexampleReplayFailed;
            }
            var initialBuffer = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
            foreach (var binding in context.ParameterBindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                initialBuffer[binding.Key] = GetRequiredValue(model, binding.Value);
            }
            var initial = initialBuffer.ToImmutable();
            var replayOptions = factory.Semantics != IrExecutionSemantics.Total ? context.ReplayOptions : new IrProgramReplayOptions(request =>
                request is null ? throw new System.ArgumentNullException(nameof(request)) : (request.Origin switch
                {
                    IrHavocOrigin.Input => initial.TryGetValue(request.Variable, out var entry) ? entry : null,
                    IrHavocOrigin.Approximation => context.ReplayOptions?.HavocValueProvider(request),
                    _ => null
                }))
            {
                SnapshotVariables = context.PreStateBindings.Keys.ToImmutableHashSet(),
                ConcreteHavocValueProvider = context.ReplayOptions?.ConcreteHavocValueProvider,
                ConcreteLockCompletionProvider = context.ReplayOptions?.ConcreteLockCompletionProvider
            };
            var execution = new IrProgramInterpreter(factory).Execute(
                program, initial, context.MaximumSteps, context.CallHost, replayOptions, cancellationToken);
            if (execution.ConsumedApproximation)
            {
                return AbstentionReason.CounterexampleNotReplayable;
            }
            heap = execution.Heap;
            if (execution.Status != IrProgramExecutionStatus.Returned)
            {
                return execution.Status == IrProgramExecutionStatus.Unsupported &&
                    (execution.Instruction is IrLockInstruction ||
                        execution.Instruction is IrCallInstruction call && context.RegisteredCalls.Contains(call.Id))
                    ? AbstentionReason.CounterexampleNotReplayable
                    : AbstentionReason.CounterexampleReplayFailed;
            }
            foreach (var binding in context.ParameterBindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                finalBuilder[binding.Value] = GetRequiredValue(execution.Values, binding.Key);
                approximationBuilder.Remove(binding.Value);
                if (execution.ApproximationVariables.Contains(binding.Key))
                {
                    approximationBuilder.Add(binding.Value);
                }
            }
            if (context.ResultVariables.Length > 1 ||
                context.ResultVariables.Length == 0 && execution.ReturnValue != null ||
                context.ResultVariables.Length == 1 &&
                (execution.ReturnValue == null || execution.ReturnValue.Type !=
                    factory.GetVariableInfo(context.ResultVariables[0]).Type))
            {
                return AbstentionReason.CounterexampleReplayFailed;
            }
            if (context.ResultVariables.Length == 1)
            {
                finalBuilder[context.ResultVariables[0]] = execution.ReturnValue!;
                approximationBuilder.Remove(context.ResultVariables[0]);
            }
        }
        else if (context.Program != null || !context.ParameterBindings.IsEmpty ||
                 !context.RegisteredCalls.IsEmpty || !context.ResultVariables.IsEmpty)
        {
            return AbstentionReason.CounterexampleReplayFailed;
        }
        foreach (var binding in context.PreStateBindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (binding.Value is not { } entryVariable || !model.TryGetValue(entryVariable, out var value) ||
                value.Type != factory.GetVariableInfo(binding.Key).Type)
            {
                return AbstentionReason.CounterexampleReplayFailed;
            }
            finalBuilder[binding.Key] = value;
            approximationBuilder.Remove(binding.Key);
        }
        var final = finalBuilder.ToImmutable();
        var approximationVariables = approximationBuilder.ToImmutable();
        foreach (var domain in context.IntegerDomains)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (final.TryGetValue(domain.Key, out var value) &&
                (value.Kind != IrValueKind.Integer || value.IntegerNumericValue < domain.Value.Minimum ||
                    value.IntegerNumericValue > domain.Value.Maximum))
            {
                return AbstentionReason.CounterexampleReplayFailed;
            }
        }
        var consumedApproximation = false;
        void ObserveRead(IrVarId variable)
        {
            RepositoryParameterGuard.ThrowIfInvalid(variable.IsDefault, nameof(variable));
            if (approximationVariables.Contains(variable))
            {
                consumedApproximation = true;
            }
        }
        var interpreter = new IrInterpreter(factory);
        // An Old read sees the objects as the callable entered.
        var snapshots = context.PreStateBindings.Keys.ToImmutableHashSet();
        if (factory.Semantics == IrExecutionSemantics.Total)
        {
            if (context.PostconditionGuard is not { } guard || guard.Type != factory.BooleanType)
            {
                return AbstentionReason.CounterexampleNotReplayable;
            }
            var defined = interpreter.Evaluate(guard, final, ObserveRead, heap, cancellationToken, snapshots);
            if (consumedApproximation || heap?.ConsumedApproximation == true)
            {
                return AbstentionReason.CounterexampleNotReplayable;
            }
            if (defined.Status != IrEvaluationStatus.Value || defined.Value is not { Kind: IrValueKind.Boolean })
            {
                return AbstentionReason.CounterexampleReplayFailed;
            }
            if (!defined.Value.Boolean)
            {
                return AbstentionReason.PostconditionMayBeUndefined;
            }
        }
        var evaluated = interpreter.Evaluate(context.Postcondition, final, ObserveRead, heap, cancellationToken, snapshots);
        if (consumedApproximation || heap?.ConsumedApproximation == true)
        {
            return AbstentionReason.CounterexampleNotReplayable;
        }
        if (evaluated.Status == IrEvaluationStatus.Exception)
        {
            return AbstentionReason.PostconditionMayBeUndefined;
        }
        return evaluated.Status == IrEvaluationStatus.Value &&
            evaluated.Value is { Kind: IrValueKind.Boolean, Boolean: false }
                ? null
                : AbstentionReason.CounterexampleReplayFailed;
    }

    private static IrValue GetRequiredValue(
        ImmutableDictionary<IrVarId, IrValue> values, IrVarId variable)
    {
        // Entry and returned bindings require the same concrete evidence.
        if (!values.TryGetValue(variable, out var value))
        {
            throw new KeyNotFoundException("A required replay binding is missing.");
        }
        return value;
    }
}
