namespace SharpProof.Worker;
#pragma warning disable IDE0055 // Compact replay kernel preserves the fixed production-size ceiling.
internal static class CallableCounterexampleReplayer {
    internal static bool TryReplay(CompilerCallablePreparation target, int claimOrdinal,
        ImmutableDictionary<IrVarId, IrValue> model, CancellationToken cancellationToken = default) {
        if (target == null || model == null || !target.IsSuccess ||
            target.Body is not { } body) return false;
        try {
            cancellationToken.ThrowIfCancellationRequested();
            var factory = target.Factory;
            var ensures = target.Clauses.Where(static clause =>
                clause.Kind == CompilerContractKind.Ensures).ToArray();
            if ((uint)claimOrdinal >= (uint)ensures.Length || model.Any(assignment =>
                    assignment.Value == null ||
                    factory.GetVariableInfo(assignment.Key).Type != assignment.Value.Type))
                return false;
            var final = model.ToBuilder();
            if (body.Kind == CompilerPreparedBodyKind.Program) {
                if (body.Program == null || !ReferenceEquals(body.Program.Factory, factory)) return false;
                var initial = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
                foreach (var binding in body.ParameterBindings) {
                    if (!model.TryGetValue(binding.Value, out var value) ||
                        factory.GetVariableInfo(binding.Key).Type != value.Type) return false;
                    initial.Add(binding.Key, value);
                }
                var maximumSteps = body.Program.Blocks.Sum(static block => (long)block.Instructions.Length);
                if (maximumSteps is < 1 or > CompilerPreparedBody.MaximumInstructions) return false;
                var execution = new IrProgramInterpreter(factory).Execute(
                    body.Program, initial.ToImmutable(), (int)maximumSteps, cancellationToken);
                if (execution.Status != IrProgramExecutionStatus.Returned) return false;
                foreach (var binding in body.ParameterBindings) {
                    if (!execution.Values.TryGetValue(binding.Key, out var value)) return false;
                    final[binding.Value] = value;
                }
                var results = target.Variables.Where(static variable =>
                    variable.Role == CompilerVariableRole.Result).ToArray();
                if (results.Length > 1 || results.Length == 1 &&
                    (execution.ReturnValue == null || execution.ReturnValue.Type !=
                     factory.GetVariableInfo(results[0].Variable).Type)) return false;
                if (results.Length == 1) final[results[0].Variable] = execution.ReturnValue!;
            }
            else if (body.Kind != CompilerPreparedBodyKind.Trivial || body.Program != null ||
                     !body.ParameterBindings.IsEmpty || !body.SpecCalls.IsEmpty ||
                     target.Variables.Any(static variable => variable.Role == CompilerVariableRole.Result))
                return false;
            foreach (var variable in target.Variables.Where(static variable =>
                         variable.Role == CompilerVariableRole.PreState)) {
                if (!variable.CurrentStateVariable.HasValue ||
                    !model.TryGetValue(variable.CurrentStateVariable.Value, out var value) ||
                    value.Type != factory.GetVariableInfo(variable.Variable).Type) return false;
                final[variable.Variable] = value;
            }
            var evaluated = new IrInterpreter(factory).Evaluate(
                ensures[claimOrdinal].Condition, final, cancellationToken);
            return evaluated.Status == IrEvaluationStatus.Value &&
                   evaluated.Value is { Kind: IrValueKind.Boolean, Boolean: false };
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
