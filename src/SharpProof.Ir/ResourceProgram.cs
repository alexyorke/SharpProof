using System.Numerics;

namespace SharpProof.Ir;

internal enum ResourceProgramKind { Zero, AllocateArray, Sequence, Choice, Repeat, MetadataBoundary, MetadataCall }
internal enum ResourceLoopKind { IncreasingAdd, IncreasingMultiply, DecreasingSubtract, DecreasingDivide }
internal enum ResourceRepeatKind { Loop, TailRecurrence }

// A closed resource semantics. Lowering must preserve every allocating operation
// and reject writes/aliases to a repeated region's controlling scalar. A repeat
// carries the actual initialization, strict guard and update, never a trip count.
internal sealed class ResourceLoopRank
{
    internal ResourceLoopKind Kind { get; }
    internal ResourceExpression Initial { get; }
    internal ResourceExpression Limit { get; }
    internal BigInteger Step { get; }
    internal int CounterWidth { get; }
    internal bool CheckedStep { get; }

    internal ResourceLoopRank(ResourceLoopKind kind, ResourceExpression initial,
        ResourceExpression limit, BigInteger step, int counterWidth, bool checkedStep)
    {
        Initial = ArgumentNullGuard.NotNull(initial, nameof(initial));
        Limit = ArgumentNullGuard.NotNull(limit, nameof(limit));
        Kind = kind;
        Step = step;
        CounterWidth = counterWidth;
        CheckedStep = checkedStep;
    }
}

internal sealed class ResourceProgram
{
    internal const int ModelVersion = 3;
    internal ResourceProgramKind Kind { get; }
    internal ResourceExpression? Length { get; }
    internal ResourceLoopRank? Rank { get; }
    internal ResourceMetadataReference? Metadata { get; }
    internal ResourceAllocationShape? Allocation { get; }
    internal ResourceRepeatKind RepeatKind { get; }
    internal ImmutableArray<ResourceProgram> Children { get; }
    internal int NodeCount { get; }
    internal int Depth { get; }
    internal static ResourceProgram Zero { get; } = new(ResourceProgramKind.Zero, null, null, default, []);
    internal static ResourceProgram MetadataBoundary { get; } = new(ResourceProgramKind.MetadataBoundary, null, null, default, []);

    private ResourceProgram(ResourceProgramKind kind, ResourceExpression? length, ResourceLoopRank? rank,
        ResourceRepeatKind repeatKind, ImmutableArray<ResourceProgram> children, ResourceMetadataReference? metadata = null, ResourceAllocationShape? allocation = null)
    {
        if (children.IsDefault)
        { throw new ArgumentException("Children must be initialized.", nameof(children)); }
        Kind = kind;
        Length = length;
        Rank = rank;
        Metadata = metadata;
        Allocation = allocation;
        RepeatKind = repeatKind;
        Children = children;
        var nodes = 1 + (length?.NodeCount ?? 0) + (rank is null ? 0 : rank.Initial.NodeCount + rank.Limit.NodeCount);
        var depth = 1 + Math.Max(length?.Depth ?? 0, rank is null ? 0 : Math.Max(rank.Initial.Depth, rank.Limit.Depth));
        foreach (var child in children)
        {
            if (child is null)
            { throw new ArgumentException("Children cannot contain null.", nameof(children)); }
            nodes += child.NodeCount;
            depth = Math.Max(depth, 1 + child.Depth);
        }
        if (nodes > ResourceExpression.MaximumNodes || depth > ResourceExpression.MaximumDepth ||
            rank is not null && rank.Step.ToByteArray().Length > 8192)
        { throw new ResourceExpressionLimitException(); }
        NodeCount = nodes;
        Depth = depth;
    }

    internal static ResourceProgram MetadataCall(ResourceMetadataReference metadata)
    {
        metadata = ArgumentNullGuard.NotNull(metadata, nameof(metadata));
        return new(ResourceProgramKind.MetadataCall, null, null, default, [], metadata);
    }

    internal static ResourceProgram Array(ResourceExpression length)
    {
        length = ArgumentNullGuard.NotNull(length, nameof(length));
        return new(ResourceProgramKind.AllocateArray, length, null, default, []);
    }

    internal static ResourceProgram TypedArray(ResourceAllocationShape allocation)
    {
        allocation = ArgumentNullGuard.NotNull(allocation, nameof(allocation));
        if (allocation.Kind != ResourceAllocationKind.Array)
        { throw new ArgumentException("An array program requires an array storage shape.", nameof(allocation)); }
        return new(ResourceProgramKind.AllocateArray, allocation.Extent, null, default, [], allocation: allocation);
    }

    internal static ResourceProgram Sequence(ResourceProgram left, ResourceProgram right)
    {
        left = ArgumentNullGuard.NotNull(left, nameof(left));
        right = ArgumentNullGuard.NotNull(right, nameof(right));
        if (left.Kind == ResourceProgramKind.Zero)
        { return right; }
        if (right.Kind == ResourceProgramKind.Zero)
        { return left; }
        return new(ResourceProgramKind.Sequence, null, null, default, [left, right]);
    }

    internal static ResourceProgram Choice(ResourceProgram left, ResourceProgram right)
    {
        left = ArgumentNullGuard.NotNull(left, nameof(left));
        right = ArgumentNullGuard.NotNull(right, nameof(right));
        if (ReferenceEquals(left, right))
        { return left; }
        return new(ResourceProgramKind.Choice, null, null, default, [left, right]);
    }

    internal static ResourceProgram Repeat(ResourceLoopRank rank, ResourceRepeatKind kind, ResourceProgram body)
    {
        rank = ArgumentNullGuard.NotNull(rank, nameof(rank));
        body = ArgumentNullGuard.NotNull(body, nameof(body));
        return new(ResourceProgramKind.Repeat, null, rank, kind, [body]);
    }
}
