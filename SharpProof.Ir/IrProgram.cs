namespace SharpProof.Ir;

public abstract partial class IrInstruction
{
    public bool IsTerminal => Kind is IrInstructionKind.Branch
        or IrInstructionKind.Goto or IrInstructionKind.Return;
}

public sealed partial class IrBasicBlock
{
    public IrInstruction Terminator => Instructions[Instructions.Length - 1];
}

public sealed partial class IrProgram
{
    private readonly ImmutableDictionary<IrBlockId, IrBasicBlock> _blocksById;

    internal IrProgram(
        IrFactory factory, long scope, IrBlockId entry, ImmutableArray<IrBasicBlock> blocks)
    {
        (Factory, Scope, Entry, Blocks) = (factory, scope, entry, blocks);
        _blocksById = blocks.ToImmutableDictionary(static block => block.Id);
    }

    public IrBasicBlock GetBlock(IrBlockId id)
    {
        if (id.Scope != Scope)
        {
            throw new ArgumentException(
                "The block identifier belongs to a different program.",
                nameof(id));
        }

        if (!_blocksById.TryGetValue(id, out var block))
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        return block;
    }
}
