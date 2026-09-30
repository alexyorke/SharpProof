namespace SharpProof.Worker;
internal static class CallableReplayContextBuilder
{
    internal static CallableReplayContext Create(CompilerCallablePreparation target, IrTerm postcondition)
    {
        var body = target.Body;
        var maximumSteps = body?.Program?.Blocks.Sum(static block => (long)block.Instructions.Length) ?? 0;
        return new CallableReplayContext(
            body?.Program,
            body?.Kind == CompilerPreparedBodyKind.Trivial,
            body?.ParameterBindings ?? ImmutableDictionary<IrVarId, IrVarId>.Empty,
            target.Variables.Where(static variable => variable.Role == CompilerVariableRole.PreState)
                .ToImmutableDictionary(static variable => variable.Variable,
                    static variable => variable.CurrentStateVariable),
            [.. target.Variables.Where(static variable => variable.Role == CompilerVariableRole.Result)
                .Select(static variable => variable.Variable)],
            postcondition,
            target.Variables.Where(static variable => variable.SourceIntegerInterval.HasValue)
                .ToImmutableDictionary(static variable => variable.Variable,
                    static variable => ((System.Numerics.BigInteger)variable.SourceIntegerInterval!.Value.Minimum,
                        (System.Numerics.BigInteger)variable.SourceIntegerInterval.Value.Maximum)),
            (int)Math.Min(maximumSteps, int.MaxValue),
            (body?.SpecCalls.Keys ?? []).Concat(body?.SummaryCalls.Keys ?? []).ToImmutableHashSet(),
            (call, receiver, arguments) => ReplayRegisteredSpecCall(target, call, receiver, arguments));
    }
    internal static IrValue? ReplayRegisteredSpecCall(
        CompilerCallablePreparation target,
        IrCallInstruction call,
        IrValue? receiver,
        ImmutableArray<IrValue> arguments)
    {
        if (target.Body is not { } body ||
            !body.SpecCalls.TryGetValue(call.Id, out var prepared))
        {
            return null;
        }

        return ApiSpecReplayCallHost.TryInvoke(
            target.Factory,
            prepared.CallIdentity,
            prepared.WitnessIdentifier,
            call,
            receiver,
            arguments);
    }
}
