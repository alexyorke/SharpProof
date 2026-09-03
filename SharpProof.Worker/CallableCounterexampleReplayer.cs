namespace SharpProof.Worker;
internal static partial class CallableCounterexampleReplayer
{
    internal static WorkerClaimReason Replay(CompilerCallablePreparation target, int claimOrdinal,
        ImmutableDictionary<IrVarId, IrValue> model,
        IReadOnlyList<CompilerPreparedClause> preparedEnsures,
        CancellationToken cancellationToken = default)
    {
        if (target.Body is not { } body)
        {
            return WorkerClaimReason.CounterexampleReplayFailed;
        }

        try
        {
            var factory = target.Factory;
            if ((uint)claimOrdinal >= (uint)preparedEnsures.Count)
            {
                return WorkerClaimReason.CounterexampleReplayFailed;
            }

            var final = model.ToBuilder();
            var variables = new List<CompilerCanonicalVariable>(
                target.Variables.Length);
            var results = new List<CompilerCanonicalVariable>();
            var preStateVariables = new List<CompilerCanonicalVariable>();
            foreach (var variable in target.Variables)
            {
                variables.Add(variable);
                if (variable.Role == CompilerVariableRole.Result)
                {
                    results.Add(variable);
                }
                else if (variable.Role == CompilerVariableRole.PreState)
                {
                    preStateVariables.Add(variable);
                }
            }

            if (body.Kind == CompilerPreparedBodyKind.Program)
            {
                if (body.Program is not { } program || !ReferenceEquals(program.Factory, factory))
                {
                    return WorkerClaimReason.CounterexampleReplayFailed;
                }

                var initial = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
                foreach (var binding in body.ParameterBindings)
                {
                    if (!model.TryGetValue(binding.Value, out var value))
                    {
                        return WorkerClaimReason.CounterexampleReplayFailed;
                    }

                    initial.Add(binding.Key, value);
                }
                var maximumSteps = program.Blocks.Sum(static block => (long)block.Instructions.Length);
                if (maximumSteps is < 1 or > CompilerPreparedBody.MaximumInstructions)
                {
                    return WorkerClaimReason.CounterexampleReplayFailed;
                }

                var execution = new IrProgramInterpreter(factory).Execute(
                    program, initial.ToImmutable(), (int)maximumSteps, cancellationToken);
                if (execution.Status != IrProgramExecutionStatus.Returned)
                {
                    return execution is { Status: IrProgramExecutionStatus.Unsupported, Instruction: IrCallInstruction call } &&
                           (body.SpecCalls.ContainsKey(call.Id) ||
                            body.SummaryCalls.ContainsKey(call.Id))
                        ? WorkerClaimReason.CounterexampleNotReplayable : WorkerClaimReason.CounterexampleReplayFailed;
                }

                foreach (var binding in body.ParameterBindings)
                {
                    if (!execution.Values.TryGetValue(binding.Key, out var value))
                    {
                        return WorkerClaimReason.CounterexampleReplayFailed;
                    }

                    final[binding.Value] = value;
                }
                if (results.Count == 0 && execution.ReturnValue != null ||
                    results.Count > 1 || results.Count == 1 &&
                    (execution.ReturnValue == null || execution.ReturnValue.Type !=
                     factory.GetVariableInfo(results[0].Variable).Type))
                {
                    return WorkerClaimReason.CounterexampleReplayFailed;
                }

                if (results.Count == 1)
                {
                    final[results[0].Variable] = execution.ReturnValue!;
                }
            }
            else if (body.Kind != CompilerPreparedBodyKind.Trivial || body.Program != null ||
                     !body.ParameterBindings.IsEmpty || !body.SpecCalls.IsEmpty ||
                     !body.SummaryCalls.IsEmpty ||
                     results.Count != 0)
            {
                return WorkerClaimReason.CounterexampleReplayFailed;
            }

            foreach (var variable in preStateVariables)
            {
                if (!variable.CurrentStateVariable.HasValue ||
                    !model.TryGetValue(variable.CurrentStateVariable.Value, out var value) ||
                    value.Type != factory.GetVariableInfo(variable.Variable).Type)
                {
                    return WorkerClaimReason.CounterexampleReplayFailed;
                }

                final[variable.Variable] = value;
            }

            foreach (var variable in variables)
            {
                if (!final.TryGetValue(variable.Variable, out var value))
                {
                    continue;
                }

                if (!CompilerSourceIntegerDomain.Contains(
                        variable.SourceIntegerInterval,
                        value))
                {
                    return WorkerClaimReason.CounterexampleReplayFailed;
                }
            }

            var evaluated = new IrInterpreter(factory).Evaluate(
                preparedEnsures[claimOrdinal].Condition, final, cancellationToken);
            return evaluated.Status == IrEvaluationStatus.Exception ? WorkerClaimReason.PostconditionMayBeUndefined :
                evaluated.Status == IrEvaluationStatus.Value &&
                evaluated.Value is { Kind: IrValueKind.Boolean, Boolean: false }
                    ? WorkerClaimReason.None : WorkerClaimReason.CounterexampleReplayFailed;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return WorkerClaimReason.CounterexampleReplayFailed;
        }
    }
}
