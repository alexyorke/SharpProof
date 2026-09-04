namespace SharpProof.Ir;

public sealed class IrProgramBuilder(IrFactory factory)
{
    private static long s_nextScope;
    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));
    private readonly long _scope = Interlocked.Increment(ref s_nextScope);
    private readonly List<MutableBlock> _blocks = [];
    private int _nextInstruction;
    private IrBlockId? _entry;
    private bool _built;

    public IrBlockId CreateBlock(string? name = null)
    {
        EnsureMutable();
        var id = new IrBlockId(_scope, _blocks.Count);
        var nameId = string.IsNullOrWhiteSpace(name)
            ? (IrStringId?)null
            : _factory.InternString(name!);
        _blocks.Add(new MutableBlock(id, nameId));
        _entry ??= id;
        return id;
    }

    public void SetEntry(IrBlockId entry)
    {
        EnsureMutable();
        GetBlock(entry);
        _entry = entry;
    }

    public IrMemberLocation MemberLocation(IrMemberId member, IrTerm? receiver, params IrTerm[] arguments)
    {
        ArgumentNullGuard.NotNull(arguments, nameof(arguments));
        ImmutableArray<IrTerm> immutableArguments = [.. arguments];

        var memberInfo = _factory.GetMemberInfo(member);
        _factory.ValidateCallShape(
            memberInfo,
            receiver,
            immutableArguments,
            nameof(arguments));
        return new IrMemberLocation(
            memberInfo.ReturnType,
            member,
            receiver,
            immutableArguments);
    }

    public IrSequenceLocation SequenceLocation(IrTerm sequence, IrTerm index)
    {
        var elementType = _factory.ValidateSequenceTerms(
            sequence, index,
            "A sequence location requires a sequence term.",
            "A sequence location requires an integer index.",
            nameof(sequence), nameof(index));
        return new IrSequenceLocation(
            elementType, sequence, index);
    }

    public IrAssignInstruction Assign(IrBlockId block, OperationId operation, IrVarId target, IrTerm value)
    {
        return Append(block, id => new IrAssignInstruction(id, operation, target, value));
    }

    public IrLoadInstruction Load(IrBlockId block, OperationId operation, IrVarId target, IrLocation location)
    {
        return Append(block, id => new IrLoadInstruction(id, operation, target, location));
    }

    public IrStoreInstruction Store(IrBlockId block, OperationId operation, IrLocation location, IrTerm value)
    {
        return Append(block, id => new IrStoreInstruction(id, operation, location, value));
    }

    public IrCallInstruction Call(
        IrBlockId block,
        OperationId operation,
        IrVarId? target,
        IrMemberId member,
        IrTerm? receiver,
        params IrTerm[] arguments)
    {
        ArgumentNullGuard.NotNull(arguments, nameof(arguments));

        return Append(block, id => new IrCallInstruction(
            id, operation, target, member, receiver, [.. arguments]));
    }

    public IrAssumeInstruction Assume(IrBlockId block, OperationId operation, IrTerm condition)
    {
        return Append(block, id => new IrAssumeInstruction(id, operation, condition));
    }

    public IrAssertInstruction Assert(IrBlockId block, OperationId operation, IrTerm condition)
    {
        return Append(block, id => new IrAssertInstruction(id, operation, condition));
    }

    public IrHavocInstruction Havoc(
        IrBlockId block,
        OperationId operation,
        IrHavocKind havocKind,
        params IrVarId[] variables)
    {
        ArgumentNullGuard.NotNull(variables, nameof(variables));

        var distinct = variables
            .Distinct()
            .OrderBy(static variable => variable.Value)
            .ToImmutableArray();
        return Append(block,
            id => new IrHavocInstruction(id, operation, havocKind, distinct));
    }

    public IrBranchInstruction Branch(
        IrBlockId block, OperationId operation, IrTerm condition,
        IrBlockId whenTrue, IrBlockId whenFalse)
    {
        return Append(block, id => new IrBranchInstruction(
            id, operation, condition, whenTrue, whenFalse));
    }

    public IrGotoInstruction Goto(IrBlockId block, OperationId operation, IrBlockId target)
    {
        return Append(block, id => new IrGotoInstruction(id, operation, target));
    }

    public IrReturnInstruction Return(IrBlockId block, OperationId operation, IrTerm? value = null)
    {
        return Append(block, id => new IrReturnInstruction(id, operation, value));
    }

    public IrProgram Build()
    {
        EnsureMutable();
        if (_entry == null)
        {
            throw new InvalidOperationException(
                "A program must contain at least one block.");
        }

        var blocks = ImmutableArray.CreateBuilder<IrBasicBlock>(_blocks.Count);
        foreach (var block in _blocks)
        {
            if (block.Instructions.Count == 0 ||
                !block.Instructions[block.Instructions.Count - 1].IsTerminal)
            {
                throw new InvalidOperationException(
                    "Every program block must end in branch, goto, or return.");
            }

            blocks.Add(block.Freeze());
        }
        _built = true;
        return new IrProgram(
            _factory,
            _scope,
            _entry.Value,
            blocks.MoveToImmutable());
    }

    private T Append<T>(IrBlockId blockId, Func<IrInstructionId, T> create) where T : IrInstruction
    {
        EnsureMutable();
        var instruction = create(new IrInstructionId(_scope, _nextInstruction));
        ValidateInstruction(instruction);
        var block = GetBlock(blockId);
        if (block.Instructions.Count != 0 &&
            block.Instructions[block.Instructions.Count - 1].IsTerminal)
        {
            throw new InvalidOperationException(
                "No instruction can follow a block terminator.");
        }

        _nextInstruction++;
        block.Instructions.Add(instruction);
        return instruction;
    }

    private void ValidateInstruction(IrInstruction instruction)
    {
        ValidateOperation(instruction.Operation);
        switch (instruction)
        {
            case IrAssignInstruction value:
                RequireSameType(
                    _factory.GetVariableInfo(value.Target).Type, ValidateTerm(value.Value, "value"),
                    "The assigned value does not match the target type.", "value");
                break;
            case IrLoadInstruction value:
                RequireSameType(
                    _factory.GetVariableInfo(value.Target).Type, ValidateLocation(value.Location),
                    "The loaded location does not match the target type.", "location");
                break;
            case IrStoreInstruction value:
                RequireSameType(
                    ValidateLocation(value.Location), ValidateTerm(value.Value, "value"),
                    "The stored value does not match the location type.", "value");
                break;
            case IrCallInstruction value:
                var member = _factory.GetMemberInfo(value.Member);
                _factory.ValidateCallShape(
                    member, value.Receiver, value.Arguments, "arguments");
                if (value.Target.HasValue &&
                    _factory.GetVariableInfo(value.Target.Value).Type != member.ReturnType)
                {
                    throw InvalidArgument(
                        "The call result does not match the target type.", "target");
                }

                break;
            case IrAssumeInstruction or IrAssertInstruction:
                ValidateBoolean(instruction is IrAssumeInstruction assume
                    ? assume.Condition
                    : ((IrAssertInstruction)instruction).Condition, "condition");
                break;
            case IrHavocInstruction value:
                _ = ArgumentNullGuard.RequireDefined(
                    value.HavocKind,
                    "havocKind");

                var memoryOnly = value.HavocKind == IrHavocKind.Memory;
                if (memoryOnly != value.Variables.IsEmpty)
                {
                    throw InvalidArgument(memoryOnly
                        ? "Memory havoc cannot name variables."
                        : "Variable havoc requires at least one variable.", "variables");
                }

                foreach (var variableId in value.Variables)
                {
                    _factory.GetVariableInfo(variableId);
                }

                break;
            case IrBranchInstruction value:
                ValidateBoolean(value.Condition, "condition");
                GetBlock(value.WhenTrue);
                GetBlock(value.WhenFalse);
                break;
            case IrGotoInstruction value:
                GetBlock(value.Target);
                break;
            case IrReturnInstruction value:
                if (value.Value != null)
                {
                    ValidateTerm(value.Value, "value");
                }

                break;
            default:
                throw new ArgumentException(
                    "Unknown IR instruction kind.", nameof(instruction));
        }
    }

    private MutableBlock GetBlock(IrBlockId id)
    {
        if (id.Scope != _scope)
        {
            throw new ArgumentException(
                "The block identifier belongs to a different program builder.",
                nameof(id));
        }

        return _blocks[ArgumentNullGuard.RequireIndex(
            id.Value,
            _blocks.Count,
            nameof(id))];
    }

    private IrTypeId ValidateLocation(IrLocation location)
    {
        ArgumentNullGuard.NotNull(location, nameof(location));

        switch (location)
        {
            case IrMemberLocation member:
                var memberInfo = _factory.GetMemberInfo(member.Member);
                _factory.ValidateCallShape(
                    memberInfo, member.Receiver, member.Arguments, nameof(location));
                RequireSameType(
                    memberInfo.ReturnType, member.Type, "The member location has an invalid type.", nameof(location));
                break;
            case IrSequenceLocation sequence:
                var elementType = _factory.ValidateSequenceTerms(
                    sequence.Sequence, sequence.Index,
                    "The sequence location has an invalid type.",
                    "The sequence location has an invalid type.",
                    nameof(location), nameof(location));
                RequireSameType(
                    elementType, sequence.Type, "The sequence location has an invalid type.", nameof(location));
                break;
            default:
                throw new ArgumentException(
                    "Unknown IR location kind.",
                    nameof(location));
        }
        return location.Type;
    }

    private void ValidateBoolean(IrTerm condition, string parameterName)
    {
        _ = IrFactory.RequireBooleanTerm(
            _factory,
            condition,
            parameterName,
            "The condition must be boolean.");
    }

    private IrTypeId ValidateTerm(IrTerm term, string parameterName)
    {
        ArgumentNullGuard.NotNull(term, parameterName);

        _factory.EnsureTerm(term, parameterName);
        return term.Type;
    }

    private void ValidateOperation(OperationId operation)
    {
        _factory.GetOperationInfo(operation);
    }

    private static void RequireSameType(
        IrTypeId actual, IrTypeId expected, string detail, string parameterName)
    {
        if (actual != expected)
        {
            throw new ArgumentException(detail, parameterName);
        }
    }

    private static ArgumentException InvalidArgument(
        string detail, string parameterName)
    {
        return new(detail, parameterName);
    }

    private void EnsureMutable()
    {
        if (_built)
        {
            throw new InvalidOperationException(
                "The program builder has already been consumed.");
        }
    }

    private sealed class MutableBlock(IrBlockId id, IrStringId? name)
    {
        internal IrBlockId Id { get; } = id;
        internal IrStringId? Name { get; } = name;
        internal List<IrInstruction> Instructions { get; } = [];

        internal IrBasicBlock Freeze()
        {
            return new(Id, Name, [.. Instructions]);
        }
    }
}
