namespace SharpProof.Ir;

public enum IrTypeKind {
    Boolean,
    Integer,
    String,
    Reference,
    Sequence
}

public enum IrTermKind {
    Boolean,
    Integer,
    String,
    Null,
    Variable,
    Opaque,
    Unary,
    Binary,
    Conditional,
    Cast,
    Length,
    SequenceAccess
}

public enum IrUnaryOperator {
    Not,
    Negate
}

public enum IrBinaryOperator {
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder,
    AndAlso,
    OrElse,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    StringConcat
}

public enum IrOpaquePurity {
    Pure,
    Impure
}

public sealed class IrTypeInfo {
    internal IrTypeInfo(IrTypeId id, IrStringId name, IrTypeKind kind, IrTypeId? elementType) {
        Id = id;
        Name = name;
        Kind = kind;
        ElementType = elementType;
    }

    public IrTypeId Id { get; }
    public IrStringId Name { get; }
    public IrTypeKind Kind { get; }
    public IrTypeId? ElementType { get; }
}

public sealed class IrVariableInfo {
    internal IrVariableInfo(IrVarId id, IrStringId name, IrTypeId type) {
        Id = id;
        Name = name;
        Type = type;
    }

    public IrVarId Id { get; }
    public IrStringId Name { get; }
    public IrTypeId Type { get; }
}

public sealed class IrMemberInfo {
    internal IrMemberInfo(
        IrMemberId id,
        IrTypeId declaringType,
        IrStringId name,
        IrTypeId returnType,
        bool isStatic,
        ImmutableArray<IrTypeId> parameterTypes) {
        Id = id;
        DeclaringType = declaringType;
        Name = name;
        ReturnType = returnType;
        IsStatic = isStatic;
        ParameterTypes = parameterTypes;
    }

    public IrMemberId Id { get; }
    public IrTypeId DeclaringType { get; }
    public IrStringId Name { get; }
    public IrTypeId ReturnType { get; }
    public bool IsStatic { get; }
    public ImmutableArray<IrTypeId> ParameterTypes { get; }
}

public sealed class IrOperationInfo {
    internal IrOperationInfo(OperationId id, IrStringId? description) {
        Id = id;
        Description = description;
    }

    public OperationId Id { get; }
    public IrStringId? Description { get; }
}

public abstract class IrTerm {
    private protected IrTerm(IrId id, IrTypeId type, IrTermKind kind) {
        Id = id;
        Type = type;
        Kind = kind;
    }

    public IrId Id { get; }
    public IrTypeId Type { get; }
    public IrTermKind Kind { get; }
}

public sealed class IrBooleanTerm : IrTerm {
    internal IrBooleanTerm(IrId id, IrTypeId type, bool value)
        : base(id, type, IrTermKind.Boolean) => Value = value;

    public bool Value { get; }
}

public sealed class IrIntegerTerm : IrTerm {
    internal IrIntegerTerm(IrId id, IrTypeId type, long value)
        : base(id, type, IrTermKind.Integer) => Value = value;

    public long Value { get; }
}

public sealed class IrStringTerm : IrTerm {
    internal IrStringTerm(IrId id, IrTypeId type, IrStringId value)
        : base(id, type, IrTermKind.String) => Value = value;

    public IrStringId Value { get; }
}

public sealed class IrNullTerm : IrTerm {
    internal IrNullTerm(IrId id, IrTypeId type)
        : base(id, type, IrTermKind.Null) {
    }
}

public sealed class IrVariableTerm : IrTerm {
    internal IrVariableTerm(IrId id, IrTypeId type, IrVarId variable)
        : base(id, type, IrTermKind.Variable) => Variable = variable;

    public IrVarId Variable { get; }
}

public sealed class IrOpaqueTerm : IrTerm {
    internal IrOpaqueTerm(
        IrId id,
        IrTypeId type,
        IrMemberId member,
        IrTerm? receiver,
        ImmutableArray<IrTerm> arguments,
        IrOpaquePurity purity,
        OperationId operation)
        : base(id, type, IrTermKind.Opaque) {
        Member = member;
        Receiver = receiver;
        Arguments = arguments;
        Purity = purity;
        Operation = operation;
    }

    public IrMemberId Member { get; }
    public IrTerm? Receiver { get; }
    public ImmutableArray<IrTerm> Arguments { get; }
    public IrOpaquePurity Purity { get; }
    public OperationId Operation { get; }
}

public sealed class IrUnaryTerm : IrTerm {
    internal IrUnaryTerm(IrId id, IrTypeId type, IrUnaryOperator @operator, IrTerm operand)
        : base(id, type, IrTermKind.Unary) {
        Operator = @operator;
        Operand = operand;
    }

    public IrUnaryOperator Operator { get; }
    public IrTerm Operand { get; }
}

public sealed class IrBinaryTerm : IrTerm {
    internal IrBinaryTerm(
        IrId id,
        IrTypeId type,
        IrBinaryOperator @operator,
        IrTerm left,
        IrTerm right)
        : base(id, type, IrTermKind.Binary) {
        Operator = @operator;
        Left = left;
        Right = right;
    }

    public IrBinaryOperator Operator { get; }
    public IrTerm Left { get; }
    public IrTerm Right { get; }
}

public sealed class IrConditionalTerm : IrTerm {
    internal IrConditionalTerm(
        IrId id,
        IrTypeId type,
        IrTerm condition,
        IrTerm whenTrue,
        IrTerm whenFalse)
        : base(id, type, IrTermKind.Conditional) {
        Condition = condition;
        WhenTrue = whenTrue;
        WhenFalse = whenFalse;
    }

    public IrTerm Condition { get; }
    public IrTerm WhenTrue { get; }
    public IrTerm WhenFalse { get; }
}

public sealed class IrCastTerm : IrTerm {
    internal IrCastTerm(IrId id, IrTypeId type, IrTerm operand)
        : base(id, type, IrTermKind.Cast) => Operand = operand;

    public IrTerm Operand { get; }
}

public sealed class IrLengthTerm : IrTerm {
    internal IrLengthTerm(IrId id, IrTypeId type, IrTerm value)
        : base(id, type, IrTermKind.Length) => Value = value;

    public IrTerm Value { get; }
}

public sealed class IrSequenceAccessTerm : IrTerm {
    internal IrSequenceAccessTerm(IrId id, IrTypeId type, IrTerm sequence, IrTerm index)
        : base(id, type, IrTermKind.SequenceAccess) {
        Sequence = sequence;
        Index = index;
    }

    public IrTerm Sequence { get; }
    public IrTerm Index { get; }
}
