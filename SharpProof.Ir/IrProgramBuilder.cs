namespace SharpProof.Ir;

public sealed class IrProgramBuilder(IrFactory factory) {
    private static long s_nextScope;
    private readonly IrFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly long _scope = Interlocked.Increment(ref s_nextScope);
    private readonly List<MutableBlock> _blocks = [];
    private int _nextInstruction;
    private IrBlockId? _entry;
    private bool _built;

    public IrBlockId CreateBlock(string? name = null) {
        EnsureMutable();
        var id = new IrBlockId(_scope, _blocks.Count);
        var nameId = string.IsNullOrWhiteSpace(name)
            ? (IrStringId?)null
            : _factory.InternString(name!);
        _blocks.Add(new MutableBlock(id, nameId));
        _entry ??= id;
        return id;
    }

    public void SetEntry(IrBlockId entry) {
        EnsureMutable();
        GetBlock(entry);
        _entry = entry;
    }

    public IrMemberLocation MemberLocation(
        IrMemberId member,
        IrTerm? receiver,
        params IrTerm[] arguments) {
        if (arguments == null) throw new ArgumentNullException(nameof(arguments));
        var memberInfo = _factory.GetMemberInfo(member);
        ValidateCallShape(memberInfo, receiver, arguments, nameof(arguments));
        return new IrMemberLocation(
            memberInfo.ReturnType,
            member,
            receiver,
            [.. arguments]);
    }

    public IrSequenceLocation SequenceLocation(IrTerm sequence, IrTerm index) {
        if (sequence == null) throw new ArgumentNullException(nameof(sequence));
        if (index == null) throw new ArgumentNullException(nameof(index));
        _factory.EnsureTerm(sequence, nameof(sequence));
        _factory.EnsureTerm(index, nameof(index));
        var sequenceType = _factory.GetTypeInfo(sequence.Type);
        if (sequenceType.Kind != IrTypeKind.Sequence ||
            sequenceType.ElementType == null)
            throw new ArgumentException(
                "A sequence location requires a sequence term.",
                nameof(sequence));
        if (index.Type != _factory.IntegerType)
            throw new ArgumentException(
                "A sequence location requires an integer index.",
                nameof(index));
        return new IrSequenceLocation(
            sequenceType.ElementType.Value,
            sequence,
            index);
    }

    public IrAssignInstruction Assign(
        IrBlockId block,
        OperationId operation,
        IrVarId target,
        IrTerm value) {
        var variable = _factory.GetVariableInfo(target);
        ValidateOperation(operation);
        ValidateTerm(value, nameof(value));
        if (variable.Type != value.Type)
            throw new ArgumentException(
                "The assigned value does not match the target type.",
                nameof(value));
        return Append(
            block,
            id => new IrAssignInstruction(id, operation, target, value));
    }

    public IrLoadInstruction Load(
        IrBlockId block,
        OperationId operation,
        IrVarId target,
        IrLocation location) {
        var variable = _factory.GetVariableInfo(target);
        ValidateOperation(operation);
        ValidateLocation(location);
        if (variable.Type != location.Type)
            throw new ArgumentException(
                "The loaded location does not match the target type.",
                nameof(location));
        return Append(
            block,
            id => new IrLoadInstruction(id, operation, target, location));
    }

    public IrStoreInstruction Store(
        IrBlockId block,
        OperationId operation,
        IrLocation location,
        IrTerm value) {
        ValidateOperation(operation);
        ValidateLocation(location);
        ValidateTerm(value, nameof(value));
        if (location.Type != value.Type)
            throw new ArgumentException(
                "The stored value does not match the location type.",
                nameof(value));
        return Append(
            block,
            id => new IrStoreInstruction(id, operation, location, value));
    }

    public IrCallInstruction Call(
        IrBlockId block,
        OperationId operation,
        IrVarId? target,
        IrMemberId member,
        IrTerm? receiver,
        params IrTerm[] arguments) {
        if (arguments == null) throw new ArgumentNullException(nameof(arguments));
        ValidateOperation(operation);
        var memberInfo = _factory.GetMemberInfo(member);
        ValidateCallShape(memberInfo, receiver, arguments, nameof(arguments));
        if (target.HasValue &&
            _factory.GetVariableInfo(target.Value).Type != memberInfo.ReturnType)
            throw new ArgumentException(
                "The call result does not match the target type.",
                nameof(target));
        return Append(
            block,
            id => new IrCallInstruction(
                id,
                operation,
                target,
                member,
                receiver,
                [.. arguments]));
    }

    public IrAssumeInstruction Assume(
        IrBlockId block,
        OperationId operation,
        IrTerm condition) {
        ValidateBoolean(condition, nameof(condition));
        ValidateOperation(operation);
        return Append(
            block,
            id => new IrAssumeInstruction(id, operation, condition));
    }

    public IrAssertInstruction Assert(
        IrBlockId block,
        OperationId operation,
        IrTerm condition) {
        ValidateBoolean(condition, nameof(condition));
        ValidateOperation(operation);
        return Append(
            block,
            id => new IrAssertInstruction(id, operation, condition));
    }

    public IrHavocInstruction Havoc(
        IrBlockId block,
        OperationId operation,
        IrHavocKind havocKind,
        params IrVarId[] variables) {
        if (variables == null) throw new ArgumentNullException(nameof(variables));
        ValidateOperation(operation);
        if (!Enum.IsDefined(typeof(IrHavocKind), havocKind))
            throw new ArgumentOutOfRangeException(nameof(havocKind));
        if (havocKind == IrHavocKind.Memory && variables.Length != 0)
            throw new ArgumentException(
                "Memory havoc cannot name variables.",
                nameof(variables));
        if (havocKind != IrHavocKind.Memory && variables.Length == 0)
            throw new ArgumentException(
                "Variable havoc requires at least one variable.",
                nameof(variables));
        foreach (var variable in variables) _factory.GetVariableInfo(variable);
        var distinct = variables
            .Distinct()
            .OrderBy(static variable => variable.Value)
            .ToImmutableArray();
        return Append(
            block,
            id => new IrHavocInstruction(
                id,
                operation,
                havocKind,
                distinct));
    }

    public IrBranchInstruction Branch(
        IrBlockId block,
        OperationId operation,
        IrTerm condition,
        IrBlockId whenTrue,
        IrBlockId whenFalse) {
        ValidateBoolean(condition, nameof(condition));
        ValidateOperation(operation);
        GetBlock(whenTrue);
        GetBlock(whenFalse);
        return Append(
            block,
            id => new IrBranchInstruction(
                id,
                operation,
                condition,
                whenTrue,
                whenFalse));
    }

    public IrGotoInstruction Goto(
        IrBlockId block,
        OperationId operation,
        IrBlockId target) {
        ValidateOperation(operation);
        GetBlock(target);
        return Append(
            block,
            id => new IrGotoInstruction(id, operation, target));
    }

    public IrReturnInstruction Return(
        IrBlockId block,
        OperationId operation,
        IrTerm? value = null) {
        ValidateOperation(operation);
        if (value != null) ValidateTerm(value, nameof(value));
        return Append(
            block,
            id => new IrReturnInstruction(id, operation, value));
    }

    public IrProgram Build() {
        EnsureMutable();
        if (_entry == null)
            throw new InvalidOperationException(
                "A program must contain at least one block.");
        foreach (var block in _blocks) {
            if (block.Instructions.Count == 0 ||
                !block.Instructions[block.Instructions.Count - 1].IsTerminal)
                throw new InvalidOperationException(
                    "Every program block must end in branch, goto, or return.");
        }
        _built = true;
        return new IrProgram(
            _factory,
            _scope,
            _entry.Value,
            [.. _blocks.Select(static block => block.Freeze())]);
    }

    private T Append<T>(IrBlockId blockId, Func<IrInstructionId, T> create)
        where T : IrInstruction {
        EnsureMutable();
        var block = GetBlock(blockId);
        if (block.Instructions.Count != 0 &&
            block.Instructions[block.Instructions.Count - 1].IsTerminal)
            throw new InvalidOperationException(
                "No instruction can follow a block terminator.");
        var instruction = create(
            new IrInstructionId(_scope, _nextInstruction++));
        block.Instructions.Add(instruction);
        return instruction;
    }

    private MutableBlock GetBlock(IrBlockId id) {
        if (id.Scope != _scope)
            throw new ArgumentException(
                "The block identifier belongs to a different program builder.",
                nameof(id));
        if (id.Value < 0 || id.Value >= _blocks.Count)
            throw new ArgumentOutOfRangeException(nameof(id));
        return _blocks[id.Value];
    }

    private void ValidateLocation(IrLocation location) {
        if (location == null) throw new ArgumentNullException(nameof(location));
        _factory.GetTypeInfo(location.Type);
        switch (location) {
            case IrMemberLocation member:
                var memberInfo = _factory.GetMemberInfo(member.Member);
                ValidateCallShape(
                    memberInfo,
                    member.Receiver,
                    member.Arguments,
                    nameof(location));
                if (memberInfo.ReturnType != member.Type)
                    throw new ArgumentException(
                        "The member location has an invalid type.",
                        nameof(location));
                break;
            case IrSequenceLocation sequence:
                ValidateTerm(sequence.Sequence, nameof(location));
                ValidateTerm(sequence.Index, nameof(location));
                var sequenceInfo = _factory.GetTypeInfo(sequence.Sequence.Type);
                if (sequenceInfo.Kind != IrTypeKind.Sequence ||
                    sequenceInfo.ElementType != sequence.Type ||
                    sequence.Index.Type != _factory.IntegerType)
                    throw new ArgumentException(
                        "The sequence location has an invalid type.",
                        nameof(location));
                break;
            default:
                throw new ArgumentException(
                    "Unknown IR location kind.",
                    nameof(location));
        }
    }

    private void ValidateCallShape(
        IrMemberInfo member,
        IrTerm? receiver,
        IReadOnlyList<IrTerm> arguments,
        string parameterName) {
        if (member.IsStatic != (receiver == null))
            throw new ArgumentException(
                member.IsStatic
                    ? "A static member cannot have a receiver."
                    : "An instance member requires a receiver.",
                parameterName);
        if (receiver != null) {
            ValidateTerm(receiver, parameterName);
            if (receiver.Type != member.DeclaringType)
                throw new ArgumentException(
                    "An instance receiver must match the member declaring type.",
                    parameterName);
        }
        if (arguments.Count != member.ParameterTypes.Length)
            throw new ArgumentException(
                "The argument count does not match the member signature.",
                parameterName);
        for (var index = 0; index < arguments.Count; index++) {
            var argument = arguments[index] ??
                           throw new ArgumentException(
                               "Arguments cannot contain null.",
                               parameterName);
            ValidateTerm(argument, parameterName);
            if (argument.Type != member.ParameterTypes[index])
                throw new ArgumentException(
                    "An argument does not match the member signature.",
                    parameterName);
        }
    }

    private void ValidateBoolean(IrTerm condition, string parameterName) {
        ValidateTerm(condition, parameterName);
        if (condition.Type != _factory.BooleanType)
            throw new ArgumentException(
                "The condition must be boolean.",
                parameterName);
    }

    private void ValidateTerm(IrTerm term, string parameterName) {
        if (term == null) throw new ArgumentNullException(parameterName);
        _factory.EnsureTerm(term, parameterName);
    }

    private void ValidateOperation(OperationId operation) =>
        _factory.GetOperationInfo(operation);

    private void EnsureMutable() {
        if (_built)
            throw new InvalidOperationException(
                "The program builder has already been consumed.");
    }

    private sealed class MutableBlock(IrBlockId id, IrStringId? name) {
        internal IrBlockId Id { get; } = id;
        internal IrStringId? Name { get; } = name;
        internal List<IrInstruction> Instructions { get; } = [];

        internal IrBasicBlock Freeze() =>
            new(Id, Name, [.. Instructions]);
    }
}
