namespace SharpProof.Worker;

internal sealed record PassiveCallableCheckResult(ProofOutcome? Outcome, WorkerClaimReason Reason,
    ImmutableDictionary<IrVarId, IrValue> EntryModel, ImmutableArray<string> Core,
    ImmutableArray<OperationId> BodyAssumptions, bool QueryCompleted = false, IrExceptionInfo? ExceptionWitness = null,
    OperationId? AllocationWitness = null, bool HasFeasibleEntryWitness = false, OperationId? WriteWitness = null,
    OperationId? LockWitness = null, OperationId? CallPreconditionWitness = null, bool ReceiverWriteWitness = false)
{
    public ImmutableDictionary<IrVarId, IrValue> EntryModel { get; init; } = EntryModel ?? throw new System.ArgumentNullException(nameof(EntryModel));
    public ImmutableArray<string> Core { get; init; } = Core.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Core)) : Core;
    public ImmutableArray<OperationId> BodyAssumptions { get; init; } = BodyAssumptions.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(BodyAssumptions)) : BodyAssumptions;
}

internal enum PassiveCallableFeasibilityKind { Feasible, ContradictoryEntry, NoModeledNormalReturn, Unknown }

internal sealed record PassiveCallableFeasibility(PassiveCallableFeasibilityKind Kind,
    PassiveCallableCheckResult Evidence, PassiveCallableCheckResult EntryEvidence)
{
    public PassiveCallableCheckResult Evidence { get; init; } = Evidence ?? throw new System.ArgumentNullException(nameof(Evidence));
    public PassiveCallableCheckResult EntryEvidence { get; init; } = EntryEvidence ?? throw new System.ArgumentNullException(nameof(EntryEvidence));
}

// One session per owned plan; the existing method meter reads the actual solver
// consumption.
internal sealed class PassiveCallableSolver : IDisposable
{
    private readonly PassiveCallableVcPlan _plan;
    private readonly CallableSolverSession? _session;
    private readonly uint _queryRlimit = WorkerBudgets.DefaultQueryRlimit;
    private long _auxiliaryResources;
    private readonly Func<long>? _readConsumedResourceCount;
    private readonly MethodResourceBudget _budget;
    private readonly ProofKernel _kernel;
    private bool _entryFeasible;
    // A normal return was found, but only through approximation havocs.
    private bool _approximateNormalReturn;
    private bool _invariantsSearched;
    private InvariantSet? _invariantPlan;

    internal PassiveCallableSolver(PassiveCallableVcPlan plan,
        uint queryRlimit = WorkerBudgets.DefaultQueryRlimit, uint methodRlimit = WorkerBudgets.DefaultMethodRlimit)
    {
        _plan = ArgumentNullGuard.NotNull(plan, nameof(plan));
        if (plan.IsBoundedSearch)
        { throw new ArgumentException("A bounded witness encoding cannot own a proof session.", nameof(plan)); }
        ArgumentOutOfRangeException.ThrowIfLessThan(methodRlimit, queryRlimit);
        _queryRlimit = queryRlimit;
        _session = new(plan.Factory, new IrSmtBackendOptions(queryRlimit)) { ConcreteOpaqueValueProvider = plan.ObserveFrameworkOpaqueValue };
        _budget = new(() => ConsumedResourceCount, queryRlimit, methodRlimit);
        _kernel = new(_session) { ConcreteOpaqueValueProvider = plan.ObserveFrameworkOpaqueValue };
    }

    internal PassiveCallableSolver(PassiveCallableVcPlan plan, ISmtBackend backend,
        MethodResourceBudget resourceBudget, Func<long>? readConsumedResourceCount = null)
    {
        _plan = ArgumentNullGuard.NotNull(plan, nameof(plan));
        if (plan.IsBoundedSearch)
        { throw new ArgumentException("A bounded witness encoding cannot own a proof session.", nameof(plan)); }
        _budget = ArgumentNullGuard.NotNull(resourceBudget, nameof(resourceBudget));
        _kernel = new(ArgumentNullGuard.NotNull(backend, nameof(backend))) { ConcreteOpaqueValueProvider = plan.ObserveFrameworkOpaqueValue };
        _readConsumedResourceCount = readConsumedResourceCount;
    }

    internal long ConsumedResourceCount => (_session?.ConsumedResourceCount ?? _readConsumedResourceCount?.Invoke() ?? 0) + _auxiliaryResources;
    internal Task<PassiveCallableCheckResult> VerifyEntryAsync(CancellationToken cancellationToken)
    { return VerifyAsync(_plan.EntryQuery(), null, cancellationToken); }

    internal Task<PassiveCallableCheckResult> VerifyAllocationsAsync(CancellationToken cancellationToken)
    { return VerifySitesAsync(allocations: true, cancellationToken); }

    internal Task<PassiveCallableCheckResult> VerifyPurityAsync(CancellationToken cancellationToken)
    { return VerifySitesAsync(allocations: false, cancellationToken); }

    // Only a concretely reached lock refutes a capability claim; a call's
    // specified capabilities are may-effects.
    internal async Task<PassiveCallableCheckResult> VerifyCapabilitiesAsync(bool synchronizationAllowed,
        Func<IrOpaqueCallEffects, bool> callViolates, CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(callViolates, nameof(callViolates));
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
            return await ProveWithInvariantsAsync(witness, plan => plan is null ? throw new System.ArgumentNullException(nameof(plan)) : (plan.CapabilityQuery(synchronizationAllowed, callViolates)), cancellationToken)
                .ConfigureAwait(false) ?? (witness.Outcome is ProvenOutcome ? Inconclusive() : witness);
        }
        OperationId? site = null;
        _plan.ReplayEffects(witness.EntryModel, cancellationToken, lockPrefixObserver: (instruction, approximation) =>
        { System.ArgumentNullException.ThrowIfNull(instruction, nameof(instruction)); if (!approximation) { site ??= instruction.Operation; } });
        cancellationToken.ThrowIfCancellationRequested();
        return site != null ? witness with { LockWitness = site } :
            new(null, WorkerClaimReason.CounterexampleNotReplayable, ImmutableDictionary<IrVarId, IrValue>.Empty, [], [], QueryCompleted: true);
    }

    // A concretely reached violating write or lock refutes an effect summary;
    // reads and call effects are may-effects.
    internal async Task<PassiveCallableCheckResult> VerifyEffectSitesAsync(Func<IrWriteRegion, bool, bool> writeViolates, bool readsViolate,
        bool locksViolate, Func<IrOpaqueCallEffects, bool> callViolates, CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(writeViolates, nameof(writeViolates));
        ArgumentNullGuard.NotNull(callViolates, nameof(callViolates));
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
            return await ProveWithInvariantsAsync(witness, plan => plan is null ? throw new System.ArgumentNullException(nameof(plan)) : (plan.EffectSiteQuery(writeViolates, readsViolate, locksViolate, callViolates)),
                cancellationToken).ConfigureAwait(false) ?? (witness.Outcome is ProvenOutcome ? Inconclusive() : witness);
        }
        OperationId? write = null;
        var receiverWriteWitness = false;
        OperationId? synchronization = null;
        _plan.ReplayEffects(witness.EntryModel, cancellationToken,
            writePrefixObserver: (instruction, approximation) =>
            {
                System.ArgumentNullException.ThrowIfNull(instruction);
                if (!approximation && IrWriteSites.IsObservable(_plan.Factory, instruction) &&
                    writeViolates(instruction.Region, _plan.IsReceiverWrite(instruction.Id)))
                {
                    if (write == null)
                    {
                        write = instruction.Operation;
                        receiverWriteWitness = _plan.IsReceiverWrite(instruction.Id);
                    }
                }
            },
            lockPrefixObserver: locksViolate ? (instruction, approximation) =>
            {
                System.ArgumentNullException.ThrowIfNull(instruction);
                if (!approximation)
                { synchronization ??= instruction.Operation; }
            }
        : null);
        cancellationToken.ThrowIfCancellationRequested();
        return synchronization != null ? witness with { LockWitness = synchronization }
            : write != null ? witness with { WriteWitness = write, ReceiverWriteWitness = receiverWriteWitness }
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
        if (!allocations && (proof.Reason == WorkerClaimReason.ResourceLimit || witness.Reason == WorkerClaimReason.ResourceLimit) && _session != null &&
            encoding.FirstPurityPrefixQuery() is { } prefix && _budget.TryStartQuery())
        {
            // A resource-exhausted native session is retired. Retry the smaller
            // witness query in a fresh context, charging the same method meter.
            using var prefixSession = new CallableSolverSession(_plan.Factory, new IrSmtBackendOptions(_queryRlimit)) { ConcreteOpaqueValueProvider = _plan.ObserveFrameworkOpaqueValue };
            ProofOutcome prefixOutcome;
            try
            { prefixOutcome = await new ProofKernel(prefixSession) { ConcreteOpaqueValueProvider = _plan.ObserveFrameworkOpaqueValue }.VerifyAsync(prefix, cancellationToken).ConfigureAwait(false); }
            finally
            { _auxiliaryResources += prefixSession.ConsumedResourceCount; }
            if (!_budget.IsExceeded && prefixOutcome is RefutedOutcome refutedPrefix)
            {
                witness = new(refutedPrefix, WorkerClaimReason.None, encoding.ProjectModel(refutedPrefix), [], []);
            }
        }
        if (witness.Outcome is not RefutedOutcome)
        {
            return await ProveWithInvariantsAsync(witness, plan => plan is null ? throw new System.ArgumentNullException(nameof(plan)) : (allocations ? plan.AllocationQuery() : plan.PurityQuery()), cancellationToken)
                .ConfigureAwait(false) ?? (witness.Outcome is ProvenOutcome ? Inconclusive() : witness);
        }
        OperationId? site = null;
        OperationId? synchronizationSite = null;
        if (allocations)
        {
            _plan.ReplayEffects(witness.EntryModel, cancellationToken,
                allocationPrefixObserver: (allocation, approximation) =>
                {
                    System.ArgumentNullException.ThrowIfNull(allocation);
                    if (!approximation)
                    { site ??= allocation.Operation; }
                });
        }
        else
        {
            _plan.ReplayPurityEffects(witness.EntryModel,
                (write, approximation) =>
                {
                    System.ArgumentNullException.ThrowIfNull(write);
                    if (!approximation && IrWriteSites.IsObservable(_plan.Factory, write))
                    { site ??= write.Operation; }
                },
                (synchronization, approximation) =>
                {
                    System.ArgumentNullException.ThrowIfNull(synchronization);
                    if (!approximation)
                    { synchronizationSite ??= synchronization.Operation; }
                }, cancellationToken);
        }
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
        CancellationToken cancellationToken, bool allowsNoException = false,
        Func<OperationId, bool>? allowedSite = null, Func<OperationId, bool>? exactSite = null)
    {
        ArgumentNullGuard.NotNull(allowed, nameof(allowed));
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
            return await ProveWithInvariantsAsync(witness, plan => plan is null ? throw new System.ArgumentNullException(nameof(plan)) : (plan.ExceptionQuery(allowed, allowedSite)), cancellationToken)
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
    internal async Task<PassiveCallableCheckResult> VerifyCallPreconditionAsync(int ordinal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var queries = _plan.CallPreconditionQueries(ordinal);
        PassiveCallableCheckResult? proven = null;
        PassiveCallableCheckResult? incomplete = null;
        var cores = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var assumptions = ImmutableHashSet.CreateBuilder<OperationId>();
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
                Core = [.. cores.OrderBy(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value), StringComparer.Ordinal)],
                BodyAssumptions = [.. assumptions.OrderBy(value => value.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(value)) : (value.Value))]
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

    internal async Task<PassiveCallableCheckResult> VerifyEnsuresAsync(int ordinal, CancellationToken cancellationToken)
    {
        if (_plan.HasBodyAbstraction)
        { return await VerifyBodyAsync(_plan.EnsuresQuery(ordinal), _plan.Replay(ordinal), cancellationToken).ConfigureAwait(false); }
        if (_plan.LoopSearch is not { } search)
        { return await VerifyBodyAsync(_plan.EnsuresQuery(ordinal), _plan.Replay(ordinal), cancellationToken).ConfigureAwait(false); }
        var proof = await VerifyAsync(_plan.EnsuresQuery(ordinal), null, cancellationToken).ConfigureAwait(false);
        if (proof.Outcome is ProvenOutcome)
        { return proof; }
        var witness = await VerifyAsync(search.EnsuresQuery(ordinal), search.Replay(ordinal), cancellationToken, search).ConfigureAwait(false);
        if (await ProveWithInvariantsAsync(witness, plan => plan is null ? throw new System.ArgumentNullException(nameof(plan)) : (plan.EnsuresQuery(ordinal)), cancellationToken).ConfigureAwait(false) is { } proven)
        { return proven; }
        // A cut model is never a refutation; finite search is never a proof.
        return witness.Outcome is RefutedOutcome or UnknownOutcome || witness.Outcome == null ? witness : Inconclusive();
    }

    // Retries a goal the loop cut could not prove with the checked loop
    // invariants assumed; null when they do not prove it. Spacer's proposals
    // for this goal join the templates when those alone do not suffice.
    private async Task<PassiveCallableCheckResult?> ProveWithInvariantsAsync(PassiveCallableCheckResult witness,
        Func<PassiveCallableVcPlan, VerificationQuery> query, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(query);
        if (_plan.LoopSearch == null || witness.Outcome is RefutedOutcome || witness.Reason == WorkerClaimReason.ResourceLimit)
        { return null; }
        var invariants = await InvariantPlanAsync(cancellationToken).ConfigureAwait(false);
        if (invariants != null)
        {
            var proven = await VerifyAsync(query(invariants.Plan), null, cancellationToken, invariants.Plan).ConfigureAwait(false);
            if (proven.Outcome is ProvenOutcome)
            { return WithInvariantPremises(proven, invariants); }
        }
        // Only the surviving templates join Spacer's proposals, which keeps
        // the second search within the method's budget.
        var proposals = HornProposals(query, cancellationToken);
        if (proposals.IsEmpty ||
            await HoudiniAsync([.. invariants?.Invariants ?? [], .. proposals], cancellationToken).ConfigureAwait(false) is not { } extended)
        { return null; }
        var result = await VerifyAsync(query(extended.Plan), null, cancellationToken, extended.Plan).ConfigureAwait(false);
        return result.Outcome is ProvenOutcome ? WithInvariantPremises(result, extended) : null;
    }

    // A proof over assumed invariants also rests on the premises that proved them.
    private static PassiveCallableCheckResult WithInvariantPremises(PassiveCallableCheckResult proven, InvariantSet invariants)
    {
        System.ArgumentNullException.ThrowIfNull(invariants);
        System.ArgumentNullException.ThrowIfNull(proven);
        return proven with
        {
            Core = [.. proven.Core.Union(invariants.Core, StringComparer.Ordinal).OrderBy(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value), StringComparer.Ordinal)],
            BodyAssumptions = [.. proven.BodyAssumptions.Union(invariants.Assumptions).OrderBy(value => value.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(value)) : (value.Value))]
        };
    }

    private sealed record InvariantSet(PassiveCallableVcPlan Plan, ImmutableArray<PassiveLoopCutter.Invariant> Invariants,
        ImmutableArray<string> Core, ImmutableArray<OperationId> Assumptions)
    {
        public PassiveCallableVcPlan Plan { get; init; } = Plan ?? throw new System.ArgumentNullException(nameof(Plan));
        public ImmutableArray<PassiveLoopCutter.Invariant> Invariants { get; init; } = Invariants.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Invariants)) : Invariants;
        public ImmutableArray<string> Core { get; init; } = Core.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Core)) : Core;
        public ImmutableArray<OperationId> Assumptions { get; init; } = Assumptions.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Assumptions)) : Assumptions;
    }

    private ImmutableArray<PassiveLoopCutter.Invariant> Templates(CancellationToken cancellationToken)
    {
        var candidate = _plan.Candidate;
        return LoopInvariantCandidates.Generate(candidate, PassiveLoopCutter.Loops(candidate, cancellationToken), cancellationToken);
    }

    // The template invariants that survive Houdini, searched once.
    private async Task<InvariantSet?> InvariantPlanAsync(CancellationToken cancellationToken)
    {
        if (_invariantsSearched)
        { return _invariantPlan; }
        _invariantsSearched = true;
        return _invariantPlan = await HoudiniAsync(Templates(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    // The cut proof plan with the largest inductive set of candidate loop
    // invariants (Houdini): a candidate whose checkpoint the kernel does not
    // prove is dropped and the rest are checked again.
    private async Task<InvariantSet?> HoudiniAsync(ImmutableArray<PassiveLoopCutter.Invariant> active, CancellationToken cancellationToken)
    {

        var candidate = _plan.Candidate;
        // A candidate the encoding cannot state (it reads a value undefined on
        // an entry edge) is dropped before any check.
        var initialPlan = active.IsEmpty ? null : PassiveCallableVcBuilder.TryBuildWithInvariants(candidate, active, cancellationToken);
        if (!active.IsEmpty && initialPlan == null)
        { active = [.. active.Where(invariant => invariant is null ? throw new System.ArgumentNullException(nameof(invariant)) : (PassiveCallableVcBuilder.TryBuildWithInvariants(candidate, [invariant], cancellationToken) != null))]; }
        var rechecking = false;
        while (!active.IsEmpty)
        {
            var plan = initialPlan ?? PassiveCallableVcBuilder.TryBuildWithInvariants(candidate, active, cancellationToken);
            initialPlan = null;
            if (plan == null)
            { return null; }
            // Usually only the first round has failing candidates; a later one
            // is one query.
            if (rechecking)
            {
                var together = await VerifyAsync(plan.AllInvariantsQuery(), null, cancellationToken, plan).ConfigureAwait(false);
                if (together.Reason == WorkerClaimReason.ResourceLimit)
                { return null; }
                if (together.Outcome is ProvenOutcome)
                { return new(plan, active, together.Core, together.BodyAssumptions); }
            }
            rechecking = true;
            var failed = ImmutableHashSet.CreateBuilder<int>();
            var cores = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            var assumptions = ImmutableHashSet.CreateBuilder<OperationId>();
            var coreOrder = ImmutableArray.CreateBuilder<string>();
            var assumptionOrder = ImmutableArray.CreateBuilder<OperationId>();
            foreach (var (invariant, query) in plan.InvariantQueries())
            {
                if (failed.Contains(invariant))
                { continue; }
                var evidence = await VerifyAsync(query, null, cancellationToken, plan).ConfigureAwait(false);
                if (evidence.Reason == WorkerClaimReason.ResourceLimit)
                { return null; }
                if (evidence.Outcome is ProvenOutcome)
                {
                    foreach (var core in evidence.Core)
                    {
                        if (cores.Add(core))
                        { coreOrder.Add(core); }
                    }
                    foreach (var assumption in evidence.BodyAssumptions)
                    {
                        if (assumptions.Add(assumption))
                        { assumptionOrder.Add(assumption); }
                    }
                }
                else
                { failed.Add(invariant); }
            }
            if (failed.Count == 0)
            { return new(plan, active, coreOrder.ToImmutable(), assumptionOrder.ToImmutable()); }
            active = [.. active.Where((_, ordinal) => !failed.Contains(ordinal))];
        }
        return null;
    }

    // Each loop's invariant is an unknown relation over the state it carries
    // and the variables it reads; the checkpoints and the goal are Horn
    // clauses over them, which Spacer solves. Its answers are only guesses.
    private ImmutableArray<PassiveLoopCutter.Invariant> HornProposals(Func<PassiveCallableVcPlan, VerificationQuery> query,
        CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(query);
        var candidate = _plan.Candidate;
        var factory = candidate.Factory;
        var states = ImmutableDictionary.CreateBuilder<IrMemberId, ImmutableArray<IrTerm>>();
        var relations = ImmutableArray.CreateBuilder<PassiveLoopCutter.Invariant>();
        foreach (var loop in PassiveLoopCutter.Loops(candidate, cancellationToken))
        {
            var (tracked, anchors) = LoopInvariantCandidates.State(candidate, loop, cancellationToken);
            ImmutableArray<IrTerm> state = [.. tracked.Concat(anchors.Where(anchor => anchor is null ? throw new System.ArgumentNullException(nameof(anchor)) : (anchor is not IrIntegerTerm))).Distinct()
                .Where(term => term is null ? throw new System.ArgumentNullException(nameof(term)) : (factory.GetTypeInfo(term.Type) is { Kind: IrTypeKind.Boolean } or { Kind: IrTypeKind.Integer, Width: > 0 }))];
            if (state.IsEmpty)
            { continue; }
            var relation = IrInvariantRelations.Create(factory, relations.Count, state);
            states.AddUnique(relation.Member, state);
            relations.Add(new(loop.Header, relation));
        }
        if (relations.Count == 0 ||
            PassiveCallableVcBuilder.TryBuildWithInvariants(candidate, relations.ToImmutable(), cancellationToken) is not { } plan)
        { return []; }
        ImmutableArray<VerificationQuery> clauses = [.. plan.InvariantQueries().Select(pair => pair.Query), query(plan)];
        var proposals = HornInvariantSearch.Propose(factory, clauses,
            states.ToImmutable(), WorkerBudgets.DefaultQueryRlimit, cancellationToken);
        return [.. relations.SelectMany(relation => relation is null ? throw new System.ArgumentNullException(nameof(relation)) : (proposals.TryGetValue(((IrOpaqueTerm)relation.Condition).Member, out var invariant)
            ? Conjuncts(invariant).Select(conjunct => conjunct is null ? throw new System.ArgumentNullException(nameof(conjunct)) : (new PassiveLoopCutter.Invariant(relation.Header, conjunct)))
            : []))];

        static IEnumerable<IrTerm> Conjuncts(IrTerm term)
        {
            System.ArgumentNullException.ThrowIfNull(term);
            return term is IrBinaryTerm { Operator: IrBinaryOperator.AndAlso } conjunction
                ? Conjuncts(conjunction.Left).Concat(Conjuncts(conjunction.Right))
                : term is IrBooleanTerm { Value: true } ? [] : [term];
        }
    }

    internal async Task<PassiveCallableCheckResult> VerifyNormalCompletionAsync(CancellationToken cancellationToken)
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
        System.ArgumentNullException.ThrowIfNull(query);
        System.ArgumentNullException.ThrowIfNull(replay);
        var evidence = await VerifyAsync(query, _plan.HasBodyAbstraction ? null : replay, cancellationToken).ConfigureAwait(false);
        return _plan.HasBodyAbstraction && evidence.Outcome is RefutedOutcome
            ? new(null, WorkerClaimReason.CounterexampleNotReplayable, ImmutableDictionary<IrVarId, IrValue>.Empty, [], [], QueryCompleted: true)
            : evidence;
    }

    private static PassiveCallableCheckResult Inconclusive()
    { return new(null, WorkerClaimReason.SolverIncomplete, ImmutableDictionary<IrVarId, IrValue>.Empty, [], [], QueryCompleted: true); }

    internal Task<PassiveCallableFeasibility> VerifyFeasibilityAsync(CancellationToken cancellationToken)
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
        System.ArgumentNullException.ThrowIfNull(query);
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
