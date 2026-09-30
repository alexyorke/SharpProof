namespace SharpProof.Worker;

internal sealed record PassiveCallableCheckResult(ProofOutcome? Outcome, WorkerClaimReason Reason,
    ImmutableDictionary<IrVarId, IrValue> EntryModel, ImmutableArray<string> Core,
    ImmutableArray<OperationId> BodyAssumptions);

internal enum PassiveCallableFeasibilityKind { Feasible, ContradictoryEntry, NoModeledNormalReturn, Unknown }

internal sealed record PassiveCallableFeasibility(PassiveCallableFeasibilityKind Kind, PassiveCallableCheckResult Evidence);

// One session per owned plan; the existing method meter reads the actual solver
// consumption. This is a standalone candidate API, never a worker authority.
internal sealed class PassiveCallableSolver : IDisposable
{
    private readonly PassiveCallableVcPlan _plan;
    private readonly CallableSolverSession _session;
    private readonly MethodResourceBudget _budget;
    private readonly ProofKernel _kernel;

    internal PassiveCallableSolver(PassiveCallableVcPlan plan,
        uint queryRlimit = WorkerBudgets.DefaultQueryRlimit, uint methodRlimit = WorkerBudgets.DefaultMethodRlimit)
    {
        _plan = ArgumentNullGuard.NotNull(plan, nameof(plan));
        ArgumentOutOfRangeException.ThrowIfLessThan(methodRlimit, queryRlimit);
        _session = new(plan.Factory, new IrSmtBackendOptions(queryRlimit));
        _budget = new(() => _session.ConsumedResourceCount, queryRlimit, methodRlimit);
        _kernel = new(_session);
    }

    internal long ConsumedResourceCount => _session.ConsumedResourceCount;
    internal Task<PassiveCallableCheckResult> VerifyEntryAsync(CancellationToken cancellationToken = default)
    { return VerifyAsync(_plan.EntryQuery(), null, cancellationToken); }
    internal Task<PassiveCallableCheckResult> VerifyEnsuresAsync(int ordinal, CancellationToken cancellationToken = default)
    { return VerifyAsync(_plan.EnsuresQuery(ordinal), _plan.Replay(ordinal), cancellationToken); }

    internal Task<PassiveCallableCheckResult> VerifyNormalCompletionAsync(CancellationToken cancellationToken = default)
    { return VerifyAsync(_plan.NormalCompletionQuery(), _plan.NormalCompletionReplay(), cancellationToken); }

    internal async Task<PassiveCallableFeasibility> VerifyFeasibilityAsync(CancellationToken cancellationToken = default)
    {
        var entry = await VerifyEntryAsync(cancellationToken).ConfigureAwait(false);
        if (entry.Outcome is ProvenOutcome)
        { return new(PassiveCallableFeasibilityKind.ContradictoryEntry, entry); }
        if (entry.Outcome is not RefutedOutcome)
        { return new(PassiveCallableFeasibilityKind.Unknown, entry); }
        var normal = await VerifyNormalCompletionAsync(cancellationToken).ConfigureAwait(false);
        return new(normal.Outcome switch
        {
            RefutedOutcome => PassiveCallableFeasibilityKind.Feasible,
            ProvenOutcome => PassiveCallableFeasibilityKind.NoModeledNormalReturn,
            _ => PassiveCallableFeasibilityKind.Unknown
        }, normal);
    }

    private async Task<PassiveCallableCheckResult> VerifyAsync(VerificationQuery query,
        CallableReplayContext? replay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_budget.TryStartQuery())
        { return new(null, WorkerClaimReason.ResourceLimit, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
        var outcome = replay == null ? await _kernel.VerifyAsync(query, cancellationToken).ConfigureAwait(false)
            : await _kernel.VerifyCallableAsync(query, replay, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (_budget.IsExceeded)
        { return new(null, WorkerClaimReason.ResourceLimit, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
        return new(outcome, outcome is UnknownOutcome unknown ? WorkerProjections.MapAbstention(unknown.Reason) : WorkerClaimReason.None,
            outcome is RefutedOutcome refuted ? _plan.ProjectModel(refuted) : ImmutableDictionary<IrVarId, IrValue>.Empty,
            outcome is ProvenOutcome proven ? _plan.CoreLabels(proven) : [],
            outcome is ProvenOutcome conditional ? _plan.UsedBodyAssumptions(conditional) : []);
    }

    public void Dispose()
    { _session.Dispose(); }
}
