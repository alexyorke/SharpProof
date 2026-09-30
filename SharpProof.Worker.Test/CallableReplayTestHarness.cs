using System.Collections.Immutable;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal static class CallableReplayTestHarness
{
    internal static WorkerClaimReason Replay(CompilerCallablePreparation target, int ordinal,
        ImmutableDictionary<IrVarId, IrValue> model, IReadOnlyList<CompilerPreparedClause> ensures,
        CancellationToken cancellationToken = default)
    {
        if ((uint)ordinal >= (uint)ensures.Count)
        {
            return WorkerClaimReason.CounterexampleReplayFailed;
        }
        var factory = target.Factory;
        var query = new VerificationQuery(factory, [],
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(ordinal)),
            [.. model.Keys]);
        var outcome = new ProofKernel(new ModelBackend(model)).VerifyCallableAsync(query,
            CallableReplayContextBuilder.Create(target, ensures[ordinal].Condition), cancellationToken)
            .GetAwaiter().GetResult();
        return outcome is UnknownOutcome unknown
            ? WorkerProjections.MapAbstention(unknown.Reason)
            : WorkerClaimReason.None;
    }

    private sealed class ModelBackend(ImmutableDictionary<IrVarId, IrValue> model) : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BackendCheckResult.Satisfiable(new BackendModel(model)));
        }
    }
}
