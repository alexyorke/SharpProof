namespace SharpProof.Worker;

// Both encodings are derived here from one owned original. A cut graph is an
// overapproximation of finite returns; an unroll graph is only witness search.
internal sealed partial class PassiveLoopCutter
{
    internal const int SearchBackEdges = 4;
    private readonly PassiveCallableCandidate _candidate;
    private readonly CancellationToken _cancellation;
    private int _work = PassiveCallableVcBuilder.MaximumSteps * WorkerBudgets.DefaultMaximumExpressionDepth;
    private readonly Dictionary<IrBlockId, List<IrBlockId>> _predecessors = [];
    private readonly HashSet<(IrBlockId From, IrBlockId To)> _backEdges = [];
    private readonly Dictionary<IrBlockId, HashSet<IrBlockId>> _loops = [];
    private readonly HashSet<IrBlockId> _reachable = [];
    private readonly List<IrBlockId> _finished = [];
    private readonly Dictionary<IrBlockId, ImmutableArray<IrVarId>> _havoc = [];
    private readonly Dictionary<IrBlockId, ExceptionComponent> _exceptionComponents = [];

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
            { active.Remove(block); _finished.Add(block); continue; }
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
                if (instruction is not (IrAllocationInstruction or IrWriteInstruction or IrLockInstruction or IrAssignInstruction or IrHavocInstruction or IrAssumeInstruction or
                    IrBranchInstruction or IrGotoInstruction or IrThrowInstruction or IrExceptionalExitInstruction or IrReturnInstruction))
                { return false; }
            }
        }
        if (!FindExceptionComponents())
        { return false; }
        foreach (var (latch, header) in _backEdges.OrderBy(edge => edge.To.Value).ThenBy(edge => edge.From.Value))
        {
            if (_exceptionComponents.ContainsKey(header))
            { continue; }
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
        foreach (var (header, nodes) in _loops)
        { Spend(); _havoc.Add(header, WrittenVariables(nodes)); }
        return true;
    }

    private Encoding Encode(bool unroll)
    {
        var builder = new IrProgramBuilder(_candidate.Factory);
        var blocks = new Dictionary<(IrBlockId Block, int Layer), IrBlockId>();
        var pending = new Queue<(IrBlockId Block, int Layer)>();
        var stops = ImmutableHashSet.CreateBuilder<IrInstructionId>();
        var instructions = 0;
        var allocatedBlocks = 0;
        IrBlockId CreateBlock(string name)
        {
            Spend();
            if (++allocatedBlocks > PassiveCallableVcBuilder.MaximumSteps)
            { throw new ConstructionLimitException(); }
            return builder.CreateBlock(name);
        }
        IrBlockId Block(IrBlockId original, int layer)
        {
            Spend();
            var key = (original, layer);
            if (!blocks.TryGetValue(key, out var encoded))
            {
                encoded = CreateBlock("loop:" + original.Value.ToString(CultureInfo.InvariantCulture) + ":" + layer.ToString(CultureInfo.InvariantCulture));
                blocks.Add(key, encoded);
                pending.Enqueue(key);
            }
            return encoded;
        }
        var originalEntry = _candidate.Program.Entry;
        builder.SetEntry(!unroll && _exceptionComponents.TryGetValue(originalEntry, out var entryComponent)
            ? Router(entryComponent, _candidate.Program.GetBlock(originalEntry).Instructions[0].Operation)
            : Block(originalEntry, 0));
        while (pending.Count != 0)
        {
            Spend();
            var (original, layer) = pending.Dequeue();
            var encoded = blocks[(original, layer)];
            var source = _candidate.Program.GetBlock(original);
            if (!unroll && _havoc.TryGetValue(original, out var writes) && writes.Length != 0)
            {
                Spend(writes.Length);
                Count();
                builder.Havoc(encoded, source.Instructions[0].Operation, IrHavocKind.Variables, IrHavocOrigin.Approximation, [.. writes]);
            }
            foreach (var instruction in source.Instructions)
            {
                Count();
                switch (instruction)
                {
                    case IrAllocationInstruction allocation:
                        builder.Allocate(encoded, allocation.Operation, allocation.AllocatedType);
                        break;
                    case IrLockInstruction synchronization:
                        builder.Lock(encoded, synchronization.Operation, synchronization.Receiver);
                        break;
                    case IrWriteInstruction write:
                        builder.Write(encoded, write.Operation, write.Region);
                        break;
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
                if (!unroll && _exceptionComponents.TryGetValue(destination, out var component) &&
                    (!_exceptionComponents.TryGetValue(original, out var sourceComponent) || !ReferenceEquals(component, sourceComponent)))
                { return Router(component, site); }
                if (!_backEdges.Contains((original, destination)))
                { return Block(destination, layer); }
                if (unroll && layer < SearchBackEdges)
                { return Block(destination, layer + 1); }
                var stop = CreateBlock("loop:stop");
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

        IrBlockId Router(ExceptionComponent component, OperationId site)
        {
            // Take the original finite trace after its LAST cut edge. Its suffix
            // uses no deleted edge; the router can select that edge's target and
            // supply its state. Only SCC writes can differ from the incoming
            // state. This is a normal-return/poststate abstraction, not evidence
            // that skipped operation/effect sites execute. Each original body
            // stays shared, and no pending exception is synthesized here.
            var length = component.Blocks.Length;
            var missingOriginalBlocks = 0;
            foreach (var original in component.Blocks)
            {
                Spend();
                if (!blocks.ContainsKey((original, 0)))
                { missingOriginalBlocks++; }
            }
            // Reserve the complete dispatch, choice terms and union mapping
            // before allocating any router storage, blocks or instructions.
            Spend(component.Writes.Length + 6 * length + 1);
            if (allocatedBlocks + length + missingOriginalBlocks > PassiveCallableVcBuilder.MaximumSteps ||
                instructions + length + 1 > PassiveCallableVcBuilder.MaximumSteps)
            { throw new ConstructionLimitException(); }
            var dispatch = CreateBlock("loop:exception-entry");
            var choice = _candidate.Factory.CreateVariable("loop:choice", _candidate.Factory.IntegerType);
            Count();
            builder.Havoc(dispatch, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, [.. component.Writes, choice]);
            var first = dispatch;
            for (var ordinal = 0; ordinal < length - 1; ordinal++)
            {
                Spend();
                var next = CreateBlock("loop:exception-choice");
                Count();
                builder.Branch(dispatch, site, _candidate.Factory.Binary(IrBinaryOperator.Equal,
                    _candidate.Factory.Variable(choice), _candidate.Factory.Integer(ordinal)), Block(component.Blocks[ordinal], 0), next);
                dispatch = next;
            }
            Count();
            // All other choice values select the final block, so every choice
            // is valid without a range premise or a synthetic user assumption.
            builder.Goto(dispatch, site, Block(component.Blocks[^1], 0));
            return first;
        }

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
