namespace SharpProof.Ir;

internal interface IIrTermVisitor<TState, TResult>
{
    TResult Visit(IrBooleanTerm term, TState state);
    TResult Visit(IrIntegerTerm term, TState state);
    TResult Visit(IrStringTerm term, TState state);
    TResult Visit(IrNullTerm term, TState state);
    TResult Visit(IrEmptyArrayTerm term, TState state);
    TResult Visit(IrVariableTerm term, TState state);
    TResult Visit(IrOpaqueTerm term, TState state);
    TResult Visit(IrUnaryTerm term, TState state);
    TResult Visit(IrBinaryTerm term, TState state);
    TResult Visit(IrConditionalTerm term, TState state);
    TResult Visit(IrCastTerm term, TState state);
    TResult Visit(IrLengthTerm term, TState state);
    TResult Visit(IrSequenceAccessTerm term, TState state);
}

internal abstract class IrTerm
{
    internal abstract TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState state);

    private protected IrTerm(IrId id, IrTypeId type, IrTermKind kind)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        (Id, Type, Kind) =
            (id, type, kind);
    }

    public IrId Id { get; }

    public IrTypeId Type { get; }

    public IrTermKind Kind { get; }
}

internal sealed class IrBooleanTerm : IrTerm
{
    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrBooleanTerm(IrId id, IrTypeId type, bool value) : base(id, type, IrTermKind.Boolean)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        Value = value;
    }

    public bool Value { get; }
}

internal sealed class IrIntegerTerm : IrTerm
{
    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrIntegerTerm(IrId id, IrTypeId type, IrInteger value) : base(id, type, IrTermKind.Integer)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        Integer = value;
    }

    internal IrInteger Integer { get; }

    public long Value => Integer.Int64;

    public ulong Bits => Integer.Bits;

    public int Width => Integer.Width;

    public bool Signed => Integer.Signed;
}

internal sealed class IrStringTerm : IrTerm
{
    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrStringTerm(IrId id, IrTypeId type, IrStringId value) : base(id, type, IrTermKind.String)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        RepositoryParameterGuard.ThrowIfInvalid(value.IsDefault, nameof(value));
        Value = value;
    }

    public IrStringId Value { get; }
}

internal sealed class IrNullTerm : IrTerm
{
    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrNullTerm(IrId id, IrTypeId type) : base(id, type, IrTermKind.Null)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
    }
}

internal sealed class IrEmptyArrayTerm : IrTerm
{
    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrEmptyArrayTerm(IrId id, IrTypeId type) : base(id, type, IrTermKind.EmptyArray)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
    }
}

internal sealed class IrVariableTerm : IrTerm
{
    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrVariableTerm(IrId id, IrTypeId type, IrVarId variable) : base(id, type, IrTermKind.Variable)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        RepositoryParameterGuard.ThrowIfInvalid(variable.IsDefault, nameof(variable));
        Variable = variable;
    }

    public IrVarId Variable { get; }
}

internal sealed class IrOpaqueTerm : IrTerm
{
    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrOpaqueTerm(
        IrId id,
        IrTypeId type,
        IrMemberId member,
        IrTerm? receiver,
        ImmutableArray<IrTerm> arguments,
        IrOpaquePurity purity,
        OperationId operation /* SPINT001: all values valid - default marks an opaque term with no source operation */
    ) : base(id, type, IrTermKind.Opaque)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        RepositoryParameterGuard.ThrowIfInvalid(member.IsDefault, nameof(member));
        RepositoryParameterGuard.RequireInitialized(arguments.IsDefault, nameof(arguments), "Array must be initialized.");
        (Member, Receiver, Arguments, Purity, Operation) =
            (member, receiver, arguments, purity, operation);
    }

    public IrMemberId Member { get; }

    public IrTerm? Receiver { get; }

    public ImmutableArray<IrTerm> Arguments { get; }

    public IrOpaquePurity Purity { get; }

    public OperationId Operation { get; }
}

internal abstract class IrUnaryOperatorInfo
{
    private protected IrUnaryOperatorInfo((int Key, IrTypeKind Operand, string Token) metadata)
    {
        Metadata = metadata;
    }

    internal (int Key, IrTypeKind Operand, string Token) Metadata { get; }

    internal abstract IrEvaluationResult Evaluate((IrInterpreter Interpreter, IrValue Value) state);
}

internal sealed class IrNotOperatorInfo : IrUnaryOperatorInfo
{
    internal static IrNotOperatorInfo Instance { get; } = new();

    private IrNotOperatorInfo() : base((0, IrTypeKind.Boolean, "!")) { }

    internal override IrEvaluationResult Evaluate((IrInterpreter Interpreter, IrValue Value) state)
    {
        return IrInterpreter.EvaluateUnaryOperator(this, state);
    }
}

internal sealed class IrNegateOperatorInfo : IrUnaryOperatorInfo
{
    internal static IrNegateOperatorInfo Instance { get; } = new();

    private IrNegateOperatorInfo() : base((1, IrTypeKind.Integer, "-")) { }

    internal override IrEvaluationResult Evaluate((IrInterpreter Interpreter, IrValue Value) state)
    {
        return IrInterpreter.EvaluateUnaryOperator(this, state);
    }
}

internal sealed class IrUnaryTerm : IrTerm
{
    private readonly IrUnaryOperatorInfo _operatorInfo;

    internal IrEvaluationResult EvaluateOperator((IrInterpreter Interpreter, IrValue Value) state)
    {
        return _operatorInfo.Evaluate(state);
    }

    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrUnaryTerm(
        IrId id,
        IrTypeId type,
        IrUnaryOperator @operator,
        IrTerm operand
    ) : base(id, type, IrTermKind.Unary)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        operand = RepositoryParameterGuard.NotNull(operand, nameof(operand));
        (Operator, Operand) =
            (@operator, operand);
        _operatorInfo = IrOperatorCatalog.Resolve(@operator);
    }

    public IrUnaryOperator Operator { get; }

    public IrTerm Operand { get; }
}

internal abstract class IrBinaryOperatorInfo
{
    private protected IrBinaryOperatorInfo((int Key, IrTypeKind? Operand, IrTypeKind Result, string Token) metadata)
    {
        Metadata = metadata;
    }

    internal (int Key, IrTypeKind? Operand, IrTypeKind Result, string Token) Metadata { get; }

    internal abstract IrEvaluationResult Evaluate((IrInterpreter Interpreter, IrValue Left, IrValue Right) state);
}

internal sealed class IrIntegerBinaryOperatorInfo : IrBinaryOperatorInfo
{
    internal static IrIntegerBinaryOperatorInfo Add { get; } = new(IrBinaryOperator.Add, (0, IrTypeKind.Integer, IrTypeKind.Integer, "+"));
    internal static IrIntegerBinaryOperatorInfo Subtract { get; } = new(IrBinaryOperator.Subtract, (1, IrTypeKind.Integer, IrTypeKind.Integer, "-"));
    internal static IrIntegerBinaryOperatorInfo Multiply { get; } = new(IrBinaryOperator.Multiply, (2, IrTypeKind.Integer, IrTypeKind.Integer, "*"));
    internal static IrIntegerBinaryOperatorInfo Divide { get; } = new(IrBinaryOperator.Divide, (3, IrTypeKind.Integer, IrTypeKind.Integer, "/"));
    internal static IrIntegerBinaryOperatorInfo Remainder { get; } = new(IrBinaryOperator.Remainder, (4, IrTypeKind.Integer, IrTypeKind.Integer, "%"));
    internal static IrIntegerBinaryOperatorInfo LessThan { get; } = new(IrBinaryOperator.LessThan, (9, IrTypeKind.Integer, IrTypeKind.Boolean, "<"));
    internal static IrIntegerBinaryOperatorInfo LessThanOrEqual { get; } = new(IrBinaryOperator.LessThanOrEqual, (10, IrTypeKind.Integer, IrTypeKind.Boolean, "<="));
    internal static IrIntegerBinaryOperatorInfo GreaterThan { get; } = new(IrBinaryOperator.GreaterThan, (11, IrTypeKind.Integer, IrTypeKind.Boolean, ">"));
    internal static IrIntegerBinaryOperatorInfo GreaterThanOrEqual { get; } = new(IrBinaryOperator.GreaterThanOrEqual, (12, IrTypeKind.Integer, IrTypeKind.Boolean, ">="));
    internal static IrIntegerBinaryOperatorInfo BitwiseAnd { get; } = new(IrBinaryOperator.BitwiseAnd, (15, IrTypeKind.Integer, IrTypeKind.Integer, "&"));
    internal static IrIntegerBinaryOperatorInfo ShiftLeft { get; } = new(IrBinaryOperator.ShiftLeft, (16, IrTypeKind.Integer, IrTypeKind.Integer, "<<"));
    internal static IrIntegerBinaryOperatorInfo ShiftRight { get; } = new(IrBinaryOperator.ShiftRight, (17, IrTypeKind.Integer, IrTypeKind.Integer, ">>"));
    internal static IrIntegerBinaryOperatorInfo BitwiseOr { get; } = new(IrBinaryOperator.BitwiseOr, (18, IrTypeKind.Integer, IrTypeKind.Integer, "|"));
    internal static IrIntegerBinaryOperatorInfo BitwiseXor { get; } = new(IrBinaryOperator.BitwiseXor, (19, IrTypeKind.Integer, IrTypeKind.Integer, "^"));

    private IrIntegerBinaryOperatorInfo(IrBinaryOperator @operator, (int Key, IrTypeKind? Operand, IrTypeKind Result, string Token) metadata)
        : base(metadata)
    {
        Operator = @operator;
    }

    internal IrBinaryOperator Operator { get; }

    internal override IrEvaluationResult Evaluate((IrInterpreter Interpreter, IrValue Left, IrValue Right) state)
    {
        return IrInterpreter.EvaluateBinaryOperator(this, state);
    }
}

internal sealed class IrBooleanBinaryOperatorInfo : IrBinaryOperatorInfo
{
    internal static IrBooleanBinaryOperatorInfo AndAlso { get; } = new((5, IrTypeKind.Boolean, IrTypeKind.Boolean, "&&"));
    internal static IrBooleanBinaryOperatorInfo OrElse { get; } = new((6, IrTypeKind.Boolean, IrTypeKind.Boolean, "||"));

    private IrBooleanBinaryOperatorInfo((int Key, IrTypeKind? Operand, IrTypeKind Result, string Token) metadata)
        : base(metadata) { }

    internal override IrEvaluationResult Evaluate((IrInterpreter Interpreter, IrValue Left, IrValue Right) state)
    {
        return IrInterpreter.EvaluateBinaryOperator(this, state);
    }
}

internal sealed class IrEqualityBinaryOperatorInfo : IrBinaryOperatorInfo
{
    internal static IrEqualityBinaryOperatorInfo Equal { get; } = new(false, (7, null, IrTypeKind.Boolean, "=="));
    internal static IrEqualityBinaryOperatorInfo NotEqual { get; } = new(true, (8, null, IrTypeKind.Boolean, "!="));

    private IrEqualityBinaryOperatorInfo(bool negate, (int Key, IrTypeKind? Operand, IrTypeKind Result, string Token) metadata)
        : base(metadata)
    {
        Negate = negate;
    }

    internal bool Negate { get; }

    internal override IrEvaluationResult Evaluate((IrInterpreter Interpreter, IrValue Left, IrValue Right) state)
    {
        return IrInterpreter.EvaluateBinaryOperator(this, state);
    }
}

internal sealed class IrStringConcatBinaryOperatorInfo : IrBinaryOperatorInfo
{
    internal static IrStringConcatBinaryOperatorInfo Instance { get; } = new((13, IrTypeKind.String, IrTypeKind.String, "++"));

    private IrStringConcatBinaryOperatorInfo((int Key, IrTypeKind? Operand, IrTypeKind Result, string Token) metadata)
        : base(metadata) { }

    internal override IrEvaluationResult Evaluate((IrInterpreter Interpreter, IrValue Left, IrValue Right) state)
    {
        return IrInterpreter.EvaluateBinaryOperator(this, state);
    }
}

internal sealed class IrStringEqualsBinaryOperatorInfo : IrBinaryOperatorInfo
{
    internal static IrStringEqualsBinaryOperatorInfo Instance { get; } = new((14, IrTypeKind.String, IrTypeKind.Boolean, "string=="));

    private IrStringEqualsBinaryOperatorInfo((int Key, IrTypeKind? Operand, IrTypeKind Result, string Token) metadata)
        : base(metadata) { }

    internal override IrEvaluationResult Evaluate((IrInterpreter Interpreter, IrValue Left, IrValue Right) state)
    {
        return IrInterpreter.EvaluateBinaryOperator(this, state);
    }
}

internal sealed class IrBinaryTerm : IrTerm
{
    private readonly IrBinaryOperatorInfo _operatorInfo;

    internal IrEvaluationResult EvaluateOperator((IrInterpreter Interpreter, IrValue Left, IrValue Right) state)
    {
        return _operatorInfo.Evaluate(state);
    }

    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrBinaryTerm(
        IrId id,
        IrTypeId type,
        IrBinaryOperator @operator,
        IrTerm left,
        IrTerm right
    ) : base(id, type, IrTermKind.Binary)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        (Operator, Left, Right) =
            (@operator, left, right);
        _operatorInfo = IrOperatorCatalog.Resolve(@operator);
    }

    public IrBinaryOperator Operator { get; }

    public IrTerm Left { get; }

    public IrTerm Right { get; }
}

internal sealed class IrConditionalTerm : IrTerm
{
    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrConditionalTerm(
        IrId id,
        IrTypeId type,
        IrTerm condition,
        IrTerm whenTrue,
        IrTerm whenFalse
    ) : base(id, type, IrTermKind.Conditional)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        condition = RepositoryParameterGuard.NotNull(condition, nameof(condition));
        whenTrue = RepositoryParameterGuard.NotNull(whenTrue, nameof(whenTrue));
        whenFalse = RepositoryParameterGuard.NotNull(whenFalse, nameof(whenFalse));
        (Condition, WhenTrue, WhenFalse) =
            (condition, whenTrue, whenFalse);
    }

    public IrTerm Condition { get; }

    public IrTerm WhenTrue { get; }

    public IrTerm WhenFalse { get; }
}

internal sealed class IrCastTerm : IrTerm
{
    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrCastTerm(IrId id, IrTypeId type, IrTerm operand) : base(id, type, IrTermKind.Cast)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        operand = RepositoryParameterGuard.NotNull(operand, nameof(operand));
        Operand = operand;
    }

    public IrTerm Operand { get; }
}

internal sealed class IrLengthTerm : IrTerm
{
    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrLengthTerm(IrId id, IrTypeId type, IrTerm value) : base(id, type, IrTermKind.Length)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        Value = value;
    }

    public IrTerm Value { get; }
}

internal sealed class IrSequenceAccessTerm : IrTerm
{
    internal override TResult Accept<TState, TResult>(IIrTermVisitor<TState, TResult> visitor, TState /* SPINT001: all values valid - visitor state passes through unchanged */ state)
    {
        visitor = RepositoryParameterGuard.NotNull(visitor, nameof(visitor));
        return visitor.Visit(this, state);
    }


    internal IrSequenceAccessTerm(
        IrId id,
        IrTypeId type,
        IrTerm sequence,
        IrTerm index
    ) : base(id, type, IrTermKind.SequenceAccess)
    {
        RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        sequence = RepositoryParameterGuard.NotNull(sequence, nameof(sequence));
        index = RepositoryParameterGuard.NotNull(index, nameof(index));
        (Sequence, Index) =
            (sequence, index);
    }

    public IrTerm Sequence { get; }

    public IrTerm Index { get; }
}
