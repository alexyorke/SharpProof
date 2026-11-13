namespace SharpProof.Ir;

internal sealed partial class IrBasicBlock
{
    internal IrBasicBlock(IrBlockId id, IrStringId? name, ImmutableArray<IrInstruction> instructions)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.RequireInitialized(instructions.IsDefault, nameof(instructions), "Array must be initialized.");
        (Id, Name, Instructions) =
            (id, name, instructions);
        ControlFlow = (IIrTerminator)instructions[instructions.Length - 1];
    }

    public IrBlockId Id { get; }

    public IrStringId? Name { get; }

    public ImmutableArray<IrInstruction> Instructions { get; }
}

internal sealed partial class IrProgram
{

    public IrFactory Factory { get; }

    internal long Scope { get; }

    public IrBlockId Entry { get; }

    public ImmutableArray<IrBasicBlock> Blocks { get; }
}
