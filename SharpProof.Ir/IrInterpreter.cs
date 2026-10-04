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
    internal static bool TryNegate(long value, out long result)
    {
        if (value == long.MinValue)
        {
            result = 0;
            return false;
        }

        result = -value;
        return true;
    }

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
                IrBinaryOperator.BitwiseAnd => Integer(left & right),
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
    // Computed string values carry contents, but hash-consed terms do not
    // model distinct runtime allocations. Preserve this fact through values
    // selected by conditionals, sequence accesses, and later evaluations.
    private bool _hasUnknownStringIdentity;

    internal static bool HasUnknownStringIdentity(IrValue value)
    {
        return value._hasUnknownStringIdentity;
    }

    internal static IrValue WithUnknownStringIdentity(IrValue value)
    {
        return new IrValue(value.Type, value.Kind, value.Payload)
        {
            _hasUnknownStringIdentity = true
        };
    }

    public bool Boolean => Get<bool>(IrValueKind.Boolean, "The IR value is not boolean.");
    internal IrInteger IntegerData => Get<IrInteger>(IrValueKind.Integer, "The IR value is not an integer.");
    public long Integer => IntegerData.Int64;
    public ulong IntegerBits => IntegerData.Bits;
    public System.Numerics.BigInteger IntegerNumericValue => IntegerData.NumericValue;
    public int IntegerWidth => IntegerData.Width;
    public bool IntegerSigned => IntegerData.Signed;
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

    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));

    public IrEvaluationResult Evaluate(
        IrTerm term,
        IReadOnlyDictionary<IrVarId, IrValue>? variables = null,
        CancellationToken cancellationToken = default)
    {
        return Evaluate(term, variables, onVariableRead: null, cancellationToken);
    }

    internal IrEvaluationResult Evaluate(
        IrTerm term, IReadOnlyDictionary<IrVarId, IrValue>? variables,
        Action<IrVarId>? onVariableRead, CancellationToken cancellationToken)
    {
        return Evaluate(term, variables, onVariableRead, heap: null, cancellationToken);
    }

    // The heap holds the current contents of each array and field stored to
    // so far. A read through an Old snapshot variable sees the entry contents.
    internal IrEvaluationResult Evaluate(
        IrTerm term, IReadOnlyDictionary<IrVarId, IrValue>? variables,
        Action<IrVarId>? onVariableRead, IrHeap? heap, CancellationToken cancellationToken,
        IReadOnlyCollection<IrVarId>? snapshots = null)
    {
        ArgumentNullGuard.NotNull(term, nameof(term));

        _factory.EnsureTerm(term, nameof(term));
        return EvaluateCore(
            term,
            new(variables ?? ImmutableDictionary<IrVarId, IrValue>.Empty, onVariableRead, cancellationToken)
            { Heap = heap, Snapshots = snapshots });
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
            IrIntegerTerm value => Value(_factory.CreateIntegerValueFromBits(value.Type, value.Bits)),
            IrStringTerm value => Text(_factory.GetString(value.Value)),
            IrNullTerm => Value(_factory.CreateNullValue(term.Type)),
            IrEmptyArrayTerm => Value(_factory.CreateEmptyArrayValue(term.Type)),
            IrVariableTerm variable => EvaluateVariable(variable, state),
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
        IrVariableTerm variable, EvaluationState state)
    {
        if (!state.Variables.TryGetValue(variable.Variable, out var value))
        {
            return Unsupported(IrUnsupportedReason.MissingVariable,
                "No value was supplied for " + variable.Variable + ".");
        }

        state.OnVariableRead?.Invoke(variable.Variable);
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
        // A field reads what the execution stored, else its entry value.
        if (IrFieldSites.IsFieldRead(_factory, opaque) && receiverValue is { Kind: IrValueKind.Reference } owner)
        {
            if (!TryCurrentHeap(opaque.Receiver!, state, out var heap, out var failure))
            { return failure!; }
            if (heap != null && heap.Fields.TryGetValue((owner.Reference, opaque.Member), out var stored))
            { return Value(stored); }
            if (owner.Reference is IrObjectState entry && entry.Fields.TryGetValue(opaque.Member, out var initial))
            { return Value(initial); }
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
        if (unary.Operator == IrUnaryOperator.Negate && value.Kind == IrValueKind.Integer &&
            _factory.GetTypeInfo(value.Type).Width != 0)
        {
            return Value(_factory.CreateIntegerValueFromBits(value.Type,
                unchecked(0UL - value.IntegerBits) & IrInteger.Mask(value.IntegerWidth)));
        }
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
                or IrBinaryOperator.Divide or IrBinaryOperator.Remainder or IrBinaryOperator.BitwiseAnd
                or IrBinaryOperator.LessThan or IrBinaryOperator.LessThanOrEqual
                or IrBinaryOperator.GreaterThan or IrBinaryOperator.GreaterThanOrEqual =>
                EvaluateIntegerBinary(binary.Operator, left.Value!, right.Value!),
            IrBinaryOperator.AndAlso or IrBinaryOperator.OrElse =>
                EvaluateBooleanBinary(right.Value!),
            IrBinaryOperator.Equal => EvaluateEquality(left.Value!, right.Value!, negate: false),
            IrBinaryOperator.NotEqual => EvaluateEquality(left.Value!, right.Value!, negate: true),
            IrBinaryOperator.StringConcat => EvaluateStringConcat(left.Value!, right.Value!),
            IrBinaryOperator.StringEquals => EvaluateStringEquals(left.Value!, right.Value!),
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

        if (_factory.GetTypeInfo(left.Type).Width != 0)
        {
            var typed = IrBitVectorOperations.Evaluate(@operator, left.IntegerData, right.IntegerData, _factory.Semantics);
            return typed.Kind switch
            {
                IrScalarResultKind.Integer => Value(_factory.CreateIntegerValueFromBits(left.Type, typed.Bits)),
                IrScalarResultKind.Boolean => Boolean(typed.Bits != 0),
                IrScalarResultKind.DivideByZero => Fault(IrExceptionKind.DivideByZero, "Integer division or remainder by zero."),
                IrScalarResultKind.Overflow => Fault(IrExceptionKind.Overflow, "Signed integer division or remainder overflowed."),
                _ => Unsupported(IrUnsupportedReason.UnsupportedOperation, "Unsupported integer operator: " + @operator + ".")
            };
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

    private IrEvaluationResult EvaluateBooleanBinary(IrValue right)
    {
        if (right.Kind != IrValueKind.Boolean)
        {
            return InvalidValue("Boolean operators require boolean values.");
        }

        return Boolean(right.Boolean);
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
            (IrValueKind.Integer, IrValueKind.Integer) => left.IntegerBits == right.IntegerBits,
            (IrValueKind.String, IrValueKind.String) =>
                _factory.Semantics == IrExecutionSemantics.Total ? ReferenceEquals(left.String, right.String) :
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

    private IrEvaluationResult EvaluateStringEquals(IrValue left, IrValue right)
    {
        if (left.Kind is not (IrValueKind.String or IrValueKind.Null) ||
            right.Kind is not (IrValueKind.String or IrValueKind.Null))
        {
            return InvalidValue("String equality requires string values.");
        }

        return Boolean(left.Kind == IrValueKind.Null || right.Kind == IrValueKind.Null
            ? left.Kind == right.Kind
            : string.Equals(left.String, right.String, StringComparison.Ordinal));
    }

    private IrEvaluationResult EvaluateStringConcat(IrValue left, IrValue right)
    {
        if (left.Kind is not (IrValueKind.String or IrValueKind.Null) ||
            right.Kind is not (IrValueKind.String or IrValueKind.Null))
        {
            return InvalidValue("String concatenation requires string values.");
        }

        var value = _factory.CreateStringValue(
            (left.Kind == IrValueKind.Null ? "" : left.String) +
            (right.Kind == IrValueKind.Null ? "" : right.String));
        return Value(IrValue.WithUnknownStringIdentity(value));
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
        if (operand.Value.Kind == IrValueKind.Integer && target.Kind == IrTypeKind.Integer && target.Width != 0)
        {
            return Value(_factory.CreateIntegerValueFromBits(cast.Type,
                operand.Value.IntegerData.ConvertBits(target.Width)));
        }
        if (operand.Value.Kind == IrValueKind.Null)
        {
            if (target.Kind is IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence)
            {
                return Value(_factory.CreateNullValue(cast.Type));
            }

            return CastFault(cast.Type, IrExceptionKind.NullReference,
                "Null cannot be unboxed to a non-nullable IR type.");
        }

        // Literal and supplied string values have a concrete identity.
        // Computed contents alone cannot establish allocation identity.
        if (cast.Type == _factory.ObjectType &&
            operand.Value.Kind == IrValueKind.String)
        {
            if (IrValue.HasUnknownStringIdentity(operand.Value))
            {
                return Unsupported(IrUnsupportedReason.UnsupportedCast,
                    "Computed string allocation identity is not represented.");
            }
            return Value(_factory.CreateReferenceValue(
                cast.Type,
                operand.Value.String));
        }

        // A Total array widened to object keeps the array as its identity.
        if (cast.Type == _factory.ObjectType && operand.Value.Kind == IrValueKind.Sequence &&
            _factory.Semantics == IrExecutionSemantics.Total)
        { return Value(_factory.CreateReferenceValue(cast.Type, operand.Value)); }

        if (operand.Value.Kind != IrValueKind.Reference)
        {
            return Unsupported(IrUnsupportedReason.UnsupportedCast,
                "The interpreter has no runtime type relation for this cast.");
        }

        // Total casts between class types keep the reference; a downcast's
        // type test is a separate approximated guard.
        if (cast.Type == _factory.ObjectType ||
            target.Kind == IrTypeKind.Reference && _factory.Semantics == IrExecutionSemantics.Total)
        { return Value(_factory.CreateReferenceValue(cast.Type, operand.Value.Reference)); }

        if (target.Kind == IrTypeKind.String)
        {
            return operand.Value.Reference is string value
                ? Text(value)
                : CastFault(cast.Type, IrExceptionKind.InvalidCast,
                    "The concrete reference is not a string.");
        }
        if (target.Kind == IrTypeKind.Integer)
        {
            if (target.Width != 0)
            {
                ulong? bits = (target.Width, target.Signed, operand.Value.Reference) switch
                {
                    (8, true, sbyte integer) => unchecked((ulong)integer) & 0xff,
                    (8, false, byte integer) => integer,
                    (16, true, short integer) => unchecked((ulong)integer) & 0xffff,
                    (16, false, ushort integer) => integer,
                    (32, true, int integer) => unchecked((ulong)integer) & 0xffffffff,
                    (32, false, uint integer) => integer,
                    (64, true, long integer) => unchecked((ulong)integer),
                    (64, false, ulong integer) => integer,
                    _ => null
                };
                return bits.HasValue
                    ? Value(_factory.CreateIntegerValueFromBits(cast.Type, bits.Value))
                    : CastFault(cast.Type, IrExceptionKind.InvalidCast, "The boxed integer has a different width or signedness.");
            }
            return operand.Value.Reference is long value
                ? Integer(value)
                : CastFault(cast.Type, IrExceptionKind.InvalidCast,
                    "The concrete reference does not contain a boxed integer.");
        }
        if (target.Kind == IrTypeKind.Boolean)
        {
            return operand.Value.Reference is bool value
                ? Boolean(value)
                : CastFault(cast.Type, IrExceptionKind.InvalidCast,
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
            return _factory.Semantics == IrExecutionSemantics.Total ? Integer(0)
                : Fault(IrExceptionKind.NullReference, "Length was requested from null.");
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
        if (invalid?.Status == IrEvaluationStatus.Exception && _factory.Semantics == IrExecutionSemantics.Total)
        {
            return DefaultValue(access.Type);
        }
        if (invalid != null)
        { return invalid; }
        var position = (int)index.Value!.Integer;
        if (!TryCurrentHeap(access.Sequence, state, out var heap, out var failure))
        { return failure!; }
        return Value(heap != null && heap.Elements.TryGetValue(sequence.Value!, out var stored)
            ? stored[position] : sequence.Value!.Elements[position]);
    }

    private IrEvaluationResult CastFault(IrTypeId type, IrExceptionKind kind, string detail)
    {
        return _factory.Semantics == IrExecutionSemantics.Total ? DefaultValue(type) : Fault(kind, detail);
    }

    private IrEvaluationResult DefaultValue(IrTypeId type)
    {
        return _factory.GetTypeInfo(type).Kind switch
        {
            IrTypeKind.Boolean => Boolean(false),
            IrTypeKind.Integer => Value(_factory.CreateIntegerValueFromBits(type, 0)),
            IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence => Value(_factory.CreateNullValue(type)),
            _ => InvalidValue("The term has no supported default value.")
        };
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

    private bool TryCurrentHeap(IrTerm owner, EvaluationState state, out IrHeap? heap, out IrEvaluationResult? failure)
    {
        heap = state.Heap;
        failure = null;
        if (heap == null || state.Snapshots is not { Count: > 0 } snapshots)
        { return true; }
        if (!IrSnapshotReads.TrySelector(_factory, owner, snapshots.Contains, static guard => guard,
            () => --state.SnapshotWork >= 0, state.SnapshotSelectors, out var selector, state.CancellationToken))
        {
            failure = Unsupported(IrUnsupportedReason.UnsupportedOperation, "Snapshot receiver selection is unsupported or exceeds its work limit.");
            return false;
        }
        var selected = EvaluateCore(selector, state);
        if (selected.Status != IrEvaluationStatus.Value)
        { failure = selected; return false; }
        if (selected.Value!.Boolean)
        { heap = null; }
        return true;
    }

    private sealed class EvaluationState(
        IReadOnlyDictionary<IrVarId, IrValue> variables,
        Action<IrVarId>? onVariableRead,
        CancellationToken cancellationToken)
    {
        internal IReadOnlyDictionary<IrVarId, IrValue> Variables { get; } = variables;
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal Action<IrVarId>? OnVariableRead { get; } = onVariableRead;
        internal Dictionary<IrId, IrEvaluationResult> Results { get; } = [];
        internal int Depth { get; set; }
        internal IrHeap? Heap { get; set; }
        internal IReadOnlyCollection<IrVarId>? Snapshots { get; set; }
        internal Dictionary<IrId, IrTerm> SnapshotSelectors { get; } = [];
        internal int SnapshotWork = 4096;
    }
}
