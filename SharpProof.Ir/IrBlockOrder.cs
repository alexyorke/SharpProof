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
        var states = new byte[blockCapacity];
        var pending = new Stack<(IrBlockId Block, bool Exit)>(blockCapacity);
        var result = new IrBlockId[blockCapacity];
        var resultCount = 0;
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
                states[frame.Block.Value] = 2;
                result[resultCount++] = frame.Block;

                continue;
            }

            var block = program.GetBlock(frame.Block);
            var state = states[frame.Block.Value];
            if (state != 0)
            {
                if (state == 2)
                {
                    continue;
                }

                failure = IrAcyclicOrderFailure.CyclicControlFlow;
                return default;
            }

            states[frame.Block.Value] = 1;
            pending.Push((frame.Block, true));
            switch (block.Terminator)
            {
                case IrBranchInstruction branch:
                    pending.Push((branch.WhenFalse, false));
                    pending.Push((branch.WhenTrue, false));
                    break;
                case IrGotoInstruction go:
                    pending.Push((go.Target, false));
                    break;
                case IrThrowInstruction thrown:
                    pending.Push((thrown.Target, false));
                    break;
                case IrReturnInstruction or IrExceptionalExitInstruction:
                    break;
                default:
                    failure = IrAcyclicOrderFailure.UnsupportedInstruction;
                    return default;
            }
        }

        Array.Reverse(result, 0, resultCount);
        failure = IrAcyclicOrderFailure.None;
        return ImmutableArray.Create(result, 0, resultCount);
    }
}
