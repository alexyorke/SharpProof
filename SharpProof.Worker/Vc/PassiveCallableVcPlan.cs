using System.Numerics;

namespace SharpProof.Worker;

internal sealed class PassiveCallableVcPlan
{
    private readonly PassiveCallableCandidate _candidate;
    private readonly ImmutableArray<Assumption> _entry;
    private readonly ImmutableArray<Assumption> _body;
    private readonly ImmutableArray<IrTerm> _goals;
    private readonly IrTerm _normalCompletion;
    private readonly ImmutableArray<(IrTerm Reach, IrTerm Kind)> _exceptions;
    private readonly ImmutableArray<OperationId> _explicitSites;
    private readonly ImmutableArray<(IrTerm Reach, OperationId Site)> _allocations;
    private readonly ImmutableArray<(IrTerm Reach, OperationId Site, IrWriteRegion Region)> _writes;
    private readonly ImmutableArray<(IrTerm Reach, OperationId Site)> _locks;
    private readonly ImmutableArray<(int Ordinal, IrTerm Reach, IrTerm Predicate, ImmutableArray<Assumption> Facts)> _callPreconditions;
    private readonly ImmutableArray<(int Ordinal, IrTerm Reach, IrTerm Predicate, ImmutableArray<Assumption> Facts)> _checkpoints;
    private readonly ImmutableArray<IrTerm> _potentialExceptionAllocations;
    private readonly ImmutableArray<(IrTerm Reach, IrOpaqueCallEffects Effects)> _opaqueCalls;
    private readonly ImmutableArray<IrTerm> _reads;
    private readonly ImmutableArray<IrTerm> _ambientReads;
    private readonly ImmutableArray<IrVarId> _model;
    private readonly ImmutableDictionary<ProofJustification, string> _labels;
    private readonly ImmutableDictionary<ProofJustification, OperationId> _assumes;

    internal PassiveCallableVcPlan(PassiveCallableVcBuilder builder, PassiveCallableVcPlan? loopSearch = null, bool boundedSearch = false)
    {
        _candidate = builder.Candidate;
        _entry = builder.EntryAssumptions;
        _body = builder.Facts;
        _goals = builder.Goals;
        _normalCompletion = builder.NormalCompletion;
        _exceptions = builder.Exceptions;
        _explicitSites = builder.ExplicitSites;
        _allocations = builder.Allocations;
        _writes = builder.Writes;
        _locks = builder.Locks;
        _callPreconditions = builder.CallPreconditions;
        _checkpoints = builder.Checkpoints;
        _potentialExceptionAllocations = builder.PotentialExceptionAllocations;
        _opaqueCalls = builder.OpaqueCalls;
        _reads = builder.Reads;
        _ambientReads = builder.AmbientReads;
        HasUnmodeledAllocations = builder.HasUnmodeledAllocations;
        _model = builder.Model;
        _labels = builder.Labels;
        _assumes = builder.Assumes;
        if (loopSearch != null && (!ReferenceEquals(_candidate, loopSearch._candidate) || loopSearch.LoopSearch != null || !loopSearch.IsBoundedSearch || boundedSearch))
        { throw new ArgumentException("A loop search must derive from the same owned original.", nameof(loopSearch)); }
        LoopSearch = loopSearch;
        IsBoundedSearch = boundedSearch;
    }

    internal IrFactory Factory => _candidate.Factory;
    internal PassiveCallableCandidate Candidate => _candidate;
    internal int EnsuresCount => _goals.Length;
    internal int CallPreconditionCount => _candidate.CallPreconditions.Length;
    internal string CallableId => _candidate.CallableId;
    internal PassiveCallableVcPlan? LoopSearch { get; }
    internal bool IsBoundedSearch { get; }
    internal bool HasBodyAbstraction => _candidate.IsBodyAbstraction;
    internal bool HasUnmodeledAllocations { get; private set; }

    internal VerificationQuery EntryQuery()
    {
        return new(Factory, _entry, Goal.CreateInternalConsistency(Factory), [.. _candidate.Parameters.Select(parameter => parameter.Entry)]);
    }

    internal VerificationQuery EnsuresQuery(int ordinal)
    {
        RequireOrdinal(ordinal);
        return new(Factory, _entry.AddRange(_body), new Goal(Factory, BeforeSynchronization(_goals[ordinal]),
            ProofDiagnosticKind.Postcondition, new SourceLocationId(ordinal)), _model);
    }

    // Each check that an assumed loop invariant holds on an edge into its
    // header, with the invariant's ordinal.
    internal ImmutableArray<(int Invariant, VerificationQuery Query)> InvariantQueries()
    {
        return [.. _checkpoints.Select(checkpoint => (checkpoint.Ordinal, new VerificationQuery(Factory, _entry.AddRange(checkpoint.Facts),
            new Goal(Factory, Factory.Binary(IrBinaryOperator.OrElse, Factory.Unary(IrUnaryOperator.Not, checkpoint.Reach), checkpoint.Predicate),
                ProofDiagnosticKind.Postcondition, new SourceLocationId(checkpoint.Ordinal)), _model)))];
    }

    // Every invariant checkpoint at once: one query when they all hold.
    internal VerificationQuery AllInvariantsQuery()
    {
        IrTerm goal = Factory.Boolean(true);
        foreach (var checkpoint in _checkpoints)
        {
            goal = Factory.Binary(IrBinaryOperator.AndAlso, goal,
                Factory.Binary(IrBinaryOperator.OrElse, Factory.Unary(IrUnaryOperator.Not, checkpoint.Reach), checkpoint.Predicate));
        }
        return new(Factory, _entry.AddRange(_checkpoints.SelectMany(checkpoint => checkpoint.Facts).Distinct()),
            new Goal(Factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), _model);
    }

    internal ImmutableArray<VerificationQuery> CallPreconditionQueries(int ordinal)
    {
        if (ordinal < 0 || ordinal >= CallPreconditionCount)
        { throw new ArgumentOutOfRangeException(nameof(ordinal)); }
        var occurrences = _callPreconditions.Where(clause => clause.Ordinal == ordinal).ToArray();
        if (occurrences.Length == 0)
        {
            return [new(Factory, _entry, new Goal(Factory, Factory.Boolean(true),
                ProofDiagnosticKind.Precondition, new SourceLocationId(ordinal)), _model)];
        }
        return [.. occurrences.Select(clause => new VerificationQuery(Factory, _entry.AddRange(clause.Facts),
            new Goal(Factory, Factory.Binary(IrBinaryOperator.OrElse, Factory.Unary(IrUnaryOperator.Not, clause.Reach), clause.Predicate),
                ProofDiagnosticKind.Precondition, new SourceLocationId(ordinal)), _model))];
    }

    internal OperationId? ReplayCallPrecondition(int ordinal, ImmutableDictionary<IrVarId, IrValue> inputs, CancellationToken cancellationToken)
    {
        if (ordinal < 0 || ordinal >= CallPreconditionCount)
        { throw new ArgumentOutOfRangeException(nameof(ordinal)); }
        var marker = _candidate.CallPreconditions[ordinal].Marker;
        OperationId? witness = null;
        var initial = new Dictionary<IrVarId, IrValue>();
        foreach (var parameter in _candidate.Parameters)
        {
            initial[parameter.Entry] = inputs[parameter.Entry];
            initial[parameter.Current] = inputs[parameter.Entry];
        }
        var options = ReplayOptions(initial: initial);
        options.AssignmentObserver = (assignment, value, approximation) =>
        {
            if (assignment.Id == marker && !value.Boolean && !approximation)
            { witness ??= assignment.Operation; }
        };
        new IrProgramInterpreter(Factory).Execute(_candidate.Program, initial,
            PassiveCallableVcBuilder.MaximumSteps, options, cancellationToken);
        return witness;
    }

    internal CallableReplayContext Replay(int ordinal)
    {
        RequireOrdinal(ordinal);
        var clause = _candidate.Ensures[ordinal];
        return CreateReplay(clause.Value, clause.Safe);
    }

    internal VerificationQuery NormalCompletionQuery()
    {
        return new(Factory, _entry.AddRange(_body), new Goal(Factory,
            BeforeSynchronization(Factory.Unary(IrUnaryOperator.Not, _normalCompletion)), ProofDiagnosticKind.InternalConsistency, new SourceLocationId(0)), _model);
    }

    internal CallableReplayContext NormalCompletionReplay()
    {
        // A false owned postcondition makes an actual normal return the only
        // accepted counterexample. The kernel still validates every SSA fact.
        return CreateReplay(Factory.Boolean(false), Factory.Boolean(true));
    }

    internal VerificationQuery ExceptionQuery(ImmutableHashSet<IrExceptionKind> allowed, Func<OperationId, bool>? allowedSite = null)
    {
        ArgumentNullGuard.NotNull(allowed, nameof(allowed));
        if (allowed.Any(kind => !Enum.IsDefined(kind)))
        { throw new ArgumentException("An allowed exception kind is undefined.", nameof(allowed)); }
        IrTerm goal = Factory.Boolean(true);
        foreach (var exit in _exceptions)
        {
            IrTerm admitted = Factory.Boolean(false);
            foreach (var kind in allowed.OrderBy(kind => kind))
            { admitted = Factory.Binary(IrBinaryOperator.OrElse, admitted, Factory.Binary(IrBinaryOperator.Equal, exit.Kind, Factory.Integer((int)kind))); }
            for (var ordinal = 0; ordinal < _explicitSites.Length; ordinal++)
            {
                if (allowed.Contains(IrExceptionKind.Explicit) || allowedSite?.Invoke(_explicitSites[ordinal]) == true)
                {
                    admitted = Factory.Binary(IrBinaryOperator.OrElse, admitted, Factory.Binary(IrBinaryOperator.Equal, exit.Kind,
                        Factory.Integer(PassiveCallableVcBuilder.ExplicitSiteCode + ordinal)));
                }
            }
            goal = Factory.Binary(IrBinaryOperator.AndAlso, goal,
                Factory.Binary(IrBinaryOperator.OrElse, Factory.Unary(IrUnaryOperator.Not, exit.Reach), admitted));
        }
        return new(Factory, _entry.AddRange(_body), new Goal(Factory, BeforeSynchronization(goal),
            ProofDiagnosticKind.EffectContract, new SourceLocationId(0)), _model);
    }

    // No reachable site uses a disallowed capability: a lock synchronizes, and
    // an opaque call uses its specified capabilities.
    internal VerificationQuery CapabilityQuery(bool synchronizationAllowed, Func<IrOpaqueCallEffects, bool> callViolates)
    {
        return new(Factory, _entry.AddRange(_body), new Goal(Factory,
            EffectGoalBuilder.NoReachableSites(Factory, (synchronizationAllowed ? [] : _locks.Select(site => site.Reach))
                .Concat(OpaqueCalls(callViolates))),
            ProofDiagnosticKind.EffectContract, new SourceLocationId(0)), _model);
    }

    // An effect summary forbids reachable writes, reads, locks and calls whose
    // effects it does not declare.
    internal VerificationQuery EffectSiteQuery(Func<IrWriteRegion, bool> writeViolates, bool readsViolate, bool locksViolate,
        Func<IrOpaqueCallEffects, bool> callViolates)
    {
        return new(Factory, _entry.AddRange(_body), new Goal(Factory,
            EffectGoalBuilder.NoReachableSites(Factory, _writes.Where(write => writeViolates(write.Region)).Select(write => write.Reach)
                .Concat(readsViolate ? _reads : [])
                .Concat(locksViolate ? _locks.Select(site => site.Reach) : [])
                .Concat(OpaqueCalls(callViolates))),
            ProofDiagnosticKind.EffectContract, new SourceLocationId(0)), _model);
    }

    private IEnumerable<IrTerm> OpaqueCalls(Func<IrOpaqueCallEffects, bool> selected)
    { return _opaqueCalls.Where(call => selected(call.Effects)).Select(call => call.Reach); }

    internal VerificationQuery AllocationQuery()
    {
        return new(Factory, _entry.AddRange(_body), new Goal(Factory,
            EffectGoalBuilder.NoReachableSites(Factory, _allocations.Select(allocation => allocation.Reach)
                .Concat(_potentialExceptionAllocations).Concat(_locks.Select(synchronization => synchronization.Reach))
                .Concat(OpaqueCalls(effects => (effects & IrOpaqueCallEffects.Allocates) != 0))),
            ProofDiagnosticKind.EffectContract, new SourceLocationId(0)), _model);
    }

    internal VerificationQuery PurityQuery()
    {
        return new(Factory, _entry.AddRange(_body), new Goal(Factory,
            EffectGoalBuilder.NoReachableSites(Factory, _writes.Where(write => write.Region != IrWriteRegion.Local).Select(write => write.Reach)
                .Concat(_locks.Select(synchronization => synchronization.Reach)).Concat(_ambientReads)
                .Concat(OpaqueCalls(IrOpaqueCallSite.IsObservablyImpure))),
            ProofDiagnosticKind.EffectContract, new SourceLocationId(0)), _model);
    }

    internal IrProgramExecutionResult ReplayException(ImmutableDictionary<IrVarId, IrValue> inputs, CancellationToken cancellationToken)
    { return ReplayEffects(inputs, cancellationToken); }

    internal IrProgramExecutionResult ReplayEffects(ImmutableDictionary<IrVarId, IrValue> inputs, CancellationToken cancellationToken,
        Action<IrAllocationInstruction>? allocationObserver = null, Action<IrWriteInstruction>? writeObserver = null,
        Action<IrLockInstruction>? lockObserver = null,
        Action<IrAllocationInstruction, bool>? allocationPrefixObserver = null,
        Action<IrWriteInstruction, bool>? writePrefixObserver = null,
        Action<IrLockInstruction, bool>? lockPrefixObserver = null)
    {
        var initial = new Dictionary<IrVarId, IrValue>();
        foreach (var parameter in _candidate.Parameters)
        {
            initial[parameter.Entry] = inputs[parameter.Entry];
            initial[parameter.Current] = inputs[parameter.Entry];
        }
        return new IrProgramInterpreter(Factory).Execute(_candidate.Program, initial,
            PassiveCallableVcBuilder.MaximumSteps, ReplayOptions(allocationObserver, writeObserver, lockObserver, initial,
                allocationPrefixObserver, writePrefixObserver, lockPrefixObserver), cancellationToken);
    }

    private IrProgramReplayOptions ReplayOptions(Action<IrAllocationInstruction>? allocationObserver = null, Action<IrWriteInstruction>? writeObserver = null,
        Action<IrLockInstruction>? lockObserver = null, Dictionary<IrVarId, IrValue>? initial = null,
        Action<IrAllocationInstruction, bool>? allocationPrefixObserver = null,
        Action<IrWriteInstruction, bool>? writePrefixObserver = null,
        Action<IrLockInstruction, bool>? lockPrefixObserver = null)
    {
        return new(request => request.Origin == IrHavocOrigin.Input
            ? initial != null && initial.TryGetValue(request.Variable, out var input) ? input : null
            : request.Origin == IrHavocOrigin.SpecResult ? null
            : Factory.GetVariableInfo(request.Variable).Type == Factory.BooleanType
            ? Factory.CreateBooleanValue(false)
            : Factory.GetTypeInfo(Factory.GetVariableInfo(request.Variable).Type).Kind == IrTypeKind.Integer
                ? Factory.CreateIntegerValue(Factory.GetVariableInfo(request.Variable).Type, 0L)
                : Factory.CreateNullValue(Factory.GetVariableInfo(request.Variable).Type))
        {
            AllocationObserver = allocationObserver,
            WriteObserver = writeObserver,
            LockObserver = lockObserver,
            AllocationPrefixObserver = allocationPrefixObserver,
            WritePrefixObserver = writePrefixObserver,
            LockPrefixObserver = lockPrefixObserver
        };
    }

    private IrTerm BeforeSynchronization(IrTerm goal)
    {
        if (_locks.IsEmpty)
        { return goal; }
        return Factory.Binary(IrBinaryOperator.AndAlso, goal,
            EffectGoalBuilder.NoReachableSites(Factory, _locks.Select(synchronization => synchronization.Reach)));
    }

    private CallableReplayContext CreateReplay(IrTerm value, IrTerm safe)
    {
        var bindings = ImmutableDictionary.CreateBuilder<IrVarId, IrVarId>();
        var old = ImmutableDictionary.CreateBuilder<IrVarId, IrVarId?>();
        foreach (var parameter in _candidate.Parameters)
        {
            bindings[parameter.Entry] = parameter.Entry;
            bindings[parameter.Current] = parameter.Current;
            old[parameter.Old] = parameter.Entry;
        }
        return new(_candidate.Program, false, bindings.ToImmutable(), old.ToImmutable(),
            _candidate.Result is { } result ? [result] : [], value,
            ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty, PassiveCallableVcBuilder.MaximumSteps, [],
            postconditionGuard: safe, replayOptions: ReplayOptions());
    }

    internal ImmutableArray<string> CoreLabels(ProvenOutcome outcome)
    {
        if (outcome.Core.Any(justification => !_labels.ContainsKey(justification)))
        { throw new ArgumentException("The core contains a foreign justification.", nameof(outcome)); }
        return [.. outcome.Core.Select(justification => _labels[justification]).Distinct(StringComparer.Ordinal).OrderBy(label => label, StringComparer.Ordinal)];
    }

    internal ImmutableArray<OperationId> UsedBodyAssumptions(ProvenOutcome outcome)
    {
        CoreLabels(outcome);
        return [.. outcome.Core.Where(_assumes.ContainsKey).Select(justification => _assumes[justification]).Distinct()];
    }

    internal ImmutableDictionary<IrVarId, IrValue> ProjectModel(RefutedOutcome outcome)
    {
        return _candidate.Parameters.ToImmutableDictionary(parameter => parameter.Entry, parameter => outcome.Model.Assignments[parameter.Entry]);
    }

    private void RequireOrdinal(int ordinal)
    {
        if ((uint)ordinal >= (uint)_goals.Length)
        { throw new ArgumentOutOfRangeException(nameof(ordinal)); }
    }
}
