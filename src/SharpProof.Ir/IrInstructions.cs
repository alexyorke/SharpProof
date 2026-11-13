namespace SharpProof.Ir;

internal enum IrInstructionKind
{
    Assign = 0,
    Load = 1,
    Store = 2,
    Call = 3,
    Assume = 4,
    Assert = 5,
    Havoc = 6,
    Branch = 7,
    Goto = 8,
    Return = 9,
    Throw = 10,
    ExceptionalExit = 11,
    Allocate = 12,
    Write = 13,
    Lock = 14
}

internal enum IrWriteRegion
{
    Local = 0,
    Parameter = 1,
    Field = 2,
    Static = 3,
    Element = 4,
    Unknown = 5
}

internal enum IrLocationKind
{
    Member = 0,
    Sequence = 1
}

internal enum IrHavocKind
{
    Variables = 0,
    Memory = 1,
    VariablesAndMemory = 2
}

internal interface IIrLocationVisitor<TState, TResult>
{
    TResult Visit(IrMemberLocation location, TState state);
    TResult Visit(IrSequenceLocation location, TState state);
}

internal abstract class IrLocation
{
    internal abstract TResult Accept<TState, TResult>(IIrLocationVisitor<TState, TResult> visitor, TState state);

    private protected IrLocation(IrTypeId type, IrLocationKind kind)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        (Type, Kind) =
            (type, kind);
    }

    public IrTypeId Type { get; }

    public IrLocationKind Kind { get; }
}

internal sealed class IrMemberLocation : IrLocation
{
    internal override TResult Accept<TState, TResult>(IIrLocationVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }

    internal IrMemberLocation(
        IrTypeId type,
        IrMemberId member,
        IrTerm? receiver,
        ImmutableArray<IrTerm> arguments
    ) : base(type, IrLocationKind.Member)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        RepositoryParameterGuard.ThrowIfInvalid(member.IsDefault, nameof(member));
        RepositoryParameterGuard.RequireInitialized(arguments.IsDefault, nameof(arguments), "Array must be initialized.");
        (Member, Receiver, Arguments) =
            (member, receiver, arguments);
    }

    public IrMemberId Member { get; }

    public IrTerm? Receiver { get; }

    public ImmutableArray<IrTerm> Arguments { get; }
}

internal sealed class IrSequenceLocation : IrLocation
{
    internal override TResult Accept<TState, TResult>(IIrLocationVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }

    internal IrSequenceLocation(IrTypeId type, IrTerm sequence, IrTerm index) : base(type, IrLocationKind.Sequence)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        sequence = RepositoryParameterGuard.NotNull(sequence, nameof(sequence));
        index = RepositoryParameterGuard.NotNull(index, nameof(index));
        (Sequence, Index) =
            (sequence, index);
    }

    public IrTerm Sequence { get; }

    public IrTerm Index { get; }
}

internal interface IIrTerminator
{
    IrInstruction Instruction { get; }
    void Accept<TState>(IIrTerminatorVisitor<TState> visitor, TState state);
}

internal interface IIrTerminatorVisitor<TState>
{
    void Visit(IrBranchInstruction instruction, TState state);
    void Visit(IrGotoInstruction instruction, TState state);
    void Visit(IrThrowInstruction instruction, TState state);
    void Visit(IrReturnInstruction instruction, TState state);
    void Visit(IrExceptionalExitInstruction instruction, TState state);
}

internal abstract partial class IrInstruction
{
    private protected IrInstruction(IrInstructionId id, IrInstructionKind kind, OperationId operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        (Id, Kind, Operation) =
            (id, kind, operation);
    }

    public IrInstructionId Id { get; }

    public IrInstructionKind Kind { get; }

    public OperationId Operation { get; }
}

internal sealed partial class IrAllocationInstruction : IrInstruction
{
    internal IrAllocationInstruction(IrInstructionId id, OperationId operation, IrTypeId allocatedType, IrVarId? target = null,
        IrTerm? length = null, ImmutableArray<IrTerm> initialValues = default)
        : base(id, IrInstructionKind.Allocate, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(allocatedType.IsDefault, nameof(allocatedType));
        AllocatedType = allocatedType;
        Target = target;
        Length = length;
        InitialValues = initialValues.IsDefault ? [] : initialValues;
    }

    public IrTypeId AllocatedType { get; }
    public IrVarId? Target { get; }
    public IrTerm? Length { get; }
    public ImmutableArray<IrTerm> InitialValues { get; }
}

// A synchronization attempt is an explicit effect and a concrete replay
// barrier. It does not claim acquisition, release, completion or exception facts.
internal sealed partial class IrLockInstruction : IrInstruction
{
    internal IrLockInstruction(IrInstructionId id, OperationId operation, IrTerm receiver)
        : base(id, IrInstructionKind.Lock, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        receiver = RepositoryParameterGuard.NotNull(receiver, nameof(receiver));
        Receiver = receiver;
    }

    public IrTerm Receiver { get; }
}

internal sealed partial class IrWriteInstruction : IrInstruction
{
    internal IrWriteInstruction(IrInstructionId id, OperationId operation, IrWriteRegion region)
        : base(id, IrInstructionKind.Write, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        Region = region;
    }

    internal IrWriteInstruction(IrInstructionId id, OperationId operation, IrTerm target, IrTerm index, IrTerm value, IrTerm? length = null)
        : base(id, IrInstructionKind.Write, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        index = RepositoryParameterGuard.NotNull(index, nameof(index));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        Region = IrWriteRegion.Element;
        Target = target;
        Index = index;
        Value = value;
        Length = length;
    }

    internal IrWriteInstruction(IrInstructionId id, OperationId operation, IrWriteRegion region,
        IrTerm target, IrMemberId field, IrTerm value, IrTerm? key = null)
        : base(id, IrInstructionKind.Write, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        RepositoryParameterGuard.ThrowIfInvalid(field.IsDefault, nameof(field));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        Region = region;
        Target = target;
        Field = field;
        Value = value;
        Index = key;
    }

    public IrWriteRegion Region { get; }
    // A store names its array and index, or its object, field and optional key, and the
    // stored value; other writes are effect sites only.
    public IrTerm? Target { get; }
    public IrTerm? Index { get; }
    // A half-open range [Index, Index + Length); absent for a single cell.
    public IrTerm? Length { get; }
    public IrMemberId? Field { get; }
    public IrTerm? Value { get; }
    public bool IsElementStore => Index != null && Field == null;
    public bool IsFieldStore => Field != null;
    public bool IsStore => Value != null;
}

internal sealed partial class IrAssignInstruction : IrInstruction
{
    internal IrAssignInstruction(
        IrInstructionId id,
        OperationId operation,
        IrVarId target,
        IrTerm value
    ) : base(id, IrInstructionKind.Assign, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(target.IsDefault, nameof(target));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        (Target, Value) =
            (target, value);
    }

    public IrVarId Target { get; }

    public IrTerm Value { get; }
}

internal sealed partial class IrLoadInstruction : IrInstruction
{
    internal IrLoadInstruction(
        IrInstructionId id,
        OperationId operation,
        IrVarId target,
        IrLocation location
    ) : base(id, IrInstructionKind.Load, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(target.IsDefault, nameof(target));
        location = RepositoryParameterGuard.NotNull(location, nameof(location));
        (Target, Location) =
            (target, location);
    }

    public IrVarId Target { get; }

    public IrLocation Location { get; }
}

internal sealed partial class IrStoreInstruction : IrInstruction
{
    internal IrStoreInstruction(
        IrInstructionId id,
        OperationId operation,
        IrLocation location,
        IrTerm value
    ) : base(id, IrInstructionKind.Store, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        location = RepositoryParameterGuard.NotNull(location, nameof(location));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        (Location, Value) =
            (location, value);
    }

    public IrLocation Location { get; }

    public IrTerm Value { get; }
}

internal sealed partial class IrCallInstruction : IrInstruction
{
    internal IrCallInstruction(
        IrInstructionId id,
        OperationId operation,
        IrVarId? target,
        IrMemberId member,
        IrTerm? receiver,
        ImmutableArray<IrTerm> arguments
    ) : base(id, IrInstructionKind.Call, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(member.IsDefault, nameof(member));
        RepositoryParameterGuard.RequireInitialized(arguments.IsDefault, nameof(arguments), "Array must be initialized.");
        (Target, Member, Receiver, Arguments) =
            (target, member, receiver, arguments);
    }

    public IrVarId? Target { get; }

    public IrMemberId Member { get; }

    public IrTerm? Receiver { get; }

    public ImmutableArray<IrTerm> Arguments { get; }
}

internal sealed partial class IrAssumeInstruction : IrInstruction
{
    internal IrAssumeInstruction(
        IrInstructionId id,
        OperationId operation,
        IrTerm condition
    ) : base(id, IrInstructionKind.Assume, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        condition = RepositoryParameterGuard.NotNull(condition, nameof(condition));
        Condition = condition;
    }

    public IrTerm Condition { get; }
}

internal sealed partial class IrAssertInstruction : IrInstruction
{
    internal IrAssertInstruction(
        IrInstructionId id,
        OperationId operation,
        IrTerm condition
    ) : base(id, IrInstructionKind.Assert, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        condition = RepositoryParameterGuard.NotNull(condition, nameof(condition));
        Condition = condition;
    }

    public IrTerm Condition { get; }
}

internal sealed partial class IrHavocInstruction : IrInstruction
{
    internal IrHavocInstruction(
        IrInstructionId id,
        OperationId operation,
        IrHavocKind havocKind,
        ImmutableArray<IrVarId> variables,
        IrHavocOrigin origin = IrHavocOrigin.Approximation
    ) : base(id, IrInstructionKind.Havoc, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        RepositoryParameterGuard.RequireInitialized(variables.IsDefault, nameof(variables), "Array must be initialized.");
        (HavocKind, Variables) =
            (havocKind, variables);
        Origin = origin;
    }

    public IrHavocKind HavocKind { get; }

    public ImmutableArray<IrVarId> Variables { get; }
    public IrHavocOrigin Origin { get; }
}

internal sealed partial class IrThrowInstruction : IrInstruction, IIrTerminator
{
    IrInstruction IIrTerminator.Instruction => this;

    void IIrTerminator.Accept<TState>(IIrTerminatorVisitor<TState> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        visitor.Visit(this, state);
    }

    internal IrThrowInstruction(IrInstructionId id, OperationId operation,
        IrExceptionKind exceptionKind, IrBlockId target)
        : base(id, IrInstructionKind.Throw, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(target.IsDefault, nameof(target));
        (ExceptionKind, Target) = (exceptionKind, target);
    }

    public IrExceptionKind ExceptionKind { get; }
    public IrBlockId Target { get; }
}

internal sealed partial class IrExceptionalExitInstruction : IrInstruction, IIrTerminator
{
    IrInstruction IIrTerminator.Instruction => this;

    void IIrTerminator.Accept<TState>(IIrTerminatorVisitor<TState> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        visitor.Visit(this, state);
    }

    internal IrExceptionalExitInstruction(IrInstructionId id, OperationId operation)
        : base(id, IrInstructionKind.ExceptionalExit, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
    }
}

internal sealed partial class IrBranchInstruction : IrInstruction, IIrTerminator
{
    IrInstruction IIrTerminator.Instruction => this;

    void IIrTerminator.Accept<TState>(IIrTerminatorVisitor<TState> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        visitor.Visit(this, state);
    }

    internal IrBranchInstruction(
        IrInstructionId id,
        OperationId operation,
        IrTerm condition,
        IrBlockId whenTrue,
        IrBlockId whenFalse
    ) : base(id, IrInstructionKind.Branch, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        condition = RepositoryParameterGuard.NotNull(condition, nameof(condition));
        RepositoryParameterGuard.ThrowIfInvalid(whenTrue.IsDefault, nameof(whenTrue));
        RepositoryParameterGuard.ThrowIfInvalid(whenFalse.IsDefault, nameof(whenFalse));
        (Condition, WhenTrue, WhenFalse) =
            (condition, whenTrue, whenFalse);
    }

    public IrTerm Condition { get; }

    public IrBlockId WhenTrue { get; }

    public IrBlockId WhenFalse { get; }
}

internal sealed partial class IrGotoInstruction : IrInstruction, IIrTerminator
{
    IrInstruction IIrTerminator.Instruction => this;

    void IIrTerminator.Accept<TState>(IIrTerminatorVisitor<TState> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        visitor.Visit(this, state);
    }

    internal IrGotoInstruction(
        IrInstructionId id,
        OperationId operation,
        IrBlockId target
    ) : base(id, IrInstructionKind.Goto, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        RepositoryParameterGuard.ThrowIfInvalid(target.IsDefault, nameof(target));
        Target = target;
    }

    public IrBlockId Target { get; }
}

internal sealed partial class IrReturnInstruction : IrInstruction, IIrTerminator
{
    IrInstruction IIrTerminator.Instruction => this;

    void IIrTerminator.Accept<TState>(IIrTerminatorVisitor<TState> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        visitor.Visit(this, state);
    }

    internal IrReturnInstruction(
        IrInstructionId id,
        OperationId operation,
        IrTerm? value
    ) : base(id, IrInstructionKind.Return, operation)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
        Value = value;
    }

    public IrTerm? Value { get; }
}
