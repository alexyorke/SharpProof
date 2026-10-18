namespace SharpProof.Dataflow;

/// <summary>
/// A control-flow block and its abstract transfer function.
/// </summary>
/// <remarks>
/// When the graph is analyzed with a
/// <see cref="CanonicalAbstractDomain{T}"/>, a transfer function that returns
/// a canonical representative may use the solver's direct strict-growth path;
/// noncanonical results retain the normalizing
/// <see cref="IAbstractDomain{T}.Join"/> fallback.
/// </remarks>
public sealed class DataflowBlock<T>(int id, Func<T, T> transfer)
{
    public int Id { get; } = ArgumentNullGuard.RequireNonnegative(id, nameof(id));
    public Func<T, T> Transfer { get; } =
        ArgumentNullGuard.NotNull(transfer, nameof(transfer));
}

public readonly record struct DataflowEdge(int SourceId, int TargetId)
{
    public int SourceId { get; } =
        ArgumentNullGuard.RequireNonnegative(SourceId, nameof(SourceId));
    public int TargetId { get; } =
        ArgumentNullGuard.RequireNonnegative(TargetId, nameof(TargetId));
}

/// <summary>
/// Small, language-neutral control-flow graph with contiguous block identifiers.
/// </summary>
public sealed class DataflowGraph<T>
{
    private readonly ImmutableArray<ImmutableArray<int>> _predecessors;
    private readonly ImmutableArray<ImmutableArray<int>> _successors;
    private readonly ImmutableArray<bool> _cyclicBlocks;

    public DataflowGraph(
        IEnumerable<DataflowBlock<T>> blocks,
        IEnumerable<DataflowEdge> edges,
        int entryBlockId = 0)
    {
        ArgumentNullGuard.NotNull(blocks, nameof(blocks));
        ArgumentNullGuard.NotNull(edges, nameof(edges));

        Blocks = [.. blocks.OrderBy(static block => ArgumentNullGuard.NotNull(block, nameof(blocks)).Id)];
        if (Blocks.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A dataflow graph must contain at least one block.", nameof(blocks));
        }

        for (var index = 0; index < Blocks.Length; index++)
        {
            if (Blocks[index].Id != index)
            {
                throw new ArgumentException(
                    "Block identifiers must be unique and contiguous from zero.",
                    nameof(blocks));
            }
        }

        _ = ArgumentNullGuard.RequireIndex(
            entryBlockId,
            Blocks.Length,
            nameof(entryBlockId));

        var distinctEdges = ImmutableHashSet.CreateBuilder<DataflowEdge>();
        foreach (var edge in edges)
        {
            if (edge.SourceId >= Blocks.Length || edge.TargetId >= Blocks.Length)
            {
                throw new ArgumentException("An edge references a block outside the graph.", nameof(edges));
            }

            distinctEdges.Add(edge);
        }
        Edges = [.. distinctEdges.OrderBy(static edge => edge.SourceId).ThenBy(static edge => edge.TargetId)];
        EntryBlockId = entryBlockId;

        var predecessors = CreateAdjacency(Blocks.Length);
        var successors = CreateAdjacency(Blocks.Length);
        foreach (var edge in Edges)
        {
            successors = successors.SetItem(edge.SourceId, successors[edge.SourceId].Add(edge.TargetId));
            predecessors = predecessors.SetItem(edge.TargetId, predecessors[edge.TargetId].Add(edge.SourceId));
        }
        _predecessors = Freeze(predecessors);
        _successors = Freeze(successors);
        _cyclicBlocks = FindCyclicBlocks(_successors);
    }

    public ImmutableArray<DataflowBlock<T>> Blocks
    {
        get;
    }
    public ImmutableArray<DataflowEdge> Edges
    {
        get;
    }
    public int EntryBlockId
    {
        get;
    }

    public DataflowBlock<T> GetBlock(int blockId)
    {
        ValidateBlockId(blockId);
        return Blocks[blockId];
    }

    public ImmutableArray<int> GetPredecessors(int blockId)
    {
        return GetNeighbors(blockId, _predecessors);
    }

    public ImmutableArray<int> GetSuccessors(int blockId)
    {
        return GetNeighbors(blockId, _successors);
    }

    private ImmutableArray<int> GetNeighbors(
        int blockId, ImmutableArray<ImmutableArray<int>> adjacency)
    {

        RepositoryParameterGuard.ThrowIfInvalid(adjacency.IsDefault, nameof(adjacency));
        ValidateBlockId(blockId);
        return adjacency[blockId];
    }

    public bool IsCyclicBlock(int blockId)
    {
        ValidateBlockId(blockId);
        return _cyclicBlocks[blockId];
    }

    private void ValidateBlockId(int blockId)
    {
        _ = ArgumentNullGuard.RequireIndex(
            blockId,
            Blocks.Length,
            nameof(blockId));
    }

    private static ImmutableList<ImmutableList<int>> CreateAdjacency(int count)
    {
        RepositoryParameterGuard.RequireInitialized(count < 0, nameof(count), "Value must be initialized.");
        return Enumerable.Repeat(ImmutableList<int>.Empty, count).ToImmutableList();
    }

    private static ImmutableArray<ImmutableArray<int>> Freeze(
        ImmutableList<ImmutableList<int>> adjacency)
    {
        adjacency = RepositoryParameterGuard.NotNull(adjacency, nameof(adjacency));
        return adjacency.Select(static neighbors => neighbors is null
            ? throw new ArgumentNullException(nameof(neighbors))
            : neighbors.ToImmutableArray()).ToImmutableArray();
    }

    private static ImmutableArray<bool> FindCyclicBlocks(
        ImmutableArray<ImmutableArray<int>> successors)
    {

        RepositoryParameterGuard.RequireInitialized(successors.IsDefault, nameof(successors), "Array must be initialized.");
        var discovery = Enumerable.Repeat(0, successors.Length).ToImmutableList();
        var lowLink = Enumerable.Repeat(0, successors.Length).ToImmutableList();
        var onStack = Enumerable.Repeat(false, successors.Length).ToImmutableList();
        for (var blockId = 0; blockId < discovery.Count; blockId++)
        {
            discovery = discovery.SetItem(blockId, -1);
        }

        var active = ImmutableStack<int>.Empty;
        var pending = ImmutableStack<(int BlockId, int NextSuccessor)>.Empty;
        var result = Enumerable.Repeat(false, successors.Length).ToImmutableList();
        var nextDiscovery = 0;
        for (var start = 0; start < successors.Length; start++)
        {
            if (discovery[start] >= 0)
            {
                continue;
            }

            discovery = discovery.SetItem(start, nextDiscovery);
            lowLink = lowLink.SetItem(start, nextDiscovery++);
            active = active.Push(start);
            onStack = onStack.SetItem(start, true);
            pending = pending.Push((start, 0));
            while (!pending.IsEmpty)
            {
                var (current, nextSuccessor) = pending.Peek();
                pending = pending.Pop();
                if (nextSuccessor < successors[current].Length)
                {
                    pending = pending.Push((current, nextSuccessor + 1));
                    var next = successors[current][nextSuccessor];
                    if (discovery[next] < 0)
                    {
                        discovery = discovery.SetItem(next, nextDiscovery);
                        lowLink = lowLink.SetItem(next, nextDiscovery++);
                        active = active.Push(next);
                        onStack = onStack.SetItem(next, true);
                        pending = pending.Push((next, 0));
                    }
                    else if (onStack[next] && discovery[next] < lowLink[current])
                    {
                        lowLink = lowLink.SetItem(current, discovery[next]);
                    }

                    continue;
                }

                if (lowLink[current] == discovery[current])
                {
                    var cyclic = false;
                    int member;
                    do
                    {
                        member = active.Peek();
                        active = active.Pop();
                        onStack = onStack.SetItem(member, false);
                        cyclic |= member != current;
                        if (member != current)
                        {
                            result = result.SetItem(member, true);
                        }
                    }
                    while (member != current);

                    result = result.SetItem(current, cyclic ||
                        successors[current].Contains(current));
                }

                if (!pending.IsEmpty)
                {
                    var parent = pending.Peek().BlockId;
                    if (lowLink[current] < lowLink[parent])
                    {
                        lowLink = lowLink.SetItem(parent, lowLink[current]);
                    }
                }
            }
        }

        return [.. result];
    }
}
