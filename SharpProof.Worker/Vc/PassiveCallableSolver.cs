namespace SharpProof.Worker;

internal sealed record PassiveCallableCheckResult(ProofOutcome? Outcome, WorkerClaimReason Reason,
    ImmutableDictionary<IrVarId, IrValue> EntryModel, ImmutableArray<string> Core,
    ImmutableArray<OperationId> BodyAssumptions, bool QueryCompleted = false, IrExceptionInfo? ExceptionWitness = null,
    OperationId? AllocationWitness = null, bool HasFeasibleEntryWitness = false);

internal enum PassiveCallableFeasibilityKind { Feasible, ContradictoryEntry, NoModeledNormalReturn, Unknown }

internal sealed record PassiveCallableFeasibility(PassiveCallableFeasibilityKind Kind,
    PassiveCallableCheckResult Evidence, PassiveCallableCheckResult EntryEvidence);

// One session per owned plan; the existing method meter reads the actual solver
// consumption. Exception checks remain shadow evidence during effect migration.
internal sealed class PassiveCallableSolver : IDisposable
{
    private readonly PassiveCallableVcPlan _plan;
    private readonly CallableSolverSession? _session;
    private readonly Func<long>? _readConsumedResourceCount;
    private readonly MethodResourceBudget _budget;
    private readonly ProofKernel _kernel;
    private bool _entryFeasible;

    internal PassiveCallableSolver(PassiveCallableVcPlan plan,
        uint queryRlimit = WorkerBudgets.DefaultQueryRlimit, uint methodRlimit = WorkerBudgets.DefaultMethodRlimit)
    {
        _plan = ArgumentNullGuard.NotNull(plan, nameof(plan));
        if (plan.IsBoundedSearch)
        { throw new ArgumentException("A bounded witness encoding cannot own a proof session.", nameof(plan)); }
        ArgumentOutOfRangeException.ThrowIfLessThan(methodRlimit, queryRlimit);
        _session = new(plan.Factory, new IrSmtBackendOptions(queryRlimit));
        _budget = new(() => _session.ConsumedResourceCount, queryRlimit, methodRlimit);
        _kernel = new(_session);
    }

    internal PassiveCallableSolver(PassiveCallableVcPlan plan, ISmtBackend backend,
        MethodResourceBudget resourceBudget, Func<long>? readConsumedResourceCount = null)
    {
        _plan = ArgumentNullGuard.NotNull(plan, nameof(plan));
        if (plan.IsBoundedSearch)
        { throw new ArgumentException("A bounded witness encoding cannot own a proof session.", nameof(plan)); }
        _budget = ArgumentNullGuard.NotNull(resourceBudget, nameof(resourceBudget));
        _kernel = new(ArgumentNullGuard.NotNull(backend, nameof(backend)));
        _readConsumedResourceCount = readConsumedResourceCount;
    }

    internal long ConsumedResourceCount => _session?.ConsumedResourceCount ?? _readConsumedResourceCount?.Invoke() ?? 0;
    internal Task<PassiveCallableCheckResult> VerifyEntryAsync(CancellationToken cancellationToken = default)
    { return VerifyAsync(_plan.EntryQuery(), null, cancellationToken); }

    internal async Task<PassiveCallableCheckResult> VerifyAllocationsAsync(CancellationToken cancellationToken = default)
    {
        if (_plan.HasBodyAbstraction || _plan.HasUnmodeledAllocations)
        { return new(null, WorkerClaimReason.UnsupportedBody, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
        var proof = await VerifyAsync(_plan.AllocationQuery(), null, cancellationToken).ConfigureAwait(false);
        if (proof.Outcome is ProvenOutcome)
        { return proof; }
        var encoding = _plan.LoopSearch ?? _plan;
        var witness = _plan.LoopSearch == null ? proof
            : await VerifyAsync(encoding.AllocationQuery(), null, cancellationToken, encoding).ConfigureAwait(false);
        if (witness.Outcome is not RefutedOutcome)
        { return witness.Outcome is ProvenOutcome ? Inconclusive() : witness; }
        OperationId? site = null;
        var replay = _plan.ReplayEffects(witness.EntryModel, cancellationToken, allocation => site ??= allocation.Operation);
        if (site != null && !replay.ConsumedApproximation)
        { return witness with { AllocationWitness = site }; }
        return new(null, WorkerClaimReason.CounterexampleNotReplayable, ImmutableDictionary<IrVarId, IrValue>.Empty, [], [], QueryCompleted: true);
    }

    internal async Task<PassiveCallableCheckResult> VerifyExceptionsAsync(ImmutableHashSet<IrExceptionKind> allowed,
        CancellationToken cancellationToken = default)
    {
        var query = _plan.ExceptionQuery(allowed);
        // Postcondition call abstractions do not yet encode all callee effects.
        if (_plan.HasBodyAbstraction)
        { return new(null, WorkerClaimReason.UnsupportedBody, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
        var proof = await VerifyAsync(query, null, cancellationToken).ConfigureAwait(false);
        if (proof.Outcome is ProvenOutcome)
        { return proof; }
        var encoding = _plan.LoopSearch ?? _plan;
        var witness = _plan.LoopSearch == null ? proof
            : await VerifyAsync(encoding.ExceptionQuery(allowed), null, cancellationToken, encoding).ConfigureAwait(false);
        if (witness.Outcome is not RefutedOutcome)
        { return witness.Outcome is ProvenOutcome ? Inconclusive() : witness; }
        if (!_plan.HasBodyAbstraction)
        {
            var replay = _plan.ReplayException(witness.EntryModel, cancellationToken);
            if (replay.Status == IrProgramExecutionStatus.Exception && !replay.ConsumedApproximation &&
                replay.Instruction is IrThrowInstruction && replay.Exception is { } exception && !allowed.Contains(exception.Kind))
            { return witness with { ExceptionWitness = exception }; }
        }
        return new(null, WorkerClaimReason.CounterexampleNotReplayable, ImmutableDictionary<IrVarId, IrValue>.Empty, [], [], QueryCompleted: true);
    }
    internal async Task<PassiveCallableCheckResult> VerifyEnsuresAsync(int ordinal, CancellationToken cancellationToken = default)
    {
        if (_plan.HasBodyAbstraction)
        { return await VerifyBodyAsync(_plan.EnsuresQuery(ordinal), _plan.Replay(ordinal), cancellationToken).ConfigureAwait(false); }
        if (_plan.LoopSearch is not { } search)
        { return await VerifyBodyAsync(_plan.EnsuresQuery(ordinal), _plan.Replay(ordinal), cancellationToken).ConfigureAwait(false); }
        var proof = await VerifyAsync(_plan.EnsuresQuery(ordinal), null, cancellationToken).ConfigureAwait(false);
        if (proof.Outcome is ProvenOutcome)
        { return proof; }
        var witness = await VerifyAsync(search.EnsuresQuery(ordinal), search.Replay(ordinal), cancellationToken, search).ConfigureAwait(false);
        // A cut model is never a refutation; finite search is never a proof.
        return witness.Outcome is RefutedOutcome or UnknownOutcome || witness.Outcome == null ? witness : Inconclusive();
    }

    internal async Task<PassiveCallableCheckResult> VerifyNormalCompletionAsync(CancellationToken cancellationToken = default)
    {
        if (_plan.HasBodyAbstraction)
        { return await VerifyBodyAsync(_plan.NormalCompletionQuery(), _plan.NormalCompletionReplay(), cancellationToken).ConfigureAwait(false); }
        if (_plan.LoopSearch is not { } search)
        { return await VerifyBodyAsync(_plan.NormalCompletionQuery(), _plan.NormalCompletionReplay(), cancellationToken).ConfigureAwait(false); }
        var proof = await VerifyAsync(_plan.NormalCompletionQuery(), null, cancellationToken).ConfigureAwait(false);
        if (proof.Outcome is ProvenOutcome)
        { return proof; }
        var witness = await VerifyAsync(search.NormalCompletionQuery(), search.NormalCompletionReplay(), cancellationToken, search).ConfigureAwait(false);
        return witness.Outcome is RefutedOutcome or UnknownOutcome || witness.Outcome == null ? witness : Inconclusive();
    }

    internal bool CanCheckWithoutNormalWitness => (_plan.LoopSearch != null || _plan.HasBodyAbstraction) && _entryFeasible;

    private async Task<PassiveCallableCheckResult> VerifyBodyAsync(VerificationQuery query,
        CallableReplayContext replay, CancellationToken cancellationToken)
    {
        var evidence = await VerifyAsync(query, _plan.HasBodyAbstraction ? null : replay, cancellationToken).ConfigureAwait(false);
        return _plan.HasBodyAbstraction && evidence.Outcome is RefutedOutcome
            ? new(null, WorkerClaimReason.CounterexampleNotReplayable, ImmutableDictionary<IrVarId, IrValue>.Empty, [], [], QueryCompleted: true)
            : evidence;
    }

    private static PassiveCallableCheckResult Inconclusive()
    { return new(null, WorkerClaimReason.SolverIncomplete, ImmutableDictionary<IrVarId, IrValue>.Empty, [], [], QueryCompleted: true); }

    internal Task<PassiveCallableFeasibility> VerifyFeasibilityAsync(CancellationToken cancellationToken = default)
    { return VerifyFeasibilityAsync(null, cancellationToken); }

    internal async Task<PassiveCallableFeasibility> VerifyFeasibilityAsync(
        Action<PassiveCallableCheckResult>? publishEntry, CancellationToken cancellationToken)
    {
        var entry = await VerifyEntryAsync(cancellationToken).ConfigureAwait(false);
        publishEntry?.Invoke(entry);
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.Outcome is ProvenOutcome)
        { return new(PassiveCallableFeasibilityKind.ContradictoryEntry, entry, entry); }
        if (entry.Outcome is not RefutedOutcome)
        { return new(PassiveCallableFeasibilityKind.Unknown, entry, entry); }
        _entryFeasible = true;
        var normal = await VerifyNormalCompletionAsync(cancellationToken).ConfigureAwait(false);
        return new(normal.Outcome switch
        {
            RefutedOutcome => PassiveCallableFeasibilityKind.Feasible,
            ProvenOutcome => PassiveCallableFeasibilityKind.NoModeledNormalReturn,
            _ => PassiveCallableFeasibilityKind.Unknown
        }, normal, entry);
    }

    private async Task<PassiveCallableCheckResult> VerifyAsync(VerificationQuery query,
        CallableReplayContext? replay, CancellationToken cancellationToken, PassiveCallableVcPlan? encoding = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_budget.TryStartQuery())
        { return new(null, WorkerClaimReason.ResourceLimit, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
        var outcome = replay == null ? await _kernel.VerifyAsync(query, cancellationToken).ConfigureAwait(false)
            : await _kernel.VerifyCallableAsync(query, replay, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (_budget.IsExceeded)
        { return new(null, WorkerClaimReason.ResourceLimit, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
        encoding ??= _plan;
        return new(outcome, outcome is UnknownOutcome unknown ? WorkerProjections.MapAbstention(unknown.Reason) : WorkerClaimReason.None,
            outcome is RefutedOutcome refuted ? encoding.ProjectModel(refuted) : ImmutableDictionary<IrVarId, IrValue>.Empty,
            outcome is ProvenOutcome proven ? encoding.CoreLabels(proven) : [],
            outcome is ProvenOutcome conditional ? encoding.UsedBodyAssumptions(conditional) : []);
    }

    public void Dispose()
    { _session?.Dispose(); }
}
