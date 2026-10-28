namespace SharpProof.Worker;

// Both encodings are derived here from one owned original. A cut graph is an
// overapproximation of finite returns; an unroll graph is only witness search.
internal sealed partial class PassiveLoopCutter
{
    internal const int SearchBackEdges = 4;
    private readonly PassiveCallableCandidate _candidate;
    private readonly CancellationToken _cancellation;
    private int _work = PassiveCallableVcBuilder.MaximumSteps * WorkerBudgets.DefaultMaximumExpressionDepth;
    private readonly ImmutableDictionary<IrBlockId, ImmutableArray<IrBlockId>.Builder>.Builder _predecessors = ImmutableDictionary.CreateBuilder<IrBlockId, ImmutableArray<IrBlockId>.Builder>();
    private readonly ImmutableHashSet<(IrBlockId From, IrBlockId To)>.Builder _backEdges = ImmutableHashSet.CreateBuilder<(IrBlockId From, IrBlockId To)>();
    private readonly ImmutableDictionary<IrBlockId, ImmutableHashSet<IrBlockId>.Builder>.Builder _loops = ImmutableDictionary.CreateBuilder<IrBlockId, ImmutableHashSet<IrBlockId>.Builder>();
    private readonly ImmutableDictionary<IrBlockId, ImmutableArray<IrBlockId>.Builder>.Builder _loopOrders = ImmutableDictionary.CreateBuilder<IrBlockId, ImmutableArray<IrBlockId>.Builder>();
    private readonly ImmutableArray<IrBlockId>.Builder _loopHeaderOrder = ImmutableArray.CreateBuilder<IrBlockId>();
    private readonly ImmutableArray<IrBlockId>.Builder _reachableOrder = ImmutableArray.CreateBuilder<IrBlockId>();
    private readonly ImmutableHashSet<IrBlockId>.Builder _reachable = ImmutableHashSet.CreateBuilder<IrBlockId>();
    private readonly ImmutableArray<IrBlockId>.Builder _finished = ImmutableArray.CreateBuilder<IrBlockId>();
    private readonly ImmutableDictionary<IrBlockId, ImmutableArray<IrVarId>>.Builder _havoc = ImmutableDictionary.CreateBuilder<IrBlockId, ImmutableArray<IrVarId>>();
    // The field stores of each loop whose every heap store is one of them,
    // through a receiver over inputs, or an element store; any other heap
    // store forgets the heap.
    private readonly ImmutableDictionary<IrBlockId, ImmutableArray<Cell>?>.Builder _cells = ImmutableDictionary.CreateBuilder<IrBlockId, ImmutableArray<Cell>?>();
    private readonly ImmutableHashSet<IrBlockId>.Builder _storesElements = ImmutableHashSet.CreateBuilder<IrBlockId>();

    // A location a loop stores to: its receiver over inputs the loop does not
    // write, the field, and a store that writes it.
    private sealed record Cell(IrTerm Target, IrMemberId Field, IrWriteInstruction Store)
    {
        public IrMemberId Field { get; init; } = Field.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(Field)) : Field;
        public IrTerm Target { get; init; } = Target ?? throw new System.ArgumentNullException(nameof(Target));
        public IrWriteInstruction Store { get; init; } = Store ?? throw new System.ArgumentNullException(nameof(Store));
    }
    private readonly ImmutableDictionary<IrBlockId, ExceptionComponent>.Builder _exceptionComponents = ImmutableDictionary.CreateBuilder<IrBlockId, ExceptionComponent>();
    private ImmutableArray<Invariant> _invariants = [];

    internal sealed record Encoding(IrProgram Program, ImmutableHashSet<IrInstructionId> Stops)
    {
        internal ImmutableDictionary<IrInstructionId, IrInstructionId> CallMarkers { get; init; } =
            ImmutableDictionary<IrInstructionId, IrInstructionId>.Empty;
        // Each assignment that checks an assumed loop invariant on an edge into
        // its header, with the invariant's ordinal.
        internal ImmutableDictionary<IrInstructionId, int> Checkpoints { get; init; } =
            ImmutableDictionary<IrInstructionId, int>.Empty;
        // Proof routers can represent iterations whose throws replaced the
        // incoming pending exception. Witness search has no such summaries.
        internal ImmutableDictionary<IrInstructionId, ImmutableArray<IrThrowInstruction>> PendingThrows { get; init; } =
            ImmutableDictionary<IrInstructionId, ImmutableArray<IrThrowInstruction>>.Empty;
        public IrProgram Program { get; init; } = Program ?? throw new System.ArgumentNullException(nameof(Program));
        public ImmutableHashSet<IrInstructionId> Stops { get; init; } = Stops ?? throw new System.ArgumentNullException(nameof(Stops));
    }

    // A natural loop: its header, its blocks, the variables it writes and the
    // fields it stores to, read at the header.
    internal sealed record Loop(IrBlockId Header, ImmutableHashSet<IrBlockId> Blocks, ImmutableArray<IrVarId> Writes,
        ImmutableArray<IrTerm> Fields)
    {
        public IrBlockId Header { get; init; } = Header.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(Header)) : Header;
        public ImmutableHashSet<IrBlockId> Blocks { get; init; } = Blocks ?? throw new System.ArgumentNullException(nameof(Blocks));
        public ImmutableArray<IrVarId> Writes { get; init; } = Writes.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Writes)) : Writes;
        public ImmutableArray<IrTerm> Fields { get; init; } = Fields.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Fields)) : Fields;
    }

    // An invariant assumed at a loop header once its writes are cut.
    internal sealed record Invariant(IrBlockId Header, IrTerm Condition)
    {
        public IrBlockId Header { get; init; } = Header.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(Header)) : Header;
        public IrTerm Condition { get; init; } = Condition ?? throw new System.ArgumentNullException(nameof(Condition));
    }
    private PassiveLoopCutter(PassiveCallableCandidate candidate, CancellationToken cancellation)
    { ArgumentNullException.ThrowIfNull(candidate); _candidate = candidate; _cancellation = cancellation; }

    internal static bool TryCreate(PassiveCallableCandidate candidate, out Encoding? proof, out Encoding? search,
        out WorkerClaimReason reason, CancellationToken cancellation)
    {
        System.ArgumentNullException.ThrowIfNull(candidate);
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

    // The cut loops whose headers can carry invariants.
    internal static ImmutableArray<Loop> Loops(PassiveCallableCandidate candidate, CancellationToken cancellation)
    {
        System.ArgumentNullException.ThrowIfNull(candidate);
        var cutter = new PassiveLoopCutter(candidate, cancellation);
        try
        {
            return cutter.FindLoops()
                ? [.. cutter._loops.OrderBy(loop => loop.Key.Value).Select(loop =>
                    new Loop(loop.Key, loop.Value.ToImmutable(), cutter._havoc[loop.Key], cutter._cells[loop.Key] is { } cells
                        ? [.. cells.Select(cell => cell is null ? throw new System.ArgumentNullException(nameof(cell)) : ((IrTerm)candidate.Factory.PureOpaque(cell.Field, cell.Target)))]
                        : []))]
                : [];
        }
        catch (ConstructionLimitException)
        { return []; }
    }

    // The proof encoding with each invariant assumed after its header's cut
    // and checked on every edge into that header. Invariants hold only when
    // every checkpoint is proven.
    internal static Encoding? TryEncodeWithInvariants(PassiveCallableCandidate candidate, ImmutableArray<Invariant> invariants,
        CancellationToken cancellation)
    {
        System.ArgumentNullException.ThrowIfNull(candidate);
        if (invariants.IsDefault)
        { throw new ArgumentException("Invariants must be initialized.", nameof(invariants)); }
        var cutter = new PassiveLoopCutter(candidate, cancellation) { _invariants = invariants };
        try
        { return cutter.FindLoops() && invariants.All(invariant => invariant is null ? throw new System.ArgumentNullException(nameof(invariant)) : (cutter._loops.ContainsKey(invariant.Header))) ? cutter.Encode(unroll: false) : null; }
        catch (ConstructionLimitException)
        { return null; }
    }

    private bool FindLoops()
    {
        if (_candidate.Program.Blocks.Length > PassiveCallableVcBuilder.MaximumSteps ||
            _candidate.Parameters.Length > PassiveCallableVcBuilder.MaximumSteps)
        { throw new ConstructionLimitException(); }
        var active = ImmutableHashSet.CreateBuilder<IrBlockId>();
        var stack = ImmutableStack<(IrBlockId Block, bool Exit)>.Empty;
        stack = stack.Push((_candidate.Program.Entry, false));
        while (!stack.IsEmpty)
        {
            Spend();
            stack = stack.Pop(out var frame);
            var (block, exit) = frame;
            if (exit)
            { active.Remove(block); _finished.Add(block); continue; }
            if (!_reachable.Add(block))
            { continue; }
            _reachableOrder.Add(block);
            active.Add(block);
            stack = stack.Push((block, true));
            foreach (var target in Targets(_candidate.Program.GetBlock(block).Terminator))
            {
                Spend();
                if (!_predecessors.TryGetValue(target, out var predecessors))
                { _predecessors.AddUnique(target, predecessors = ImmutableArray.CreateBuilder<IrBlockId>()); }
                predecessors.Add(block);
                if (active.Contains(target))
                { _backEdges.Add((block, target)); }
                else
                { stack = stack.Push((target, false)); }
            }
        }
        if (_backEdges.Count == 0)
        { return false; }
        foreach (var block in _reachableOrder)
        {
            foreach (var instruction in _candidate.Program.GetBlock(block).Instructions)
            {
                Spend();
                if (instruction is not (IrAllocationInstruction or IrWriteInstruction or IrLockInstruction or IrAssignInstruction or IrHavocInstruction or IrAssumeInstruction or
                    IrBranchInstruction or IrGotoInstruction or IrThrowInstruction or IrExceptionalExitInstruction or IrReturnInstruction) &&
                    instruction is not IrCallInstruction { Target: null, Receiver: null })
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
            var seen = ImmutableHashSet.CreateBuilder<IrBlockId>();
            var pending = ImmutableStack<IrBlockId>.Empty;
            pending = pending.Push(_candidate.Program.Entry);
            while (!pending.IsEmpty)
            {
                Spend();
                pending = pending.Pop(out var block);
                if (block == header || !seen.Add(block))
                { continue; }
                if (block == latch)
                { return false; }
                foreach (var target in Targets(_candidate.Program.GetBlock(block).Terminator))
                { Spend(); pending = pending.Push(target); }
            }
            if (!_loops.TryGetValue(header, out var nodes))
            {
                nodes = ImmutableHashSet.CreateBuilder<IrBlockId>();
                nodes.Add(header);
                _loops.AddUnique(header, nodes);
                var order = ImmutableArray.CreateBuilder<IrBlockId>();
                order.Add(header);
                _loopOrders.AddUnique(header, order);
                _loopHeaderOrder.Add(header);
            }
            var nodeOrder = _loopOrders[header];
            pending = pending.Push(latch);
            while (!pending.IsEmpty)
            {
                Spend();
                pending = pending.Pop(out var block);
                if (block == header || !nodes.Add(block))
                { continue; }
                nodeOrder.Add(block);
                foreach (var predecessor in _predecessors[block])
                { Spend(); pending = pending.Push(predecessor); }
            }
        }
        var inputs = _candidate.Parameters.SelectMany(parameter => ((parameter.Entry.IsDefault || parameter.Current.IsDefault || parameter.Old.IsDefault) ? throw new System.ArgumentException("Value must be initialized.", nameof(parameter)) : (ImmutableArray.Create(parameter.Entry, parameter.Current, parameter.Old)))).ToImmutableHashSet();
        foreach (var header in _loopHeaderOrder)
        {
            var nodeOrder = _loopOrders[header];
            Spend();
            var written = WrittenVariables(nodeOrder);
            _havoc.AddUnique(header, written);
            var instructions = nodeOrder.SelectMany(node => node.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(node)) : (_candidate.Program.GetBlock(node).Instructions)).ToImmutableArray();
            Spend(instructions.Length);
            // A copy of one unwritten input can name a stable cell, but only
            // after its assignment dominates that store.
            var copyBuilder = ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
            foreach (var group in instructions.SelectMany(IrInstructionFacts.WrittenVariables).Distinct()
                .Select(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : ((Variable: variable, Writers: instructions.Where(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (IrInstructionFacts.WrittenVariables(instruction).Contains(variable))).ToImmutableArray()))))
            {
                Spend(group.Writers.Length);
                if (group.Writers.All(writer => writer is null ? throw new System.ArgumentNullException(nameof(writer)) : (writer is IrAssignInstruction { Value: IrVariableTerm source } &&
                        inputs.Contains(source.Variable) && !written.Contains(source.Variable))) &&
                    group.Writers.Select(writer => writer is null ? throw new System.ArgumentNullException(nameof(writer)) : (((IrVariableTerm)((IrAssignInstruction)writer).Value).Variable)).Distinct().Count() == 1)
                { copyBuilder.AddUnique(group.Variable, ((IrAssignInstruction)group.Writers[0]).Value); }
            }
            var copies = copyBuilder.ToImmutable();
            var copiesBeforeStore = ImmutableDictionary.CreateBuilder<IrInstructionId, ImmutableDictionary<IrVarId, IrTerm>>();
            foreach (var node in nodeOrder)
            {
                var available = ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
                foreach (var instruction in _candidate.Program.GetBlock(node).Instructions)
                {
                    Spend();
                    if (instruction is IrAssignInstruction assignment && copies.TryGetValue(assignment.Target, out var source))
                    { available[assignment.Target] = source; }
                    if (instruction is IrWriteInstruction store)
                    { copiesBeforeStore.AddUnique(store.Id, available.ToImmutable()); }
                }
            }
            // Calls and memory havocs can change every cell. A fixed cell's
            // address must also remain stable when a guard selects its owner.
            ImmutableArray<Cell>.Builder? cells = instructions.Any(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction is IrCallInstruction ||
                instruction is IrHavocInstruction { HavocKind: IrHavocKind.Memory or IrHavocKind.VariablesAndMemory })) ? null : ImmutableArray.CreateBuilder<Cell>();
            bool StableAddress(IrTerm target)
            {
                System.ArgumentNullException.ThrowIfNull(target);
                return !IrTraversal.Any(target, term =>
                {
                    System.ArgumentNullException.ThrowIfNull(term);
                    Spend();
                    return term is IrVariableTerm variable && (!inputs.Contains(variable.Variable) || written.Contains(variable.Variable)) ||
                        IrFieldSites.IsFieldRead(_candidate.Factory, term) || term is IrSequenceAccessTerm;
                });
            }
            foreach (var write in cells == null ? [] : instructions.OfType<IrWriteInstruction>()
                .Where(write => write is null ? throw new System.ArgumentNullException(nameof(write)) : (write.Region is IrWriteRegion.Element or IrWriteRegion.Field or IrWriteRegion.Parameter or IrWriteRegion.Unknown)))
            {
                if (write.Region == IrWriteRegion.Element)
                {
                    _storesElements.Add(header);
                    continue;
                }
                var target = write.IsFieldStore && write.Target is not null && write.Value is not null
                    ? IrSubstitution.Substitute(_candidate.Factory, write.Target, copiesBeforeStore[write.Id]) : null;
                if (target == null || write.Index != null || !StableAddress(target))
                { cells = null; break; }
                if (!cells!.Any(cell => cell is null ? throw new System.ArgumentNullException(nameof(cell)) : (cell.Target.Id == target.Id && cell.Field == write.Field!.Value)))
                { cells!.Add(new(target, write.Field!.Value, write)); }
            }
            _cells.AddUnique(header, cells == null ? null : cells.ToImmutable());
        }
        return true;
    }

    private Encoding Encode(bool unroll)
    {
        var builder = new IrProgramBuilder(_candidate.Factory);
        var blocks = ImmutableDictionary.CreateBuilder<(IrBlockId Block, int Layer), IrBlockId>();
        var pending = ImmutableQueue<(IrBlockId Block, int Layer)>.Empty;
        var stops = ImmutableHashSet.CreateBuilder<IrInstructionId>();
        var callMarkers = ImmutableDictionary.CreateBuilder<IrInstructionId, IrInstructionId>();
        var checkpoints = ImmutableDictionary.CreateBuilder<IrInstructionId, int>();
        var pendingThrows = ImmutableDictionary.CreateBuilder<IrInstructionId, ImmutableArray<IrThrowInstruction>>();
        var originalMarkers = _candidate.CallPreconditions.Select(clause => clause.Marker).ToImmutableHashSet();
        var instructions = 0;
        var allocatedBlocks = 0;
        IrBlockId CreateBlock(string name)
        {
            System.ArgumentNullException.ThrowIfNull(name);
            Spend();
            if (++allocatedBlocks > PassiveCallableVcBuilder.MaximumSteps)
            { throw new ConstructionLimitException(); }
            return builder.CreateBlock(name);
        }
        IrBlockId Block(IrBlockId original, int layer)
        {
            RepositoryParameterGuard.ThrowIfInvalid(original.IsDefault, nameof(original));
            Spend();
            var key = (original, layer);
            if (!blocks.TryGetValue(key, out var encoded))
            {
                encoded = CreateBlock("loop:" + original.Value.ToString(CultureInfo.InvariantCulture) + ":" + layer.ToString(CultureInfo.InvariantCulture));
                blocks.AddUnique(key, encoded);
                pending = pending.Enqueue(key);
            }
            return encoded;
        }
        var originalEntry = _candidate.Program.Entry;
        var entrySite = _candidate.Program.GetBlock(originalEntry).Instructions[0].Operation;
        builder.SetEntry(!unroll && _exceptionComponents.TryGetValue(originalEntry, out var entryComponent)
            ? Router(entryComponent, entrySite)
            : Checked(originalEntry, entrySite, Block(originalEntry, 0)));
        while (!pending.IsEmpty)
        {
            Spend();
            pending = pending.Dequeue(out var frame);
            var (original, layer) = frame;
            var encoded = blocks[(original, layer)];
            var source = _candidate.Program.GetBlock(original);
            if (!unroll && _havoc.TryGetValue(original, out var writes))
            {
                // A loop that stores only to fixed fields and to elements
                // forgets those; any other heap store forgets all heap contents.
                var cells = _cells[original];
                RepositoryParameterGuard.ThrowIfMissing(cells is { IsDefault: true }, nameof(cells));
                var memory = cells is null;
                var values = cells is { } stored
                    ? stored.Select(cell => cell is null ? throw new System.ArgumentNullException(nameof(cell)) : (_candidate.Factory.CreateVariable("loop:cell", cell.Store.Value!.Type))).ToImmutableArray()
                    : ImmutableArray<IrVarId>.Empty;
                if (writes.Length != 0 || memory || values.Length != 0)
                {
                    Spend(writes.Length + values.Length);
                    Count();
                    builder.Havoc(encoded, source.Instructions[0].Operation,
                        memory ? writes.Length == 0 ? IrHavocKind.Memory : IrHavocKind.VariablesAndMemory : IrHavocKind.Variables,
                        IrHavocOrigin.Approximation, [.. writes, .. values]);
                }
                for (var ordinal = 0; ordinal < values.Length; ordinal++)
                {
                    Count();
                    var cell = cells!.Value[ordinal];
                    builder.FieldStore(encoded, _candidate.Factory.CreateOperation(IrWriteSites.LoopCellPrefix +
                        original.Value.ToString(CultureInfo.InvariantCulture), _candidate.Factory.GetOperationInfo(cell.Store.Operation).SourceSpan),
                        cell.Store.Region == IrWriteRegion.Parameter ? IrWriteRegion.Parameter : IrWriteRegion.Field, cell.Target, cell.Field,
                        _candidate.Factory.Variable(values[ordinal]));
                }
                if (!memory && _storesElements.Contains(original))
                {
                    Count();
                    builder.Write(encoded, _candidate.Factory.CreateOperation(IrWriteSites.LoopCellPrefix +
                        original.Value.ToString(CultureInfo.InvariantCulture)), IrWriteRegion.Element);
                }
            }
            if (!unroll)
            {
                foreach (var invariant in _invariants.Where(invariant => invariant is null ? throw new System.ArgumentNullException(nameof(invariant)) : (invariant.Header == original)))
                {
                    Count();
                    builder.Assume(encoded, source.Instructions[0].Operation, invariant.Condition);
                }
            }
            foreach (var instruction in source.Instructions)
            {
                Count();
                switch (instruction)
                {
                    case IrAllocationInstruction allocation:
                        builder.Allocate(encoded, allocation.Operation, allocation.AllocatedType, allocation.Target, allocation.Length, allocation.InitialValues);
                        break;
                    case IrLockInstruction synchronization:
                        builder.Lock(encoded, synchronization.Operation, synchronization.Receiver);
                        break;
                    case IrCallInstruction call:
                        builder.CallSnapshot(encoded, call.Operation, call.Target, call.Member, call.Receiver, call.Arguments);
                        break;
                    case IrWriteInstruction { Target: { } stored, Field: null, Index: { } index, Value: { } value } write:
                        builder.ElementStore(encoded, write.Operation, stored, index, value, write.Length);
                        break;
                    case IrWriteInstruction { Target: { } receiver, Field: { } field, Value: { } value } write:
                        builder.FieldStore(encoded, write.Operation, write.Region, receiver, field, value, write.Index);
                        break;
                    case IrWriteInstruction write:
                        builder.Write(encoded, write.Operation, write.Region);
                        break;
                    case IrAssignInstruction assign:
                        var copied = builder.Assign(encoded, assign.Operation, assign.Target, assign.Value);
                        if (originalMarkers.Contains(assign.Id))
                        { callMarkers.Add(copied.Id, assign.Id); }
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
                }
            }

            IrBlockId Target(IrBlockId destination, OperationId site)
            {
                Spend();
                if (!unroll && _exceptionComponents.TryGetValue(destination, out var component) &&
                    (!_exceptionComponents.TryGetValue(original, out var sourceComponent) || !ReferenceEquals(component, sourceComponent)))
                { return Router(component, site); }
                if (!_backEdges.Contains((original, destination)))
                { return unroll ? Block(destination, layer) : Checked(destination, site, Block(destination, layer)); }
                if (unroll && layer < SearchBackEdges)
                { return Block(destination, layer + 1); }
                var stop = CreateBlock("loop:stop");
                Check(stop, destination, site);
                Count();
                stops.Add(builder.Assume(stop, site, _candidate.Factory.Boolean(false)).Id);
                Count();
                IrTerm? filler = _candidate.Result is { } result ? Filler(_candidate.Factory.GetVariableInfo(result).Type) : null;
                builder.Return(stop, site, filler);
                return stop;
            }
        }
        var program = builder.Build();
        // Preserve the bounded traversal and its Spend/cancellation accounting.
        _ = IrBlockOrder.TryCreateAcyclicOrder(program, amount => { Spend(amount); return true; }, out _);
        return new(program, stops.ToImmutable())
        { CallMarkers = callMarkers.ToImmutable(), Checkpoints = checkpoints.ToImmutable(), PendingThrows = pendingThrows.ToImmutable() };

        // An edge into a header first checks the header's invariants.
        IrBlockId Checked(IrBlockId header, OperationId site, IrBlockId target)
        {
            RepositoryParameterGuard.ThrowIfInvalid(target.IsDefault, nameof(target));
            if (unroll || !_invariants.Any(invariant => invariant is null ? throw new System.ArgumentNullException(nameof(invariant)) : (invariant.Header == header)))
            { return target; }
            var check = CreateBlock("loop:invariant");
            Check(check, header, site);
            Count();
            builder.Goto(check, site, target);
            return check;
        }

        void Check(IrBlockId block, IrBlockId header, OperationId site)
        {
            RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
            RepositoryParameterGuard.ThrowIfInvalid(header.IsDefault, nameof(header));
            RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site));
            if (unroll)
            { return; }
            for (var ordinal = 0; ordinal < _invariants.Length; ordinal++)
            {
                if (_invariants[ordinal].Header != header)
                { continue; }
                Count();
                var marker = _candidate.Factory.CreateVariable("loop:invariant", _candidate.Factory.BooleanType);
                checkpoints.Add(builder.Assign(block, site, marker, _invariants[ordinal].Condition).Id, ordinal);
            }
        }

        IrBlockId Router(ExceptionComponent component, OperationId site)
        {
            // Take the original finite trace after its LAST cut edge. Its suffix
            // uses no deleted edge; the router can select that edge's target and
            // supply its state. Only SCC writes can differ from the incoming
            // state. This is a normal-return/poststate abstraction, not evidence
            // that skipped operation/effect sites execute. Each original body
            // stays shared, and no pending exception is synthesized here.
            RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site));
            System.ArgumentNullException.ThrowIfNull(component);
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
            var routerHavoc = builder.Havoc(dispatch, site, component.ForgetsHeap ? IrHavocKind.VariablesAndMemory : IrHavocKind.Variables,
                IrHavocOrigin.Approximation, [.. component.Writes, choice]);
            pendingThrows.Add(routerHavoc.Id, component.Throws);
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

    // Any value of the result type; a stop block never returns.
    private IrTerm Filler(IrTypeId type)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        var factory = _candidate.Factory;
        return type == factory.BooleanType ? factory.Boolean(false)
            : factory.GetTypeInfo(type).Kind == IrTypeKind.Integer ? factory.Integer(type, 0L)
            : factory.Null(type);
    }

    private static IEnumerable<IrBlockId> Targets(IrInstruction terminator)
    {
        System.ArgumentNullException.ThrowIfNull(terminator);
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
