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

    internal static IrScalarResult Evaluate(IrBinaryOperator @operator, long left /* SPINT001: all values valid - arithmetic faults are returned as evaluation statuses */, long right)
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
                IrBinaryOperator.BitwiseOr => Integer(left | right),
                IrBinaryOperator.BitwiseXor => Integer(left ^ right),
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

internal sealed partial class IrValue
{
    // Computed string values carry contents, but hash-consed terms do not
    // model distinct runtime allocations. Preserve this fact through values
    // selected by conditionals, sequence accesses, and later evaluations.
    private readonly bool _hasUnknownStringIdentity;
    private readonly IrValue? _sequenceIdentity;

    private IrValue(IrValue value, bool hasUnknownStringIdentity, IrValue? sequenceIdentity)
        : this(ArgumentNullGuard.NotNull(value, nameof(value)).Type, value.Kind, value.Payload)
    {
        _hasUnknownStringIdentity = hasUnknownStringIdentity;
        _sequenceIdentity = sequenceIdentity;
    }

    internal IrValue SequenceIdentity => _sequenceIdentity ?? this;

    internal static IrValue WithSequenceIdentity(IrValue value, IrValue storage)
    {
        storage = RepositoryParameterGuard.NotNull(storage, nameof(storage));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return new IrValue(value, hasUnknownStringIdentity: false, storage.SequenceIdentity);
    }

    internal static bool HasUnknownStringIdentity(IrValue value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return value._hasUnknownStringIdentity;
    }

    internal static IrValue WithUnknownStringIdentity(IrValue value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return new IrValue(value, hasUnknownStringIdentity: true, sequenceIdentity: null);
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
        message = RepositoryParameterGuard.NotNull(message, nameof(message));
        return Kind == expectedKind ? (T)Payload! : throw new InvalidOperationException(message);
    }
}

internal sealed partial class IrEvaluationResult
{
    internal static IrEvaluationResult FromValue(IrValue value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return new(IrEvaluationStatus.Value, value, null, null);
    }

    internal static IrEvaluationResult FromUnsupported(IrUnsupportedReason reason, string detail)
    {
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        return new(IrEvaluationStatus.Unsupported, null, new IrUnsupportedInfo(reason, detail), null);
    }

    internal static IrEvaluationResult FromException(IrExceptionKind kind, string detail)
    {
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        return new(IrEvaluationStatus.Exception, null, null, new IrExceptionInfo(kind, detail));
    }
}

internal sealed class IrInterpreter(IrFactory factory)
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
        IReadOnlyDictionary<IrVarId, IrValue>? variables = null)
    {
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(SharpProof.Ir.AnalysisDefaults.OperationTimeoutMinutes));
        return Evaluate(term, variables, cancellation.Token);
    }

    public IrEvaluationResult Evaluate(
        IrTerm term,
        IReadOnlyDictionary<IrVarId, IrValue>? variables,
        CancellationToken cancellationToken)
    {
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        return Evaluate(term, variables, onVariableRead: null, cancellationToken);
    }

    internal IrEvaluationResult Evaluate(
        IrTerm term, IReadOnlyDictionary<IrVarId, IrValue>? variables,
        Action<IrVarId>? onVariableRead, CancellationToken cancellationToken)
    {
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        return Evaluate(term, variables, onVariableRead, heap: null, cancellationToken);
    }

    // The heap holds the current contents of each array and field stored to
    // so far. A read through an Old snapshot variable sees the entry contents.
    internal IrEvaluationResult Evaluate(
        IrTerm term, IReadOnlyDictionary<IrVarId, IrValue>? variables,
        Action<IrVarId>? onVariableRead, IrHeap? heap, CancellationToken cancellationToken,
        ImmutableHashSet<IrVarId>? snapshots = null,
        Func<IrOpaqueTerm, IrValue?>? concreteOpaqueValueProvider = null)
    {
        ArgumentNullGuard.NotNull(term, nameof(term));

        _factory.EnsureTerm(term, nameof(term));
        return EvaluateCore(
            term,
            new(SnapshotVariables(variables), onVariableRead, snapshots, heap, concreteOpaqueValueProvider, cancellationToken));
    }

    private static ImmutableDictionary<IrVarId, IrValue> SnapshotVariables(
        IReadOnlyDictionary<IrVarId, IrValue>? variables)
    {
        return variables switch
        {
            null => ImmutableDictionary<IrVarId, IrValue>.Empty,
            ImmutableDictionary<IrVarId, IrValue> immutable => immutable,
            Dictionary<IrVarId, IrValue> dictionary => dictionary.ToImmutableDictionary(dictionary.Comparer),
            _ => variables.ToImmutableDictionary()
        };
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
        state = RepositoryParameterGuard.NotNull(state, nameof(state));
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        var result = term.Accept(EvaluationVisitor.Instance, (this, state));
        state.Results.Add(term.Id, result);
        return result;
    }

    private sealed class EvaluationVisitor : IIrTermVisitor<(IrInterpreter Interpreter, EvaluationState Evaluation), IrEvaluationResult>
    {
        internal static EvaluationVisitor Instance { get; } = new();

        public IrEvaluationResult Visit(IrBooleanTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return state.Interpreter.Boolean(term.Value);
        }

        public IrEvaluationResult Visit(IrIntegerTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return Value(state.Interpreter._factory.CreateIntegerValueFromBits(term.Type, term.Bits));
        }

        public IrEvaluationResult Visit(IrStringTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return state.Interpreter.Text(state.Interpreter._factory.GetString(term.Value));
        }

        public IrEvaluationResult Visit(IrNullTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return Value(state.Interpreter._factory.CreateNullValue(term.Type));
        }

        public IrEvaluationResult Visit(IrEmptyArrayTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return Value(state.Interpreter._factory.CreateEmptyArrayValue(term.Type));
        }

        public IrEvaluationResult Visit(IrVariableTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return EvaluateVariable(term, state.Evaluation);
        }

        public IrEvaluationResult Visit(IrOpaqueTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return state.Interpreter.EvaluateOpaque(term, state.Evaluation);
        }

        public IrEvaluationResult Visit(IrUnaryTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return state.Interpreter.EvaluateUnary(term, state.Evaluation);
        }

        public IrEvaluationResult Visit(IrBinaryTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return state.Interpreter.EvaluateBinary(term, state.Evaluation);
        }

        public IrEvaluationResult Visit(IrConditionalTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return state.Interpreter.EvaluateConditional(term, state.Evaluation);
        }

        public IrEvaluationResult Visit(IrCastTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return state.Interpreter.EvaluateCast(term, state.Evaluation);
        }

        public IrEvaluationResult Visit(IrLengthTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return state.Interpreter.EvaluateLength(term, state.Evaluation);
        }

        public IrEvaluationResult Visit(IrSequenceAccessTerm term, (IrInterpreter Interpreter, EvaluationState Evaluation) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return state.Interpreter.EvaluateSequenceAccess(term, state.Evaluation);
        }
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
        IrValue? keyValue = null;
        foreach (var argument in opaque.Arguments)
        {
            var argumentResult = EvaluateCore(argument, state);
            keyValue = argumentResult.Value;
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
            if (heap != null)
            {
                return heap.TryReadField(_factory, owner, opaque.Member, out var stored, keyValue) ? ValidateFieldValue(opaque, stored)
                    : Unsupported(IrUnsupportedReason.OpaqueTerm, "The current field has no concrete value.");
            }
            if (owner.Reference is IrObjectState entry && entry.TryRead(opaque.Member, keyValue, out var initial))
            { return ValidateFieldValue(opaque, initial); }
        }

        if (state.ConcreteOpaqueValueProvider?.Invoke(opaque) is { } observed)
        {
            return observed.Type == opaque.Type ? Value(observed) :
                InvalidValue("The observed opaque value has the wrong type.");
        }
        return Unsupported(IrUnsupportedReason.OpaqueTerm,
            opaque.Purity == IrOpaquePurity.Pure
                ? "Pure opaque member " + opaque.Member + " has no concrete implementation."
                : "Impure opaque operation " + opaque.Operation + " cannot be interpreted.");
    }

    private static IrEvaluationResult ValidateFieldValue(IrOpaqueTerm field, IrValue value)
    {
        field = RepositoryParameterGuard.NotNull(field, nameof(field));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return value.Type == field.Type ? Value(value) :
            InvalidValue("The supplied field value has the wrong type for " + field.Member + ".");
    }

    private IrEvaluationResult EvaluateUnary(IrUnaryTerm unary, EvaluationState state)
    {
        state = RepositoryParameterGuard.NotNull(state, nameof(state));
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
        return unary.EvaluateOperator((this, value));
    }

    internal static IrEvaluationResult EvaluateUnaryOperator(IrNotOperatorInfo operation, (IrInterpreter Interpreter, IrValue Value) state)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));

        return state.Value.Kind == IrValueKind.Boolean
            ? state.Interpreter.Boolean(!state.Value.Boolean)
            : InvalidValue("Boolean negation requires a boolean value.");
    }

    internal static IrEvaluationResult EvaluateUnaryOperator(IrNegateOperatorInfo operation, (IrInterpreter Interpreter, IrValue Value) state)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));

        if (state.Value.Kind != IrValueKind.Integer)
        {
            return InvalidValue("Integer negation requires an integer value.");
        }
        if (state.Value.Integer == long.MinValue)
        {
            return Fault(IrExceptionKind.Overflow,
                "Negating the minimum integer overflows.");
        }
        return state.Interpreter.Integer(-state.Value.Integer);
    }

    private IrEvaluationResult EvaluateBinary(IrBinaryTerm binary, EvaluationState state)
    {
        binary = RepositoryParameterGuard.NotNull(binary, nameof(binary));
        state = RepositoryParameterGuard.NotNull(state, nameof(state));
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

        return binary.EvaluateOperator((this, left.Value!, right.Value!));
    }

    internal static IrEvaluationResult EvaluateBinaryOperator(IrIntegerBinaryOperatorInfo operation, (IrInterpreter Interpreter, IrValue Left, IrValue Right) state)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return state.Interpreter.EvaluateIntegerBinary(operation.Operator, state.Left, state.Right);
    }

    internal static IrEvaluationResult EvaluateBinaryOperator(IrBooleanBinaryOperatorInfo operation, (IrInterpreter Interpreter, IrValue Left, IrValue Right) state)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));

        return state.Interpreter.EvaluateBooleanBinary(state.Right);
    }

    internal static IrEvaluationResult EvaluateBinaryOperator(IrEqualityBinaryOperatorInfo operation, (IrInterpreter Interpreter, IrValue Left, IrValue Right) state)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return state.Interpreter.EvaluateEquality(state.Left, state.Right, operation.Negate);
    }

    internal static IrEvaluationResult EvaluateBinaryOperator(IrStringConcatBinaryOperatorInfo operation, (IrInterpreter Interpreter, IrValue Left, IrValue Right) state)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));

        return state.Interpreter.EvaluateStringConcat(state.Left, state.Right);
    }

    internal static IrEvaluationResult EvaluateBinaryOperator(IrStringEqualsBinaryOperatorInfo operation, (IrInterpreter Interpreter, IrValue Left, IrValue Right) state)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));

        return state.Interpreter.EvaluateStringEquals(state.Left, state.Right);
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

    internal IrEvaluationResult EvaluateEquality(IrValue left, IrValue right, bool negate)
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
            (IrValueKind.Sequence, IrValueKind.Sequence) => ReferenceEquals(left.SequenceIdentity, right.SequenceIdentity),
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
        conditional = RepositoryParameterGuard.NotNull(conditional, nameof(conditional));
        state = RepositoryParameterGuard.NotNull(state, nameof(state));
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
        cast = RepositoryParameterGuard.NotNull(cast, nameof(cast));
        state = RepositoryParameterGuard.NotNull(state, nameof(state));
        var operand = EvaluateCore(cast.Operand, state);
        return EvaluateCastValue(cast.Type, operand);
    }

    internal IrEvaluationResult EvaluateCastValue(IrTypeId targetType, IrEvaluationResult operand)
    {
        if (operand.Status != IrEvaluationStatus.Value)
        {
            return operand;
        }

        if (operand.Value!.Type == targetType)
        {
            return operand;
        }

        var target = _factory.GetTypeInfo(targetType);
        if (operand.Value.Kind == IrValueKind.Integer && target.Kind == IrTypeKind.Integer && target.Width != 0)
        {
            return Value(_factory.CreateIntegerValueFromBits(targetType,
                operand.Value.IntegerData.ConvertBits(target.Width)));
        }
        if (operand.Value.Kind == IrValueKind.Null)
        {
            if (target.Kind is IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence)
            {
                return Value(_factory.CreateNullValue(targetType));
            }

            return CastFault(targetType, IrExceptionKind.NullReference,
                "Null cannot be unboxed to a non-nullable IR type.");
        }

        // Literal and supplied string values have a concrete identity.
        // Computed contents alone cannot establish allocation identity.
        if (targetType == _factory.ObjectType &&
            operand.Value.Kind == IrValueKind.String)
        {
            if (IrValue.HasUnknownStringIdentity(operand.Value))
            {
                return Unsupported(IrUnsupportedReason.UnsupportedCast,
                    "Computed string allocation identity is not represented.");
            }
            return Value(_factory.CreateReferenceValue(
                targetType,
                operand.Value.String));
        }

        // A Total array widened to object keeps the array as its identity.
        if (targetType == _factory.ObjectType && operand.Value.Kind == IrValueKind.Sequence &&
            _factory.Semantics == IrExecutionSemantics.Total)
        { return Value(_factory.CreateReferenceValue(targetType, operand.Value.SequenceIdentity)); }

        if (operand.Value.Kind != IrValueKind.Reference)
        {
            return Unsupported(IrUnsupportedReason.UnsupportedCast,
                "The interpreter has no runtime type relation for this cast.");
        }

        // Total casts between class types keep the reference; a downcast's
        // type test is a separate approximated guard.
        if (targetType == _factory.ObjectType ||
            target.Kind == IrTypeKind.Reference && _factory.Semantics == IrExecutionSemantics.Total)
        { return Value(_factory.CreateReferenceValue(targetType, operand.Value.Reference)); }

        if (target.Kind == IrTypeKind.String)
        {
            return operand.Value.Reference is string value
                ? Text(value)
                : CastFault(targetType, IrExceptionKind.InvalidCast,
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
                    ? Value(_factory.CreateIntegerValueFromBits(targetType, bits.Value))
                    : CastFault(targetType, IrExceptionKind.InvalidCast, "The boxed integer has a different width or signedness.");
            }
            return operand.Value.Reference is long value
                ? Integer(value)
                : CastFault(targetType, IrExceptionKind.InvalidCast,
                    "The concrete reference does not contain a boxed integer.");
        }
        if (target.Kind == IrTypeKind.Boolean)
        {
            return operand.Value.Reference is bool value
                ? Boolean(value)
                : CastFault(targetType, IrExceptionKind.InvalidCast,
                    "The concrete reference does not contain a boxed boolean.");
        }

        return Unsupported(IrUnsupportedReason.UnsupportedCast,
            "The interpreter has no runtime type relation for this cast.");
    }

    private IrEvaluationResult EvaluateLength(IrLengthTerm length, EvaluationState state)
    {
        length = RepositoryParameterGuard.NotNull(length, nameof(length));
        state = RepositoryParameterGuard.NotNull(state, nameof(state));
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
        if (heap == null)
        { return Value(sequence.Value!.Elements[position]); }
        if (!heap.TryReadElement(sequence.Value!, position, out var stored))
        { return Unsupported(IrUnsupportedReason.UnsupportedOperation, "The current array element has no concrete value."); }
        if (stored.Type == access.Type)
        { return Value(stored); }
        var written = _factory.GetTypeInfo(stored.Type);
        var observed = _factory.GetTypeInfo(access.Type);
        return written.Kind == IrTypeKind.Integer && observed.Kind == IrTypeKind.Integer && written.Width == observed.Width &&
            stored.Kind == IrValueKind.Integer
            ? Value(_factory.CreateIntegerValueFromBits(access.Type, stored.IntegerBits))
            : InvalidValue("The stored array element has an incompatible view.");
    }

    private IrEvaluationResult CastFault(IrTypeId type, IrExceptionKind kind, string detail)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        return _factory.Semantics == IrExecutionSemantics.Total ? DefaultValue(type) : Fault(kind, detail);
    }

    private IrEvaluationResult DefaultValue(IrTypeId type)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        return Value(_factory.GetTypeInfo(type).CreateDefaultValue(_factory));
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
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return Value(_factory.CreateStringValue(value));
    }

    private static IrEvaluationResult Value(IrValue value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return IrEvaluationResult.FromValue(value);
    }

    private static IrEvaluationResult InvalidValue(string detail)
    {
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        return Unsupported(IrUnsupportedReason.InvalidVariableValue, detail);
    }

    private static IrEvaluationResult Unsupported(IrUnsupportedReason reason, string detail)
    {
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        return IrEvaluationResult.FromUnsupported(reason, detail);
    }

    private static IrEvaluationResult Fault(IrExceptionKind kind, string detail)
    {
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        return IrEvaluationResult.FromException(kind, detail);
    }

    private bool TryCurrentHeap(IrTerm owner, EvaluationState state, out IrHeap? heap, out IrEvaluationResult? failure)
    {
        heap = state.Heap;
        failure = null;
        if (heap == null || state.Snapshots is not { Count: > 0 } snapshots)
        { return true; }
        var selection = IrSnapshotReads.TrySelector(_factory, owner, snapshots.Contains, static guard => guard is null ? throw new System.ArgumentNullException(nameof(guard)) : (guard),
            () => --state.SnapshotWork >= 0, state.SnapshotSelectors, state.CancellationToken);
        state.SnapshotSelectors = selection.Memo;
        if (!selection.Success)
        {
            failure = Unsupported(IrUnsupportedReason.UnsupportedOperation, "Snapshot receiver selection is unsupported or exceeds its work limit.");
            return false;
        }
        var selected = EvaluateCore(selection.Selector!, state);
        if (selected.Status != IrEvaluationStatus.Value)
        { failure = selected; return false; }
        if (selected.Value!.Boolean)
        { heap = null; }
        return true;
    }

    private sealed class EvaluationState(
        ImmutableDictionary<IrVarId, IrValue> variables,
        Action<IrVarId>? onVariableRead,
        ImmutableHashSet<IrVarId>? snapshots,
        IrHeap? heap,
        Func<IrOpaqueTerm, IrValue?>? concreteOpaqueValueProvider,
        CancellationToken cancellationToken)
    {
        internal ImmutableDictionary<IrVarId, IrValue> Variables { get; } = (variables ?? throw new System.ArgumentNullException(nameof(variables)));
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal Action<IrVarId>? OnVariableRead { get; } = onVariableRead;
        internal ImmutableDictionary<IrId, IrEvaluationResult>.Builder Results { get; } = ImmutableDictionary.CreateBuilder<IrId, IrEvaluationResult>();
        internal int Depth { get; set; }
        internal IrHeap? Heap { get; } = heap;
        internal ImmutableHashSet<IrVarId>? Snapshots { get; } = snapshots;
        internal ImmutableDictionary<IrId, IrTerm> SnapshotSelectors { get; set; } = ImmutableDictionary<IrId, IrTerm>.Empty;
        internal Func<IrOpaqueTerm, IrValue?>? ConcreteOpaqueValueProvider { get; } = concreteOpaqueValueProvider;
        internal int SnapshotWork = 4096;
    }
}
