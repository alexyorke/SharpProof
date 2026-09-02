namespace SharpProof.Worker;

internal static partial class CallableCounterexampleReplayer
{
    internal static WorkerClaimReason Replay(
        CompilerCallablePreparation target,
        int claimOrdinal,
        ImmutableDictionary<IrVarId, IrValue> model,
        CancellationToken cancellationToken = default)
    {
        var preparedEnsures = target.Clauses.Where(static clause =>
            clause.Kind == CompilerContractKind.Ensures).ToArray();
        return Replay(
            target,
            claimOrdinal,
            model,
            preparedEnsures,
            cancellationToken);
    }
}
