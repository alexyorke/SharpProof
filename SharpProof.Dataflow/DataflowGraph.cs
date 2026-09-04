namespace SharpProof.Dataflow;

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

        Blocks = [.. blocks.OrderBy(static block => block.Id)];
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

        var distinctEdges = new HashSet<DataflowEdge>();
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
            successors[edge.SourceId].Add(edge.TargetId);
            predecessors[edge.TargetId].Add(edge.SourceId);
        }
        _predecessors = Freeze(predecessors);
        _successors = Freeze(successors);
        _cyclicBlocks = FindCyclicBlocks(
            _successors,
            _predecessors);
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

    private static List<int>[] CreateAdjacency(int count)
    {
        var result = new List<int>[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = [];
        }

        return result;
    }

    private static ImmutableArray<ImmutableArray<int>> Freeze(List<int>[] adjacency)
    {
        var result = ImmutableArray.CreateBuilder<ImmutableArray<int>>(adjacency.Length);
        foreach (var neighbors in adjacency)
        {
            result.Add([.. neighbors]);
        }
        return result.MoveToImmutable();
    }

    private static ImmutableArray<bool> FindCyclicBlocks(
        ImmutableArray<ImmutableArray<int>> successors,
        ImmutableArray<ImmutableArray<int>> predecessors)
    {
        var visited = new bool[successors.Length];
        var finishOrder = new List<int>(successors.Length);
        var pending = new Stack<(int BlockId, int NextSuccessor)>();
        for (var start = 0; start < successors.Length; start++)
        {
            if (visited[start])
            {
                continue;
            }

            visited[start] = true;
            pending.Push((start, 0));
            while (pending.Count != 0)
            {
                var (current, nextSuccessor) = pending.Pop();
                if (nextSuccessor >= successors[current].Length)
                {
                    finishOrder.Add(current);
                    continue;
                }

                pending.Push((current, nextSuccessor + 1));
                var next = successors[current][nextSuccessor];
                if (visited[next])
                {
                    continue;
                }

                visited[next] = true;
                pending.Push((next, 0));
            }
        }

        Array.Clear(visited, 0, visited.Length);
        var result = new bool[successors.Length];
        var component = new List<int>();
        var componentPending = new Stack<int>();
        for (var index = finishOrder.Count - 1; index >= 0; index--)
        {
            var start = finishOrder[index];
            if (visited[start])
            {
                continue;
            }

            component.Clear();
            visited[start] = true;
            componentPending.Push(start);
            while (componentPending.Count != 0)
            {
                var current = componentPending.Pop();
                component.Add(current);
                foreach (var predecessor in predecessors[current])
                {
                    if (visited[predecessor])
                    {
                        continue;
                    }

                    visited[predecessor] = true;
                    componentPending.Push(predecessor);
                }
            }

            var cyclic = component.Count > 1 ||
                successors[component[0]].Contains(component[0]);
            foreach (var blockId in component)
            {
                result[blockId] = cyclic;
            }
        }

        return [.. result];
    }
}
