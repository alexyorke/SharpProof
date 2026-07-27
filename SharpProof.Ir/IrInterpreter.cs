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

    private IrValue(IrTypeId type, IrValueKind kind, object? value) =>
        (Type, Kind, _value) = (type, kind, value);

    public IrTypeId Type { get; }
    public IrValueKind Kind { get; }
    public bool Boolean => Get<bool>(IrValueKind.Boolean, "The IR value is not boolean.");
    public long Integer => Get<long>(IrValueKind.Integer, "The IR value is not an integer.");
    public string String => Get<string>(IrValueKind.String, "The IR value is not a string.");
    public object Reference => Get<object>(IrValueKind.Reference, "The IR value is not a reference.");
    public ImmutableArray<IrValue> Elements =>
        Get<ImmutableArray<IrValue>>(IrValueKind.Sequence, "The IR value is not a sequence.");

    internal static IrValue CreateBoolean(IrTypeId type, bool value) => new(type, IrValueKind.Boolean, value);
    internal static IrValue CreateInteger(IrTypeId type, long value) => new(type, IrValueKind.Integer, value);
    internal static IrValue CreateString(IrTypeId type, string value) => new(type, IrValueKind.String, value);
    internal static IrValue CreateNull(IrTypeId type) => new(type, IrValueKind.Null, null);
    internal static IrValue CreateReference(IrTypeId type, object value) => new(type, IrValueKind.Reference, value);
    internal static IrValue CreateSequence(IrTypeId type, ImmutableArray<IrValue> elements) =>
        new(type, IrValueKind.Sequence, elements);

    private T Get<T>(IrValueKind expectedKind, string message) =>
        Kind == expectedKind ? (T)_value! : throw new InvalidOperationException(message);
}

public sealed class IrUnsupportedInfo {
    internal IrUnsupportedInfo(IrUnsupportedReason reason, string detail) =>
        (Reason, Detail) = (reason, detail);

    public IrUnsupportedReason Reason { get; }
    public string Detail { get; }
}

public sealed class IrExceptionInfo {
    internal IrExceptionInfo(IrExceptionKind kind, string detail) =>
        (Kind, Detail) = (kind, detail);

    public IrExceptionKind Kind { get; }
    public string Detail { get; }
}

public sealed class IrEvaluationResult {
    private IrEvaluationResult(
        IrEvaluationStatus status,
        IrValue? value,
        IrUnsupportedInfo? unsupported,
        IrExceptionInfo? exception) =>
        (Status, Value, Unsupported, Exception) = (status, value, unsupported, exception);

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
        IReadOnlyDictionary<IrVarId, IrValue>? variables = null,
        CancellationToken cancellationToken = default) {
        if (term == null) throw new ArgumentNullException(nameof(term));
        _factory.EnsureTerm(term, nameof(term));
        return EvaluateCore(
            term,
            new EvaluationState(variables ?? EmptyEnvironment, cancellationToken));
    }

    private IrEvaluationResult EvaluateCore(
        IrTerm term,
        EvaluationState state) {
        if (state.Results.TryGetValue(term.Id, out var cached)) return cached;
        state.CancellationToken.ThrowIfCancellationRequested();
        var result = term switch {
            IrBooleanTerm boolean => IrEvaluationResult.FromValue(_factory.CreateBooleanValue(boolean.Value)),
            IrIntegerTerm integer => IrEvaluationResult.FromValue(_factory.CreateIntegerValue(integer.Value)),
            IrStringTerm text => IrEvaluationResult.FromValue(
                _factory.CreateStringValue(_factory.GetString(text.Value))),
            IrNullTerm => IrEvaluationResult.FromValue(_factory.CreateNullValue(term.Type)),
            IrVariableTerm variable => EvaluateVariable(variable, state.Variables),
            IrOpaqueTerm opaque => EvaluateOpaque(opaque, state),
            IrUnaryTerm unary => EvaluateUnary(unary, state),
            IrBinaryTerm binary => EvaluateBinary(binary, state),
            IrConditionalTerm conditional => EvaluateConditional(conditional, state),
            IrCastTerm cast => EvaluateCast(cast, state),
            IrLengthTerm length => EvaluateLength(length, state),
            IrSequenceAccessTerm access => EvaluateSequenceAccess(access, state),
            _ => IrEvaluationResult.FromUnsupported(
                IrUnsupportedReason.UnsupportedOperation,
                "Unknown IR term kind: " + term.Kind + ".")
        };
        state.Results.Add(term.Id, result);
        return result;
    }

    private static IrEvaluationResult EvaluateVariable(
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
        EvaluationState state) {
        IrValue? receiverValue = null;
        if (opaque.Receiver != null) {
            var receiver = EvaluateCore(opaque.Receiver, state);
            if (receiver.Status != IrEvaluationStatus.Value) return receiver;
            receiverValue = receiver.Value;
        }
        foreach (var argument in opaque.Arguments) {
            var argumentResult = EvaluateCore(argument, state);
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
        EvaluationState state) {
        var operand = EvaluateCore(unary.Operand, state);
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
        EvaluationState state) {
        var left = EvaluateCore(binary.Left, state);
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
        var right = EvaluateCore(binary.Right, state);
        if (right.Status != IrEvaluationStatus.Value) return right;
        return binary.Operator switch {
            IrBinaryOperator.Add or IrBinaryOperator.Subtract or IrBinaryOperator.Multiply
                or IrBinaryOperator.Divide or IrBinaryOperator.Remainder =>
                EvaluateIntegerArithmetic(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.AndAlso or IrBinaryOperator.OrElse =>
                EvaluateBooleanBinary(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.Equal => EvaluateEquality(left.Value!, right.Value!, negate: false),
            IrBinaryOperator.NotEqual => EvaluateEquality(left.Value!, right.Value!, negate: true),
            IrBinaryOperator.LessThan or IrBinaryOperator.LessThanOrEqual
                or IrBinaryOperator.GreaterThan or IrBinaryOperator.GreaterThanOrEqual =>
                EvaluateIntegerComparison(binary.Operator, left.Value!, right.Value!),
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
        EvaluationState state) {
        var condition = EvaluateCore(conditional.Condition, state);
        if (condition.Status != IrEvaluationStatus.Value) return condition;
        if (condition.Value!.Kind != IrValueKind.Boolean)
            return InvalidValue("A conditional guard requires a boolean value.");
        return EvaluateCore(condition.Value.Boolean ? conditional.WhenTrue : conditional.WhenFalse, state);
    }

    private IrEvaluationResult EvaluateCast(
        IrCastTerm cast,
        EvaluationState state) {
        var operand = EvaluateCore(cast.Operand, state);
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
        EvaluationState state) {
        var value = EvaluateCore(length.Value, state);
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
        EvaluationState state) {
        var sequence = EvaluateCore(access.Sequence, state);
        if (sequence.Status != IrEvaluationStatus.Value) return sequence;
        var index = EvaluateCore(access.Index, state);
        if (index.Status != IrEvaluationStatus.Value) return index;
        var invalid = ValidateSequenceAccess(sequence.Value!, index.Value!);
        return invalid ?? IrEvaluationResult.FromValue(
            sequence.Value!.Elements[(int)index.Value!.Integer]);
    }

    internal static IrEvaluationResult? ValidateSequenceAccess(IrValue sequence, IrValue index) {
        if (sequence.Kind == IrValueKind.Null)
            return IrEvaluationResult.FromException(
                IrExceptionKind.NullReference,
                "Sequence access used a null receiver.");
        if (sequence.Kind != IrValueKind.Sequence)
            return InvalidValue("Sequence access requires a sequence value.");
        if (index.Kind != IrValueKind.Integer)
            return InvalidValue("Sequence access requires an integer index.");
        if (index.Integer < 0 || index.Integer >= sequence.Elements.Length)
            return IrEvaluationResult.FromException(
                IrExceptionKind.IndexOutOfRange,
                "The sequence index is outside the valid range.");
        return null;
    }

    private static IrEvaluationResult InvalidValue(string detail) =>
        IrEvaluationResult.FromUnsupported(IrUnsupportedReason.InvalidVariableValue, detail);

    private sealed class EvaluationState(
        IReadOnlyDictionary<IrVarId, IrValue> variables,
        CancellationToken cancellationToken) {
        internal IReadOnlyDictionary<IrVarId, IrValue> Variables { get; } = variables;
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal Dictionary<IrId, IrEvaluationResult> Results { get; } = [];
    }
}
