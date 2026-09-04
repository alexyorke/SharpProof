namespace SharpProof.Ir;

internal static class IrInstructionFacts
{
    internal static ImmutableArray<IrBlockId>? TryGetSuccessors(
        IrInstruction terminator)
    {
        return terminator switch
        {
            IrBranchInstruction { WhenTrue: var whenTrue, WhenFalse: var whenFalse }
                when whenTrue == whenFalse => [whenTrue],
            IrBranchInstruction branch => [branch.WhenTrue, branch.WhenFalse],
            IrGotoInstruction go => [go.Target],
            IrReturnInstruction => [],
            _ => null
        };
    }
}
