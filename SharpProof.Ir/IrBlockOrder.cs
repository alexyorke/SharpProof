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
        var active = new HashSet<IrBlockId>();
        var complete = new HashSet<IrBlockId>();
        var pending = new Stack<(IrBlockId Block, bool Exit)>();
        var result = new List<IrBlockId>();
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
                active.Remove(frame.Block);
                if (complete.Add(frame.Block))
                {
                    result.Add(frame.Block);
                }

                continue;
            }

            if (complete.Contains(frame.Block))
            {
                continue;
            }

            if (!active.Add(frame.Block))
            {
                failure = IrAcyclicOrderFailure.CyclicControlFlow;
                return default;
            }

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
