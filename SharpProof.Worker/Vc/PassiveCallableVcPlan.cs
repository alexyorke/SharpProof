using System.Numerics;

namespace SharpProof.Worker;

internal sealed class PassiveCallableVcPlan
{
    private readonly PassiveCallableCandidate _candidate;
    private readonly ImmutableArray<Assumption> _entry;
    private readonly ImmutableArray<Assumption> _body;
    private readonly ImmutableArray<IrTerm> _goals;
    private readonly IrTerm _normalCompletion;
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
            postconditionGuard: safe, replayOptions: new IrProgramReplayOptions(request =>
                Factory.GetVariableInfo(request.Variable).Type == Factory.BooleanType
                    ? Factory.CreateBooleanValue(false)
                    : Factory.GetTypeInfo(Factory.GetVariableInfo(request.Variable).Type).Kind == IrTypeKind.Integer
                        ? Factory.CreateIntegerValue(Factory.GetVariableInfo(request.Variable).Type, 0L)
                        : Factory.CreateNullValue(Factory.GetVariableInfo(request.Variable).Type)));
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
