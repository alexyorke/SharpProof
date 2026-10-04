namespace SharpProof.Verify;

internal static class CallableReplayValidator
{
    internal static AbstentionReason? Validate(
        IrFactory factory, CallableReplayContext context,
        ImmutableDictionary<IrVarId, IrValue> model, CancellationToken cancellationToken)
    {
        try
        {
            return ValidateCore(factory, context, model, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return AbstentionReason.CounterexampleReplayFailed;
        }
    }

    private static AbstentionReason? ValidateCore(
        IrFactory factory, CallableReplayContext context,
        ImmutableDictionary<IrVarId, IrValue> model, CancellationToken cancellationToken)
    {
        var final = model.ToBuilder();
        var approximationVariables = new HashSet<IrVarId>();
        IReadOnlyDictionary<IrValue, IrValue[]>? heap = null;
        if (!context.IsTrivial)
        {
            if (context.Program is not { } program || !ReferenceEquals(program.Factory, factory) ||
                context.MaximumSteps is < 1 or > 4096)
            {
                return AbstentionReason.CounterexampleReplayFailed;
            }
            var initial = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
            foreach (var binding in context.ParameterBindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!model.TryGetValue(binding.Value, out var value))
                {
                    return AbstentionReason.CounterexampleReplayFailed;
                }
                initial[binding.Key] = value;
            }
            var replayOptions = factory.Semantics != IrExecutionSemantics.Total ? context.ReplayOptions : new IrProgramReplayOptions(request =>
                request.Origin switch
                {
                    IrHavocOrigin.Input => initial.TryGetValue(request.Variable, out var entry) ? entry : null,
                    IrHavocOrigin.SpecResult => null,
                    IrHavocOrigin.Approximation => context.ReplayOptions?.HavocValueProvider(request),
                    _ => null
                });
            var execution = new IrProgramInterpreter(factory).Execute(
                program, initial.ToImmutable(), context.MaximumSteps, context.CallHost, replayOptions, cancellationToken);
            if (execution.ConsumedApproximation)
            {
                return AbstentionReason.CounterexampleNotReplayable;
            }
            heap = execution.Heap;
            // An old element read sees the arrays as the callable entered; the
            // final heap holds only their current contents.
            if (heap is { Count: > 0 } && (ReadsOldElements(context.Postcondition, context) ||
                context.PostconditionGuard is { } oldGuard && ReadsOldElements(oldGuard, context)))
            {
                return AbstentionReason.CounterexampleNotReplayable;
            }
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
                if (!execution.Values.TryGetValue(binding.Key, out var value))
                {
                    return AbstentionReason.CounterexampleReplayFailed;
                }
                final[binding.Value] = value;
                approximationVariables.Remove(binding.Value);
                if (execution.ApproximationVariables.Contains(binding.Key))
                {
                    approximationVariables.Add(binding.Value);
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
                final[context.ResultVariables[0]] = execution.ReturnValue!;
                approximationVariables.Remove(context.ResultVariables[0]);
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
            final[binding.Key] = value;
            approximationVariables.Remove(binding.Key);
        }
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
            if (approximationVariables.Contains(variable))
            {
                consumedApproximation = true;
            }
        }
        var interpreter = new IrInterpreter(factory);
        if (factory.Semantics == IrExecutionSemantics.Total)
        {
            if (context.PostconditionGuard is not { } guard || guard.Type != factory.BooleanType)
            {
                return AbstentionReason.CounterexampleNotReplayable;
            }
            var defined = interpreter.Evaluate(guard, final, ObserveRead, heap, cancellationToken);
            if (consumedApproximation)
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
        var evaluated = interpreter.Evaluate(context.Postcondition, final, ObserveRead, heap, cancellationToken);
        if (consumedApproximation)
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

    private static bool ReadsOldElements(IrTerm root, CallableReplayContext context)
    {
        return IrTraversal.Any(root, term => term is IrSequenceAccessTerm access &&
            IrTraversal.CollectVariables(access.Sequence).Any(variable => context.PreStateBindings.ContainsKey(variable)));
    }
}
