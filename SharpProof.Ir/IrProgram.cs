namespace SharpProof.Ir;

public abstract partial class IrInstruction
{
    public bool IsTerminal => Kind is IrInstructionKind.Branch
        or IrInstructionKind.Goto or IrInstructionKind.Return
        or IrInstructionKind.Throw or IrInstructionKind.ExceptionalExit;
}

public sealed partial class IrBasicBlock
{
    public IrInstruction Terminator => Instructions[Instructions.Length - 1];
}

public sealed partial class IrProgram
{
    public IrExecutionSemantics Semantics => Factory.Semantics;
    internal IrProgram(
        IrFactory factory, long scope, IrBlockId entry, ImmutableArray<IrBasicBlock> blocks)
    {
        (Factory, Scope, Entry, Blocks) = (factory, scope, entry, blocks);
    }

    public IrBasicBlock GetBlock(IrBlockId id)
    {
        if (id.Scope != Scope)
        {
            throw new ArgumentException(
                "The block identifier belongs to a different program.",
                nameof(id));
        }

        if ((uint)id.Value >= (uint)Blocks.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        return Blocks[id.Value];
    }
}
