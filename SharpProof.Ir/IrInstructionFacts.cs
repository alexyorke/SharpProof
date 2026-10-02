namespace SharpProof.Ir;

internal static class IrInstructionFacts
{
    internal static IEnumerable<IrVarId> WrittenVariables(IrInstruction instruction)
    {
        return instruction switch
        {
            IrAssignInstruction assign => [assign.Target],
            IrLoadInstruction load => [load.Target],
            IrAllocationInstruction { Target: { } target } => [target],
            IrCallInstruction { Target: { } target } => [target],
            IrHavocInstruction havoc => havoc.Variables,
            _ => []
        };
    }

    internal static IEnumerable<IrTerm> ReadTerms(IrInstruction instruction)
    {
        return instruction switch
        {
            IrAssignInstruction assign => [assign.Value],
            IrAllocationInstruction allocation => allocation.Length == null ? allocation.InitialValues : allocation.InitialValues.Insert(0, allocation.Length),
            IrLockInstruction synchronization => [synchronization.Receiver],
            IrCallInstruction call => call.Receiver == null ? call.Arguments : call.Arguments.Insert(0, call.Receiver),
            IrAssumeInstruction assume => [assume.Condition],
            IrAssertInstruction assert => [assert.Condition],
            IrBranchInstruction branch => [branch.Condition],
            IrReturnInstruction { Value: { } value } => [value],
            IrLoadInstruction load => ReadTerms(load.Location),
            IrStoreInstruction store => ReadTerms(store.Location).Concat([store.Value]),
            _ => []
        };
    }

    private static ImmutableArray<IrTerm> ReadTerms(IrLocation location)
    {
        return location switch
        {
            IrMemberLocation member => member.Receiver == null ? member.Arguments : member.Arguments.Insert(0, member.Receiver),
            IrSequenceLocation sequence => [sequence.Sequence, sequence.Index],
            _ => []
        };
    }

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
