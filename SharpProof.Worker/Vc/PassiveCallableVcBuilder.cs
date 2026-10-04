namespace SharpProof.Worker;

// Passive scalar SSA: every constraint is derived from the owned original
// instruction, and every predecessor equation is guarded by its own edge.
internal sealed class PassiveCallableVcBuilder
{
    internal const int MaximumSteps = 4096;
    [SuppressMessage("Design", "CA1032", Justification = "Private construction control flow has no public exception contract.")]
    private sealed class ConstructionLimitException : Exception;
    private sealed record Edge(IrTerm Reach, ImmutableDictionary<IrVarId, IrTerm> State, IrTerm? PendingException);
    private sealed record Exit(IrTerm Reach, ImmutableDictionary<IrVarId, IrTerm> State, IrTerm? Value, Heap Contents);
    // The element and field stores made so far in program order, each under
    // its reach, over the entry contents; forgotten contents are unknown after
    // something wrote the heap without a modeled store. A store's reach is
    // false on every path that does not execute it, and a path that executes
    // it before a read processes it first, so the latest matching store
    // decides a read.
    // Element contents can be forgotten alone: an element store never
    // changes a field.
    private sealed record Heap(ImmutableArray<(IrTerm Reach, IrTerm Target, IrTerm? Index, IrMemberId? Field, IrTerm Value)> Stores,
        bool Forgotten, bool ElementsForgotten = false);
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
    private readonly List<(IrTerm Reach, IrTerm Kind)> _exceptions = [];
    private readonly List<(IrTerm Reach, OperationId Site)> _allocations = [];
    private readonly List<(IrTerm Reach, OperationId Site, IrWriteRegion Region)> _writes = [];
    private readonly List<(IrTerm Reach, OperationId Site)> _locks = [];
    private readonly List<(int Ordinal, IrTerm Reach, IrTerm Predicate, ImmutableArray<Assumption> Facts)> _callPreconditions = [];
    private readonly Dictionary<IrInstructionId, int> _callMarkers;
    private readonly ImmutableDictionary<IrInstructionId, int> _checkpointMarkers;
    private readonly List<(int Ordinal, IrTerm Reach, IrTerm Predicate, ImmutableArray<Assumption> Facts)> _checkpoints = [];
    internal ImmutableArray<(int Ordinal, IrTerm Reach, IrTerm Predicate, ImmutableArray<Assumption> Facts)> Checkpoints =>
        [.. _checkpoints];
    private readonly List<IrTerm> _potentialExceptionAllocations = [];
    // Opaque calls: each may allocate, write and synchronize.
    private readonly List<(IrTerm Reach, IrOpaqueCallEffects Effects)> _opaqueCalls = [];
    // Approximated field and element reads.
    private readonly List<IrTerm> _reads = [];
    internal ImmutableArray<IrTerm> Reads => [.. _reads];
    private readonly HashSet<IrInstructionId> _sequenceReaders;
    // Static field reads, which observable purity excludes.
    private readonly List<IrTerm> _ambientReads = [];
    internal ImmutableArray<IrTerm> AmbientReads => [.. _ambientReads];
    private const string StaticReadPrefix = "StaticFieldReference@";
    private readonly List<(IrTerm Predicate, OperationId Site)> _exceptionFacts = [];
    private readonly Dictionary<IrVarId, IrVarId> _oldInputs = [];
    private readonly Dictionary<IrVarId, IrVarId> _inputBindings = [];
    private int _fresh;
    private bool _hasStringConcat;
    private Heap _heap = new([], false);
    private bool _hasUnmodeledStringAllocations;
    private readonly CancellationToken _cancellationToken;
    private int _remainingWork = MaximumSteps * WorkerBudgets.DefaultMaximumExpressionDepth;
    internal PassiveCallableCandidate Candidate => _candidate;
    internal ImmutableArray<Assumption> EntryAssumptions { get; private set; }

    // `this` is never null; an entry witness must respect it.
    internal const string ReceiverLabel = "input:receiver";

    internal static Assumption ReceiverAssumption(IrFactory factory, IrVarId receiver, OperationId site)
    {
        var value = factory.Variable(receiver);
        return new(factory, factory.Unary(IrUnaryOperator.Not, factory.Binary(IrBinaryOperator.Equal, value, factory.Null(value.Type))),
            new LoweredJustification(site));
    }
    internal ImmutableArray<IrTerm> Goals { get; private set; }
    internal IrTerm NormalCompletion { get; private set; } = null!;
    internal ImmutableArray<(IrTerm Reach, IrTerm Kind)> Exceptions => [.. _exceptions];
    // Each explicit throw site carries its own exception code, so a claim can
    // admit the sites whose static type it allows.
    internal const int ExplicitSiteCode = 1024;
    private readonly List<OperationId> _explicitSites = [];
    internal ImmutableArray<OperationId> ExplicitSites => [.. _explicitSites];
    internal ImmutableArray<(IrTerm Reach, OperationId Site)> Allocations => [.. _allocations];
    internal ImmutableArray<(IrTerm Reach, OperationId Site, IrWriteRegion Region)> Writes => [.. _writes];
    internal ImmutableArray<(IrTerm Reach, OperationId Site)> Locks => [.. _locks];
    internal ImmutableArray<(int Ordinal, IrTerm Reach, IrTerm Predicate, ImmutableArray<Assumption> Facts)> CallPreconditions =>
        [.. _callPreconditions];
    internal ImmutableArray<IrTerm> PotentialExceptionAllocations => [.. _potentialExceptionAllocations];
    internal ImmutableArray<(IrTerm Reach, IrOpaqueCallEffects Effects)> OpaqueCalls => [.. _opaqueCalls];
    internal bool HasUnmodeledAllocations => _hasUnmodeledStringAllocations;
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
        _sequenceReaders = IrSequenceReads.Readers(_program);
        _stops = encoding?.Stops ?? [];
        var originalMarkers = candidate.CallPreconditions.Select((clause, ordinal) => (clause.Marker, Ordinal: ordinal))
            .ToDictionary(row => row.Marker, row => row.Ordinal);
        _callMarkers = encoding == null ? originalMarkers : encoding.CallMarkers
            .ToDictionary(row => row.Key, row => originalMarkers[row.Value]);
        _checkpointMarkers = encoding?.Checkpoints ?? ImmutableDictionary<IrInstructionId, int>.Empty;
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

    // A proof plan whose loop headers assume the given invariants.
    internal static PassiveCallableVcPlan? TryBuildWithInvariants(PassiveCallableCandidate candidate,
        ImmutableArray<PassiveLoopCutter.Invariant> invariants, CancellationToken cancellationToken)
    {
        if (PassiveLoopCutter.TryEncodeWithInvariants(candidate, invariants, cancellationToken) is not { } encoding)
        { return null; }
        try
        { return new PassiveCallableVcBuilder(candidate, cancellationToken, encoding).Build(); }
        catch (ConstructionLimitException)
        { return null; }
    }

    private PassiveCallableVcPlan? Build(PassiveCallableVcPlan? loopSearch = null, bool boundedSearch = false)
    {
        var program = _program;
        if (program.Blocks.Length > MaximumSteps)
        { throw new ConstructionLimitException(); }
        _hasStringConcat = _candidate.Requires.Concat(_candidate.Ensures)
            .Any(clause => HasStringConcat(clause.Value) || HasStringConcat(clause.Safe)) ||
            program.Blocks.SelectMany(block => block.Instructions).Any(instruction =>
                HasStringConcat(instruction switch
                {
                    IrAssignInstruction assign => assign.Value,
                    IrBranchInstruction branch => branch.Condition,
                    IrAssumeInstruction assume => assume.Condition,
                    IrReturnInstruction returned => returned.Value,
                    _ => null
                }));
        var stringAllocationSites = program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrAllocationInstruction>().Where(allocation => allocation.AllocatedType == _factory.StringType)
            .Select(allocation => allocation.Operation).ToImmutableHashSet();
        _hasUnmodeledStringAllocations = _candidate.Requires.Concat(_candidate.Ensures)
            .Any(clause => HasStringConcat(clause.Value) || HasStringConcat(clause.Safe)) ||
            program.Blocks.SelectMany(block => block.Instructions).Any(instruction =>
                instruction is IrAssignInstruction assign && !_callMarkers.ContainsKey(assign.Id) &&
                    HasStringConcat(assign.Value) && !stringAllocationSites.Contains(assign.Operation) ||
                instruction is IrReturnInstruction returned && HasStringConcat(returned.Value) ||
                instruction is IrBranchInstruction branch && HasStringConcat(branch.Condition) ||
                instruction is IrAssumeInstruction assume && HasStringConcat(assume.Condition));
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

        _incoming.Add(program.Entry, [new(_factory.Boolean(true), initial.ToImmutable(), null)]);
        foreach (var blockId in order)
        {
            Spend();
            var block = program.GetBlock(blockId);
            var site = block.Instructions[0].Operation;
            var predecessors = _incoming[blockId];
            IrTerm reach = Fresh(_factory.BooleanType);
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
            IrTerm? pendingException = null;
            if (predecessors.All(edge => edge.PendingException != null))
            {
                pendingException = predecessors[0].PendingException!;
                if (predecessors.Any(edge => edge.PendingException!.Id != pendingException.Id))
                {
                    pendingException = Fresh(_factory.IntegerType);
                    foreach (var predecessor in predecessors)
                    {
                        Spend();
                        _exceptionFacts.Add((Guard(predecessor.Reach, Equal(pendingException, predecessor.PendingException!)), site));
                    }
                }
            }
            foreach (var instruction in block.Instructions)
            {
                Spend();
                if (_sequenceReaders.Contains(instruction.Id))
                { _reads.Add(reach); }
                switch (instruction)
                {
                    case IrAllocationInstruction allocation:
                        IrTerm? arrayLength = null;
                        if (allocation.Length is { } dimension)
                        {
                            if (!TryRewrite(dimension, state, out arrayLength))
                            { return null; }
                            var validLength = _factory.Binary(IrBinaryOperator.GreaterThanOrEqual, arrayLength, _factory.Integer(0));
                            var invalidLength = And(reach, Not(validLength));
                            _exceptions.Add((invalidLength, _factory.Integer((int)IrExceptionKind.Overflow)));
                            _potentialExceptionAllocations.Add(invalidLength);
                            reach = And(reach, validLength);
                        }
                        _allocations.Add((reach, allocation.Operation));
                        if (allocation.Target is { } allocatedTarget)
                        {
                            if (_inputBindings.TryGetValue(allocatedTarget, out var allocatedInput) && allocatedInput == allocatedTarget)
                            { return null; }
                            var allocated = Fresh(allocation.AllocatedType);
                            Fact(Guard(reach, Not(Equal(allocated, _factory.Null(allocated.Type)))), allocation.Operation, "allocation-nonnull");
                            if (arrayLength != null)
                            { Fact(Guard(reach, Equal(_factory.Length(allocated), arrayLength)), allocation.Operation, "array-length"); }
                            // The sequence encoder observes only scalar elements.
                            // Reference contents remain an overapproximation.
                            for (var index = 0; index < allocation.InitialValues.Length &&
                                _factory.GetTypeInfo(allocation.InitialValues[index].Type).Kind is IrTypeKind.Boolean or IrTypeKind.Integer; index++)
                            {
                                Spend();
                                Fact(Guard(reach, Equal(_factory.SequenceAccess(allocated, _factory.Integer(index)),
                                    allocation.InitialValues[index])), allocation.Operation, "array-initializer");
                            }
                            foreach (var existing in state.Values.Where(value => value.Type == allocated.Type).Distinct())
                            {
                                Spend();
                                Fact(Guard(reach, Not(Equal(allocated, existing))), allocation.Operation, "allocation-fresh");
                            }
                            state[allocatedTarget] = allocated;
                        }
                        break;
                    case IrLockInstruction synchronization:
                        if (!TryRewrite(synchronization.Receiver, state, out _))
                        { return null; }
                        _locks.Add((reach, synchronization.Operation));
                        break;
                    case IrWriteInstruction write:
                        if (!IrWriteSites.IsLoopCell(_factory, write))
                        { _writes.Add((reach, write.Operation, IrWriteSites.IsObservable(_factory, write) ? write.Region : IrWriteRegion.Local)); }
                        if (write is { Target: { } stored, Value: { } storedValue })
                        {
                            IrTerm? storedPosition = null;
                            if (!TryRewrite(stored, state, out var storedTarget) ||
                                write.Index is { } storedIndex && !TryRewrite(storedIndex, state, out storedPosition) ||
                                !TryRewrite(storedValue, state, out var storedElement))
                            { return null; }
                            Spend(_heap.Stores.Length);
                            _heap = _heap with { Stores = _heap.Stores.Add((reach, storedTarget, storedPosition, write.Field, storedElement)) };
                        }
                        else if (write.Region == IrWriteRegion.Element)
                        { _heap = new([.. _heap.Stores.Where(store => store.Field != null)], _heap.Forgotten, true); }
                        else if (write.Region is IrWriteRegion.Field or IrWriteRegion.Parameter or IrWriteRegion.Unknown)
                        { _heap = new([], true); }
                        break;
                    case IrCallInstruction call:
                        if (call.Receiver != null || call.Target != null)
                        { return null; }
                        foreach (var argument in call.Arguments)
                        {
                            if (!TryRewrite(argument, state, out _))
                            { return null; }
                        }
                        _opaqueCalls.Add((reach, IrOpaqueCallSite.Effects(_factory, call.Operation)));
                        // A call may write any array.
                        _heap = new([], true);
                        break;
                    case IrAssignInstruction assign:
                        if (_inputBindings.TryGetValue(assign.Target, out var assignedInput) && assignedInput == assign.Target ||
                            !TryRewrite(assign.Value, state, out var value))
                        { return null; }
                        if (_callMarkers.TryGetValue(assign.Id, out var callOrdinal))
                        {
                            // Later point assumptions must not prove an earlier
                            // call obligation by excluding its execution prefix.
                            Spend(_facts.Count);
                            _callPreconditions.Add((callOrdinal, reach, value, [.. _facts]));
                        }
                        if (_checkpointMarkers.TryGetValue(assign.Id, out var invariantOrdinal))
                        {
                            Spend(_facts.Count);
                            _checkpoints.Add((invariantOrdinal, reach, value, [.. _facts]));
                        }
                        if (value is IrStringTerm || HasStringConcat(value))
                        {
                            // This domain observes strings through nullness and
                            // length, not allocation identity. Preserve the value
                            // expression instead of asking SAT replay to reproduce
                            // a freshly allocated SSA reference or an abstract
                            // observation (such as its length) of that reference.
                            state[assign.Target] = value;
                            break;
                        }
                        var assigned = Fresh(_factory.GetVariableInfo(assign.Target).Type);
                        if (!Scalar(assigned.Type))
                        { return null; }
                        Fact(Guard(reach, Equal(assigned, value)), assign.Operation, "write");
                        // Forward only immutable atoms. The fresh write fact
                        // and model identity remain available to the kernel;
                        // compound evaluations keep their own SSA version.
                        state[assign.Target] = value is IrVariableTerm or IrBooleanTerm or IrIntegerTerm or IrNullTerm or IrEmptyArrayTerm ? value : assigned;
                        break;
                    case IrHavocInstruction havoc:
                        if (havoc.HavocKind is IrHavocKind.Memory or IrHavocKind.VariablesAndMemory &&
                            havoc.Origin == IrHavocOrigin.Approximation)
                        { _heap = new([], true); }
                        else if (havoc.HavocKind != IrHavocKind.Variables ||
                            havoc.Origin is not (IrHavocOrigin.Input or IrHavocOrigin.Approximation))
                        { return null; }
                        if (havoc.Origin == IrHavocOrigin.Approximation && ReadsState(havoc.Operation))
                        {
                            _reads.Add(reach);
                            if (Describe(havoc.Operation).StartsWith(StaticReadPrefix, StringComparison.Ordinal))
                            { _ambientReads.Add(reach); }
                        }
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
                        AddEdge(branch.WhenTrue, And(reach, branchCondition), state, pendingException, branch.Operation);
                        AddEdge(branch.WhenFalse, And(reach, Not(branchCondition)), state, pendingException, branch.Operation);
                        break;
                    case IrGotoInstruction go:
                        AddEdge(go.Target, reach, state, pendingException, go.Operation);
                        break;
                    case IrThrowInstruction thrown:
                        // Constructing a runtime fault can allocate even when
                        // a handler prevents it from escaping the callable.
                        _potentialExceptionAllocations.Add(reach);
                        var code = (int)thrown.ExceptionKind;
                        if (thrown.ExceptionKind == IrExceptionKind.Explicit)
                        {
                            if (!_explicitSites.Contains(thrown.Operation))
                            { _explicitSites.Add(thrown.Operation); }
                            code = ExplicitSiteCode + _explicitSites.IndexOf(thrown.Operation);
                        }
                        AddEdge(thrown.Target, reach, state, _factory.Integer(code), thrown.Operation);
                        break;
                    case IrExceptionalExitInstruction:
                        // Never assume validity to delete an executable naked
                        // exit. Every original incoming path must carry a throw.
                        if (pendingException == null)
                        { return null; }
                        _exceptions.Add((reach, pendingException));
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
                        _returns.Add(new(reach, state.ToImmutableDictionary(), returnedValue, _heap));
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
        if (_candidate.HasReceiver)
        {
            var receiver = ReceiverAssumption(_factory, _candidate.Parameters[_candidate.Parameters.Length - 1].Entry, entry);
            _labels.Add(receiver.Justification, ReceiverLabel);
            entryAssumptions.Add(receiver);
        }
        var goals = ImmutableArray.CreateBuilder<IrTerm>();
        foreach (var clause in _candidate.Ensures)
        {
            Spend();
            IrTerm goal = _factory.Boolean(true);
            foreach (var returned in _returns)
            {
                Spend();
                if (!TryRewrite(clause.Value, returned.State, out var value, returned, postcondition: true, returned.Contents) ||
                    !TryRewrite(clause.Safe, returned.State, out var safe, returned, postcondition: true, returned.Contents))
                { return null; }
                goal = And(goal, Guard(returned.Reach, And(safe, value)));
            }
            goals.Add(goal);
        }
        EntryAssumptions = entryAssumptions.ToImmutable();
        Goals = goals.ToImmutable();
        IrTerm normalCompletion = _factory.Boolean(false);
        foreach (var returned in _returns)
        {
            Spend();
            normalCompletion = _factory.Binary(IrBinaryOperator.OrElse, normalCompletion, returned.Reach);
        }
        // Retain structural absence of normal exits in the kernel proof core.
        // The fresh total definition adds no restriction on inputs or paths.
        NormalCompletion = Fresh(_factory.BooleanType);
        Fact(Equal(NormalCompletion, normalCompletion), entry, "normal-completion");
        // Preserve existing body/core labels while adding independent kind facts.
        foreach (var fact in _exceptionFacts)
        { Fact(fact.Predicate, fact.Site, "exception-kind"); }
        return new(this, loopSearch, boundedSearch);
    }

    private void AddEdge(IrBlockId destination, IrTerm condition, Dictionary<IrVarId, IrTerm> state,
        IrTerm? pendingException, OperationId site)
    {
        Spend(state.Count);
        var edge = Fresh(_factory.BooleanType);
        Fact(Equal(edge, condition), site, "edge");
        if (!_incoming.TryGetValue(destination, out var predecessors))
        { _incoming.Add(destination, predecessors = []); }
        predecessors.Add(new(edge, state.ToImmutableDictionary(), pendingException));
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
        Exit? returned = null, bool postcondition = false, Heap? heap = null)
    {
        heap ??= _heap;
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
        var consistent = true;
        IrTerm? Read(IrTerm original, Func<IrTerm, IrTerm> rewritten)
        {
            if (original is IrSequenceAccessTerm access)
            { return ElementRead(access, rewritten(access.Sequence), rewritten(access.Index)); }
            if (!IrFieldSites.IsFieldRead(_factory, original))
            { return null; }
            var field = (IrOpaqueTerm)original;
            var receiver = rewritten(field.Receiver!);
            Spend(heap.Stores.Length);
            // A read through an Old snapshot sees the objects as the callable
            // entered; only clauses and loop invariants read through one.
            var entry = _factory.PureOpaque(field.Member, receiver);
            if (IrTraversal.CollectVariables(field.Receiver!).Any(_oldInputs.ContainsKey))
            { return entry; }
            IrTerm read = heap.Forgotten ? Fresh(original.Type) : entry;
            foreach (var store in heap.Stores)
            {
                if (store.Field != field.Member)
                { continue; }
                if (store.Target.Type != receiver.Type || store.Value.Type != original.Type)
                { consistent = false; continue; }
                read = _factory.Conditional(And(store.Reach, Equal(receiver, store.Target)), store.Value, read);
            }
            return read;
        }
        IrTerm ElementRead(IrSequenceAccessTerm original, IrTerm sequence, IrTerm index)
        {
            Spend(heap.Stores.Length);
            // An old value reads the arrays as the callable entered.
            if (IrTraversal.CollectVariables(original.Sequence).Any(_oldInputs.ContainsKey))
            { return _factory.SequenceAccess(sequence, index); }
            IrTerm read = heap.Forgotten || heap.ElementsForgotten ? Fresh(original.Type) : _factory.SequenceAccess(sequence, index);
            foreach (var store in heap.Stores)
            {
                if (store.Index is not { } storeIndex || store.Target.Type != sequence.Type)
                { continue; }
                if (Position(storeIndex) is not { } stored || Position(index) is not { } position || store.Value.Type != original.Type)
                { consistent = false; continue; }
                read = _factory.Conditional(And(store.Reach, And(Equal(sequence, store.Target), Equal(position, stored))),
                    store.Value, read);
            }
            return read;
        }
        // Indexes of every modeled width compare as Int32.
        IrTerm? Position(IrTerm index)
        {
            return index.Type == _factory.IntegerType ? index
                : _factory.GetTypeInfo(index.Type) is { Kind: IrTypeKind.Integer, Width: > 0 and < 32 } ? _factory.Cast(_factory.IntegerType, index)
                : null;
        }
        value = IrSubstitution.SubstituteWithReads(_factory, root, substitutions, Read);
        return consistent;
    }

    private bool Term(IrTerm root)
    {
        return !IrTraversal.Any(root, term =>
        {
            Spend();
            return !Scalar(term.Type) || term is not (IrBooleanTerm or IrIntegerTerm or IrStringTerm or IrVariableTerm or IrNullTerm or IrEmptyArrayTerm or IrLengthTerm or IrSequenceAccessTerm or IrUnaryTerm or IrBinaryTerm or IrConditionalTerm or IrCastTerm or IrOpaqueTerm) ||
                term is IrOpaqueTerm && !IrFieldSites.IsFieldRead(_factory, term) && !IrInvariantRelations.IsRelation(_factory, term) ||
                term is IrSequenceAccessTerm && _factory.GetTypeInfo(term.Type).Kind is not (IrTypeKind.Boolean or IrTypeKind.Integer) ||
                term is IrCastTerm cast && _factory.GetTypeInfo(cast.Operand.Type).Kind != IrTypeKind.Integer &&
                    !(cast.Type == _factory.ObjectType && _factory.GetTypeInfo(cast.Operand.Type).Kind is IrTypeKind.Reference or IrTypeKind.String or IrTypeKind.Sequence ||
                        _factory.GetTypeInfo(cast.Operand.Type).Kind == IrTypeKind.Reference && _factory.GetTypeInfo(cast.Type).Kind == IrTypeKind.Reference) ||
                term is IrBinaryTerm binary && _factory.GetTypeInfo(binary.Left.Type).Kind == IrTypeKind.String &&
                    (binary.Operator is not (IrBinaryOperator.Equal or IrBinaryOperator.NotEqual or IrBinaryOperator.StringConcat or
                        IrBinaryOperator.StringEquals) ||
                        _hasStringConcat && binary.Operator is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual &&
                            binary.Left is not IrNullTerm && binary.Right is not IrNullTerm);
        });
    }
    private bool HasStringConcat(IrTerm? root)
    {
        Spend();
        return root != null && IrTraversal.Any(root, term =>
        {
            Spend();
            return term is IrBinaryTerm { Operator: IrBinaryOperator.StringConcat };
        });
    }
    private void Spend(int amount = 1)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_remainingWork < amount)
        { throw new ConstructionLimitException(); }
        _remainingWork -= amount;
    }
    // Field and element reads, including the read inside an increment or a
    // compound assignment, are approximations at these sites.
    private string Describe(OperationId site)
    { return _factory.GetOperationInfo(site).Description is { } id ? _factory.GetString(id) : ""; }

    private bool ReadsState(OperationId site)
    {
        var description = Describe(site);
        return description.StartsWith("FieldReference@", StringComparison.Ordinal) ||
            description.StartsWith(StaticReadPrefix, StringComparison.Ordinal) ||
            description.StartsWith("PropertyReference@", StringComparison.Ordinal) ||
            description.StartsWith("ArrayElementReference@", StringComparison.Ordinal) ||
            description.StartsWith("Increment@", StringComparison.Ordinal) ||
            description.StartsWith("Decrement@", StringComparison.Ordinal) ||
            description.StartsWith("CompoundAssignment@", StringComparison.Ordinal);
    }

    private bool Scalar(IrTypeId type)
    {
        var info = _factory.GetTypeInfo(type);
        return type == _factory.BooleanType || info is { Kind: IrTypeKind.Integer, Width: 8 or 16 or 32 or 64 } ||
            info.Kind == IrTypeKind.Reference || type == _factory.StringType || info.Kind == IrTypeKind.Sequence &&
                info.ElementType is { } element && (_factory.GetTypeInfo(element).Kind is IrTypeKind.Boolean or IrTypeKind.Integer or IrTypeKind.Reference ||
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
