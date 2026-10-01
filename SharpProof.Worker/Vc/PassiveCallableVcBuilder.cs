namespace SharpProof.Worker;

// Passive scalar SSA: every constraint is derived from the owned original
// instruction, and every predecessor equation is guarded by its own edge.
internal sealed class PassiveCallableVcBuilder
{
    internal const int MaximumSteps = 4096;
    [SuppressMessage("Design", "CA1032", Justification = "Private construction control flow has no public exception contract.")]
    private sealed class ConstructionLimitException : Exception;
    private sealed record Edge(IrTerm Reach, ImmutableDictionary<IrVarId, IrTerm> State, bool PendingThrow);
    private sealed record Exit(IrTerm Reach, ImmutableDictionary<IrVarId, IrTerm> State, IrTerm? Value);
    private readonly PassiveCallableCandidate _candidate;
    private readonly IrFactory _factory;
    private readonly IrProgram _program;
    private readonly ImmutableHashSet<IrInstructionId> _stops;
    private readonly List<Assumption> _facts = [];
    private readonly Dictionary<ProofJustification, string> _labels = [];
    private readonly Dictionary<ProofJustification, OperationId> _assumes = [];
    private readonly List<IrVarId> _model = [];
    private readonly Dictionary<IrBlockId, List<Edge>> _incoming = [];
    private readonly List<Exit> _returns = [];
    private readonly Dictionary<IrVarId, IrVarId> _oldInputs = [];
    private readonly Dictionary<IrVarId, IrVarId> _inputBindings = [];
    private int _fresh;
    private readonly CancellationToken _cancellationToken;
    private int _remainingWork = MaximumSteps * WorkerBudgets.DefaultMaximumExpressionDepth;
    internal PassiveCallableCandidate Candidate => _candidate;
    internal ImmutableArray<Assumption> EntryAssumptions { get; private set; }
    internal ImmutableArray<IrTerm> Goals { get; private set; }
    internal IrTerm NormalCompletion { get; private set; } = null!;
    internal ImmutableArray<Assumption> Facts => [.. _facts];
    internal ImmutableArray<IrVarId> Model => [.. _model.Distinct()];
    internal ImmutableDictionary<ProofJustification, string> Labels => _labels.ToImmutableDictionary();
    internal ImmutableDictionary<ProofJustification, OperationId> Assumes => _assumes.ToImmutableDictionary();

    private PassiveCallableVcBuilder(PassiveCallableCandidate candidate, CancellationToken cancellationToken,
        PassiveLoopCutter.Encoding? encoding = null)
    {
        _candidate = candidate;
        _factory = candidate.Factory;
        _program = encoding?.Program ?? candidate.Program;
        _stops = encoding?.Stops ?? [];
        _cancellationToken = cancellationToken;
    }

    internal static bool TryBuild(PassiveCallableCandidate candidate,
        out PassiveCallableVcPlan? plan, out WorkerClaimReason failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullGuard.NotNull(candidate, nameof(candidate));
        cancellationToken.ThrowIfCancellationRequested();
        var builder = new PassiveCallableVcBuilder(candidate, cancellationToken);
        try
        {
            if (candidate.Program.Blocks.Length > MaximumSteps)
            { throw new ConstructionLimitException(); }
            var order = IrBlockOrder.TryCreateAcyclicOrder(candidate.Program, amount => { builder.Spend(amount); return true; }, out var orderFailure);
            if (orderFailure == IrAcyclicOrderFailure.CyclicControlFlow)
            {
                if (!PassiveLoopCutter.TryCreate(candidate, out var proof, out var search, out failure, cancellationToken))
                { plan = null; return false; }
                var searchPlan = new PassiveCallableVcBuilder(candidate, cancellationToken, search).Build(boundedSearch: true);
                plan = searchPlan == null ? null : new PassiveCallableVcBuilder(candidate, cancellationToken, proof).Build(searchPlan);
            }
            else
            { plan = order.IsDefault ? null : builder.Build(); }
        }
        catch (ConstructionLimitException)
        { plan = null; failure = WorkerClaimReason.ResourceLimit; return false; }
        failure = plan == null ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None;
        return plan != null;
    }

    private PassiveCallableVcPlan? Build(PassiveCallableVcPlan? loopSearch = null, bool boundedSearch = false)
    {
        var program = _program;
        if (program.Blocks.Length > MaximumSteps)
        { throw new ConstructionLimitException(); }
        var order = IrBlockOrder.TryCreateAcyclicOrder(program, amount => { Spend(amount); return true; }, out var failure);
        if (failure != IrAcyclicOrderFailure.None ||
            _candidate.Result is { } result && !Scalar(_factory.GetVariableInfo(result).Type) ||
            _candidate.Requires.Concat(_candidate.Ensures).Any(clause => !Term(clause.Value) || !Term(clause.Safe)))
        { return null; }
        var instructions = 0;
        foreach (var blockId in order)
        {
            Spend();
            instructions += program.GetBlock(blockId).Instructions.Length;
            if (instructions > MaximumSteps)
            { throw new ConstructionLimitException(); }
        }
        var entry = program.GetBlock(program.Entry).Instructions[0].Operation;
        var initial = ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
        foreach (var parameter in _candidate.Parameters)
        {
            Spend();
            if (!Scalar(_factory.GetVariableInfo(parameter.Entry).Type))
            { return null; }
            initial[parameter.Entry] = _factory.Variable(parameter.Entry);
            initial[parameter.Current] = _factory.Variable(parameter.Entry);
            _oldInputs.Add(parameter.Old, parameter.Entry);
            _inputBindings.Add(parameter.Entry, parameter.Entry);
            _inputBindings.Add(parameter.Current, parameter.Entry);
            _model.Add(parameter.Entry);
            _model.Add(parameter.Current);
            // Replay initializes original current storage from this model
            // identity before running the original entry assignments.
            Fact(Equal(_factory.Variable(parameter.Current), _factory.Variable(parameter.Entry)), entry, "input");
        }
        _incoming.Add(program.Entry, [new(_factory.Boolean(true), initial.ToImmutable(), false)]);
        foreach (var blockId in order)
        {
            Spend();
            var block = program.GetBlock(blockId);
            var site = block.Instructions[0].Operation;
            var predecessors = _incoming[blockId];
            var reach = Fresh(_factory.BooleanType);
            Fact(Equal(reach, Disjoin(predecessors.Select(edge => edge.Reach))), site, "reach");
            var state = new Dictionary<IrVarId, IrTerm>();
            foreach (var variable in predecessors[0].State.Keys.OrderBy(variable => variable.Value))
            {
                Spend(predecessors.Count);
                if (predecessors.Any(edge => !edge.State.ContainsKey(variable)))
                { continue; }
                var incomingValue = predecessors[0].State[variable];
                if (predecessors.All(edge => edge.State[variable].Id == incomingValue.Id))
                {
                    state.Add(variable, incomingValue);
                    continue;
                }
                var phi = Fresh(_factory.GetVariableInfo(variable).Type);
                foreach (var predecessor in predecessors)
                { Fact(Guard(predecessor.Reach, Equal(phi, predecessor.State[variable])), site, "phi"); }
                state.Add(variable, phi);
            }
            var pendingThrow = predecessors.All(edge => edge.PendingThrow);
            foreach (var instruction in block.Instructions)
            {
                Spend();
                switch (instruction)
                {
                    case IrAssignInstruction assign:
                        if (_inputBindings.TryGetValue(assign.Target, out var assignedInput) && assignedInput == assign.Target ||
                            !TryRewrite(assign.Value, state, out var value))
                        { return null; }
                        var assigned = Fresh(_factory.GetVariableInfo(assign.Target).Type);
                        if (!Scalar(assigned.Type))
                        { return null; }
                        Fact(Guard(reach, Equal(assigned, value)), assign.Operation, "write");
                        // Forward only immutable atoms. The fresh write fact
                        // and model identity remain available to the kernel;
                        // compound evaluations keep their own SSA version.
                        state[assign.Target] = value is IrVariableTerm or IrBooleanTerm or IrIntegerTerm or IrNullTerm ? value : assigned;
                        break;
                    case IrHavocInstruction havoc:
                        if (havoc.HavocKind != IrHavocKind.Variables ||
                            havoc.Origin is not (IrHavocOrigin.Input or IrHavocOrigin.Approximation))
                        { return null; }
                        foreach (var variable in havoc.Variables)
                        {
                            Spend();
                            var type = _factory.GetVariableInfo(variable).Type;
                            if (!Scalar(type))
                            { return null; }
                            var replacement = Fresh(type);
                            if (havoc.Origin == IrHavocOrigin.Input)
                            {
                                if (!_inputBindings.TryGetValue(variable, out var input))
                                { return null; }
                                Fact(Guard(reach, Equal(replacement, _factory.Variable(input))), havoc.Operation, "input-havoc");
                            }
                            else if (_inputBindings.TryGetValue(variable, out var approximatedInput) && approximatedInput == variable)
                            { return null; }
                            state[variable] = replacement;
                        }
                        break;
                    case IrAssumeInstruction assume:
                        if (!TryRewrite(assume.Condition, state, out var condition))
                        { return null; }
                        var filtered = Fresh(_factory.BooleanType);
                        Fact(Equal(filtered, And(reach, condition)), assume.Operation, "assume", userAssume: !_stops.Contains(assume.Id));
                        reach = filtered;
                        break;
                    case IrBranchInstruction branch:
                        if (!TryRewrite(branch.Condition, state, out var branchCondition))
                        { return null; }
                        AddEdge(branch.WhenTrue, And(reach, branchCondition), state, pendingThrow, branch.Operation);
                        AddEdge(branch.WhenFalse, And(reach, Not(branchCondition)), state, pendingThrow, branch.Operation);
                        break;
                    case IrGotoInstruction go:
                        AddEdge(go.Target, reach, state, pendingThrow, go.Operation);
                        break;
                    case IrThrowInstruction thrown:
                        AddEdge(thrown.Target, reach, state, true, thrown.Operation);
                        break;
                    case IrExceptionalExitInstruction:
                        // Never assume validity to delete an executable naked
                        // exit. Every original incoming path must carry a throw.
                        if (!pendingThrow)
                        { return null; }
                        break;
                    case IrReturnInstruction returned:
                        IrTerm? returnedValue = null;
                        if (returned.Value is { } expression && !TryRewrite(expression, state, out returnedValue))
                        { return null; }
                        if (_candidate.Result is { } resultVariable
                            ? returnedValue?.Type != _factory.GetVariableInfo(resultVariable).Type
                            : returnedValue != null)
                        { return null; }
                        Spend(state.Count);
                        _returns.Add(new(reach, state.ToImmutableDictionary(), returnedValue));
                        break;
                    default:
                        return null;
                }
            }
        }
        var entryAssumptions = ImmutableArray.CreateBuilder<Assumption>();
        foreach (var clause in _candidate.Requires)
        {
            Spend();
            var assumption = new Assumption(_factory, And(clause.Safe, clause.Value), new LoweredJustification(clause.Operation));
            _labels.Add(assumption.Justification, "requires:" + entryAssumptions.Count.ToString(CultureInfo.InvariantCulture));
            entryAssumptions.Add(assumption);
        }
        var goals = ImmutableArray.CreateBuilder<IrTerm>();
        foreach (var clause in _candidate.Ensures)
        {
            Spend();
            IrTerm goal = _factory.Boolean(true);
            foreach (var returned in _returns)
            {
                Spend();
                if (!TryRewrite(clause.Value, returned.State, out var value, returned, postcondition: true) ||
                    !TryRewrite(clause.Safe, returned.State, out var safe, returned, postcondition: true))
                { return null; }
                goal = And(goal, Guard(returned.Reach, And(safe, value)));
            }
            goals.Add(goal);
        }
        EntryAssumptions = entryAssumptions.ToImmutable();
        Goals = goals.ToImmutable();
        NormalCompletion = _factory.Boolean(false);
        foreach (var returned in _returns)
        {
            Spend();
            NormalCompletion = _factory.Binary(IrBinaryOperator.OrElse, NormalCompletion, returned.Reach);
        }
        return new(this, loopSearch, boundedSearch);
    }

    private void AddEdge(IrBlockId destination, IrTerm condition, Dictionary<IrVarId, IrTerm> state,
        bool pendingThrow, OperationId site)
    {
        Spend(state.Count);
        var edge = Fresh(_factory.BooleanType);
        Fact(Equal(edge, condition), site, "edge");
        if (!_incoming.TryGetValue(destination, out var predecessors))
        { _incoming.Add(destination, predecessors = []); }
        predecessors.Add(new(edge, state.ToImmutableDictionary(), pendingThrow));
    }

    private IrVariableTerm Fresh(IrTypeId type)
    {
        Spend();
        var variable = _factory.CreateVariable("passive:" + _fresh++.ToString(CultureInfo.InvariantCulture), type);
        _model.Add(variable);
        return _factory.Variable(variable);
    }

    private void Fact(IrTerm predicate, OperationId site, string kind, bool userAssume = false)
    {
        Spend();
        ProofJustification justification = userAssume
            ? new UserAssumedJustification(new SourceLocationId(site.Value)) : new LoweredJustification(site);
        _labels.Add(justification, kind + ":" + _facts.Count.ToString(CultureInfo.InvariantCulture));
        if (userAssume)
        { _assumes.Add(justification, site); }
        _facts.Add(new(_factory, predicate, justification));
    }

    private bool TryRewrite(IrTerm root, IReadOnlyDictionary<IrVarId, IrTerm> state, out IrTerm value,
        Exit? returned = null, bool postcondition = false)
    {
        if (!Term(root))
        { value = null!; return false; }
        var substitutions = new Dictionary<IrVarId, IrTerm>();
        foreach (var variable in IrTraversal.CollectVariables(root))
        {
            Spend();
            IrTerm? replacement;
            if (postcondition && _oldInputs.TryGetValue(variable, out var entry))
            { replacement = _factory.Variable(entry); }
            else if (postcondition && variable == _candidate.Result)
            { replacement = returned!.Value; }
            else
            { state.TryGetValue(variable, out replacement); }
            if (replacement == null)
            { value = null!; return false; }
            substitutions.Add(variable, replacement);
        }
        value = IrSubstitution.Substitute(_factory, root, substitutions);
        return true;
    }

    private bool Term(IrTerm root)
    {
        return !IrTraversal.Any(root, term =>
        {
            Spend();
            return !Scalar(term.Type) || term is not (IrBooleanTerm or IrIntegerTerm or IrVariableTerm or IrNullTerm or IrLengthTerm or IrSequenceAccessTerm or IrUnaryTerm or IrBinaryTerm or IrConditionalTerm or IrCastTerm) ||
                term is IrSequenceAccessTerm && _factory.GetTypeInfo(term.Type).Kind is not (IrTypeKind.Boolean or IrTypeKind.Integer) ||
                term is IrCastTerm cast && _factory.GetTypeInfo(cast.Operand.Type).Kind != IrTypeKind.Integer ||
                term is IrBinaryTerm binary && _factory.GetTypeInfo(binary.Left.Type).Kind == IrTypeKind.String &&
                    binary.Operator is not (IrBinaryOperator.Equal or IrBinaryOperator.NotEqual);
        });
    }
    private void Spend(int amount = 1)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_remainingWork < amount)
        { throw new ConstructionLimitException(); }
        _remainingWork -= amount;
    }
    private bool Scalar(IrTypeId type)
    {
        var info = _factory.GetTypeInfo(type);
        return type == _factory.BooleanType || info is { Kind: IrTypeKind.Integer, Width: 8 or 16 or 32 or 64 } ||
            type == _factory.ObjectType || type == _factory.StringType || info.Kind == IrTypeKind.Sequence &&
                info.ElementType is { } element && (_factory.GetTypeInfo(element).Kind is IrTypeKind.Boolean or IrTypeKind.Integer ||
                    element == _factory.ObjectType || element == _factory.StringType);
    }
    private IrTerm Equal(IrTerm left, IrTerm right)
    { return _factory.Binary(IrBinaryOperator.Equal, left, right); }
    private IrTerm And(IrTerm left, IrTerm right)
    { return _factory.Binary(IrBinaryOperator.AndAlso, left, right); }
    private IrTerm Or(IrTerm left, IrTerm right)
    { return _factory.Binary(IrBinaryOperator.OrElse, left, right); }
    private IrTerm Not(IrTerm value)
    { return _factory.Unary(IrUnaryOperator.Not, value); }
    private IrTerm Guard(IrTerm reach, IrTerm predicate)
    { return Or(Not(reach), predicate); }
    private IrTerm Disjoin(IEnumerable<IrTerm> values)
    { return values.Aggregate((IrTerm)_factory.Boolean(false), Or); }
}
