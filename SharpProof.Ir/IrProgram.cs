namespace SharpProof.Ir;

public enum IrInstructionKind {
    Assign,
    Load,
    Store,
    Call,
    Assume,
    Assert,
    Havoc,
    Branch,
    Goto,
    Return
}

public enum IrLocationKind {
    Member,
    Sequence
}

public enum IrHavocKind {
    Variables,
    Memory,
    VariablesAndMemory
}

public abstract class IrLocation {
    private protected IrLocation(IrTypeId type, IrLocationKind kind) =>
        (Type, Kind) = (type, kind);

    public IrTypeId Type { get; }
    public IrLocationKind Kind { get; }
}

public sealed class IrMemberLocation : IrLocation {
    internal IrMemberLocation(
        IrTypeId type, IrMemberId member, IrTerm? receiver, ImmutableArray<IrTerm> arguments) :
        base(type, IrLocationKind.Member) =>
        (Member, Receiver, Arguments) = (member, receiver, arguments);

    public IrMemberId Member { get; }
    public IrTerm? Receiver { get; }
    public ImmutableArray<IrTerm> Arguments { get; }
}

public sealed class IrSequenceLocation : IrLocation {
    internal IrSequenceLocation(IrTypeId type, IrTerm sequence, IrTerm index) :
        base(type, IrLocationKind.Sequence) =>
        (Sequence, Index) = (sequence, index);

    public IrTerm Sequence { get; }
    public IrTerm Index { get; }
}

public abstract class IrInstruction {
    private protected IrInstruction(
        IrInstructionId id, IrInstructionKind kind, OperationId operation) =>
        (Id, Kind, Operation) = (id, kind, operation);

    public IrInstructionId Id { get; }
    public IrInstructionKind Kind { get; }
    public OperationId Operation { get; }
    public bool IsTerminal => Kind is IrInstructionKind.Branch
        or IrInstructionKind.Goto or IrInstructionKind.Return;
}

public sealed class IrAssignInstruction : IrInstruction {
    internal IrAssignInstruction(
        IrInstructionId id, OperationId operation, IrVarId target, IrTerm value) :
        base(id, IrInstructionKind.Assign, operation) =>
        (Target, Value) = (target, value);

    public IrVarId Target { get; }
    public IrTerm Value { get; }
}

public sealed class IrLoadInstruction : IrInstruction {
    internal IrLoadInstruction(
        IrInstructionId id, OperationId operation, IrVarId target, IrLocation location) :
        base(id, IrInstructionKind.Load, operation) =>
        (Target, Location) = (target, location);

    public IrVarId Target { get; }
    public IrLocation Location { get; }
}

public sealed class IrStoreInstruction : IrInstruction {
    internal IrStoreInstruction(
        IrInstructionId id, OperationId operation, IrLocation location, IrTerm value) :
        base(id, IrInstructionKind.Store, operation) =>
        (Location, Value) = (location, value);

    public IrLocation Location { get; }
    public IrTerm Value { get; }
}

public sealed class IrCallInstruction : IrInstruction {
    internal IrCallInstruction(
        IrInstructionId id, OperationId operation, IrVarId? target,
        IrMemberId member, IrTerm? receiver, ImmutableArray<IrTerm> arguments) :
        base(id, IrInstructionKind.Call, operation) =>
        (Target, Member, Receiver, Arguments) = (target, member, receiver, arguments);

    public IrVarId? Target { get; }
    public IrMemberId Member { get; }
    public IrTerm? Receiver { get; }
    public ImmutableArray<IrTerm> Arguments { get; }
}

public sealed class IrAssumeInstruction : IrInstruction {
    internal IrAssumeInstruction(
        IrInstructionId id, OperationId operation, IrTerm condition) :
        base(id, IrInstructionKind.Assume, operation) =>
        Condition = condition;

    public IrTerm Condition { get; }
}

public sealed class IrAssertInstruction : IrInstruction {
    internal IrAssertInstruction(
        IrInstructionId id, OperationId operation, IrTerm condition) :
        base(id, IrInstructionKind.Assert, operation) =>
        Condition = condition;

    public IrTerm Condition { get; }
}

public sealed class IrHavocInstruction : IrInstruction {
    internal IrHavocInstruction(
        IrInstructionId id, OperationId operation,
        IrHavocKind havocKind, ImmutableArray<IrVarId> variables) :
        base(id, IrInstructionKind.Havoc, operation) =>
        (HavocKind, Variables) = (havocKind, variables);

    public IrHavocKind HavocKind { get; }
    public ImmutableArray<IrVarId> Variables { get; }
}

public sealed class IrBranchInstruction : IrInstruction {
    internal IrBranchInstruction(
        IrInstructionId id, OperationId operation, IrTerm condition,
        IrBlockId whenTrue, IrBlockId whenFalse) :
        base(id, IrInstructionKind.Branch, operation) =>
        (Condition, WhenTrue, WhenFalse) = (condition, whenTrue, whenFalse);

    public IrTerm Condition { get; }
    public IrBlockId WhenTrue { get; }
    public IrBlockId WhenFalse { get; }
}

public sealed class IrGotoInstruction : IrInstruction {
    internal IrGotoInstruction(
        IrInstructionId id, OperationId operation, IrBlockId target) :
        base(id, IrInstructionKind.Goto, operation) =>
        Target = target;

    public IrBlockId Target { get; }
}

public sealed class IrReturnInstruction : IrInstruction {
    internal IrReturnInstruction(
        IrInstructionId id, OperationId operation, IrTerm? value) :
        base(id, IrInstructionKind.Return, operation) =>
        Value = value;

    public IrTerm? Value { get; }
}

public sealed class IrBasicBlock {
    internal IrBasicBlock(
        IrBlockId id, IrStringId? name, ImmutableArray<IrInstruction> instructions) =>
        (Id, Name, Instructions) = (id, name, instructions);

    public IrBlockId Id { get; }
    public IrStringId? Name { get; }
    public ImmutableArray<IrInstruction> Instructions { get; }
    public IrInstruction Terminator => Instructions[Instructions.Length - 1];
}

public sealed class IrProgram {
    private readonly ImmutableDictionary<IrBlockId, IrBasicBlock> _blocksById;

    internal IrProgram(
        IrFactory factory, long scope, IrBlockId entry, ImmutableArray<IrBasicBlock> blocks) {
        (Factory, Scope, Entry, Blocks) = (factory, scope, entry, blocks);
        _blocksById = blocks.ToImmutableDictionary(static block => block.Id);
    }

    public IrFactory Factory { get; }
    internal long Scope { get; }
    public IrBlockId Entry { get; }
    public ImmutableArray<IrBasicBlock> Blocks { get; }

    public IrBasicBlock GetBlock(IrBlockId id) {
        if (id.Scope != Scope)
            throw new ArgumentException(
                "The block identifier belongs to a different program.",
                nameof(id));
        if (!_blocksById.TryGetValue(id, out var block))
            throw new ArgumentOutOfRangeException(nameof(id));
        return block;
    }
}
