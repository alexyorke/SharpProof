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
        program = RepositoryParameterGuard.NotNull(program, nameof(program));

        spend = RepositoryParameterGuard.NotNull(spend, nameof(spend));

        var blockCapacity = program.Blocks.Length;
        var states = Enumerable.Repeat((byte)0, blockCapacity).ToImmutableList();
        var pending = ImmutableStack<(IrBlockId Block, bool Exit)>.Empty;
        var result = ImmutableList<IrBlockId>.Empty;
        pending = pending.Push((program.Entry, false));
        while (!pending.IsEmpty)
        {
            if (!spend(1))
            {
                failure = IrAcyclicOrderFailure.ResourceLimit;
                return default;
            }

            var frame = pending.Peek();
            pending = pending.Pop();
            if (frame.Exit)
            {
                states = states.SetItem(frame.Block.Value, 2);
                result = result.Add(frame.Block);

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

            states = states.SetItem(frame.Block.Value, 1);
            pending = pending.Push((frame.Block, true));
            pending = block.Terminator switch
            {
                IrBranchInstruction branch => pending.Push((branch.WhenFalse, false)).Push((branch.WhenTrue, false)),
                IrGotoInstruction go => pending.Push((go.Target, false)),
                IrThrowInstruction thrown => pending.Push((thrown.Target, false)),
                _ => pending
            };
        }

        failure = IrAcyclicOrderFailure.None;
        return result.Reverse().ToImmutableArray();
    }

}
