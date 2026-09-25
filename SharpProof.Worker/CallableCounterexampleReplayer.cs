namespace SharpProof.Worker;
internal static partial class CallableCounterexampleReplayer
{
    internal static WorkerClaimReason Replay(CompilerCallablePreparation target, int claimOrdinal,
        ImmutableDictionary<IrVarId, IrValue> model,
        IReadOnlyList<CompilerPreparedClause> preparedEnsures,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if ((uint)claimOrdinal >= (uint)preparedEnsures.Count)
            {
                return WorkerClaimReason.CounterexampleReplayFailed;
            }

            return CompilerCallablePostconditionReplay.Replay(
                target,
                model,
                preparedEnsures[claimOrdinal].Condition,
                rejectUnexpectedReturnValue: true,
                cancellationToken,
                (call, receiver, arguments) => ReplayRegisteredSpecCall(
                    target, call, receiver, arguments)) switch
            {
                CompilerCallableReplayStatus.Refuted => WorkerClaimReason.None,
                CompilerCallableReplayStatus.PostconditionUndefined =>
                    WorkerClaimReason.PostconditionMayBeUndefined,
                CompilerCallableReplayStatus.UnsupportedRegisteredCall =>
                    WorkerClaimReason.CounterexampleNotReplayable,
                _ => WorkerClaimReason.CounterexampleReplayFailed
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return WorkerClaimReason.CounterexampleReplayFailed;
        }
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
