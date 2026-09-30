namespace SharpProof.Ir;

internal static class IrInstructionFacts
{
    internal static IrSuccessors? TryGetSuccessors(
        IrInstruction terminator)
    {
        return terminator switch
        {
            IrBranchInstruction { WhenTrue: var whenTrue, WhenFalse: var whenFalse }
                when whenTrue == whenFalse => new(whenTrue, null),
            IrBranchInstruction branch => new(branch.WhenTrue, branch.WhenFalse),
            IrGotoInstruction go => new(go.Target, null),
            IrThrowInstruction thrown => new(thrown.Target, null),
            IrReturnInstruction or IrExceptionalExitInstruction => new(null, null),
            _ => null
        };
    }
}

internal readonly struct IrSuccessors(IrBlockId? first, IrBlockId? second)
{
    internal IrBlockId? First { get; } = first;
    internal IrBlockId? Second { get; } = second;
}
