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
            var replayOptions = context.ReplayOptions == null ? null : new IrProgramReplayOptions(request =>
                request.Origin switch
                {
                    IrHavocOrigin.Input => initial.TryGetValue(request.Variable, out var entry) ? entry : null,
                    IrHavocOrigin.SpecResult => null,
                    IrHavocOrigin.Approximation => context.ReplayOptions.HavocValueProvider(request),
                    _ => null
                });
            var execution = new IrProgramInterpreter(factory).Execute(
                program, initial.ToImmutable(), context.MaximumSteps, context.CallHost, replayOptions, cancellationToken);
            if (execution.ConsumedApproximation)
            {
                return AbstentionReason.CounterexampleNotReplayable;
            }
            if (execution.Status != IrProgramExecutionStatus.Returned)
            {
                return execution is
                {
                    Status: IrProgramExecutionStatus.Unsupported,
                    Instruction: IrCallInstruction call
                } && context.RegisteredCalls.Contains(call.Id)
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
        var observed = new ContractValues(final, approximationVariables);
        var interpreter = new IrInterpreter(factory);
        if (factory.Semantics == IrExecutionSemantics.Total)
        {
            if (context.PostconditionGuard is not { } guard || guard.Type != factory.BooleanType)
            {
                return AbstentionReason.CounterexampleNotReplayable;
            }
            var defined = interpreter.Evaluate(guard, observed, cancellationToken);
            if (observed.ConsumedApproximation)
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
        var evaluated = interpreter.Evaluate(context.Postcondition, observed, cancellationToken);
        if (observed.ConsumedApproximation)
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

    private sealed class ContractValues(
        IReadOnlyDictionary<IrVarId, IrValue> values,
        HashSet<IrVarId> approximations) : IReadOnlyDictionary<IrVarId, IrValue>
    {
        internal bool ConsumedApproximation { get; private set; }
        public IrValue this[IrVarId key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
        public IEnumerable<IrVarId> Keys => values.Keys;
        public IEnumerable<IrValue> Values => values.Values;
        public int Count => values.Count;
        public bool ContainsKey(IrVarId key)
        {
            return values.ContainsKey(key);
        }
        public bool TryGetValue(IrVarId key, out IrValue value)
        {
            var found = values.TryGetValue(key, out value!);
            if (found && approximations.Contains(key))
            {
                ConsumedApproximation = true;
            }
            return found;
        }
        public IEnumerator<KeyValuePair<IrVarId, IrValue>> GetEnumerator()
        {
            return values.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
