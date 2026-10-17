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
        if (program is null)
        {
            throw new ArgumentNullException(nameof(program));
        }

        if (spend is null)
        {
            throw new ArgumentNullException(nameof(spend));
        }

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
            block.ControlFlow.Accept(SuccessorVisitor.Instance, pending);
        }

        Array.Reverse(result, 0, resultCount);
        failure = IrAcyclicOrderFailure.None;
        return ImmutableArray.Create(result, 0, resultCount);
    }

    private sealed class SuccessorVisitor : IIrTerminatorVisitor<Stack<(IrBlockId Block, bool Exit)>>
    {
        internal static SuccessorVisitor Instance { get; } = new();

        public void Visit(IrBranchInstruction instruction, Stack<(IrBlockId Block, bool Exit)> state)
        {
            if (instruction is null)
            {
                throw new System.ArgumentNullException(nameof(instruction));
            }
            if (state is null)
            {
                throw new System.ArgumentNullException(nameof(state));
            }
            state.Push((instruction.WhenFalse, false));
            state.Push((instruction.WhenTrue, false));
        }

        public void Visit(IrGotoInstruction instruction, Stack<(IrBlockId Block, bool Exit)> state)
        {
            if (instruction is null)
            {
                throw new System.ArgumentNullException(nameof(instruction));
            }
            if (state is null)
            {
                throw new System.ArgumentNullException(nameof(state));
            }
            state.Push((instruction.Target, false));
        }

        public void Visit(IrThrowInstruction instruction, Stack<(IrBlockId Block, bool Exit)> state)
        {
            if (instruction is null)
            {
                throw new System.ArgumentNullException(nameof(instruction));
            }
            if (state is null)
            {
                throw new System.ArgumentNullException(nameof(state));
            }
            state.Push((instruction.Target, false));
        }

        public void Visit(IrReturnInstruction instruction, Stack<(IrBlockId Block, bool Exit)> state)
        {
            if (instruction is null)
            { throw new System.ArgumentNullException(nameof(instruction)); }
            if (state is null)
            { throw new System.ArgumentNullException(nameof(state)); }
        }

        public void Visit(IrExceptionalExitInstruction instruction, Stack<(IrBlockId Block, bool Exit)> state)
        {
            if (instruction is null)
            { throw new System.ArgumentNullException(nameof(instruction)); }
            if (state is null)
            { throw new System.ArgumentNullException(nameof(state)); }
        }
    }
}
