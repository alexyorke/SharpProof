namespace SharpProof.Worker;

internal sealed record PassiveCallableCheckResult(ProofOutcome? Outcome, WorkerClaimReason Reason,
    ImmutableDictionary<IrVarId, IrValue> EntryModel, ImmutableArray<string> Core,
    ImmutableArray<OperationId> BodyAssumptions, bool QueryCompleted = false, IrExceptionInfo? ExceptionWitness = null,
    OperationId? AllocationWitness = null, bool HasFeasibleEntryWitness = false, OperationId? WriteWitness = null,
    OperationId? LockWitness = null, OperationId? CallPreconditionWitness = null);

internal enum PassiveCallableFeasibilityKind { Feasible, ContradictoryEntry, NoModeledNormalReturn, Unknown }

internal sealed record PassiveCallableFeasibility(PassiveCallableFeasibilityKind Kind,
    PassiveCallableCheckResult Evidence, PassiveCallableCheckResult EntryEvidence);

// One session per owned plan; the existing method meter reads the actual solver
// consumption.
internal sealed class PassiveCallableSolver : IDisposable
{
    private readonly PassiveCallableVcPlan _plan;
    private readonly CallableSolverSession? _session;
    private readonly Func<long>? _readConsumedResourceCount;
    private readonly MethodResourceBudget _budget;
    private readonly ProofKernel _kernel;
    private bool _entryFeasible;
    // A normal return was found, but only through approximation havocs.
    private bool _approximateNormalReturn;
    private bool _invariantsSearched;
    private PassiveCallableVcPlan? _invariantPlan;
    private ImmutableArray<string> _invariantCore = [];
    private ImmutableArray<OperationId> _invariantAssumptions = [];

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

    internal Task<PassiveCallableCheckResult> VerifyAllocationsAsync(CancellationToken cancellationToken = default)
    { return VerifySitesAsync(allocations: true, cancellationToken); }

    internal Task<PassiveCallableCheckResult> VerifyPurityAsync(CancellationToken cancellationToken = default)
    { return VerifySitesAsync(allocations: false, cancellationToken); }

    // Only a concretely reached lock refutes a capability claim; a call's
    // specified capabilities are may-effects.
    internal async Task<PassiveCallableCheckResult> VerifyCapabilitiesAsync(bool synchronizationAllowed,
        Func<IrOpaqueCallEffects, bool> callViolates, CancellationToken cancellationToken = default)
    {
        if (_plan.HasBodyAbstraction)
        { return new(null, WorkerClaimReason.UnsupportedBody, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
        var proof = await VerifyAsync(_plan.CapabilityQuery(synchronizationAllowed, callViolates), null, cancellationToken).ConfigureAwait(false);
        if (proof.Outcome is ProvenOutcome)
        { return proof; }
        if (synchronizationAllowed)
        { return new(null, WorkerClaimReason.UnsupportedBody, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
        var search = _plan.LoopSearch ?? _plan;
        var witness = _plan.LoopSearch == null ? proof
            : await VerifyAsync(search.CapabilityQuery(synchronizationAllowed, callViolates), null, cancellationToken, search).ConfigureAwait(false);
        if (witness.Outcome is not RefutedOutcome)
        {
            return await ProveWithInvariantsAsync(witness, plan => plan.CapabilityQuery(synchronizationAllowed, callViolates), cancellationToken)
                .ConfigureAwait(false) ?? (witness.Outcome is ProvenOutcome ? Inconclusive() : witness);
        }
        OperationId? site = null;
        _plan.ReplayEffects(witness.EntryModel, cancellationToken, lockPrefixObserver: (instruction, approximation) =>
        { if (!approximation) { site ??= instruction.Operation; } });
        cancellationToken.ThrowIfCancellationRequested();
        return site != null ? witness with { LockWitness = site } :
            new(null, WorkerClaimReason.CounterexampleNotReplayable, ImmutableDictionary<IrVarId, IrValue>.Empty, [], [], QueryCompleted: true);
    }

    // A concretely reached violating write or lock refutes an effect summary;
    // reads and call effects are may-effects.
    internal async Task<PassiveCallableCheckResult> VerifyEffectSitesAsync(Func<IrWriteRegion, bool> writeViolates, bool readsViolate,
        bool locksViolate, Func<IrOpaqueCallEffects, bool> callViolates, CancellationToken cancellationToken = default)
    {
        if (_plan.HasBodyAbstraction)
        { return new(null, WorkerClaimReason.UnsupportedBody, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
        var proof = await VerifyAsync(_plan.EffectSiteQuery(writeViolates, readsViolate, locksViolate, callViolates), null, cancellationToken)
            .ConfigureAwait(false);
        if (proof.Outcome is ProvenOutcome)
        { return proof; }
        var encoding = _plan.LoopSearch ?? _plan;
        var witness = _plan.LoopSearch == null ? proof
            : await VerifyAsync(encoding.EffectSiteQuery(writeViolates, readsViolate, locksViolate, callViolates), null, cancellationToken, encoding)
                .ConfigureAwait(false);
        if (witness.Outcome is not RefutedOutcome)
        {
            return await ProveWithInvariantsAsync(witness, plan => plan.EffectSiteQuery(writeViolates, readsViolate, locksViolate, callViolates),
                cancellationToken).ConfigureAwait(false) ?? (witness.Outcome is ProvenOutcome ? Inconclusive() : witness);
        }
        OperationId? write = null;
        OperationId? synchronization = null;
        _plan.ReplayEffects(witness.EntryModel, cancellationToken,
            writePrefixObserver: (instruction, approximation) =>
            {
                if (!approximation && IrWriteSites.IsObservable(_plan.Factory, instruction) && writeViolates(instruction.Region))
                { write ??= instruction.Operation; }
            },
            lockPrefixObserver: locksViolate ? (instruction, approximation) =>
            {
                if (!approximation)
                { synchronization ??= instruction.Operation; }
            }
        : null);
        cancellationToken.ThrowIfCancellationRequested();
        return synchronization != null ? witness with { LockWitness = synchronization }
            : write != null ? witness with { WriteWitness = write }
            : new(null, WorkerClaimReason.CounterexampleNotReplayable, ImmutableDictionary<IrVarId, IrValue>.Empty, [], [], QueryCompleted: true);
    }

    private async Task<PassiveCallableCheckResult> VerifySitesAsync(bool allocations, CancellationToken cancellationToken)
    {
        if (_plan.HasBodyAbstraction || allocations && _plan.HasUnmodeledAllocations)
        { return new(null, WorkerClaimReason.UnsupportedBody, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
        var proof = await VerifyAsync(allocations ? _plan.AllocationQuery() : _plan.PurityQuery(), null, cancellationToken).ConfigureAwait(false);
        if (proof.Outcome is ProvenOutcome)
        { return proof; }
        var encoding = _plan.LoopSearch ?? _plan;
        var witness = _plan.LoopSearch == null ? proof
            : await VerifyAsync(allocations ? encoding.AllocationQuery() : encoding.PurityQuery(), null, cancellationToken, encoding).ConfigureAwait(false);
        if (witness.Outcome is not RefutedOutcome)
        {
            return await ProveWithInvariantsAsync(witness, plan => allocations ? plan.AllocationQuery() : plan.PurityQuery(), cancellationToken)
                .ConfigureAwait(false) ?? (witness.Outcome is ProvenOutcome ? Inconclusive() : witness);
        }
        OperationId? site = null;
        OperationId? synchronizationSite = null;
        _plan.ReplayEffects(witness.EntryModel, cancellationToken,
            allocationPrefixObserver: allocations ? (allocation, approximation) =>
        {
            if (!approximation)
            { site ??= allocation.Operation; }
        }
        : null,
            writePrefixObserver: allocations ? null : (write, approximation) =>
        {
            if (!approximation && IrWriteSites.IsObservable(_plan.Factory, write))
            { site ??= write.Operation; }
        }, lockPrefixObserver: allocations ? null : (synchronization, approximation) =>
        {
            if (!approximation)
            { synchronizationSite ??= synchronization.Operation; }
        });
        cancellationToken.ThrowIfCancellationRequested();
        if (synchronizationSite != null)
        { return witness with { LockWitness = synchronizationSite }; }
        if (site != null)
        { return allocations ? witness with { AllocationWitness = site } : witness with { WriteWitness = site }; }
        return new(null, WorkerClaimReason.CounterexampleNotReplayable, ImmutableDictionary<IrVarId, IrValue>.Empty, [], [], QueryCompleted: true);
    }

    // An explicit throw site is allowed when its static type derives from an
    // allowed type. Its exception refutes the claim when the claim allows no
    // exception, or when the site creates an exception of a disallowed type.
    internal async Task<PassiveCallableCheckResult> VerifyExceptionsAsync(ImmutableHashSet<IrExceptionKind> allowed,
        bool allowsNoException = false, Func<OperationId, bool>? allowedSite = null, Func<OperationId, bool>? exactSite = null,
        CancellationToken cancellationToken = default)
    {
        var query = _plan.ExceptionQuery(allowed, allowedSite);
        // Postcondition call abstractions do not yet encode all callee effects.
        if (_plan.HasBodyAbstraction)
        { return new(null, WorkerClaimReason.UnsupportedBody, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
        var proof = await VerifyAsync(query, null, cancellationToken).ConfigureAwait(false);
        if (proof.Outcome is ProvenOutcome)
        { return proof; }
        var encoding = _plan.LoopSearch ?? _plan;
        var witness = _plan.LoopSearch == null ? proof
            : await VerifyAsync(encoding.ExceptionQuery(allowed, allowedSite), null, cancellationToken, encoding).ConfigureAwait(false);
        if (witness.Outcome is not RefutedOutcome)
        {
            return await ProveWithInvariantsAsync(witness, plan => plan.ExceptionQuery(allowed, allowedSite), cancellationToken)
                .ConfigureAwait(false) ?? (witness.Outcome is ProvenOutcome ? Inconclusive() : witness);
        }
        if (!_plan.HasBodyAbstraction)
        {
            var replay = _plan.ReplayException(witness.EntryModel, cancellationToken);
            if (replay.Status == IrProgramExecutionStatus.Exception && !replay.ConsumedApproximation &&
                replay.Instruction is IrThrowInstruction && replay.Exception is { } exception && !allowed.Contains(exception.Kind) &&
                (exception.Kind != IrExceptionKind.Explicit || allowsNoException ||
                    exception.Site is { } site && exactSite?.Invoke(site) == true && allowedSite?.Invoke(site) != true))
            { return witness with { ExceptionWitness = exception }; }
        }
        return new(null, WorkerClaimReason.CounterexampleNotReplayable, ImmutableDictionary<IrVarId, IrValue>.Empty, [], [], QueryCompleted: true);
    }
    internal async Task<PassiveCallableCheckResult> VerifyCallPreconditionAsync(int ordinal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var queries = _plan.CallPreconditionQueries(ordinal);
        if (_plan.HasBodyAbstraction)
        { return new(null, WorkerClaimReason.UnsupportedBody, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
        PassiveCallableCheckResult? proven = null;
        PassiveCallableCheckResult? incomplete = null;
        var cores = new HashSet<string>(StringComparer.Ordinal);
        var assumptions = new HashSet<OperationId>();
        foreach (var query in queries)
        {
            var evidence = await VerifyAsync(query, null, cancellationToken).ConfigureAwait(false);
            if (evidence.Reason == WorkerClaimReason.ResourceLimit)
            { return evidence; }
            if (evidence.Outcome is ProvenOutcome)
            {
                proven ??= evidence;
                cores.UnionWith(evidence.Core);
                assumptions.UnionWith(evidence.BodyAssumptions);
                continue;
            }
            if (evidence.Outcome is RefutedOutcome &&
                _plan.ReplayCallPrecondition(ordinal, evidence.EntryModel, cancellationToken) is { } site)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return evidence with { CallPreconditionWitness = site };
            }
            incomplete ??= evidence.Outcome is RefutedOutcome
                ? new(null, WorkerClaimReason.CounterexampleNotReplayable, ImmutableDictionary<IrVarId, IrValue>.Empty, [], [], QueryCompleted: true)
                : evidence;
        }
        if (incomplete == null && proven != null)
        {
            return proven with
            {
                Core = [.. cores.OrderBy(value => value, StringComparer.Ordinal)],
                BodyAssumptions = [.. assumptions.OrderBy(value => value.Value)]
            };
        }
        if (_plan.LoopSearch is { } search)
        {
            foreach (var query in search.CallPreconditionQueries(ordinal))
            {
                var evidence = await VerifyAsync(query, null, cancellationToken, search).ConfigureAwait(false);
                if (evidence.Reason == WorkerClaimReason.ResourceLimit)
                { return evidence; }
                if (evidence.Outcome is RefutedOutcome &&
                    _plan.ReplayCallPrecondition(ordinal, evidence.EntryModel, cancellationToken) is { } site)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return evidence with { CallPreconditionWitness = site };
                }
            }
        }
        return incomplete ?? Inconclusive();
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
        if (await ProveWithInvariantsAsync(witness, plan => plan.EnsuresQuery(ordinal), cancellationToken).ConfigureAwait(false) is { } proven)
        { return proven; }
        // A cut model is never a refutation; finite search is never a proof.
        return witness.Outcome is RefutedOutcome or UnknownOutcome || witness.Outcome == null ? witness : Inconclusive();
    }

    // Retries a goal the loop cut could not prove with the checked loop
    // invariants assumed; null when they do not prove it.
    private async Task<PassiveCallableCheckResult?> ProveWithInvariantsAsync(PassiveCallableCheckResult witness,
        Func<PassiveCallableVcPlan, VerificationQuery> query, CancellationToken cancellationToken)
    {
        if (_plan.LoopSearch == null || witness.Outcome is RefutedOutcome || witness.Reason == WorkerClaimReason.ResourceLimit ||
            await InvariantPlanAsync(cancellationToken).ConfigureAwait(false) is not { } invariants)
        { return null; }
        var proven = await VerifyAsync(query(invariants), null, cancellationToken, invariants).ConfigureAwait(false);
        return proven.Outcome is ProvenOutcome ? WithInvariantPremises(proven) : null;
    }

    // A proof over assumed invariants also rests on the premises that proved them.
    private PassiveCallableCheckResult WithInvariantPremises(PassiveCallableCheckResult proven)
    {
        return proven with
        {
            Core = [.. proven.Core.Union(_invariantCore, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)],
            BodyAssumptions = [.. proven.BodyAssumptions.Union(_invariantAssumptions).OrderBy(value => value.Value)]
        };
    }

    // The cut proof plan with the largest inductive set of candidate loop
    // invariants (Houdini): a candidate whose checkpoint the kernel does not
    // prove is dropped and the rest are checked again.
    private async Task<PassiveCallableVcPlan?> InvariantPlanAsync(CancellationToken cancellationToken)
    {
        if (_invariantsSearched)
        { return _invariantPlan; }
        _invariantsSearched = true;
        var candidate = _plan.Candidate;
        var active = LoopInvariantCandidates.Generate(candidate, PassiveLoopCutter.Loops(candidate, cancellationToken), cancellationToken);
        // A candidate the encoding cannot state (it reads a value undefined on
        // an entry edge) is dropped before any check.
        if (!active.IsEmpty && PassiveCallableVcBuilder.TryBuildWithInvariants(candidate, active, cancellationToken) == null)
        { active = [.. active.Where(invariant => PassiveCallableVcBuilder.TryBuildWithInvariants(candidate, [invariant], cancellationToken) != null)]; }
        while (!active.IsEmpty)
        {
            var plan = PassiveCallableVcBuilder.TryBuildWithInvariants(candidate, active, cancellationToken);
            if (plan == null)
            { return null; }
            var failed = new HashSet<int>();
            var cores = new HashSet<string>(StringComparer.Ordinal);
            var assumptions = new HashSet<OperationId>();
            foreach (var (invariant, query) in plan.InvariantQueries())
            {
                if (failed.Contains(invariant))
                { continue; }
                var evidence = await VerifyAsync(query, null, cancellationToken, plan).ConfigureAwait(false);
                if (evidence.Reason == WorkerClaimReason.ResourceLimit)
                { return null; }
                if (evidence.Outcome is ProvenOutcome)
                {
                    cores.UnionWith(evidence.Core);
                    assumptions.UnionWith(evidence.BodyAssumptions);
                }
                else
                { failed.Add(invariant); }
            }
            if (failed.Count == 0)
            {
                _invariantCore = [.. cores];
                _invariantAssumptions = [.. assumptions];
                return _invariantPlan = plan;
            }
            active = [.. active.Where((_, ordinal) => !failed.Contains(ordinal))];
        }
        return null;
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

    internal bool CanCheckWithoutNormalWitness =>
        (_plan.LoopSearch != null || _plan.HasBodyAbstraction || _approximateNormalReturn) && _entryFeasible;

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
        _approximateNormalReturn = normal.Reason == WorkerClaimReason.CounterexampleNotReplayable;
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
