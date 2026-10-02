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
    private readonly ImmutableArray<(IrTerm Reach, OperationId Site)> _allocations;
    private readonly ImmutableArray<(IrTerm Reach, OperationId Site, IrWriteRegion Region)> _writes;
    private readonly ImmutableArray<IrTerm> _potentialExceptionAllocations;
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
        _allocations = builder.Allocations;
        _writes = builder.Writes;
        _potentialExceptionAllocations = builder.PotentialExceptionAllocations;
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
    internal int EnsuresCount => _goals.Length;
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
        return new(Factory, _entry.AddRange(_body), new Goal(Factory, _goals[ordinal],
            ProofDiagnosticKind.Postcondition, new SourceLocationId(ordinal)), _model);
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
            Factory.Unary(IrUnaryOperator.Not, _normalCompletion), ProofDiagnosticKind.InternalConsistency, new SourceLocationId(0)), _model);
    }

    internal CallableReplayContext NormalCompletionReplay()
    {
        // A false owned postcondition makes an actual normal return the only
        // accepted counterexample. The kernel still validates every SSA fact.
        return CreateReplay(Factory.Boolean(false), Factory.Boolean(true));
    }

    internal VerificationQuery ExceptionQuery(ImmutableHashSet<IrExceptionKind> allowed)
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
            goal = Factory.Binary(IrBinaryOperator.AndAlso, goal,
                Factory.Binary(IrBinaryOperator.OrElse, Factory.Unary(IrUnaryOperator.Not, exit.Reach), admitted));
        }
        return new(Factory, _entry.AddRange(_body), new Goal(Factory, goal,
            ProofDiagnosticKind.EffectContract, new SourceLocationId(0)), _model);
    }

    internal VerificationQuery AllocationQuery()
    {
        return new(Factory, _entry.AddRange(_body), new Goal(Factory,
            EffectGoalBuilder.NoReachableSites(Factory, _allocations.Select(allocation => allocation.Reach)
                .Concat(_potentialExceptionAllocations)),
            ProofDiagnosticKind.EffectContract, new SourceLocationId(0)), _model);
    }

    internal VerificationQuery PurityQuery()
    {
        return new(Factory, _entry.AddRange(_body), new Goal(Factory,
            EffectGoalBuilder.NoReachableSites(Factory, _writes.Where(write => write.Region != IrWriteRegion.Local).Select(write => write.Reach)),
            ProofDiagnosticKind.EffectContract, new SourceLocationId(0)), _model);
    }

    internal IrProgramExecutionResult ReplayException(ImmutableDictionary<IrVarId, IrValue> inputs, CancellationToken cancellationToken)
    { return ReplayEffects(inputs, cancellationToken); }

    internal IrProgramExecutionResult ReplayEffects(ImmutableDictionary<IrVarId, IrValue> inputs, CancellationToken cancellationToken,
        Action<IrAllocationInstruction>? allocationObserver = null, Action<IrWriteInstruction>? writeObserver = null)
    {
        var initial = new Dictionary<IrVarId, IrValue>();
        foreach (var parameter in _candidate.Parameters)
        {
            initial[parameter.Entry] = inputs[parameter.Entry];
            initial[parameter.Current] = inputs[parameter.Entry];
        }
        return new IrProgramInterpreter(Factory).Execute(_candidate.Program, initial,
            PassiveCallableVcBuilder.MaximumSteps, ReplayOptions(allocationObserver, writeObserver), cancellationToken);
    }

    private IrProgramReplayOptions ReplayOptions(Action<IrAllocationInstruction>? allocationObserver = null, Action<IrWriteInstruction>? writeObserver = null)
    {
        return new(request => Factory.GetVariableInfo(request.Variable).Type == Factory.BooleanType
            ? Factory.CreateBooleanValue(false)
            : Factory.GetTypeInfo(Factory.GetVariableInfo(request.Variable).Type).Kind == IrTypeKind.Integer
                ? Factory.CreateIntegerValue(Factory.GetVariableInfo(request.Variable).Type, 0L)
                : Factory.CreateNullValue(Factory.GetVariableInfo(request.Variable).Type))
        { AllocationObserver = allocationObserver, WriteObserver = writeObserver };
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
