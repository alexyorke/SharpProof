namespace SharpProof.Symbolic.Ir;

internal interface IControlFlowDomain<TState> {
    TState Transfer(TState state, IOperation operation);
    TState Refine(TState state, IOperation? condition, ControlFlowConditionKind kind, bool conditionalSuccessor);
    TState Merge(TState current, TState incoming);
    TState Widen(TState previous, TState current, BasicBlock block);
    TState CompleteBlock(TState state, BasicBlock block);
    bool Equivalent(TState left, TState right);
}

internal sealed record ControlFlowAnalysisResult<TState>(
    ImmutableDictionary<BasicBlock, TState> Entries,
    ImmutableDictionary<BasicBlock, TState> Exits,
    bool Truncated) {
}

internal static class BoundedControlFlowAnalysis {
    internal static ControlFlowAnalysisResult<TState> Run<TState>(
        ControlFlowGraph graph,
        TState initialState,
        IControlFlowDomain<TState> domain,
        CancellationToken cancellationToken,
        int maxTransfers = 4096) {
        if (graph == null) throw new ArgumentNullException(nameof(graph));
        if (domain == null) throw new ArgumentNullException(nameof(domain));
        if (maxTransfers <= 0) throw new ArgumentOutOfRangeException(nameof(maxTransfers));
        var entries = new Dictionary<BasicBlock, TState> { [graph.Blocks[0]] = initialState };
        var exits = new Dictionary<BasicBlock, TState>();
        var visits = new Dictionary<BasicBlock, int>();
        var queue = new Queue<BasicBlock>();
        var queued = new HashSet<BasicBlock>();
        queue.Enqueue(graph.Blocks[0]);
        queued.Add(graph.Blocks[0]);
        var transfers = 0;
        while (queue.Count != 0 && transfers < maxTransfers) {
            cancellationToken.ThrowIfCancellationRequested();
            var block = queue.Dequeue();
            queued.Remove(block);
            if (!block.IsReachable || !entries.TryGetValue(block, out var state)) continue;
            foreach (var operation in block.Operations) {
                cancellationToken.ThrowIfCancellationRequested();
                state = domain.Transfer(state, operation);
                transfers++;
                if (transfers >= maxTransfers) break;
            }
            if (transfers >= maxTransfers) break;
            if (block.BranchValue != null) {
                state = domain.Transfer(state, block.BranchValue);
                transfers++;
            }
            state = domain.CompleteBlock(state, block);
            exits[block] = state;
            Propagate(block.FallThroughSuccessor, conditionalSuccessor: false);
            Propagate(block.ConditionalSuccessor, conditionalSuccessor: true);

            void Propagate(ControlFlowBranch? branch, bool conditionalSuccessor) {
                if (branch?.Destination is not { IsReachable: true } destination) return;
                var incoming = domain.Refine(state, block.BranchValue, block.ConditionKind, conditionalSuccessor);
                if (entries.TryGetValue(destination, out var existing)) {
                    var merged = domain.Merge(existing, incoming);
                    visits.TryGetValue(destination, out var count);
                    visits[destination] = count + 1;
                    if (count != 0) merged = domain.Widen(existing, merged, destination);
                    if (domain.Equivalent(existing, merged)) return;
                    entries[destination] = merged;
                }
                else {
                    entries.Add(destination, incoming);
                    visits[destination] = 1;
                }
                if (queued.Add(destination)) queue.Enqueue(destination);
            }
        }
        return new ControlFlowAnalysisResult<TState>(
            entries.ToImmutableDictionary(),
            exits.ToImmutableDictionary(),
            queue.Count != 0 || transfers >= maxTransfers);
    }
}
