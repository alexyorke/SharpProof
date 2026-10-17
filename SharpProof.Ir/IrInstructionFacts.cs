namespace SharpProof.Ir;

internal static class IrInstructionFacts
{
    internal static IEnumerable<IrVarId> WrittenVariables(IrInstruction instruction)
    {
        if (instruction is null)
        {
            throw new System.ArgumentNullException(nameof(instruction));
        }
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
        if (instruction is null)
        {
            throw new System.ArgumentNullException(nameof(instruction));
        }
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
            IrLoadInstruction load => ReadTerms(load.Location),
            IrStoreInstruction store => ReadTerms(store.Location).Concat([store.Value]),
            _ => []
        };
    }

    private static ImmutableArray<IrTerm> ReadTerms(IrLocation location)
    {
        if (location is null)
        {
            throw new System.ArgumentNullException(nameof(location));
        }
        return location.Accept(LocationReadVisitor.Instance, 0);
    }

    private sealed class LocationReadVisitor : IIrLocationVisitor<int, ImmutableArray<IrTerm>>
    {
        internal static LocationReadVisitor Instance { get; } = new();

        public ImmutableArray<IrTerm> Visit(IrMemberLocation location, int state)
        {
            if (location is null)
            {
                throw new System.ArgumentNullException(nameof(location));
            }
            return location.Receiver == null ? location.Arguments : location.Arguments.Insert(0, location.Receiver);
        }

        public ImmutableArray<IrTerm> Visit(IrSequenceLocation location, int state)
        {
            if (location is null)
            {
                throw new System.ArgumentNullException(nameof(location));
            }
            return [location.Sequence, location.Index];
        }
    }

    internal static IrSuccessors? TryGetSuccessors(
        IrInstruction terminator)
    {
        if (terminator is null)
        {
            throw new System.ArgumentNullException(nameof(terminator));
        }
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
