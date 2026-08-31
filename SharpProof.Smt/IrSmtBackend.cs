namespace SharpProof.Smt;

public sealed class IrSmtBackend : ISmtBackend, IDisposable
{
    private const int MaximumEncodingDepth = 256;
    private readonly Context _context;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _queryGate = new(1, 1);
    private readonly IrSmtBackendOptions _options;
    private long _consumedResourceCount;
    private int _activeCheckCount;
    private int _disposeStarted;
    private bool _disposed;

    public IrSmtBackend()
        : this(new IrSmtBackendOptions())
    {
    }

    public IrSmtBackend(IrSmtBackendOptions options)
        : this(options, static () => new Context())
    {
    }

    internal IrSmtBackend(
        IrSmtBackendOptions options,
        Func<Context> createContext)
    {
        _options = ArgumentNullGuard.NotNull(options, nameof(options));
        var validatedFactory = ArgumentNullGuard.NotNull(
            createContext, nameof(createContext));
        _context = ArgumentNullGuard.NotNull(
            validatedFactory(), nameof(createContext));
    }

    public long ConsumedResourceCount
    {
        get
        {
            lock (_gate)
            {
                return _consumedResourceCount;
            }
        }
    }

    public Task<BackendCheckResult> CheckAsync(
        VerificationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(query, nameof(query));

        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposeStarted) != 0)
        {
            return Task.FromResult(BackendCheckResult.Unknown(
                BackendFailureReason.Unavailable));
        }

        return CheckSerializedAsync(query, cancellationToken);
    }

    private async Task<BackendCheckResult> CheckSerializedAsync(
        VerificationQuery query,
        CancellationToken cancellationToken)
    {
        await _queryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                Interlocked.Increment(ref _activeCheckCount);
                try
                {
                    lock (_gate)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (_disposed)
                        {
                            return BackendCheckResult.Unknown(
                                BackendFailureReason.Unavailable);
                        }

                        using var registration = cancellationToken.Register(
                            static state => ((IrSmtBackend)state!).Interrupt(),
                            this);
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var result = CheckCore(query, cancellationToken);
                            cancellationToken.ThrowIfCancellationRequested();
                            return result;
                        }
                        catch (QueryResourceLimitException)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return BackendCheckResult.Unknown(
                                BackendFailureReason.ResourceLimit);
                        }
                        catch (UnsupportedIrEncodingException)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return BackendCheckResult.Unknown(
                                BackendFailureReason.UnsupportedEncoding);
                        }
                        catch (Exception exception) when (exception is
                            Z3Exception or
                            InvalidOperationException or
                            ArgumentException or
                            ArithmeticException)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return BackendCheckResult.Unknown(
                                BackendFailureReason.InfrastructureFailure);
                        }
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _activeCheckCount);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _queryGate.Release();
        }
    }

    private void Interrupt()
    {
        _context.Interrupt();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _queryGate.Wait();
        try
        {
            lock (_gate)
            {
                _disposed = true;
                _context.Dispose();
            }
        }
        finally
        {
            _queryGate.Dispose();
        }
    }

    private BackendCheckResult CheckCore(
        VerificationQuery query,
        CancellationToken cancellationToken)
    {
        var meter = new QueryResourceMeter(
            _options.QueryRlimit, cancellationToken);
        try
        {
            using var owner = new Z3ExpressionOwner();
            var encoder = new QueryEncoder(_context, query, owner, meter, cancellationToken);
            using var solver = _context.MkSolver();

            foreach (var variable in encoder.IntegerVariables)
            {
                meter.Consume();
                var expression = (ArithExpr)encoder.GetVariable(variable);
                solver.Assert(owner.Own(_context.MkGe(
                    expression,
                    owner.Own(_context.MkInt(long.MinValue)))));
                solver.Assert(owner.Own(_context.MkLe(
                    expression,
                    owner.Own(_context.MkInt(long.MaxValue)))));
            }

            var tracked = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < query.Assumptions.Length; index++)
            {
                meter.Consume();
                var encoded = encoder.EncodeBoolean(query.Assumptions[index].Predicate);
                var labelName = "a" + index.ToString(CultureInfo.InvariantCulture);
                var label = owner.Own(_context.MkBoolConst(labelName));
                solver.AssertAndTrack(
                    owner.Own(_context.MkAnd(encoded.Defined, encoded.Value)),
                    label);
                tracked.Add(labelName, index);
            }

            var goal = encoder.EncodeBoolean(query.Goal.Predicate);
            solver.Assert(
                owner.Own(_context.MkNot(
                    owner.Own(_context.MkAnd(goal.Defined, goal.Value)))));
            using var parameters = _context.MkParams();
            AddOwnedParameter(
                parameters,
                _context.MkSymbol("rlimit"),
                meter.GetRemainingBudget());
            solver.Parameters = parameters;
            cancellationToken.ThrowIfCancellationRequested();
            var status = solver.Check();
            meter.ConsumeNative(ReadResourceCount(solver));
            cancellationToken.ThrowIfCancellationRequested();
            return status switch
            {
                Status.UNSATISFIABLE => CreateUnsatisfiable(
                    solver, tracked, meter, cancellationToken),
                Status.SATISFIABLE => CreateSatisfiable(
                    query, encoder, solver, meter),
                _ => BackendCheckResult.Unknown(
                    ClassifyUnknown(solver.ReasonUnknown))
            };
        }
        finally
        {
            AddResourceCount(meter.Consumed);
        }
    }

    private static BackendFailureReason ClassifyUnknown(string? reason)
    {
        if (reason?.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return BackendFailureReason.Timeout;
        }

        if (reason?.IndexOf("resource", StringComparison.OrdinalIgnoreCase) >= 0 ||
            reason?.IndexOf("rlimit", StringComparison.OrdinalIgnoreCase) >= 0 ||
            reason?.IndexOf("max. memory", StringComparison.OrdinalIgnoreCase) >= 0 ||
            string.Equals(reason, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            return BackendFailureReason.ResourceLimit;
        }

        return BackendFailureReason.InfrastructureFailure;
    }

    private static long ReadResourceCount(Solver solver)
    {
        // Statistics is a caller-owned Z3 object holding a native reference, the
        // same as Model in CreateSatisfiable. This backend outlives hundreds of
        // queries per lane, so leaving it to the finalizer accumulates.
        using var statistics = solver.Statistics;
        foreach (var entry in statistics.Entries)
        {
            if (!string.Equals(
                    entry.Key,
                    "rlimit count",
                    StringComparison.Ordinal) ||
                !entry.IsUInt)
            {
                continue;
            }

            return entry.UIntValue;
        }

        return 0;
    }

    internal static void AddOwnedParameter(
        Params parameters,
        Symbol name,
        uint value)
    {
        using (name)
        {
            parameters.Add(name, value);
        }
    }

    private void AddResourceCount(long observed)
    {
        _consumedResourceCount = checked(_consumedResourceCount + observed);
    }

    private static BackendCheckResult CreateUnsatisfiable(
        Solver solver,
        Dictionary<string, int> tracked,
        QueryResourceMeter meter,
        CancellationToken cancellationToken)
    {
        var expressions = solver.UnsatCore;
        return CreateUnsatisfiable(
            expressions,
            tracked,
            static expression => expression.ToString(),
            () =>
            {
                meter.Consume();
                cancellationToken.ThrowIfCancellationRequested();
            });
    }

    internal static BackendCheckResult CreateUnsatisfiable<T>(
        IReadOnlyList<T> expressions,
        IReadOnlyDictionary<string, int> tracked,
        Func<T, string> format,
        Action? check = null)
        where T : IDisposable
    {
        var core = ImmutableArray.CreateBuilder<int>();
        try
        {
            foreach (var expression in expressions)
            {
                check?.Invoke();
                if (!tracked.TryGetValue(format(expression), out var index))
                {
                    return BackendCheckResult.Unknown(
                        BackendFailureReason.MalformedResult);
                }

                core.Add(index);
            }
            return BackendCheckResult.Unsatisfiable(
                core.Distinct().OrderBy(static index => index));
        }
        finally
        {
            foreach (var expression in expressions)
            {
                expression.Dispose();
            }
        }
    }

    private static BackendCheckResult CreateSatisfiable(
        VerificationQuery query,
        QueryEncoder encoder,
        Solver solver,
        QueryResourceMeter meter)
    {
        using var model = solver.Model;
        var assignments = ImmutableArray.CreateBuilder<KeyValuePair<IrVarId, IrValue>>();
        foreach (var variable in encoder.Variables)
        {
            meter.Consume();
            using var evaluated = model.Evaluate(encoder.GetVariable(variable), true);
            if (!TryCreateValue(query.Factory, variable, evaluated, out var value))
            {
                return BackendCheckResult.Unknown(BackendFailureReason.MalformedResult);
            }

            assignments.Add(new KeyValuePair<IrVarId, IrValue>(variable, value!));
        }
        return BackendCheckResult.Satisfiable(new BackendModel(assignments));
    }

    private static bool TryCreateValue(
        IrFactory factory,
        IrVarId variable,
        Expr expression,
        out IrValue? value)
    {
        var type = factory.GetVariableInfo(variable).Type;
        if (type == factory.BooleanType)
        {
            bool? boolean = expression.IsTrue ? true : expression.IsFalse ? false : null;
            value = boolean.HasValue ? factory.CreateBooleanValue(boolean.Value) : null;
        }
        else if (type == factory.IntegerType &&
                 expression is IntNum integer &&
                 long.TryParse(integer.ToString(), NumberStyles.AllowLeadingSign,
                     CultureInfo.InvariantCulture, out var number))
        {
            value = factory.CreateIntegerValue(number);
        }
        else
        {
            value = null;
        }

        return value != null;
    }

    private sealed class QueryEncoder
    {
        private readonly Context _context;
        private readonly Z3ExpressionOwner _owner;
        private readonly Dictionary<IrId, EncodedValue> _encoded = [];
        private readonly Dictionary<IrVarId, Expr> _variables = [];
        private readonly IrFactory _factory;
        private readonly QueryResourceMeter _meter;
        private readonly CancellationToken _cancellationToken;

        internal QueryEncoder(
            Context context,
            VerificationQuery query,
            Z3ExpressionOwner owner,
            QueryResourceMeter meter,
            CancellationToken cancellationToken)
        {
            _context = context;
            _owner = owner;
            _factory = query.Factory;
            _meter = meter;
            _cancellationToken = cancellationToken;
            var maximumDepths = new Dictionary<IrId, int>();
            foreach (var assumption in query.Assumptions)
            {
                ValidateDepth(assumption.Predicate, maximumDepths, meter, cancellationToken);
            }
            ValidateDepth(query.Goal.Predicate, maximumDepths, meter, cancellationToken);
            Variables = query.ModelVariables;
            var integerVariables = ImmutableArray.CreateBuilder<IrVarId>();
            foreach (var variable in Variables)
            {
                meter.Consume();
                var type = _factory.GetVariableInfo(variable).Type;
                if (type != _factory.BooleanType &&
                    type != _factory.IntegerType)
                {
                    throw new UnsupportedIrEncodingException();
                }

                if (type == _factory.IntegerType)
                {
                    integerVariables.Add(variable);
                }
            }
            IntegerVariables = integerVariables.ToImmutable();
            for (var index = 0; index < Variables.Length; index++)
            {
                meter.Consume();
                var variable = Variables[index];
                var name = "v" + index.ToString(CultureInfo.InvariantCulture);
                var type = _factory.GetVariableInfo(variable).Type;
                Expr expression;
                if (type == _factory.BooleanType)
                {
                    expression = Own(_context.MkBoolConst(name));
                }
                else if (type == _factory.IntegerType)
                {
                    expression = Own(_context.MkIntConst(name));
                }
                else
                {
                    throw new InvalidOperationException(
                        "The model-variable type was not prevalidated.");
                }

                _variables.Add(variable, expression);
            }
        }

        private static void ValidateDepth(
            IrTerm root,
            Dictionary<IrId, int> maximumDepths,
            QueryResourceMeter meter,
            CancellationToken cancellationToken)
        {
            var pending = new Stack<(IrTerm Term, int Depth)>();
            pending.Push((root, 1));
            while (pending.Count != 0)
            {
                meter.Consume();
                cancellationToken.ThrowIfCancellationRequested();
                var (term, depth) = pending.Pop();
                if (depth > MaximumEncodingDepth)
                {
                    throw new UnsupportedIrEncodingException();
                }
                if (maximumDepths.TryGetValue(term.Id, out var previous) &&
                    previous >= depth)
                {
                    continue;
                }
                maximumDepths[term.Id] = depth;
                foreach (var child in Children(term))
                {
                    pending.Push((child, depth + 1));
                }
            }
        }

        private static ImmutableArray<IrTerm> Children(IrTerm term)
        {
            return term switch
            {
                IrOpaqueTerm opaque => [.. opaque.Receiver == null
                    ? opaque.Arguments
                    : opaque.Arguments.Insert(0, opaque.Receiver)],
                IrUnaryTerm unary => [unary.Operand],
                IrBinaryTerm binary => [binary.Left, binary.Right],
                IrConditionalTerm conditional => [
                    conditional.Condition,
                    conditional.WhenTrue,
                    conditional.WhenFalse
                ],
                IrCastTerm cast => [cast.Operand],
                IrLengthTerm length => [length.Value],
                IrSequenceAccessTerm access => [access.Sequence, access.Index],
                _ => []
            };
        }

        internal ImmutableArray<IrVarId> Variables
        {
            get;
        }
        internal ImmutableArray<IrVarId> IntegerVariables
        {
            get;
        }

        private T Own<T>(T expression)
            where T : Expr
        {
            return _owner.Own(expression);
        }

        internal Expr GetVariable(IrVarId variable)
        {
            return _variables[variable];
        }

        internal EncodedBoolean EncodeBoolean(IrTerm term)
        {
            var encoded = Encode(term);
            if (encoded.Value is not BoolExpr boolean)
            {
                throw new UnsupportedIrEncodingException();
            }

            return new EncodedBoolean(boolean, encoded.Defined);
        }

        private EncodedValue Encode(IrTerm term)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_encoded.TryGetValue(term.Id, out var existing))
            {
                return existing;
            }

            _meter.PollCancellation();
            var encoded = term switch
            {
                IrBooleanTerm boolean => Defined(
                    Own(boolean.Value ? _context.MkTrue() : _context.MkFalse())),
                IrIntegerTerm integer => Defined(Own(_context.MkInt(integer.Value))),
                IrStringTerm text => EncodeString(text),
                IrVariableTerm variable => Defined(GetVariable(variable.Variable)),
                IrUnaryTerm unary => EncodeUnary(unary),
                IrBinaryTerm binary => EncodeBinary(binary),
                IrConditionalTerm conditional => EncodeConditional(conditional),
                IrCastTerm cast when cast.Type == cast.Operand.Type => Encode(cast.Operand),
                IrLengthTerm length => EncodeLength(length),
                _ => throw new UnsupportedIrEncodingException()
            };
            _encoded.Add(term.Id, encoded);
            return encoded;
        }

        private EncodedValue EncodeString(IrStringTerm text)
        {
            var value = _factory.GetString(text.Value);
            // Z3's native string constructor consumes a NUL-terminated
            // buffer, so embedded NULs would otherwise be silently truncated.
            if (value.IndexOf('\0') >= 0)
            {
                throw new UnsupportedIrEncodingException();
            }

            return Defined(Own(_context.MkString(value)));
        }

        private EncodedValue EncodeUnary(IrUnaryTerm unary)
        {
            var operand = Encode(unary.Operand);
            return unary.Operator switch
            {
                IrUnaryOperator.Not when operand.Value is BoolExpr boolean =>
                    new EncodedValue(Own(_context.MkNot(boolean)), operand.Defined),
                IrUnaryOperator.Negate when operand.Value is ArithExpr integer =>
                    Bounded(Own(_context.MkUnaryMinus(integer)), operand.Defined),
                _ => throw new UnsupportedIrEncodingException()
            };
        }

        private EncodedValue EncodeBinary(IrBinaryTerm binary)
        {
            var left = Encode(binary.Left);
            if (binary.Operator == IrBinaryOperator.AndAlso &&
                left.Value is BoolExpr leftAnd &&
                binary.Left is IrBooleanTerm { Value: false })
            {
                return new EncodedValue(leftAnd, left.Defined);
            }
            if (binary.Operator == IrBinaryOperator.OrElse &&
                left.Value is BoolExpr leftOr &&
                binary.Left is IrBooleanTerm { Value: true })
            {
                return new EncodedValue(leftOr, left.Defined);
            }
            var right = Encode(binary.Right);
            if (binary.Operator == IrBinaryOperator.AndAlso &&
                left.Value is BoolExpr leftBoolean &&
                right.Value is BoolExpr rightBoolean)
            {
                var value = Own(_context.MkAnd(leftBoolean, rightBoolean));
                var shortCircuitDefined = Own(_context.MkAnd(
                    left.Defined,
                    Own(_context.MkOr(
                        Own(_context.MkNot(leftBoolean)),
                        right.Defined))));
                return new EncodedValue(value, shortCircuitDefined);
            }

            if (binary.Operator == IrBinaryOperator.OrElse &&
                left.Value is BoolExpr leftOrBoolean &&
                right.Value is BoolExpr rightOrBoolean)
            {
                var value = Own(_context.MkOr(leftOrBoolean, rightOrBoolean));
                var shortCircuitDefined = Own(_context.MkAnd(
                    left.Defined,
                    Own(_context.MkOr(leftOrBoolean, right.Defined))));
                return new EncodedValue(value, shortCircuitDefined);
            }

            var defined = Own(_context.MkAnd(left.Defined, right.Defined));
            return binary.Operator switch
            {
                IrBinaryOperator.Add => Bounded(Own(_context.MkAdd(Integer(left), Integer(right))), defined),
                IrBinaryOperator.Subtract => Bounded(Own(_context.MkSub(Integer(left), Integer(right))), defined),
                IrBinaryOperator.Multiply => Bounded(Own(_context.MkMul(Integer(left), Integer(right))), defined),
                IrBinaryOperator.Divide or IrBinaryOperator.Remainder =>
                    EncodeDivision(binary.Operator, left, right, defined),
                IrBinaryOperator.Equal => new EncodedValue(Own(_context.MkEq(left.Value, right.Value)), defined),
                IrBinaryOperator.NotEqual => new EncodedValue(
                    Own(_context.MkNot(Own(_context.MkEq(left.Value, right.Value)))), defined),
                IrBinaryOperator.LessThan => Comparison(Own(_context.MkLt(Integer(left), Integer(right))), defined),
                IrBinaryOperator.LessThanOrEqual => Comparison(Own(_context.MkLe(Integer(left), Integer(right))), defined),
                IrBinaryOperator.GreaterThan => Comparison(Own(_context.MkGt(Integer(left), Integer(right))), defined),
                IrBinaryOperator.GreaterThanOrEqual => Comparison(Own(_context.MkGe(Integer(left), Integer(right))), defined),
                _ => throw new UnsupportedIrEncodingException()
            };
        }

        private EncodedValue EncodeDivision(
            IrBinaryOperator @operator, EncodedValue left,
            EncodedValue right, BoolExpr defined)
        {
            var leftInteger = Integer(left);
            var rightInteger = Integer(right);
            var quotient = DivideTowardZero(leftInteger, rightInteger);
            var result = @operator == IrBinaryOperator.Divide
                ? quotient
                : Own(_context.MkSub(
                    leftInteger,
                    Own(_context.MkMul(quotient, rightInteger))));
            return Bounded(result,
                Own(_context.MkAnd(defined, DivisionDefined(leftInteger, rightInteger))));
        }

        private EncodedValue EncodeConditional(IrConditionalTerm conditional)
        {
            var condition = EncodeBoolean(conditional.Condition);
            if (conditional.Condition is IrBooleanTerm constant)
            {
                return constant.Value
                    ? Encode(conditional.WhenTrue)
                    : Encode(conditional.WhenFalse);
            }
            var whenTrue = Encode(conditional.WhenTrue);
            var whenFalse = Encode(conditional.WhenFalse);
            // Expr.Sort creates a fresh managed wrapper over the native sort.
            // Keep these temporary wrappers bounded by this comparison; unlike
            // expressions, they are not part of the query expression owner.
            using var whenTrueSort = whenTrue.Value.Sort;
            using var whenFalseSort = whenFalse.Value.Sort;
            if (!whenTrueSort.Equals(whenFalseSort))
            {
                throw new UnsupportedIrEncodingException();
            }

            var value = Own(_context.MkITE(
                condition.Value,
                whenTrue.Value,
                whenFalse.Value));
            var branchDefined = (BoolExpr)Own(_context.MkITE(
                condition.Value,
                whenTrue.Defined,
                whenFalse.Defined));
            var defined = Own(_context.MkAnd(condition.Defined, branchDefined));
            return new EncodedValue(value, defined);
        }

        private EncodedValue EncodeLength(IrLengthTerm length)
        {
            if (length.Value.Type == _factory.StringType)
            {
                throw new UnsupportedIrEncodingException();
            }

            var value = Encode(length.Value);
            if (value.Value is not SeqExpr sequence)
            {
                throw new UnsupportedIrEncodingException();
            }

            return Bounded(Own(_context.MkLength(sequence)), value.Defined);
        }

        private EncodedValue Defined(Expr expression)
        {
            return new(expression, Own(_context.MkTrue()));
        }

        private EncodedValue Bounded(ArithExpr expression, BoolExpr defined)
        {
            var lowerBound = Own(_context.MkGe(
                expression,
                Own(_context.MkInt(long.MinValue))));
            var upperBound = Own(_context.MkLe(
                expression,
                Own(_context.MkInt(long.MaxValue))));
            return new(expression, Own(_context.MkAnd(
                defined,
                lowerBound,
                upperBound)));
        }

        private static EncodedValue Comparison(BoolExpr expression, BoolExpr defined)
        {
            return new(expression, defined);
        }

        private static ArithExpr Integer(EncodedValue value)
        {
            return value.Value as ArithExpr ?? throw new UnsupportedIrEncodingException();
        }

        private ArithExpr DivideTowardZero(ArithExpr left, ArithExpr right)
        {
            var zero = Own(_context.MkInt(0));
            var leftMagnitude = (ArithExpr)Own(_context.MkITE(
                Own(_context.MkGe(left, zero)),
                left,
                Own(_context.MkUnaryMinus(left))));
            var rightMagnitude = (ArithExpr)Own(_context.MkITE(
                Own(_context.MkGe(right, zero)),
                right,
                Own(_context.MkUnaryMinus(right))));
            var magnitude = Own(_context.MkDiv(leftMagnitude, rightMagnitude));
            var signsDiffer = Own(_context.MkXor(
                Own(_context.MkLt(left, zero)),
                Own(_context.MkLt(right, zero))));
            return (ArithExpr)Own(_context.MkITE(
                signsDiffer,
                Own(_context.MkUnaryMinus(magnitude)),
                magnitude));
        }

        private BoolExpr DivisionDefined(ArithExpr left, ArithExpr right)
        {
            var nonzero = Own(_context.MkNot(Own(_context.MkEq(
                right,
                Own(_context.MkInt(0))))));
            var notOverflow = Own(_context.MkNot(Own(_context.MkAnd(
                Own(_context.MkEq(
                    left,
                    Own(_context.MkInt(long.MinValue)))),
                Own(_context.MkEq(
                    right,
                    Own(_context.MkInt(-1))))))));
            return Own(_context.MkAnd(nonzero, notOverflow));
        }
    }

    private sealed class QueryResourceMeter(
        uint limit,
        CancellationToken cancellationToken)
    {
        private readonly long _limit = limit;
        private long _consumed;

        internal long Consumed => _consumed;

        internal void PollCancellation()
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        internal void Consume()
        {
            PollCancellation();
            if (_consumed >= _limit)
            {
                throw new QueryResourceLimitException();
            }

            _consumed++;
        }

        internal void ConsumeNative(long consumed)
        {
            PollCancellation();
            _consumed = checked(_consumed + consumed);
            if (_consumed > _limit)
            {
                throw new QueryResourceLimitException();
            }
        }

        internal uint GetRemainingBudget()
        {
            PollCancellation();
            var remaining = _limit - _consumed;
            if (remaining <= 0)
            {
                throw new QueryResourceLimitException();
            }

            return checked((uint)remaining);
        }
    }

    private sealed class EncodedValue(Expr value, BoolExpr defined)
    {
        internal Expr Value { get; } = value;
        internal BoolExpr Defined { get; } = defined;
    }

    private readonly struct EncodedBoolean(BoolExpr value, BoolExpr defined)
    {
        internal BoolExpr Value { get; } = value;
        internal BoolExpr Defined { get; } = defined;
    }

    private sealed class UnsupportedIrEncodingException : Exception;
    private sealed class QueryResourceLimitException : Exception;
}
