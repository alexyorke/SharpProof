namespace SharpProof.Ir;

internal enum IrAcyclicOrderFailure
{
    None,
    ResourceLimit,
    CyclicControlFlow,
    UnsupportedInstruction
}

internal static class IrBlockOrder
{
    internal static ImmutableArray<IrBlockId> TryCreateAcyclicOrder(
        IrProgram program,
        Func<int, bool> spend,
        out IrAcyclicOrderFailure failure)
    {
        var blockCapacity = program.Blocks.Length;
        var states = new Dictionary<IrBlockId, byte>(blockCapacity);
        var pending = new Stack<(IrBlockId Block, bool Exit)>(blockCapacity);
        var result = new List<IrBlockId>(blockCapacity);
        pending.Push((program.Entry, false));
        while (pending.Count != 0)
        {
            if (!spend(1))
            {
                failure = IrAcyclicOrderFailure.ResourceLimit;
                return default;
            }

            var frame = pending.Pop();
            if (frame.Exit)
            {
                states[frame.Block] = 2;
                result.Add(frame.Block);

                continue;
            }

            if (states.TryGetValue(frame.Block, out var state))
            {
                if (state == 2)
                {
                    continue;
                }

                failure = IrAcyclicOrderFailure.CyclicControlFlow;
                return default;
            }

            states.Add(frame.Block, 1);
            pending.Push((frame.Block, true));
            switch (program.GetBlock(frame.Block).Terminator)
            {
                case IrBranchInstruction branch:
                    pending.Push((branch.WhenFalse, false));
                    pending.Push((branch.WhenTrue, false));
                    break;
                case IrGotoInstruction go:
                    pending.Push((go.Target, false));
                    break;
                case IrReturnInstruction:
                    break;
                default:
                    failure = IrAcyclicOrderFailure.UnsupportedInstruction;
                    return default;
            }
        }

        result.Reverse();
        failure = IrAcyclicOrderFailure.None;
        return [.. result];
    }
}
