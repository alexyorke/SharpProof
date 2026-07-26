namespace SharpProof.Ir;

public enum IrValueKind {
    Boolean,
    Integer,
    String,
    Null,
    Reference,
    Sequence
}

public enum IrEvaluationStatus {
    Value,
    Unsupported,
    Exception
}

public enum IrUnsupportedReason {
    OpaqueTerm,
    MissingVariable,
    InvalidVariableValue,
    UnsupportedCast,
    UnsupportedOperation
}

public enum IrExceptionKind {
    DivideByZero,
    Overflow,
    NullReference,
    IndexOutOfRange,
    InvalidCast
}

public sealed class IrValue {
    private readonly object? _value;

    private IrValue(IrTypeId type, IrValueKind kind, object? value) {
        Type = type;
        Kind = kind;
        _value = value;
    }

    public IrTypeId Type { get; }
    public IrValueKind Kind { get; }
    public bool Boolean => Kind == IrValueKind.Boolean
        ? (bool)_value!
        : throw new InvalidOperationException("The IR value is not boolean.");
    public long Integer => Kind == IrValueKind.Integer
        ? (long)_value!
        : throw new InvalidOperationException("The IR value is not an integer.");
    public string String => Kind == IrValueKind.String
        ? (string)_value!
        : throw new InvalidOperationException("The IR value is not a string.");
    public object Reference => Kind == IrValueKind.Reference
        ? _value!
        : throw new InvalidOperationException("The IR value is not a reference.");
    public ImmutableArray<IrValue> Elements => Kind == IrValueKind.Sequence
        ? (ImmutableArray<IrValue>)_value!
        : throw new InvalidOperationException("The IR value is not a sequence.");

    internal static IrValue CreateBoolean(IrTypeId type, bool value) =>
        new(type, IrValueKind.Boolean, value);

    internal static IrValue CreateInteger(IrTypeId type, long value) =>
        new(type, IrValueKind.Integer, value);

    internal static IrValue CreateString(IrTypeId type, string value) =>
        new(type, IrValueKind.String, value);

    internal static IrValue CreateNull(IrTypeId type) =>
        new(type, IrValueKind.Null, null);

    internal static IrValue CreateReference(IrTypeId type, object value) =>
        new(type, IrValueKind.Reference, value);

    internal static IrValue CreateSequence(IrTypeId type, ImmutableArray<IrValue> elements) =>
        new(type, IrValueKind.Sequence, elements);
}

public sealed class IrUnsupportedInfo {
    internal IrUnsupportedInfo(IrUnsupportedReason reason, string detail) {
        Reason = reason;
        Detail = detail;
    }

    public IrUnsupportedReason Reason { get; }
    public string Detail { get; }
}

public sealed class IrExceptionInfo {
    internal IrExceptionInfo(IrExceptionKind kind, string detail) {
        Kind = kind;
        Detail = detail;
    }

    public IrExceptionKind Kind { get; }
    public string Detail { get; }
}

public sealed class IrEvaluationResult {
    private IrEvaluationResult(
        IrEvaluationStatus status,
        IrValue? value,
        IrUnsupportedInfo? unsupported,
        IrExceptionInfo? exception) {
        Status = status;
        Value = value;
        Unsupported = unsupported;
        Exception = exception;
    }

    public IrEvaluationStatus Status { get; }
    public IrValue? Value { get; }
    public IrUnsupportedInfo? Unsupported { get; }
    public IrExceptionInfo? Exception { get; }

    internal static IrEvaluationResult FromValue(IrValue value) =>
        new(IrEvaluationStatus.Value, value, null, null);

    internal static IrEvaluationResult FromUnsupported(IrUnsupportedReason reason, string detail) =>
        new(IrEvaluationStatus.Unsupported, null, new IrUnsupportedInfo(reason, detail), null);

    internal static IrEvaluationResult FromException(IrExceptionKind kind, string detail) =>
        new(IrEvaluationStatus.Exception, null, null, new IrExceptionInfo(kind, detail));
}

public sealed class IrInterpreter(IrFactory factory) {
    private static readonly IReadOnlyDictionary<IrVarId, IrValue> EmptyEnvironment =
        ImmutableDictionary<IrVarId, IrValue>.Empty;
    private readonly IrFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public IrEvaluationResult Evaluate(
        IrTerm term,
        IReadOnlyDictionary<IrVarId, IrValue>? variables = null) {
        if (term == null) throw new ArgumentNullException(nameof(term));
        _factory.EnsureTerm(term, nameof(term));
        return EvaluateCore(term, variables ?? EmptyEnvironment);
    }

    private IrEvaluationResult EvaluateCore(
        IrTerm term,
        IReadOnlyDictionary<IrVarId, IrValue> variables) {
        switch (term) {
            case IrBooleanTerm boolean:
                return IrEvaluationResult.FromValue(_factory.CreateBooleanValue(boolean.Value));
            case IrIntegerTerm integer:
                return IrEvaluationResult.FromValue(_factory.CreateIntegerValue(integer.Value));
            case IrStringTerm text:
                return IrEvaluationResult.FromValue(_factory.CreateStringValue(_factory.GetString(text.Value)));
            case IrNullTerm:
                return IrEvaluationResult.FromValue(_factory.CreateNullValue(term.Type));
            case IrVariableTerm variable:
                return EvaluateVariable(variable, variables);
            case IrOpaqueTerm opaque:
                return EvaluateOpaque(opaque, variables);
            case IrUnaryTerm unary:
                return EvaluateUnary(unary, variables);
            case IrBinaryTerm binary:
                return EvaluateBinary(binary, variables);
            case IrConditionalTerm conditional:
                return EvaluateConditional(conditional, variables);
            case IrCastTerm cast:
                return EvaluateCast(cast, variables);
            case IrLengthTerm length:
                return EvaluateLength(length, variables);
            case IrSequenceAccessTerm access:
                return EvaluateSequenceAccess(access, variables);
            default:
                return IrEvaluationResult.FromUnsupported(
                    IrUnsupportedReason.UnsupportedOperation,
                    "Unknown IR term kind: " + term.Kind + ".");
        }
    }

    private IrEvaluationResult EvaluateVariable(
        IrVariableTerm variable,
        IReadOnlyDictionary<IrVarId, IrValue> variables) {
        if (!variables.TryGetValue(variable.Variable, out var value))
            return IrEvaluationResult.FromUnsupported(
                IrUnsupportedReason.MissingVariable,
                "No value was supplied for " + variable.Variable + ".");
        if (value == null || value.Type != variable.Type)
            return IrEvaluationResult.FromUnsupported(
                IrUnsupportedReason.InvalidVariableValue,
                "The supplied value has the wrong type for " + variable.Variable + ".");
        return IrEvaluationResult.FromValue(value);
    }

    private IrEvaluationResult EvaluateOpaque(
        IrOpaqueTerm opaque,
        IReadOnlyDictionary<IrVarId, IrValue> variables) {
        IrValue? receiverValue = null;
        if (opaque.Receiver != null) {
            var receiver = EvaluateCore(opaque.Receiver, variables);
            if (receiver.Status != IrEvaluationStatus.Value) return receiver;
            receiverValue = receiver.Value;
        }
        foreach (var argument in opaque.Arguments) {
            var argumentResult = EvaluateCore(argument, variables);
            if (argumentResult.Status != IrEvaluationStatus.Value) return argumentResult;
        }
        if (receiverValue?.Kind == IrValueKind.Null)
            return IrEvaluationResult.FromException(
                IrExceptionKind.NullReference,
                "The opaque call receiver is null.");
        return IrEvaluationResult.FromUnsupported(
            IrUnsupportedReason.OpaqueTerm,
            opaque.Purity == IrOpaquePurity.Pure
                ? "Pure opaque member " + opaque.Member + " has no concrete implementation."
                : "Impure opaque operation " + opaque.Operation + " cannot be interpreted.");
    }

    private IrEvaluationResult EvaluateUnary(
        IrUnaryTerm unary,
        IReadOnlyDictionary<IrVarId, IrValue> variables) {
        var operand = EvaluateCore(unary.Operand, variables);
        if (operand.Status != IrEvaluationStatus.Value) return operand;
        switch (unary.Operator) {
            case IrUnaryOperator.Not:
                if (operand.Value!.Kind != IrValueKind.Boolean)
                    return InvalidValue("Boolean negation requires a boolean value.");
                return IrEvaluationResult.FromValue(_factory.CreateBooleanValue(!operand.Value.Boolean));
            case IrUnaryOperator.Negate:
                if (operand.Value!.Kind != IrValueKind.Integer)
                    return InvalidValue("Integer negation requires an integer value.");
                if (operand.Value.Integer == long.MinValue)
                    return IrEvaluationResult.FromException(
                        IrExceptionKind.Overflow,
                        "Negating the minimum integer overflows.");
                return IrEvaluationResult.FromValue(_factory.CreateIntegerValue(-operand.Value.Integer));
            default:
                return IrEvaluationResult.FromUnsupported(
                    IrUnsupportedReason.UnsupportedOperation,
                    "Unsupported unary operator: " + unary.Operator + ".");
        }
    }

    private IrEvaluationResult EvaluateBinary(
        IrBinaryTerm binary,
        IReadOnlyDictionary<IrVarId, IrValue> variables) {
        var left = EvaluateCore(binary.Left, variables);
        if (left.Status != IrEvaluationStatus.Value) return left;
        if (binary.Operator == IrBinaryOperator.AndAlso) {
            if (left.Value!.Kind != IrValueKind.Boolean)
                return InvalidValue("Conditional conjunction requires boolean values.");
            if (!left.Value.Boolean) return IrEvaluationResult.FromValue(_factory.CreateBooleanValue(false));
        }
        if (binary.Operator == IrBinaryOperator.OrElse) {
            if (left.Value!.Kind != IrValueKind.Boolean)
                return InvalidValue("Conditional disjunction requires boolean values.");
            if (left.Value.Boolean) return IrEvaluationResult.FromValue(_factory.CreateBooleanValue(true));
        }
        var right = EvaluateCore(binary.Right, variables);
        if (right.Status != IrEvaluationStatus.Value) return right;
        return binary.Operator switch {
            IrBinaryOperator.Add => EvaluateIntegerArithmetic(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.Subtract => EvaluateIntegerArithmetic(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.Multiply => EvaluateIntegerArithmetic(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.Divide => EvaluateIntegerArithmetic(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.Remainder => EvaluateIntegerArithmetic(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.AndAlso => EvaluateBooleanBinary(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.OrElse => EvaluateBooleanBinary(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.Equal => EvaluateEquality(left.Value!, right.Value!, negate: false),
            IrBinaryOperator.NotEqual => EvaluateEquality(left.Value!, right.Value!, negate: true),
            IrBinaryOperator.LessThan => EvaluateIntegerComparison(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.LessThanOrEqual => EvaluateIntegerComparison(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.GreaterThan => EvaluateIntegerComparison(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.GreaterThanOrEqual => EvaluateIntegerComparison(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.StringConcat => EvaluateStringConcat(left.Value!, right.Value!),
            _ => IrEvaluationResult.FromUnsupported(
                IrUnsupportedReason.UnsupportedOperation,
                "Unsupported binary operator: " + binary.Operator + ".")
        };
    }

    private IrEvaluationResult EvaluateIntegerArithmetic(
        IrBinaryOperator @operator,
        IrValue left,
        IrValue right) {
        if (left.Kind != IrValueKind.Integer || right.Kind != IrValueKind.Integer)
            return InvalidValue("Integer arithmetic requires integer values.");
        if (right.Integer == 0 && @operator is IrBinaryOperator.Divide or IrBinaryOperator.Remainder)
            return IrEvaluationResult.FromException(
                IrExceptionKind.DivideByZero,
                "Integer division or remainder by zero.");
        try {
            var value = @operator switch {
                IrBinaryOperator.Add => checked(left.Integer + right.Integer),
                IrBinaryOperator.Subtract => checked(left.Integer - right.Integer),
                IrBinaryOperator.Multiply => checked(left.Integer * right.Integer),
                IrBinaryOperator.Divide => checked(left.Integer / right.Integer),
                IrBinaryOperator.Remainder => left.Integer % right.Integer,
                _ => throw new InvalidOperationException("Unexpected arithmetic operator.")
            };
            return IrEvaluationResult.FromValue(_factory.CreateIntegerValue(value));
        }
        catch (OverflowException) {
            return IrEvaluationResult.FromException(
                IrExceptionKind.Overflow,
                "Checked integer arithmetic overflowed.");
        }
    }

    private IrEvaluationResult EvaluateBooleanBinary(
        IrBinaryOperator @operator,
        IrValue left,
        IrValue right) {
        if (left.Kind != IrValueKind.Boolean || right.Kind != IrValueKind.Boolean)
            return InvalidValue("Boolean operators require boolean values.");
        var value = @operator == IrBinaryOperator.AndAlso
            ? left.Boolean && right.Boolean
            : left.Boolean || right.Boolean;
        return IrEvaluationResult.FromValue(_factory.CreateBooleanValue(value));
    }

    private IrEvaluationResult EvaluateEquality(IrValue left, IrValue right, bool negate) {
        if (left.Type != right.Type)
            return InvalidValue("Equality requires values with the same type.");
        bool equal;
        if (left.Kind == IrValueKind.Null || right.Kind == IrValueKind.Null) {
            equal = left.Kind == IrValueKind.Null && right.Kind == IrValueKind.Null;
        }
        else if (left.Kind != right.Kind) {
            return InvalidValue("Equality requires values with compatible runtime kinds.");
        }
        else {
            equal = left.Kind switch {
                IrValueKind.Boolean => left.Boolean == right.Boolean,
                IrValueKind.Integer => left.Integer == right.Integer,
                IrValueKind.String => string.Equals(left.String, right.String, StringComparison.Ordinal),
                IrValueKind.Reference => ReferenceEquals(left.Reference, right.Reference),
                IrValueKind.Sequence => ReferenceEquals(left, right),
                _ => false
            };
        }
        return IrEvaluationResult.FromValue(_factory.CreateBooleanValue(negate ? !equal : equal));
    }

    private IrEvaluationResult EvaluateIntegerComparison(
        IrBinaryOperator @operator,
        IrValue left,
        IrValue right) {
        if (left.Kind != IrValueKind.Integer || right.Kind != IrValueKind.Integer)
            return InvalidValue("Integer comparison requires integer values.");
        var value = @operator switch {
            IrBinaryOperator.LessThan => left.Integer < right.Integer,
            IrBinaryOperator.LessThanOrEqual => left.Integer <= right.Integer,
            IrBinaryOperator.GreaterThan => left.Integer > right.Integer,
            IrBinaryOperator.GreaterThanOrEqual => left.Integer >= right.Integer,
            _ => throw new InvalidOperationException("Unexpected comparison operator.")
        };
        return IrEvaluationResult.FromValue(_factory.CreateBooleanValue(value));
    }

    private IrEvaluationResult EvaluateStringConcat(IrValue left, IrValue right) {
        if (left.Kind is not (IrValueKind.String or IrValueKind.Null) ||
            right.Kind is not (IrValueKind.String or IrValueKind.Null))
            return InvalidValue("String concatenation requires string values.");
        return IrEvaluationResult.FromValue(
            _factory.CreateStringValue(
                (left.Kind == IrValueKind.Null ? "" : left.String) +
                (right.Kind == IrValueKind.Null ? "" : right.String)));
    }

    private IrEvaluationResult EvaluateConditional(
        IrConditionalTerm conditional,
        IReadOnlyDictionary<IrVarId, IrValue> variables) {
        var condition = EvaluateCore(conditional.Condition, variables);
        if (condition.Status != IrEvaluationStatus.Value) return condition;
        if (condition.Value!.Kind != IrValueKind.Boolean)
            return InvalidValue("A conditional guard requires a boolean value.");
        return EvaluateCore(condition.Value.Boolean ? conditional.WhenTrue : conditional.WhenFalse, variables);
    }

    private IrEvaluationResult EvaluateCast(
        IrCastTerm cast,
        IReadOnlyDictionary<IrVarId, IrValue> variables) {
        var operand = EvaluateCore(cast.Operand, variables);
        if (operand.Status != IrEvaluationStatus.Value) return operand;
        if (operand.Value!.Type == cast.Type) return operand;
        var target = _factory.GetTypeInfo(cast.Type);
        if (operand.Value.Kind == IrValueKind.Null) {
            if (target.Kind is IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence)
                return IrEvaluationResult.FromValue(_factory.CreateNullValue(cast.Type));
            return IrEvaluationResult.FromException(
                IrExceptionKind.InvalidCast,
                "Null cannot be cast to a non-nullable IR type.");
        }
        if (target.Kind == IrTypeKind.String &&
            operand.Value.Kind == IrValueKind.Reference)
            return operand.Value.Reference is string value
                ? IrEvaluationResult.FromValue(
                    _factory.CreateStringValue(value))
                : IrEvaluationResult.FromException(
                    IrExceptionKind.InvalidCast,
                    "The concrete reference is not a string.");
        return IrEvaluationResult.FromUnsupported(
            IrUnsupportedReason.UnsupportedCast,
            "The interpreter has no runtime type relation for this cast.");
    }

    private IrEvaluationResult EvaluateLength(
        IrLengthTerm length,
        IReadOnlyDictionary<IrVarId, IrValue> variables) {
        var value = EvaluateCore(length.Value, variables);
        if (value.Status != IrEvaluationStatus.Value) return value;
        if (value.Value!.Kind == IrValueKind.Null)
            return IrEvaluationResult.FromException(
                IrExceptionKind.NullReference,
                "Length was requested from null.");
        return value.Value.Kind switch {
            IrValueKind.String => IrEvaluationResult.FromValue(
                _factory.CreateIntegerValue(value.Value.String.Length)),
            IrValueKind.Sequence => IrEvaluationResult.FromValue(
                _factory.CreateIntegerValue(value.Value.Elements.Length)),
            _ => InvalidValue("Length requires a string or sequence value.")
        };
    }

    private IrEvaluationResult EvaluateSequenceAccess(
        IrSequenceAccessTerm access,
        IReadOnlyDictionary<IrVarId, IrValue> variables) {
        var sequence = EvaluateCore(access.Sequence, variables);
        if (sequence.Status != IrEvaluationStatus.Value) return sequence;
        var index = EvaluateCore(access.Index, variables);
        if (index.Status != IrEvaluationStatus.Value) return index;
        if (sequence.Value!.Kind == IrValueKind.Null)
            return IrEvaluationResult.FromException(
                IrExceptionKind.NullReference,
                "Sequence access used a null receiver.");
        if (sequence.Value.Kind != IrValueKind.Sequence)
            return InvalidValue("Sequence access requires a sequence value.");
        if (index.Value!.Kind != IrValueKind.Integer)
            return InvalidValue("Sequence access requires an integer index.");
        if (index.Value.Integer < 0 || index.Value.Integer >= sequence.Value.Elements.Length)
            return IrEvaluationResult.FromException(
                IrExceptionKind.IndexOutOfRange,
                "The sequence index is outside the valid range.");
        return IrEvaluationResult.FromValue(sequence.Value.Elements[(int)index.Value.Integer]);
    }

    private static IrEvaluationResult InvalidValue(string detail) =>
        IrEvaluationResult.FromUnsupported(IrUnsupportedReason.InvalidVariableValue, detail);
}
