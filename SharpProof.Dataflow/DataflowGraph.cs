namespace SharpProof.Dataflow;

public sealed class DataflowBlock<T>(int id, Func<T, T> transfer) {
    public int Id { get; } = id >= 0
        ? id
        : throw new ArgumentOutOfRangeException(nameof(id));
    public Func<T, T> Transfer { get; } =
        transfer ?? throw new ArgumentNullException(nameof(transfer));
}

public readonly struct DataflowEdge(int sourceId, int targetId)
    : IEquatable<DataflowEdge> {
    public int SourceId { get; } = sourceId >= 0
        ? sourceId
        : throw new ArgumentOutOfRangeException(nameof(sourceId));
    public int TargetId { get; } = targetId >= 0
        ? targetId
        : throw new ArgumentOutOfRangeException(nameof(targetId));

    public bool Equals(DataflowEdge other) =>
        SourceId == other.SourceId && TargetId == other.TargetId;

    public override bool Equals(object? obj) => obj is DataflowEdge other && Equals(other);

    public override int GetHashCode() {
        unchecked {
            return (SourceId * 397) ^ TargetId;
        }
    }

    public static bool operator ==(DataflowEdge left, DataflowEdge right) => left.Equals(right);
    public static bool operator !=(DataflowEdge left, DataflowEdge right) => !left.Equals(right);
}

/// <summary>
/// Small, language-neutral control-flow graph with contiguous block identifiers.
/// </summary>
public sealed class DataflowGraph<T> {
    private readonly ImmutableArray<ImmutableArray<int>> _predecessors;
    private readonly ImmutableArray<ImmutableArray<int>> _successors;
    private readonly ImmutableArray<bool> _cyclicBlocks;

    public DataflowGraph(
        IEnumerable<DataflowBlock<T>> blocks,
        IEnumerable<DataflowEdge> edges,
        int entryBlockId = 0) {
        if (blocks == null) throw new ArgumentNullException(nameof(blocks));
        if (edges == null) throw new ArgumentNullException(nameof(edges));

        Blocks = [.. blocks.OrderBy(static block => block.Id)];
        if (Blocks.IsDefaultOrEmpty)
            throw new ArgumentException("A dataflow graph must contain at least one block.", nameof(blocks));
        for (var index = 0; index < Blocks.Length; index++)
            if (Blocks[index].Id != index)
                throw new ArgumentException(
                    "Block identifiers must be unique and contiguous from zero.",
                    nameof(blocks));
        if (entryBlockId < 0 || entryBlockId >= Blocks.Length)
            throw new ArgumentOutOfRangeException(nameof(entryBlockId));

        var distinctEdges = new HashSet<DataflowEdge>();
        foreach (var edge in edges) {
            if (edge.SourceId >= Blocks.Length || edge.TargetId >= Blocks.Length)
                throw new ArgumentException("An edge references a block outside the graph.", nameof(edges));
            distinctEdges.Add(edge);
        }
        Edges = [.. distinctEdges
            .OrderBy(static edge => edge.SourceId)
            .ThenBy(static edge => edge.TargetId)];
        EntryBlockId = entryBlockId;

        var predecessors = CreateAdjacency(Blocks.Length);
        var successors = CreateAdjacency(Blocks.Length);
        foreach (var edge in Edges) {
            successors[edge.SourceId].Add(edge.TargetId);
            predecessors[edge.TargetId].Add(edge.SourceId);
        }
        _predecessors = Freeze(predecessors);
        _successors = Freeze(successors);
        _cyclicBlocks = FindCyclicBlocks(_successors);
    }

    public ImmutableArray<DataflowBlock<T>> Blocks { get; }
    public ImmutableArray<DataflowEdge> Edges { get; }
    public int EntryBlockId { get; }

    public DataflowBlock<T> GetBlock(int blockId) {
        ValidateBlockId(blockId);
        return Blocks[blockId];
    }

    public ImmutableArray<int> GetPredecessors(int blockId) =>
        GetNeighbors(blockId, _predecessors);

    public ImmutableArray<int> GetSuccessors(int blockId) =>
        GetNeighbors(blockId, _successors);

    private ImmutableArray<int> GetNeighbors(
        int blockId,
        ImmutableArray<ImmutableArray<int>> adjacency) {
        ValidateBlockId(blockId);
        return adjacency[blockId];
    }

    public bool IsCyclicBlock(int blockId) {
        ValidateBlockId(blockId);
        return _cyclicBlocks[blockId];
    }

    private void ValidateBlockId(int blockId) {
        if (blockId < 0 || blockId >= Blocks.Length)
            throw new ArgumentOutOfRangeException(nameof(blockId));
    }

    private static List<int>[] CreateAdjacency(int count) {
        var result = new List<int>[count];
        for (var index = 0; index < count; index++)
            result[index] = [];
        return result;
    }

    private static ImmutableArray<ImmutableArray<int>> Freeze(List<int>[] adjacency) {
        var result = ImmutableArray.CreateBuilder<ImmutableArray<int>>(adjacency.Length);
        foreach (var neighbors in adjacency) {
            neighbors.Sort();
            result.Add([.. neighbors]);
        }
        return result.MoveToImmutable();
    }

    private static ImmutableArray<bool> FindCyclicBlocks(
        ImmutableArray<ImmutableArray<int>> successors) {
        var result = ImmutableArray.CreateBuilder<bool>(successors.Length);
        for (var start = 0; start < successors.Length; start++) {
            var seen = new HashSet<int>();
            var pending = new Stack<int>(successors[start].Reverse());
            var cyclic = false;
            while (pending.Count != 0 && !cyclic) {
                var current = pending.Pop();
                if (current == start) {
                    cyclic = true;
                    break;
                }
                if (!seen.Add(current)) continue;
                for (var index = successors[current].Length - 1; index >= 0; index--)
                    pending.Push(successors[current][index]);
            }
            result.Add(cyclic);
        }
        return result.MoveToImmutable();
    }
}
