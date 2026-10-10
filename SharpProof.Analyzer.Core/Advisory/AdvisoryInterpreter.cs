using SharpProof.Advisory.Core;
using SharpProof.Dataflow;

namespace SharpProof.Analyzer;

// Advisory Core IR facts remain separate from VC construction and native proof enrollment.
internal sealed record CoreIrAdvisoryState(bool Reachable, ImmutableDictionary<IrVarId, IntervalValue> Values)
{
    internal bool HasGap { get; init; }
    internal ImmutableDictionary<IrVarId, NullnessValue> Nullness { get; init; } = ImmutableDictionary<IrVarId, NullnessValue>.Empty;
    internal ImmutableDictionary<IrVarId, SequenceCardinalityValue> Cardinality { get; init; } = ImmutableDictionary<IrVarId, SequenceCardinalityValue>.Empty;
    // Captured values that still equal a pure expression over current variables.
    internal ImmutableDictionary<IrVarId, IrTerm> Origins { get; init; } = ImmutableDictionary<IrVarId, IrTerm>.Empty;
}
internal sealed record CoreIrAdvisoryMarkerSnapshot(IrAssignInstruction Marker, IntervalValue Value, bool PrefixHasGap);
internal sealed record CoreIrAdvisoryResult(ImmutableArray<CoreIrAdvisoryState> Inputs, ImmutableArray<CoreIrAdvisoryState> Outputs, ImmutableArray<string> Gaps, int Iterations, bool Accepted = true)
{
    internal ImmutableArray<CoreIrAdvisoryMarkerSnapshot> Markers { get; init; } = [];
    internal bool TryGetOutput(int block, out CoreIrAdvisoryState? state)
    {
        state = null;
        if (!Accepted || block < 0 || block >= Outputs.Length)
        {
            return false;
        }

        state = Outputs[block];
        return true;
    }
    internal CoreIrAdvisoryState RequireOutput(int block)
    {
        if (!TryGetOutput(block, out var state))
        {
            throw new InvalidOperationException("No advisory block state is available.");
        }

        return state!;
    }
}
internal sealed class CoreIrAdvisoryDomain(IrFactory factory, ImmutableArray<IrVarId> variables) : IAbstractDomain<CoreIrAdvisoryState>
{
    internal IntervalValue Range(IrVarId variable)
    {
        return RangeType(factory.GetVariableInfo(variable).Type);
    }

    internal IntervalValue RangeType(IrTypeId type)
    {
        return factory.GetTypeInfo(type).Kind == IrTypeKind.Boolean ? IntervalValue.Range(0, 1) : CoreIrScalarIntervalTransfer.TryTypeRange(factory.GetTypeInfo(type), out var range) ? range : IntervalDomain.Instance.Top;
    }

    internal IntervalValue Get(CoreIrAdvisoryState state, IrVarId variable)
    {
        return state.Values.TryGetValue(variable, out var value) ? value : Range(variable);
    }

    internal CoreIrAdvisoryState Set(CoreIrAdvisoryState state, IrVarId variable, IntervalValue value)
    {
        return value.IsBottom ? Bottom : state with { Values = state.Values.SetItem(variable, value) };
    }

    public CoreIrAdvisoryState Bottom => new(false, ImmutableDictionary<IrVarId, IntervalValue>.Empty);
    public CoreIrAdvisoryState Top => new(true, ImmutableDictionary<IrVarId, IntervalValue>.Empty) { HasGap = true };
    public bool LessThanOrEqual(CoreIrAdvisoryState left, CoreIrAdvisoryState right)
    {
        return !left.Reachable || right.Reachable && (!left.HasGap || right.HasGap) &&
            right.Origins.All(pair => left.Origins.TryGetValue(pair.Key, out var origin) && ReferenceEquals(origin, pair.Value)) &&
            variables.All(v => IntervalDomain.Instance.LessThanOrEqual(Get(left, v), Get(right, v)) && NullnessDomain.Instance.LessThanOrEqual(GetNull(left, v), GetNull(right, v)) && SequenceCardinalityDomain.Instance.LessThanOrEqual(GetCard(left, v), GetCard(right, v)));
    }

    public bool AreEquivalent(CoreIrAdvisoryState left, CoreIrAdvisoryState right)
    {
        return LessThanOrEqual(left, right) && LessThanOrEqual(right, left);
    }

    public CoreIrAdvisoryState Join(CoreIrAdvisoryState left, CoreIrAdvisoryState right)
    {
        return !left.Reachable ? right : !right.Reachable ? left : Product(new(true, variables.ToImmutableDictionary(v => v, v => IntervalDomain.Instance.Join(Get(left, v), Get(right, v)))), left, right, false);
    }

    public CoreIrAdvisoryState Widen(CoreIrAdvisoryState previous, CoreIrAdvisoryState candidate)
    {
        if (!previous.Reachable)
        {
            return candidate;
        }

        if (!candidate.Reachable)
        {
            return previous;
        }

        return Product(new(true, variables.ToImmutableDictionary(v => v, v => Restrict(IntervalDomain.Instance.Widen(Get(previous, v), Get(candidate, v)), Range(v)))), previous, candidate, true);
    }
    internal static NullnessValue GetNull(CoreIrAdvisoryState state, IrVarId variable)
    { return state.Nullness.TryGetValue(variable, out var value) ? value : NullnessValue.MaybeNull; }
    internal static SequenceCardinalityValue GetCard(CoreIrAdvisoryState state, IrVarId variable)
    { return state.Cardinality.TryGetValue(variable, out var value) ? value : SequenceCardinalityDomain.Instance.Top; }
    internal CoreIrAdvisoryState SetReference(CoreIrAdvisoryState state, IrVarId variable, NullnessValue nullness, SequenceCardinalityValue cardinality)
    {
        if (nullness == NullnessValue.Bottom || cardinality.IsBottom)
        {
            return Bottom;
        }

        var kind = factory.GetTypeInfo(factory.GetVariableInfo(variable).Type).Kind;
        if (kind is IrTypeKind.String or IrTypeKind.Sequence)
        {
            if (nullness == NullnessValue.Null)
            {
                cardinality = SequenceCardinalityDomain.Instance.Empty;
            }

            if (cardinality.Length.LowerBound >= 1)
            {
                nullness = NullnessDomain.Instance.AssumeNonNull(nullness);
            }
        }
        return nullness == NullnessValue.Bottom ? Bottom : state with { Nullness = state.Nullness.SetItem(variable, nullness), Cardinality = state.Cardinality.SetItem(variable, cardinality) };
    }
    private CoreIrAdvisoryState Product(CoreIrAdvisoryState result, CoreIrAdvisoryState left, CoreIrAdvisoryState right, bool widen)
    {
        result = result with
        {
            HasGap = left.HasGap || right.HasGap,
            Origins = left.Origins.Where(pair => right.Origins.TryGetValue(pair.Key, out var origin) && ReferenceEquals(origin, pair.Value))
                .ToImmutableDictionary(pair => pair.Key, pair => pair.Value)
        };
        foreach (var variable in variables)
        {
            var kind = factory.GetTypeInfo(factory.GetVariableInfo(variable).Type).Kind;
            if (kind is not (IrTypeKind.String or IrTypeKind.Sequence or IrTypeKind.Reference))
            {
                continue;
            }

            var nullness = NullnessDomain.Instance.Join(GetNull(left, variable), GetNull(right, variable));
            var card = widen ? SequenceCardinalityDomain.Instance.Widen(GetCard(left, variable), GetCard(right, variable)) : SequenceCardinalityDomain.Instance.Join(GetCard(left, variable), GetCard(right, variable));
            result = SetReference(result, variable, nullness, card);
        }
        return result;
    }
    internal static IntervalValue Restrict(IntervalValue value, IntervalValue range)
    {
        return IntervalDomain.Instance.AssumeAtMost(IntervalDomain.Instance.AssumeAtLeast(value, range.LowerBound ?? long.MinValue), range.UpperBound ?? long.MaxValue);
    }

    public CoreIrAdvisoryState Havoc(CoreIrAdvisoryState value)
    {
        return value.Reachable ? Top : Bottom;
    }
}
internal sealed class CoreIrAdvisoryInterpreter
{
    private readonly IrProgram _program;
    private readonly IrFactory _factory;
    private readonly CoreIrAdvisoryDomain _domain;
    private readonly ImmutableArray<IrVarId> _variables;
    private readonly HashSet<string> _gaps = [];
    private CancellationToken _token;
    private int _termWorkRemaining;
    private bool _termBudgetExceeded;
    private bool _activeTransferHasGap;
    private Dictionary<IrInstructionId, IrAssignInstruction>? _markerSites;
    private List<CoreIrAdvisoryMarkerSnapshot>? _markerSnapshots;
    internal CoreIrAdvisoryInterpreter(IrProgram program, ImmutableArray<IrVarId> variables)
    { _program = program; _factory = program.Factory; _domain = new(_factory, variables); _variables = variables; }
    internal CoreIrAdvisoryResult Run(ImmutableDictionary<IrVarId, IntervalValue> initial, int maxIterations = 1000, int maximumTermWork = 65536, int maximumSyntheticEdges = 8192, ImmutableArray<IrAssignInstruction> markers = default, CancellationToken token = default)
    {
        _token = token;
        token.ThrowIfCancellationRequested();
        _gaps.Clear();
        _markerSites = null;
        _markerSnapshots = null;
        if (_factory.Semantics != IrExecutionSemantics.Total || _program.Blocks.Length > 4096)
        { return Incomplete("admission/graph budget"); }
        _termWorkRemaining = maximumTermWork;
        _termBudgetExceeded = false;
        if (maximumTermWork < 1 || maximumSyntheticEdges < 0)
        {
            return Incomplete("invalid work budget");
        }

        var instructionCount = 0;
        foreach (var block in _program.Blocks)
        {
            _token.ThrowIfCancellationRequested();
            if (block.Instructions.Length > 4096 - instructionCount)
            {
                return Incomplete("admission/graph budget");
            }

            instructionCount += block.Instructions.Length;
        }
        // Roslyn lowering keeps a detached structural exceptional exit. Only
        // unsupported flow reachable from the actual entry blocks admission.
        var reachable = new HashSet<IrBlockId>();
        var pending = new Stack<IrBlockId>();
        pending.Push(_program.Entry);
        while (pending.Count != 0)
        {
            _token.ThrowIfCancellationRequested();
            var current = pending.Pop();
            if (!reachable.Add(current))
            {
                continue;
            }
            switch (_program.GetBlock(current).Terminator)
            {
                case IrGotoInstruction go:
                    pending.Push(go.Target);
                    break;
                case IrBranchInstruction branch:
                    pending.Push(branch.WhenTrue);
                    pending.Push(branch.WhenFalse);
                    break;
                case IrThrowInstruction thrown:
                    pending.Push(thrown.Target);
                    break;
            }
        }
        foreach (var block in _program.Blocks)
        {
            _token.ThrowIfCancellationRequested();
            if (reachable.Contains(block.Id) &&
                block.Terminator is not (IrBranchInstruction or IrGotoInstruction or IrReturnInstruction or IrThrowInstruction or
                    IrExceptionalExitInstruction or IrWriteInstruction { Region: IrWriteRegion.Local }))
            {
                return Incomplete("unsupported exceptional control flow");
            }
        }
        if (_variables.Length > 4096 || _variables.Distinct().Count() != _variables.Length)
        {
            return Incomplete("variable budget/ownership");
        }

        try
        {
            foreach (var variable in _variables)
            {
                _token.ThrowIfCancellationRequested();
                var info = _factory.GetTypeInfo(_factory.GetVariableInfo(variable).Type);
                if (info.Kind == IrTypeKind.Integer && !CoreIrScalarIntervalTransfer.TryTypeRange(info, out _))
                {
                    return Incomplete("unsupported integer type");
                }
            }
            if (initial.Keys.Any(v => !_variables.Contains(v)))
            {
                return Incomplete("initial variable ownership");
            }
            foreach (var pair in initial)
            {
                var variable = pair.Key;
                var value = pair.Value;
                _token.ThrowIfCancellationRequested();
                var kind = _factory.GetTypeInfo(_factory.GetVariableInfo(variable).Type).Kind;
                if (kind is not (IrTypeKind.Integer or IrTypeKind.Boolean) || value.IsBottom ||
                    !IntervalDomain.Instance.LessThanOrEqual(value, _domain.Range(variable)))
                {
                    return Incomplete("invalid initial scalar facts");
                }
            }
        }
        catch (ArgumentException) { return Incomplete("variable ownership"); }
        if (!markers.IsDefaultOrEmpty)
        {
            if (markers.Length > 4096)
            {
                return Incomplete("marker budget");
            }
            var owned = _program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssignInstruction>().ToDictionary(assign => assign.Id);
            var selected = new Dictionary<IrInstructionId, IrAssignInstruction>();
            foreach (var marker in markers)
            {
                _token.ThrowIfCancellationRequested();
                if (!owned.TryGetValue(marker.Id, out var original) || !ReferenceEquals(original, marker) ||
                    _factory.GetTypeInfo(marker.Value.Type).Kind != IrTypeKind.Boolean || selected.ContainsKey(marker.Id))
                {
                    return Incomplete("marker ownership");
                }
                selected.Add(marker.Id, marker);
            }
            _markerSites = selected;
        }
        var ids = _program.Blocks.Select((b, i) => (b.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var blocks = new List<DataflowBlock<CoreIrAdvisoryState>>();
        var edges = new List<DataflowEdge>();
        foreach (var block in _program.Blocks)
        {
            blocks.Add(new(ids[block.Id], state => { _token.ThrowIfCancellationRequested(); return Transfer(block, state); }));
        }

        foreach (var block in _program.Blocks)
        {
            _token.ThrowIfCancellationRequested();
            var from = ids[block.Id];
            var last = block.Instructions.Last();
            if (last is IrGotoInstruction go)
            {
                edges.Add(new(from, ids[go.Target]));
            }
            else if (last is IrBranchInstruction branch)
            {
                foreach (var (target, truth) in new[] { (branch.WhenTrue, true), (branch.WhenFalse, false) })
                {
                    _token.ThrowIfCancellationRequested();
                    if (blocks.Count - _program.Blocks.Length >= maximumSyntheticEdges)
                    {
                        return Incomplete("synthetic edge budget");
                    }

                    var edgeId = blocks.Count;
                    blocks.Add(new(edgeId, state => RefineEdge(state, branch.Condition, truth)));
                    edges.Add(new(from, edgeId));
                    edges.Add(new(edgeId, ids[target]));
                }
            }
            // An exception carries the state at its throw to its handler.
            else if (last is IrThrowInstruction thrown)
            {
                edges.Add(new(from, ids[thrown.Target]));
            }
            else if (reachable.Contains(block.Id) && last is not (IrReturnInstruction or IrExceptionalExitInstruction))
            {
                AddGap("unsupported terminator");
            }
        }
        if (!ForwardDataflowAnalysis.TryAnalyze(new DataflowGraph<CoreIrAdvisoryState>(blocks, edges, ids[_program.Entry]), _domain, new(true, initial), out var result, out var failure, new ForwardDataflowAnalysisOptions(2, maxIterations)))
        {
            return Incomplete(_termBudgetExceeded ? "aggregate term work budget" : failure == DataflowAnalysisFailure.NonmonotoneTransfer ? "nonmonotone transfer" : "iteration limit");
        }
        if (_markerSites != null)
        {
            _markerSnapshots = [];
            try
            {
                for (var ordinal = 0; ordinal < _program.Blocks.Length; ordinal++)
                {
                    _token.ThrowIfCancellationRequested();
                    _ = Transfer(_program.Blocks[ordinal], result!.InputStates[ordinal]);
                }
            }
            catch (DataflowConvergenceException) when (_termBudgetExceeded)
            {
                _markerSnapshots = null;
                return Incomplete("aggregate term work budget");
            }
        }
        return new(result!.InputStates, result.OutputStates, [.. _gaps.OrderBy(static gap => gap, StringComparer.Ordinal)], result.Iterations)
        {
            Markers = _markerSnapshots == null ? [] : [.. _markerSnapshots]
        };
    }
    private CoreIrAdvisoryResult Incomplete(string reason)
    {
        AddGap(reason);
        return new([], [], [.. _gaps.OrderBy(static gap => gap, StringComparer.Ordinal)], 0, Accepted: false);
    }
    private void AddGap(string reason)
    {
        _gaps.Add(reason);
        _activeTransferHasGap = true;
    }
    private CoreIrAdvisoryState RefineEdge(CoreIrAdvisoryState state, IrTerm condition, bool truth)
    {
        _activeTransferHasGap = state.HasGap;
        var result = Refine(state, condition, truth);
        return result with { HasGap = result.HasGap || _activeTransferHasGap };
    }
    private void SpendTerm()
    {
        _token.ThrowIfCancellationRequested();
        if (_termWorkRemaining-- <= 0)
        {
            _termBudgetExceeded = true;
            throw new DataflowConvergenceException("The advisory term work budget was exhausted.");
        }
    }
    private CoreIrAdvisoryState Forget(CoreIrAdvisoryState state, IrVarId target)
    {
        var kind = _factory.GetTypeInfo(_factory.GetVariableInfo(target).Type).Kind;
        state = ForgetOrigins(state, target);
        return kind is IrTypeKind.String or IrTypeKind.Sequence or IrTypeKind.Reference ? _domain.SetReference(state, target, NullnessValue.MaybeNull, SequenceCardinalityDomain.Instance.Top) : _domain.Set(state, target, _domain.Range(target));
    }
    private static bool ReadsVariable(IrTerm term, IrVarId variable)
    {
        var pending = new Stack<IrTerm>();
        pending.Push(term);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (current is IrVariableTerm read && read.Variable == variable)
            { return true; }
            IrTraversal.PushChildren(current, pending);
        }
        return false;
    }
    private static CoreIrAdvisoryState ForgetOrigins(CoreIrAdvisoryState state, IrVarId target)
    {
        var origins = state.Origins.Remove(target);
        foreach (var pair in state.Origins)
        {
            if (ReadsVariable(pair.Value, target))
            { origins = origins.Remove(pair.Key); }
        }
        return state with { Origins = origins };
    }
    private static CoreIrAdvisoryState CaptureOrigin(CoreIrAdvisoryState state, IrVarId target, IrTerm value)
    {
        state = ForgetOrigins(state, target);
        if (state.Reachable && !ReadsVariable(value, target) && value is IrVariableTerm or IrIntegerTerm or IrBooleanTerm or IrLengthTerm or
            IrBinaryTerm { Operator: IrBinaryOperator.Equal or IrBinaryOperator.NotEqual })
        { state = state with { Origins = state.Origins.SetItem(target, value) }; }
        return state;
    }
    private CoreIrAdvisoryState Transfer(IrBasicBlock block, CoreIrAdvisoryState state)
    {
        _activeTransferHasGap = state.HasGap;
        if (!state.Reachable)
        {
            return state;
        }

        foreach (var instruction in block.Instructions)
        {
            _token.ThrowIfCancellationRequested();
            if (instruction is IrAssignInstruction assign)
            {
                if (!_variables.Contains(assign.Target))
                { AddGap("target ownership"); state = _domain.Top; continue; }
                var kind = _factory.GetTypeInfo(assign.Value.Type).Kind;
                if (kind is IrTypeKind.String or IrTypeKind.Sequence or IrTypeKind.Reference)
                { var value = Reference(assign.Value, state); state = _domain.SetReference(state, assign.Target, value.Nullness, value.Cardinality); }
                else
                {
                    state = _domain.Set(state, assign.Target, Eval(assign.Value, state));
                }
                state = CaptureOrigin(state, assign.Target, assign.Value);
                if (_markerSnapshots != null && _markerSites!.ContainsKey(assign.Id) && state.Reachable)
                {
                    _markerSnapshots.Add(new(assign, _domain.Get(state, assign.Target), state.HasGap || _activeTransferHasGap));
                }
            }
            else if (instruction is IrAllocationInstruction allocation)
            {
                if (allocation.Target is { } target)
                {
                    state = ForgetOrigins(state, target);
                    if (allocation.Length is { } length)
                    {
                        var value = Eval(length, state);
                        if (value.LowerBound < 0 || value.LowerBound == null)
                        { AddGap("negative allocation may fault"); state = Forget(state, target); }
                        else
                        {
                            state = _domain.SetReference(state, target, NullnessValue.NonNull, SequenceCardinalityDomain.Instance.Create(SequenceCardinalityKind.Top, value));
                        }
                    }
                    else
                    {
                        state = _domain.SetReference(state, target, NullnessValue.NonNull, SequenceCardinalityDomain.Instance.Top);
                    }
                }
            }
            else if (instruction is IrHavocInstruction havoc)
            {
                foreach (var variable in havoc.Variables)
                {
                    state = Forget(state, variable);
                }

                if (havoc.Origin == IrHavocOrigin.Approximation)
                {
                    AddGap("approximation havoc");
                }

                if (havoc.HavocKind != IrHavocKind.Variables)
                {
                    AddGap("memory havoc");
                }
            }
            else if (instruction is IrAssumeInstruction assume)
            {
                state = Refine(state, assume.Condition, true);
            }
            else if (instruction is IrCallInstruction call)
            {
                AddGap("unsupported call");
                if (call.Target is { } target)
                {
                    state = Forget(state, target);
                }
            }
            else if (instruction is IrLoadInstruction load)
            { AddGap("unsupported load"); state = Forget(state, load.Target); }
            // Effect sites and exits change no scalar.
            else if (instruction is not (IrBranchInstruction or IrGotoInstruction or IrReturnInstruction or IrWriteInstruction or
                IrLockInstruction or IrThrowInstruction or IrExceptionalExitInstruction))
            { AddGap("unsupported instruction"); state = _domain.Top; }
            state = state with { HasGap = state.HasGap || _activeTransferHasGap };
            if (!state.Reachable)
            {
                break;
            }
        }
        return state;
    }
    private (NullnessValue Nullness, SequenceCardinalityValue Cardinality) Reference(IrTerm term, CoreIrAdvisoryState state, int depth = 0)
    {
        SpendTerm();
        if (depth > 128)
        { AddGap("term depth"); return (NullnessValue.MaybeNull, SequenceCardinalityDomain.Instance.Top); }
        if (term is IrNullTerm)
        {
            return (NullnessValue.Null, SequenceCardinalityDomain.Instance.Empty);
        }

        if (term is IrStringTerm text)
        {
            return (NullnessValue.NonNull, SequenceCardinalityDomain.Instance.KnownLength(_factory.GetString(text.Value).Length));
        }

        if (term is IrEmptyArrayTerm)
        {
            return (NullnessValue.NonNull, SequenceCardinalityDomain.Instance.Empty);
        }

        if (term is IrVariableTerm variable)
        { if (!_variables.Contains(variable.Variable)) { AddGap("read ownership"); } return (CoreIrAdvisoryDomain.GetNull(state, variable.Variable), CoreIrAdvisoryDomain.GetCard(state, variable.Variable)); }
        if (term is IrConditionalTerm conditional)
        {
            var condition = Eval(conditional.Condition, state, depth + 1);
            if (condition.IsSingleton)
            {
                return Reference(condition.SingletonValue != 0 ? conditional.WhenTrue : conditional.WhenFalse, state, depth + 1);
            }

            var a = Reference(conditional.WhenTrue, state, depth + 1);
            var b = Reference(conditional.WhenFalse, state, depth + 1);
            return (NullnessDomain.Instance.Join(a.Nullness, b.Nullness), SequenceCardinalityDomain.Instance.Join(a.Cardinality, b.Cardinality));
        }
        if (term is IrBinaryTerm { Operator: IrBinaryOperator.StringConcat } concat)
        {
            var a = Reference(concat.Left, state, depth + 1);
            var b = Reference(concat.Right, state, depth + 1);
            var lo = (System.Numerics.BigInteger)(a.Cardinality.Length.LowerBound ?? 0) + (b.Cardinality.Length.LowerBound ?? 0);
            var hi = a.Cardinality.Length.UpperBound.HasValue && b.Cardinality.Length.UpperBound.HasValue ? (System.Numerics.BigInteger?)a.Cardinality.Length.UpperBound.Value + b.Cardinality.Length.UpperBound.Value : null;
            return (NullnessValue.NonNull, SequenceCardinalityDomain.Instance.Create(SequenceCardinalityKind.Top, IntervalValue.Range(lo <= int.MaxValue ? (long)lo : 0, hi <= int.MaxValue ? (long?)hi : null)));
        }
        AddGap("unsupported reference term");
        return (NullnessValue.MaybeNull, SequenceCardinalityDomain.Instance.Top);
    }
    private IntervalValue Eval(IrTerm term, CoreIrAdvisoryState state, int depth = 0)
    {
        SpendTerm();
        if (depth > 128)
        { AddGap("term depth"); return _domain.RangeType(term.Type); }
        var info = _factory.GetTypeInfo(term.Type);
        if (term is IrLengthTerm length)
        {
            // A cardinality may be unbounded above, but a length is a value
            // of its own integer type; widening clamps to that type's range.
            return CoreIrAdvisoryDomain.Restrict(Reference(length.Value, state, depth + 1).Cardinality.Length, _domain.RangeType(term.Type));
        }

        if (term is IrBinaryTerm { Operator: IrBinaryOperator.Equal or IrBinaryOperator.NotEqual, Right: IrNullTerm } nullComparison)
        {
            var value = Reference(nullComparison.Left, state, depth + 1).Nullness;
            if (value is NullnessValue.Null or NullnessValue.NonNull)
            {
                return IntervalValue.Constant((value == NullnessValue.Null) == (nullComparison.Operator == IrBinaryOperator.Equal) ? 1 : 0);
            }

            return IntervalValue.Range(0, 1);
        }
        // Content is not tracked; only nullness decides string equality.
        if (term is IrBinaryTerm { Operator: IrBinaryOperator.StringEquals } stringEquality)
        {
            var left = Reference(stringEquality.Left, state, depth + 1).Nullness;
            var right = Reference(stringEquality.Right, state, depth + 1).Nullness;
            return left == NullnessValue.Null && right == NullnessValue.Null ? IntervalValue.Constant(1)
                : left == NullnessValue.Null && right == NullnessValue.NonNull ||
                    left == NullnessValue.NonNull && right == NullnessValue.Null ? IntervalValue.Constant(0)
                : IntervalValue.Range(0, 1);
        }
        if (term is IrIntegerTerm integer && CoreIrScalarIntervalTransfer.TryTypeRange(info, out _))
        {
            return IntervalValue.Constant(integer.Value);
        }

        if (term is IrBooleanTerm boolean)
        {
            return IntervalValue.Constant(boolean.Value ? 1 : 0);
        }

        if (term is IrVariableTerm variable)
        { if (!_variables.Contains(variable.Variable)) { AddGap("read ownership"); } return _domain.Get(state, variable.Variable); }
        if (term is IrUnaryTerm unary)
        {
            var value = Eval(unary.Operand, state, depth + 1);
            if (unary.Operator == IrUnaryOperator.Not)
            {
                return value.IsSingleton ? IntervalValue.Constant(value.SingletonValue == 0 ? 1 : 0) : IntervalValue.Range(0, 1);
            }

            if (CoreIrScalarIntervalTransfer.TryTypeRange(info, out _))
            {
                return CoreIrScalarIntervalTransfer.Negate(info, value);
            }
        }
        if (term is IrCastTerm cast && CoreIrScalarIntervalTransfer.TryTypeRange(info, out _) && CoreIrScalarIntervalTransfer.TryTypeRange(_factory.GetTypeInfo(cast.Operand.Type), out _))
        {
            return CoreIrScalarIntervalTransfer.Cast(_factory.GetTypeInfo(cast.Operand.Type), info, Eval(cast.Operand, state, depth + 1));
        }

        if (term is IrConditionalTerm conditional)
        {
            var condition = Eval(conditional.Condition, state, depth + 1);
            if (condition.IsSingleton)
            {
                return Eval(condition.SingletonValue != 0 ? conditional.WhenTrue : conditional.WhenFalse, state, depth + 1);
            }

            return IntervalDomain.Instance.Join(Eval(conditional.WhenTrue, state, depth + 1), Eval(conditional.WhenFalse, state, depth + 1));
        }
        if (term is IrBinaryTerm binary)
        {
            var left = Eval(binary.Left, state, depth + 1);
            if (binary.Operator == IrBinaryOperator.AndAlso && left.IsSingleton && left.SingletonValue == 0)
            {
                return IntervalValue.Constant(0);
            }

            if (binary.Operator == IrBinaryOperator.OrElse && left.IsSingleton && left.SingletonValue == 1)
            {
                return IntervalValue.Constant(1);
            }

            var right = Eval(binary.Right, state, depth + 1);
            if (binary.Operator is IrBinaryOperator.Add or IrBinaryOperator.Subtract or IrBinaryOperator.Multiply or IrBinaryOperator.Divide or IrBinaryOperator.Remainder && CoreIrScalarIntervalTransfer.TryTypeRange(info, out _))
            {
                return CoreIrScalarIntervalTransfer.Binary(binary.Operator, info, left, right);
            }

            if (binary.Operator is IrBinaryOperator.AndAlso or IrBinaryOperator.OrElse)
            {
                return left.IsSingleton && right.IsSingleton ? IntervalValue.Constant(binary.Operator == IrBinaryOperator.AndAlso ? (left.SingletonValue != 0 && right.SingletonValue != 0 ? 1 : 0) : (left.SingletonValue != 0 || right.SingletonValue != 0 ? 1 : 0)) : IntervalValue.Range(0, 1);
            }

            if (binary.Operator is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual or IrBinaryOperator.LessThan or IrBinaryOperator.LessThanOrEqual or IrBinaryOperator.GreaterThan or IrBinaryOperator.GreaterThanOrEqual)
            {
                if (left.IsSingleton && right.IsSingleton)
                {
                    var a = left.SingletonValue;
                    var b = right.SingletonValue;
                    var result = binary.Operator switch { IrBinaryOperator.Equal => a == b, IrBinaryOperator.NotEqual => a != b, IrBinaryOperator.LessThan => a < b, IrBinaryOperator.LessThanOrEqual => a <= b, IrBinaryOperator.GreaterThan => a > b, _ => a >= b };
                    return IntervalValue.Constant(result ? 1 : 0);
                }
                return IntervalValue.Range(0, 1);
            }
        }
        // Field contents are not tracked: a field read is any value of its type.
        if (IrFieldSites.IsFieldRead(_factory, term))
        { return _domain.RangeType(term.Type); }
        AddGap("unsupported term");
        return _domain.RangeType(term.Type);
    }
    private CoreIrAdvisoryState Refine(CoreIrAdvisoryState state, IrTerm condition, bool truth)
    {
        SpendTerm();
        if (!state.Reachable)
        {
            return state;
        }

        var evaluated = Eval(condition, state);
        if (evaluated.IsSingleton)
        {
            return (evaluated.SingletonValue != 0) == truth ? state : _domain.Bottom;
        }

        if (condition is IrBinaryTerm { Operator: IrBinaryOperator.Equal or IrBinaryOperator.NotEqual, Left: IrVariableTerm nullable, Right: IrNullTerm } nullComparison)
        {
            var isNull = truth == (nullComparison.Operator == IrBinaryOperator.Equal);
            var value = isNull ? NullnessDomain.Instance.AssumeNull(CoreIrAdvisoryDomain.GetNull(state, nullable.Variable)) : NullnessDomain.Instance.AssumeNonNull(CoreIrAdvisoryDomain.GetNull(state, nullable.Variable));
            return _domain.SetReference(state, nullable.Variable, value, CoreIrAdvisoryDomain.GetCard(state, nullable.Variable));
        }
        if (condition is IrBinaryTerm { Operator: IrBinaryOperator.GreaterThan, Left: IrLengthTerm { Value: IrVariableTerm sequence }, Right: IrIntegerTerm { Value: 0 } })
        {
            var card = SequenceCardinalityDomain.Instance.Create(truth ? SequenceCardinalityKind.NonEmpty : SequenceCardinalityKind.Empty, CoreIrAdvisoryDomain.GetCard(state, sequence.Variable).Length);
            return _domain.SetReference(state, sequence.Variable, CoreIrAdvisoryDomain.GetNull(state, sequence.Variable), card);
        }
        if (condition is IrBinaryTerm { Operator: IrBinaryOperator.Equal or IrBinaryOperator.NotEqual } zeroLengthComparison &&
            ResolveCapturedScalar(state, zeroLengthComparison.Left) is IrLengthTerm { Value: IrVariableTerm zeroLengthSequence } &&
            ResolveCapturedScalar(state, zeroLengthComparison.Right) is IrIntegerTerm { Value: 0 })
        {
            var empty = truth == (zeroLengthComparison.Operator == IrBinaryOperator.Equal);
            var aliasVariable = zeroLengthSequence.Variable;
            var seen = new HashSet<IrVarId>();
            while (seen.Add(aliasVariable))
            {
                var card = SequenceCardinalityDomain.Instance.Create(empty ? SequenceCardinalityKind.Empty : SequenceCardinalityKind.NonEmpty, CoreIrAdvisoryDomain.GetCard(state, aliasVariable).Length);
                state = _domain.SetReference(state, aliasVariable, CoreIrAdvisoryDomain.GetNull(state, aliasVariable), card);
                if (!state.Reachable || !state.Origins.TryGetValue(aliasVariable, out var alias) || alias is not IrVariableTerm source)
                { break; }
                aliasVariable = source.Variable;
            }
            return state;
        }
        if (condition is IrVariableTerm boolVariable && _factory.GetTypeInfo(condition.Type).Kind == IrTypeKind.Boolean)
        {
            state = _domain.Set(state, boolVariable.Variable, IntervalValue.Constant(truth ? 1 : 0));
            var origin = ResolveCapturedScalar(state, condition);
            return origin is IrVariableTerm ? state : Refine(state, origin, truth);
        }

        if (condition is IrUnaryTerm { Operator: IrUnaryOperator.Not } negation)
        {
            return Refine(state, negation.Operand, !truth);
        }

        if (condition is IrBinaryTerm { Left: IrVariableTerm variable, Right: IrIntegerTerm constant } comparison)
        {
            var op = comparison.Operator;
            var bound = constant.Value;
            var value = _domain.Get(state, variable.Variable);
            var intervals = IntervalDomain.Instance;
            if (!truth)
            {
                op = op switch { IrBinaryOperator.LessThan => IrBinaryOperator.GreaterThanOrEqual, IrBinaryOperator.LessThanOrEqual => IrBinaryOperator.GreaterThan, IrBinaryOperator.GreaterThan => IrBinaryOperator.LessThanOrEqual, IrBinaryOperator.GreaterThanOrEqual => IrBinaryOperator.LessThan, IrBinaryOperator.Equal => IrBinaryOperator.NotEqual, IrBinaryOperator.NotEqual => IrBinaryOperator.Equal, _ => op };
            }

            value = op switch { IrBinaryOperator.LessThan => bound == long.MinValue ? IntervalValue.Bottom : intervals.AssumeAtMost(value, bound - 1), IrBinaryOperator.LessThanOrEqual => intervals.AssumeAtMost(value, bound), IrBinaryOperator.GreaterThan => bound == long.MaxValue ? IntervalValue.Bottom : intervals.AssumeAtLeast(value, bound + 1), IrBinaryOperator.GreaterThanOrEqual => intervals.AssumeAtLeast(value, bound), IrBinaryOperator.Equal => intervals.AssumeAtMost(intervals.AssumeAtLeast(value, bound), bound), _ => value };
            return _domain.Set(state, variable.Variable, value);
        }
        return state;
    }
    private static IrTerm ResolveCapturedScalar(CoreIrAdvisoryState state, IrTerm term)
    {
        var seen = new HashSet<IrVarId>();
        while (term is IrVariableTerm variable && seen.Add(variable.Variable) &&
            state.Origins.TryGetValue(variable.Variable, out var origin))
        { term = origin; }
        return term;
    }
}
