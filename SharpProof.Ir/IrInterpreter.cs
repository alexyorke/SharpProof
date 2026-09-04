namespace SharpProof.Ir;

internal enum IrScalarResultKind
{
    Integer,
    Boolean,
    DivideByZero,
    Overflow,
    Unsupported
}

internal readonly struct IrScalarResult(IrScalarResultKind kind, long value)
{
    internal IrScalarResultKind Kind { get; } = kind;
    internal long Value { get; } = value;
}

internal static class IrScalarOperations
{
    internal static IrScalarResult Evaluate(IrBinaryOperator @operator, long left, long right)
    {
        if (right == 0 &&
            @operator is IrBinaryOperator.Divide or IrBinaryOperator.Remainder)
        {
            return new(IrScalarResultKind.DivideByZero, 0);
        }

        try
        {
            return @operator switch
            {
                IrBinaryOperator.Add => Integer(checked(left + right)),
                IrBinaryOperator.Subtract => Integer(checked(left - right)),
                IrBinaryOperator.Multiply => Integer(checked(left * right)),
                IrBinaryOperator.Divide => Integer(checked(left / right)),
                IrBinaryOperator.Remainder => Integer(left % right),
                IrBinaryOperator.LessThan => Boolean(left < right),
                IrBinaryOperator.LessThanOrEqual => Boolean(left <= right),
                IrBinaryOperator.GreaterThan => Boolean(left > right),
                IrBinaryOperator.GreaterThanOrEqual => Boolean(left >= right),
                _ => new(IrScalarResultKind.Unsupported, 0)
            };
        }
        catch (OverflowException)
        {
            return new(IrScalarResultKind.Overflow, 0);
        }
    }

    private static IrScalarResult Integer(long value)
    {
        return new(IrScalarResultKind.Integer, value);
    }

    private static IrScalarResult Boolean(bool value)
    {
        return new(IrScalarResultKind.Boolean, value ? 1 : 0);
    }
}

public sealed partial class IrValue
{
    public bool Boolean => Get<bool>(IrValueKind.Boolean, "The IR value is not boolean.");
    public long Integer => Get<long>(IrValueKind.Integer, "The IR value is not an integer.");
    public string String => Get<string>(IrValueKind.String, "The IR value is not a string.");
    public object Reference => Get<object>(IrValueKind.Reference, "The IR value is not a reference.");
    public ImmutableArray<IrValue> Elements =>
        Get<ImmutableArray<IrValue>>(IrValueKind.Sequence, "The IR value is not a sequence.");

    private T Get<T>(IrValueKind expectedKind, string message)
    {
        return Kind == expectedKind ? (T)Payload! : throw new InvalidOperationException(message);
    }
}

public sealed partial class IrEvaluationResult
{
    internal static IrEvaluationResult FromValue(IrValue value)
    {
        return new(IrEvaluationStatus.Value, value, null, null);
    }

    internal static IrEvaluationResult FromUnsupported(IrUnsupportedReason reason, string detail)
    {
        return new(IrEvaluationStatus.Unsupported, null, new IrUnsupportedInfo(reason, detail), null);
    }

    internal static IrEvaluationResult FromException(IrExceptionKind kind, string detail)
    {
        return new(IrEvaluationStatus.Exception, null, null, new IrExceptionInfo(kind, detail));
    }
}

public sealed class IrInterpreter(IrFactory factory)
{
    /// <summary>
    /// Ceiling on term nesting evaluated recursively, matching the verifier's
    /// hard expression-depth cap.
    /// </summary>
    private const int MaximumEvaluationDepth = 256;

    private static readonly IReadOnlyDictionary<IrVarId, IrValue> EmptyEnvironment =
        ImmutableDictionary<IrVarId, IrValue>.Empty;
    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));

    public IrEvaluationResult Evaluate(
        IrTerm term,
        IReadOnlyDictionary<IrVarId, IrValue>? variables = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullGuard.NotNull(term, nameof(term));

        _factory.EnsureTerm(term, nameof(term));
        return EvaluateCore(term, new(variables ?? EmptyEnvironment, cancellationToken));
    }

    private IrEvaluationResult EvaluateCore(IrTerm term, EvaluationState state)
    {
        state.CancellationToken.ThrowIfCancellationRequested();

        // Evaluation is deliberately lazy — conditionals and AndAlso/OrElse
        // evaluate only the taken side, which is what makes definedness work —
        // so this cannot be flattened into an explicit stack. Bound the depth
        // instead: StackOverflowException is uncatchable and would kill the
        // worker with no result file.
        if (state.Depth >= MaximumEvaluationDepth)
        {
            return Unsupported(IrUnsupportedReason.UnsupportedOperation,
                "The term nests deeper than " +
                MaximumEvaluationDepth.ToString(CultureInfo.InvariantCulture) +
                " levels.");
        }

        if (state.Results.TryGetValue(term.Id, out var cached))
        {
            return cached;
        }

        state.Depth++;
        try
        {
            return EvaluateBounded(term, state);
        }
        finally
        {
            state.Depth--;
        }
    }

    private IrEvaluationResult EvaluateBounded(IrTerm term, EvaluationState state)
    {
        var result = term switch
        {
            IrBooleanTerm value => Boolean(value.Value),
            IrIntegerTerm value => Integer(value.Value),
            IrStringTerm value => Text(_factory.GetString(value.Value)),
            IrNullTerm => Value(_factory.CreateNullValue(term.Type)),
            IrVariableTerm variable => EvaluateVariable(variable, state.Variables),
            IrOpaqueTerm opaque => EvaluateOpaque(opaque, state),
            IrUnaryTerm unary => EvaluateUnary(unary, state),
            IrBinaryTerm binary => EvaluateBinary(binary, state),
            IrConditionalTerm conditional => EvaluateConditional(conditional, state),
            IrCastTerm cast => EvaluateCast(cast, state),
            IrLengthTerm length => EvaluateLength(length, state),
            IrSequenceAccessTerm access => EvaluateSequenceAccess(access, state),
            _ => Unsupported(IrUnsupportedReason.UnsupportedOperation,
                "Unknown IR term kind: " + term.Kind + ".")
        };
        state.Results.Add(term.Id, result);
        return result;
    }

    private static IrEvaluationResult EvaluateVariable(
        IrVariableTerm variable, IReadOnlyDictionary<IrVarId, IrValue> variables)
    {
        if (!variables.TryGetValue(variable.Variable, out var value))
        {
            return Unsupported(IrUnsupportedReason.MissingVariable,
                "No value was supplied for " + variable.Variable + ".");
        }

        if (value == null || value.Type != variable.Type)
        {
            return Unsupported(IrUnsupportedReason.InvalidVariableValue,
                "The supplied value has the wrong type for " + variable.Variable + ".");
        }

        return Value(value);
    }

    private IrEvaluationResult EvaluateOpaque(IrOpaqueTerm opaque, EvaluationState state)
    {
        IrValue? receiverValue = null;
        if (opaque.Receiver != null)
        {
            var receiver = EvaluateCore(opaque.Receiver, state);
            if (receiver.Status != IrEvaluationStatus.Value)
            {
                return receiver;
            }

            receiverValue = receiver.Value;
        }
        foreach (var argument in opaque.Arguments)
        {
            var argumentResult = EvaluateCore(argument, state);
            if (argumentResult.Status != IrEvaluationStatus.Value)
            {
                return argumentResult;
            }
        }
        if (receiverValue?.Kind == IrValueKind.Null)
        {
            return Fault(IrExceptionKind.NullReference,
                "The opaque call receiver is null.");
        }

        return Unsupported(IrUnsupportedReason.OpaqueTerm,
            opaque.Purity == IrOpaquePurity.Pure
                ? "Pure opaque member " + opaque.Member + " has no concrete implementation."
                : "Impure opaque operation " + opaque.Operation + " cannot be interpreted.");
    }

    private IrEvaluationResult EvaluateUnary(IrUnaryTerm unary, EvaluationState state)
    {
        var operand = EvaluateCore(unary.Operand, state);
        if (operand.Status != IrEvaluationStatus.Value)
        {
            return operand;
        }

        var value = operand.Value!;
        return unary.Operator switch
        {
            IrUnaryOperator.Not when value.Kind == IrValueKind.Boolean =>
                Boolean(!value.Boolean),
            IrUnaryOperator.Not =>
                InvalidValue("Boolean negation requires a boolean value."),
            IrUnaryOperator.Negate when value.Kind != IrValueKind.Integer =>
                InvalidValue("Integer negation requires an integer value."),
            IrUnaryOperator.Negate when value.Integer == long.MinValue =>
                Fault(IrExceptionKind.Overflow,
                    "Negating the minimum integer overflows."),
            IrUnaryOperator.Negate => Integer(-value.Integer),
            _ => Unsupported(IrUnsupportedReason.UnsupportedOperation,
                "Unsupported unary operator: " + unary.Operator + ".")
        };
    }

    private IrEvaluationResult EvaluateBinary(IrBinaryTerm binary, EvaluationState state)
    {
        var left = EvaluateCore(binary.Left, state);
        if (left.Status != IrEvaluationStatus.Value)
        {
            return left;
        }

        if (binary.Operator is IrBinaryOperator.AndAlso or IrBinaryOperator.OrElse)
        {
            if (left.Value!.Kind != IrValueKind.Boolean)
            {
                return InvalidValue(binary.Operator == IrBinaryOperator.AndAlso
                    ? "Conditional conjunction requires boolean values."
                    : "Conditional disjunction requires boolean values.");
            }

            var shortCircuitValue = binary.Operator == IrBinaryOperator.OrElse;
            if (left.Value.Boolean == shortCircuitValue)
            {
                return Boolean(shortCircuitValue);
            }
        }
        var right = EvaluateCore(binary.Right, state);
        if (right.Status != IrEvaluationStatus.Value)
        {
            return right;
        }

        return binary.Operator switch
        {
            IrBinaryOperator.Add or IrBinaryOperator.Subtract or IrBinaryOperator.Multiply
                or IrBinaryOperator.Divide or IrBinaryOperator.Remainder
                or IrBinaryOperator.LessThan or IrBinaryOperator.LessThanOrEqual
                or IrBinaryOperator.GreaterThan or IrBinaryOperator.GreaterThanOrEqual =>
                EvaluateIntegerBinary(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.AndAlso or IrBinaryOperator.OrElse =>
                EvaluateBooleanBinary(binary.Operator, left.Value!.Boolean, right.Value!),
            IrBinaryOperator.Equal => EvaluateEquality(left.Value!, right.Value!, negate: false),
            IrBinaryOperator.NotEqual => EvaluateEquality(left.Value!, right.Value!, negate: true),
            IrBinaryOperator.StringConcat => EvaluateStringConcat(left.Value!, right.Value!),
            _ => Unsupported(IrUnsupportedReason.UnsupportedOperation,
                "Unsupported binary operator: " + binary.Operator + ".")
        };
    }

    private IrEvaluationResult EvaluateIntegerBinary(IrBinaryOperator @operator, IrValue left, IrValue right)
    {
        if (left.Kind != IrValueKind.Integer || right.Kind != IrValueKind.Integer)
        {
            return InvalidValue(@operator is
                IrBinaryOperator.LessThan or
                IrBinaryOperator.LessThanOrEqual or
                IrBinaryOperator.GreaterThan or
                IrBinaryOperator.GreaterThanOrEqual
                ? "Integer comparison requires integer values."
                : "Integer arithmetic requires integer values.");
        }

        var result = IrScalarOperations.Evaluate(@operator, left.Integer, right.Integer);
        return result.Kind switch
        {
            IrScalarResultKind.Integer => Integer(result.Value),
            IrScalarResultKind.Boolean => Boolean(result.Value != 0),
            IrScalarResultKind.DivideByZero => Fault(IrExceptionKind.DivideByZero,
                "Integer division or remainder by zero."),
            IrScalarResultKind.Overflow => Fault(IrExceptionKind.Overflow,
                "Checked integer arithmetic overflowed."),
            _ => Unsupported(IrUnsupportedReason.UnsupportedOperation,
                "Unsupported integer operator: " + @operator + ".")
        };
    }

    private IrEvaluationResult EvaluateBooleanBinary(IrBinaryOperator @operator, bool left, IrValue right)
    {
        if (right.Kind != IrValueKind.Boolean)
        {
            return InvalidValue("Boolean operators require boolean values.");
        }

        var value = @operator == IrBinaryOperator.AndAlso
            ? left && right.Boolean
            : left || right.Boolean;
        return Boolean(value);
    }

    private IrEvaluationResult EvaluateEquality(IrValue left, IrValue right, bool negate)
    {
        if (left.Type != right.Type)
        {
            return InvalidValue("Equality requires values with the same type.");
        }

        bool? equal = (left.Kind, right.Kind) switch
        {
            (IrValueKind.Null, _) or (_, IrValueKind.Null) =>
                left.Kind == IrValueKind.Null && right.Kind == IrValueKind.Null,
            (IrValueKind.Boolean, IrValueKind.Boolean) => left.Boolean == right.Boolean,
            (IrValueKind.Integer, IrValueKind.Integer) => left.Integer == right.Integer,
            (IrValueKind.String, IrValueKind.String) =>
                string.Equals(left.String, right.String, StringComparison.Ordinal),
            (IrValueKind.Reference, IrValueKind.Reference) =>
                ReferenceEquals(left.Reference, right.Reference),
            (IrValueKind.Sequence, IrValueKind.Sequence) => ReferenceEquals(left, right),
            _ => null
        };
        return equal is bool established
            ? Boolean(negate != established)
            : InvalidValue("Equality requires values with compatible runtime kinds.");
    }

    private IrEvaluationResult EvaluateStringConcat(IrValue left, IrValue right)
    {
        if (left.Kind is not (IrValueKind.String or IrValueKind.Null) ||
            right.Kind is not (IrValueKind.String or IrValueKind.Null))
        {
            return InvalidValue("String concatenation requires string values.");
        }

        return Text(
            (left.Kind == IrValueKind.Null ? "" : left.String) +
            (right.Kind == IrValueKind.Null ? "" : right.String));
    }

    private IrEvaluationResult EvaluateConditional(IrConditionalTerm conditional, EvaluationState state)
    {
        var condition = EvaluateCore(conditional.Condition, state);
        if (condition.Status != IrEvaluationStatus.Value)
        {
            return condition;
        }

        if (condition.Value!.Kind != IrValueKind.Boolean)
        {
            return InvalidValue("A conditional guard requires a boolean value.");
        }

        return EvaluateCore(condition.Value.Boolean ? conditional.WhenTrue : conditional.WhenFalse, state);
    }

    private IrEvaluationResult EvaluateCast(IrCastTerm cast, EvaluationState state)
    {
        var operand = EvaluateCore(cast.Operand, state);
        if (operand.Status != IrEvaluationStatus.Value)
        {
            return operand;
        }

        if (operand.Value!.Type == cast.Type)
        {
            return operand;
        }

        var target = _factory.GetTypeInfo(cast.Type);
        if (operand.Value.Kind == IrValueKind.Null)
        {
            if (target.Kind is IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence)
            {
                return Value(_factory.CreateNullValue(cast.Type));
            }

            return Fault(IrExceptionKind.NullReference,
                "Null cannot be unboxed to a non-nullable IR type.");
        }
        if (operand.Value.Kind != IrValueKind.Reference)
        {
            return Unsupported(IrUnsupportedReason.UnsupportedCast,
                "The interpreter has no runtime type relation for this cast.");
        }

        if (target.Kind == IrTypeKind.String)
        {
            return operand.Value.Reference is string value
                ? Text(value)
                : Fault(IrExceptionKind.InvalidCast,
                    "The concrete reference is not a string.");
        }
        if (target.Kind == IrTypeKind.Integer)
        {
            return operand.Value.Reference is long value
                ? Integer(value)
                : Fault(IrExceptionKind.InvalidCast,
                    "The concrete reference does not contain a boxed integer.");
        }
        if (target.Kind == IrTypeKind.Boolean)
        {
            return operand.Value.Reference is bool value
                ? Boolean(value)
                : Fault(IrExceptionKind.InvalidCast,
                    "The concrete reference does not contain a boxed boolean.");
        }

        return Unsupported(IrUnsupportedReason.UnsupportedCast,
            "The interpreter has no runtime type relation for this cast.");
    }

    private IrEvaluationResult EvaluateLength(IrLengthTerm length, EvaluationState state)
    {
        var value = EvaluateCore(length.Value, state);
        if (value.Status != IrEvaluationStatus.Value)
        {
            return value;
        }

        if (value.Value!.Kind == IrValueKind.Null)
        {
            return Fault(IrExceptionKind.NullReference,
                "Length was requested from null.");
        }

        return value.Value.Kind switch
        {
            IrValueKind.String => Integer(value.Value.String.Length),
            IrValueKind.Sequence => Integer(value.Value.Elements.Length),
            _ => InvalidValue("Length requires a string or sequence value.")
        };
    }

    private IrEvaluationResult EvaluateSequenceAccess(IrSequenceAccessTerm access, EvaluationState state)
    {
        var sequence = EvaluateCore(access.Sequence, state);
        if (sequence.Status != IrEvaluationStatus.Value)
        {
            return sequence;
        }

        var index = EvaluateCore(access.Index, state);
        if (index.Status != IrEvaluationStatus.Value)
        {
            return index;
        }

        var invalid = ValidateSequenceAccess(sequence.Value!, index.Value!);
        return invalid ?? Value(sequence.Value!.Elements[(int)index.Value!.Integer]);
    }

    internal static IrEvaluationResult? ValidateSequenceAccess(IrValue sequence, IrValue index)
    {
        if (sequence.Kind == IrValueKind.Null)
        {
            return Fault(IrExceptionKind.NullReference,
                "Sequence access used a null receiver.");
        }

        if (sequence.Kind != IrValueKind.Sequence)
        {
            return InvalidValue("Sequence access requires a sequence value.");
        }

        if (index.Kind != IrValueKind.Integer)
        {
            return InvalidValue("Sequence access requires an integer index.");
        }

        if (index.Integer < 0 || index.Integer >= sequence.Elements.Length)
        {
            return Fault(IrExceptionKind.IndexOutOfRange,
                "The sequence index is outside the valid range.");
        }

        return null;
    }

    private IrEvaluationResult Boolean(bool value)
    {
        return Value(_factory.CreateBooleanValue(value));
    }

    private IrEvaluationResult Integer(long value)
    {
        return Value(_factory.CreateIntegerValue(value));
    }

    private IrEvaluationResult Text(string value)
    {
        return Value(_factory.CreateStringValue(value));
    }

    private static IrEvaluationResult Value(IrValue value)
    {
        return IrEvaluationResult.FromValue(value);
    }

    private static IrEvaluationResult InvalidValue(string detail)
    {
        return Unsupported(IrUnsupportedReason.InvalidVariableValue, detail);
    }

    private static IrEvaluationResult Unsupported(IrUnsupportedReason reason, string detail)
    {
        return IrEvaluationResult.FromUnsupported(reason, detail);
    }

    private static IrEvaluationResult Fault(IrExceptionKind kind, string detail)
    {
        return IrEvaluationResult.FromException(kind, detail);
    }

    private sealed class EvaluationState(
        IReadOnlyDictionary<IrVarId, IrValue> variables,
        CancellationToken cancellationToken)
    {
        internal IReadOnlyDictionary<IrVarId, IrValue> Variables { get; } = variables;
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal Dictionary<IrId, IrEvaluationResult> Results { get; } = [];
        internal int Depth { get; set; }
    }
}
