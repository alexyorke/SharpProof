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
            var execution = new IrProgramInterpreter(factory).Execute(
                program, initial.ToImmutable(), context.MaximumSteps, context.CallHost, cancellationToken);
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
        }
        foreach (var domain in context.IntegerDomains)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (final.TryGetValue(domain.Key, out var value) &&
                (value.Kind != IrValueKind.Integer || value.Integer < domain.Value.Minimum ||
                    value.Integer > domain.Value.Maximum))
            {
                return AbstentionReason.CounterexampleReplayFailed;
            }
        }
        var evaluated = new IrInterpreter(factory).Evaluate(context.Postcondition, final, cancellationToken);
        if (evaluated.Status == IrEvaluationStatus.Exception)
        {
            return AbstentionReason.PostconditionMayBeUndefined;
        }
        return evaluated.Status == IrEvaluationStatus.Value &&
            evaluated.Value is { Kind: IrValueKind.Boolean, Boolean: false }
                ? null
                : AbstentionReason.CounterexampleReplayFailed;
    }
}
