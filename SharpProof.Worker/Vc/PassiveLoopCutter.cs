namespace SharpProof.Worker;

// Both encodings are derived here from one owned original. A cut graph is an
// overapproximation of finite returns; an unroll graph is only witness search.
internal sealed class PassiveLoopCutter
{
    internal const int SearchBackEdges = 4;
    private readonly PassiveCallableCandidate _candidate;
    private readonly CancellationToken _cancellation;
    private int _work = PassiveCallableVcBuilder.MaximumSteps * WorkerBudgets.DefaultMaximumExpressionDepth;
    private readonly Dictionary<IrBlockId, List<IrBlockId>> _predecessors = [];
    private readonly HashSet<(IrBlockId From, IrBlockId To)> _backEdges = [];
    private readonly Dictionary<IrBlockId, HashSet<IrBlockId>> _loops = [];
    private readonly HashSet<IrBlockId> _reachable = [];

    internal sealed record Encoding(IrProgram Program, ImmutableHashSet<IrInstructionId> Stops);
    private PassiveLoopCutter(PassiveCallableCandidate candidate, CancellationToken cancellation)
    { _candidate = candidate; _cancellation = cancellation; }

    internal static bool TryCreate(PassiveCallableCandidate candidate, out Encoding? proof, out Encoding? search,
        out WorkerClaimReason reason, CancellationToken cancellation)
    {
        var cutter = new PassiveLoopCutter(candidate, cancellation);
        proof = null;
        search = null;
        try
        {
            if (!cutter.FindLoops())
            { reason = WorkerClaimReason.UnsupportedBody; return false; }
            proof = cutter.Encode(unroll: false);
            search = cutter.Encode(unroll: true);
            reason = WorkerClaimReason.None;
            return true;
        }
        catch (ConstructionLimitException)
        { reason = WorkerClaimReason.ResourceLimit; return false; }
    }

    private bool FindLoops()
    {
        if (_candidate.Program.Blocks.Length > PassiveCallableVcBuilder.MaximumSteps ||
            _candidate.Parameters.Length > PassiveCallableVcBuilder.MaximumSteps)
        { throw new ConstructionLimitException(); }
        var active = new HashSet<IrBlockId>();
        var stack = new Stack<(IrBlockId Block, bool Exit)>();
        stack.Push((_candidate.Program.Entry, false));
        while (stack.Count != 0)
        {
            Spend();
            var (block, exit) = stack.Pop();
            if (exit)
            { active.Remove(block); continue; }
            if (!_reachable.Add(block))
            { continue; }
            active.Add(block);
            stack.Push((block, true));
            foreach (var target in Targets(_candidate.Program.GetBlock(block).Terminator))
            {
                Spend();
                if (!_predecessors.TryGetValue(target, out var predecessors))
                { _predecessors.Add(target, predecessors = []); }
                predecessors.Add(block);
                if (active.Contains(target))
                { _backEdges.Add((block, target)); }
                else
                { stack.Push((target, false)); }
            }
        }
        if (_backEdges.Count == 0)
        { return false; }
        foreach (var block in _reachable)
        {
            foreach (var instruction in _candidate.Program.GetBlock(block).Instructions)
            {
                Spend();
                if (instruction is not (IrAssignInstruction or IrHavocInstruction or IrAssumeInstruction or
                    IrBranchInstruction or IrGotoInstruction or IrThrowInstruction or IrExceptionalExitInstruction or IrReturnInstruction))
                { return false; }
            }
        }
        foreach (var (latch, header) in _backEdges.OrderBy(edge => edge.To.Value).ThenBy(edge => edge.From.Value))
        {
            // A natural header must dominate its latch. Bounded reachability
            // with that header removed detects an irreducible entry.
            var seen = new HashSet<IrBlockId>();
            var pending = new Stack<IrBlockId>();
            pending.Push(_candidate.Program.Entry);
            while (pending.Count != 0)
            {
                Spend();
                var block = pending.Pop();
                if (block == header || !seen.Add(block))
                { continue; }
                if (block == latch)
                { return false; }
                foreach (var target in Targets(_candidate.Program.GetBlock(block).Terminator))
                { Spend(); pending.Push(target); }
            }
            if (!_loops.TryGetValue(header, out var nodes))
            { _loops.Add(header, nodes = [header]); }
            pending.Push(latch);
            while (pending.Count != 0)
            {
                Spend();
                var block = pending.Pop();
                if (block == header || !nodes.Add(block))
                { continue; }
                foreach (var predecessor in _predecessors[block])
                { Spend(); pending.Push(predecessor); }
            }
        }
        var immutable = new HashSet<IrVarId>();
        foreach (var parameter in _candidate.Parameters)
        {
            Spend();
            immutable.Add(parameter.Entry);
            immutable.Add(parameter.Old);
        }
        foreach (var nodes in _loops.Values)
        {
            foreach (var block in nodes)
            {
                foreach (var instruction in _candidate.Program.GetBlock(block).Instructions)
                {
                    Spend();
                    // Cyclic exception contexts and point assumptions are a
                    // later admission milestone, not an implicit invariant.
                    if (instruction is IrThrowInstruction or IrAssumeInstruction ||
                        instruction is IrAssignInstruction assign && immutable.Contains(assign.Target))
                    { return false; }
                    if (instruction is IrHavocInstruction havoc)
                    {
                        foreach (var variable in havoc.Variables)
                        {
                            Spend();
                            if (immutable.Contains(variable))
                            { return false; }
                        }
                    }
                }
            }
        }
        return true;
    }

    private Encoding Encode(bool unroll)
    {
        var builder = new IrProgramBuilder(_candidate.Factory);
        var blocks = new Dictionary<(IrBlockId Block, int Layer), IrBlockId>();
        var pending = new Queue<(IrBlockId Block, int Layer)>();
        var stops = ImmutableHashSet.CreateBuilder<IrInstructionId>();
        var instructions = 0;
        IrBlockId Block(IrBlockId original, int layer)
        {
            Spend();
            var key = (original, layer);
            if (!blocks.TryGetValue(key, out var encoded))
            {
                if (blocks.Count >= PassiveCallableVcBuilder.MaximumSteps)
                { throw new ConstructionLimitException(); }
                encoded = builder.CreateBlock("loop:" + original.Value.ToString(CultureInfo.InvariantCulture) + ":" + layer.ToString(CultureInfo.InvariantCulture));
                blocks.Add(key, encoded);
                pending.Enqueue(key);
            }
            return encoded;
        }
        builder.SetEntry(Block(_candidate.Program.Entry, 0));
        while (pending.Count != 0)
        {
            Spend();
            var (original, layer) = pending.Dequeue();
            var encoded = blocks[(original, layer)];
            var source = _candidate.Program.GetBlock(original);
            if (!unroll && _loops.TryGetValue(original, out var nodes))
            {
                var writes = new HashSet<IrVarId>();
                foreach (var node in nodes)
                {
                    foreach (var instruction in _candidate.Program.GetBlock(node).Instructions)
                    {
                        Spend();
                        if (instruction is IrAssignInstruction assign)
                        { writes.Add(assign.Target); }
                        else if (instruction is IrHavocInstruction havoc)
                        { foreach (var variable in havoc.Variables) { Spend(); writes.Add(variable); } }
                    }
                }
                if (writes.Count != 0)
                {
                    Count();
                    builder.Havoc(encoded, source.Instructions[0].Operation, IrHavocKind.Variables, IrHavocOrigin.Approximation,
                        [.. writes.OrderBy(variable => variable.Value)]);
                }
            }
            foreach (var instruction in source.Instructions)
            {
                Count();
                switch (instruction)
                {
                    case IrAssignInstruction assign:
                        builder.Assign(encoded, assign.Operation, assign.Target, assign.Value);
                        break;
                    case IrHavocInstruction havoc:
                        builder.Havoc(encoded, havoc.Operation, havoc.HavocKind, havoc.Origin, [.. havoc.Variables]);
                        break;
                    case IrAssumeInstruction assume:
                        builder.Assume(encoded, assume.Operation, assume.Condition);
                        break;
                    case IrBranchInstruction branch:
                        builder.Branch(encoded, branch.Operation, branch.Condition, Target(branch.WhenTrue, branch.Operation), Target(branch.WhenFalse, branch.Operation));
                        break;
                    case IrGotoInstruction go:
                        builder.Goto(encoded, go.Operation, Target(go.Target, go.Operation));
                        break;
                    case IrThrowInstruction thrown:
                        builder.Throw(encoded, thrown.Operation, thrown.ExceptionKind, Target(thrown.Target, thrown.Operation));
                        break;
                    case IrExceptionalExitInstruction exceptional:
                        builder.ExceptionalExit(encoded, exceptional.Operation);
                        break;
                    case IrReturnInstruction returned:
                        builder.Return(encoded, returned.Operation, returned.Value);
                        break;
                    default:
                        throw new ConstructionLimitException();
                }
            }

            IrBlockId Target(IrBlockId destination, OperationId site)
            {
                Spend();
                if (!_backEdges.Contains((original, destination)))
                { return Block(destination, layer); }
                if (unroll && layer < SearchBackEdges)
                { return Block(destination, layer + 1); }
                if (blocks.Count + stops.Count >= PassiveCallableVcBuilder.MaximumSteps)
                { throw new ConstructionLimitException(); }
                var stop = builder.CreateBlock("loop:stop");
                Count();
                stops.Add(builder.Assume(stop, site, _candidate.Factory.Boolean(false)).Id);
                Count();
                IrTerm? filler = _candidate.Result is { } result ? _candidate.Factory.GetVariableInfo(result).Type == _candidate.Factory.BooleanType
                    ? _candidate.Factory.Boolean(false) : _candidate.Factory.Integer(_candidate.Factory.GetVariableInfo(result).Type, 0L) : null;
                builder.Return(stop, site, filler);
                return stop;
            }
        }
        var program = builder.Build();
        var order = IrBlockOrder.TryCreateAcyclicOrder(program, amount => { Spend(amount); return true; }, out var failure);
        if (failure != IrAcyclicOrderFailure.None || order.IsDefault)
        { throw new ConstructionLimitException(); }
        return new(program, stops.ToImmutable());

        void Count()
        {
            Spend();
            if (++instructions > PassiveCallableVcBuilder.MaximumSteps)
            { throw new ConstructionLimitException(); }
        }
    }

    private static IEnumerable<IrBlockId> Targets(IrInstruction terminator)
    {
        if (terminator is IrBranchInstruction branch)
        { yield return branch.WhenTrue; yield return branch.WhenFalse; }
        else if (terminator is IrGotoInstruction go)
        { yield return go.Target; }
        else if (terminator is IrThrowInstruction thrown)
        { yield return thrown.Target; }
    }
    private void Spend(int amount = 1)
    {
        _cancellation.ThrowIfCancellationRequested();
        if ((_work -= amount) < 0)
        { throw new ConstructionLimitException(); }
    }
    [SuppressMessage("Design", "CA1032", Justification = "Private bounded construction control flow.")]
    private sealed class ConstructionLimitException : Exception;
}
