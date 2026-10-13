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
            IrWriteInstruction { Target: { } target, Index: { } index, Value: { } value } => [target, index, value],
            IrWriteInstruction { Target: { } target, Value: { } value } => [target, value],
            IrCallInstruction call => call.Receiver == null ? call.Arguments : call.Arguments.Insert(0, call.Receiver),
            IrAssumeInstruction assume => [assume.Condition],
            IrAssertInstruction assert => [assert.Condition],
            IrBranchInstruction branch => [branch.Condition],
            IrReturnInstruction { Value: { } value } => [value],
            IrLoadInstruction load => load.Location.ReadTerms(),
            IrStoreInstruction store => store.Location.ReadTerms().Concat([store.Value]),
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

public abstract partial class IrLocation
{
    internal abstract ImmutableArray<IrTerm> ReadTerms();
}

public sealed partial class IrMemberLocation
{
    internal override ImmutableArray<IrTerm> ReadTerms()
    {
        return Receiver == null ? Arguments : Arguments.Insert(0, Receiver);
    }
}

public sealed partial class IrSequenceLocation
{
    internal override ImmutableArray<IrTerm> ReadTerms()
    {
        return [Sequence, Index];
    }
}

internal readonly struct IrSuccessors(IrBlockId? first, IrBlockId? second)
{
    internal IrBlockId? First { get; } = first;
    internal IrBlockId? Second { get; } = second;
}
