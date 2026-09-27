namespace SharpProof.Ir;

internal enum IrAcyclicOrderFailure
{
    None,
    ResourceLimit,
    CyclicControlFlow,
    UnsupportedInstruction
}

// A reducible program with its back edges removed. Order is a topological
// order of the remaining forward edges; each loop header maps to the blocks
// of its natural loop (header included).
internal sealed class IrLoopCut(
    ImmutableArray<IrBlockId> order,
    ImmutableHashSet<(IrBlockId From, IrBlockId To)> backEdges,
    ImmutableDictionary<IrBlockId, ImmutableArray<IrBlockId>> loops)
{
    internal ImmutableArray<IrBlockId> Order { get; } = order;

    internal ImmutableHashSet<(IrBlockId From, IrBlockId To)> BackEdges { get; } = backEdges;

    internal ImmutableDictionary<IrBlockId, ImmutableArray<IrBlockId>> Loops { get; } = loops;
}

internal static class IrBlockOrder
{
    internal static ImmutableArray<IrBlockId> TryCreateAcyclicOrder(
        IrProgram program,
        Func<int, bool> spend,
        out IrAcyclicOrderFailure failure)
    {
        var cut = TryCutLoops(program, spend, out failure);
        if (cut == null)
        {
            return default;
        }

        if (!cut.BackEdges.IsEmpty)
        {
            failure = IrAcyclicOrderFailure.CyclicControlFlow;
            return default;
        }

        return cut.Order;
    }

    // Loop cutting (Boogie-style): every back edge u -> h must target a
    // header h that dominates u. Irreducible control flow is rejected as
    // CyclicControlFlow.
    internal static IrLoopCut? TryCutLoops(
        IrProgram program,
        Func<int, bool> spend,
        out IrAcyclicOrderFailure failure)
    {
        var blockCapacity = program.Blocks.Length;
        var states = new byte[blockCapacity];
        var pending = new Stack<(IrBlockId Block, bool Exit)>(blockCapacity);
        var result = new IrBlockId[blockCapacity];
        var resultCount = 0;
        var backEdges = ImmutableHashSet.CreateBuilder<(IrBlockId From, IrBlockId To)>();
        var predecessors = new Dictionary<IrBlockId, List<IrBlockId>>();
        pending.Push((program.Entry, false));
        while (pending.Count != 0)
        {
            if (!spend(1))
            {
                failure = IrAcyclicOrderFailure.ResourceLimit;
                return null;
            }

            var frame = pending.Pop();
            if (frame.Exit)
            {
                states[frame.Block.Value] = 2;
                result[resultCount++] = frame.Block;

                continue;
            }

            if (states[frame.Block.Value] != 0)
            {
                continue;
            }

            states[frame.Block.Value] = 1;
            pending.Push((frame.Block, true));
            IrBlockId[] successors;
            switch (program.GetBlock(frame.Block).Terminator)
            {
                case IrBranchInstruction branch:
                    successors = [branch.WhenTrue, branch.WhenFalse];
                    break;
                case IrGotoInstruction go:
                    successors = [go.Target];
                    break;
                case IrReturnInstruction:
                    successors = [];
                    break;
                default:
                    failure = IrAcyclicOrderFailure.UnsupportedInstruction;
                    return null;
            }

            for (var index = successors.Length - 1; index >= 0; index--)
            {
                var successor = successors[index];
                if (!predecessors.TryGetValue(successor, out var list))
                {
                    predecessors.Add(successor, list = []);
                }

                list.Add(frame.Block);
                if (states[successor.Value] == 1)
                {
                    backEdges.Add((frame.Block, successor));
                }
                else
                {
                    pending.Push((successor, false));
                }
            }
        }

        Array.Reverse(result, 0, resultCount);
        var loops = ImmutableDictionary.CreateBuilder<IrBlockId, ImmutableArray<IrBlockId>>();
        foreach (var header in backEdges.Select(static edge => edge.To).Distinct())
        {
            var body = new HashSet<IrBlockId> { header };
            var work = new Stack<IrBlockId>(
                backEdges.Where(edge => edge.To == header).Select(static edge => edge.From));
            while (work.Count != 0)
            {
                if (!spend(1))
                {
                    failure = IrAcyclicOrderFailure.ResourceLimit;
                    return null;
                }

                var block = work.Pop();
                if (!body.Add(block))
                {
                    continue;
                }

                if (block == program.Entry)
                {
                    // The entry reaches this latch without passing the
                    // header, so the header does not dominate it.
                    failure = IrAcyclicOrderFailure.CyclicControlFlow;
                    return null;
                }

                if (predecessors.TryGetValue(block, out var incoming))
                {
                    foreach (var predecessor in incoming)
                    {
                        work.Push(predecessor);
                    }
                }
            }

            loops.Add(header, [.. result.Take(resultCount).Where(body.Contains)]);
        }

        failure = IrAcyclicOrderFailure.None;
        return new IrLoopCut(
            ImmutableArray.Create(result, 0, resultCount),
            backEdges.ToImmutable(),
            loops.ToImmutable());
    }
}
