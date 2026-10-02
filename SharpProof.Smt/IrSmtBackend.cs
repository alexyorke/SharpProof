namespace SharpProof.Smt;

public sealed class IrSmtBackend : ISmtBackend, IDisposable
{
    private readonly Context _context;
    private readonly SmtNativeRunner _runner;
    private readonly IrSmtBackendOptions _options;
    private long _consumedResourceCount;

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
        _runner = new SmtNativeRunner(validatedFactory);
        _context = _runner.Context;
    }

    public long ConsumedResourceCount => Interlocked.Read(ref _consumedResourceCount);

    public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(query, nameof(query));
        return _runner.CheckAsync(() => CheckCore(query, cancellationToken), cancellationToken);
    }

    public void Dispose()
    {
        _runner.Dispose();
    }

    private BackendCheckResult CheckCore(
        VerificationQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Factory.Semantics != IrExecutionSemantics.Legacy)
        {
            return BackendCheckResult.Unknown(BackendFailureReason.UnsupportedEncoding);
        }
        var meter = new SmtQueryResourceMeter(
            _options.QueryRlimit, cancellationToken);
        try
        {
            using var owner = new Z3ExpressionOwner();
            var encoder = new QueryEncoder(_context, query, owner, meter, cancellationToken);
            using var solver = _context.MkSolver();

            foreach (var (variable, type) in encoder.Variables)
            {
                if (type != query.Factory.IntegerType)
                {
                    continue;
                }

                meter.Consume();
                var expression = (ArithExpr)encoder.GetVariable(variable);
                solver.Assert(owner.Own(_context.MkGe(
                    expression, encoder.LongMin)));
                solver.Assert(owner.Own(_context.MkLe(
                    expression, encoder.LongMax)));
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
            SmtNativeUtilities.AddOwnedParameter(
                parameters,
                _context.MkSymbol("rlimit"),
                meter.GetRemainingBudget());
            solver.Parameters = parameters;
            var status = SmtNativeCheck.Run(solver, [], meter);
            return status switch
            {
                Status.UNSATISFIABLE => CreateUnsatisfiable(
                    solver, tracked, meter, cancellationToken),
                Status.SATISFIABLE => CreateSatisfiable(
                    query, encoder, solver, meter),
                _ => BackendCheckResult.Unknown(
                    SmtNativeUtilities.ClassifyUnknown(solver.ReasonUnknown))
            };
        }
        finally
        {
            Interlocked.Exchange(ref _consumedResourceCount,
                checked(Interlocked.Read(ref _consumedResourceCount) + meter.Consumed));
        }
    }

    private static BackendCheckResult CreateUnsatisfiable(
        Solver solver,
        Dictionary<string, int> tracked,
        SmtQueryResourceMeter meter,
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
        var core = new HashSet<int>();
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
                core.OrderBy(static index => index));
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
        SmtQueryResourceMeter meter)
    {
        using var model = solver.Model;
        var assignments = new Dictionary<IrVarId, IrValue>(encoder.Variables.Length);
        foreach (var (variable, type) in encoder.Variables)
        {
            meter.Consume();
            using var evaluated = model.Evaluate(encoder.GetVariable(variable), true);
            if (!TryCreateValue(query.Factory, type, evaluated, out var value))
            {
                return BackendCheckResult.Unknown(BackendFailureReason.MalformedResult);
            }

            assignments.Add(variable, value!);
        }
        return BackendCheckResult.Satisfiable(new BackendModel(assignments));
    }

    private static bool TryCreateValue(
        IrFactory factory,
        IrTypeId type,
        Expr expression,
        out IrValue? value)
    {
        if (type == factory.BooleanType)
        {
            value = SmtNativeUtilities.CreateBooleanValue(factory, expression);
        }
        else if (type == factory.IntegerType &&
                 expression is IntNum integer)
        {
            try
            {
                value = factory.CreateIntegerValue(integer.Int64);
            }
            catch (Z3Exception)
            {
                value = null;
            }
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
        private readonly Dictionary<string, ArithExpr> _stringConstants =
            new(StringComparer.Ordinal);
        private readonly IrFactory _factory;
        private readonly CancellationToken _cancellationToken;
        private ArithExpr? _longMin;
        private ArithExpr? _longMax;
        private ArithExpr? _zero;
        private ArithExpr? _minusOne;
        private BoolExpr? _defined;

        internal QueryEncoder(
            Context context,
            VerificationQuery query,
            Z3ExpressionOwner owner,
            SmtQueryResourceMeter meter,
            CancellationToken cancellationToken)
        {
            _context = context;
            _owner = owner;
            _factory = query.Factory;
            _cancellationToken = cancellationToken;
            var maximumDepths = new Dictionary<IrId, int>();
            foreach (var assumption in query.Assumptions)
            {
                SmtEncodingDepth.Validate(assumption.Predicate, maximumDepths, meter, cancellationToken);
            }
            SmtEncodingDepth.Validate(query.Goal.Predicate, maximumDepths, meter, cancellationToken);
            var variables = ImmutableArray.CreateBuilder<
                (IrVarId Variable, IrTypeId Type)>(query.ModelVariables.Length);
            for (var index = 0; index < query.ModelVariables.Length; index++)
            {
                meter.Consume();
                var variable = query.ModelVariables[index];
                var type = _factory.GetVariableInfo(variable).Type;
                if (type != _factory.BooleanType &&
                    type != _factory.IntegerType)
                {
                    throw new UnsupportedIrEncodingException();
                }

                variables.Add((variable, type));
                var name = "v" + index.ToString(CultureInfo.InvariantCulture);
                _variables.Add(variable, type == _factory.BooleanType
                    ? _owner.Own(_context.MkBoolConst(name))
                    : _owner.Own(_context.MkIntConst(name)));
            }
            Variables = variables.ToImmutable();
        }

        internal ImmutableArray<(IrVarId Variable, IrTypeId Type)> Variables
        {
            get;
        }
        internal ArithExpr LongMin => _longMin ??= _owner.Own(
            _context.MkInt(long.MinValue));
        internal ArithExpr LongMax => _longMax ??= _owner.Own(
            _context.MkInt(long.MaxValue));
        internal ArithExpr Zero => _zero ??= _owner.Own(_context.MkInt(0));
        internal ArithExpr MinusOne => _minusOne ??= _owner.Own(
            _context.MkInt(-1));

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

            var encoded = term switch
            {
                IrBooleanTerm boolean => Defined(
                    _owner.Own(boolean.Value ? _context.MkTrue() : _context.MkFalse())),
                // Typed bitvectors have separate semantics; the legacy encoder
                // must abstain until the candidate BV encoder is selected.
                IrIntegerTerm integer when integer.Type == _factory.IntegerType =>
                    Defined(_owner.Own(_context.MkInt(integer.Value))),
                IrStringTerm text => EncodeString(text),
                IrVariableTerm variable => Defined(GetVariable(variable.Variable)),
                IrUnaryTerm unary => EncodeUnary(unary),
                IrBinaryTerm binary => EncodeBinary(binary),
                IrConditionalTerm conditional => EncodeConditional(conditional),
                _ => throw new UnsupportedIrEncodingException()
            };
            _encoded.Add(term.Id, encoded);
            return encoded;
        }

        private EncodedValue EncodeString(IrStringTerm text)
        {
            var value = _factory.GetString(text.Value);
            if (!_stringConstants.TryGetValue(value, out var encoded))
            {
                // The backend only supports string equality and conditionals.
                // Interning each ordinal value as a distinct integer keeps the
                // value out of Z3's native string parser and P/Invoke boundary.
                encoded = _owner.Own(_context.MkInt(_stringConstants.Count));
                _stringConstants.Add(value, encoded);
            }

            return Defined(encoded);
        }

        private EncodedValue EncodeUnary(IrUnaryTerm unary)
        {
            var operand = Encode(unary.Operand);
            return unary.Operator switch
            {
                IrUnaryOperator.Not when operand.Value is BoolExpr boolean =>
                    new EncodedValue(_owner.Own(_context.MkNot(boolean)), operand.Defined),
                IrUnaryOperator.Negate when operand.Value is ArithExpr integer =>
                    Bounded(_owner.Own(_context.MkUnaryMinus(integer)), operand.Defined),
                _ => throw new UnsupportedIrEncodingException()
            };
        }

        private EncodedValue EncodeBinary(IrBinaryTerm binary)
        {
            var left = Encode(binary.Left);
            var right = Encode(binary.Right);
            if (binary.Operator == IrBinaryOperator.AndAlso &&
                left.Value is BoolExpr leftBoolean &&
                right.Value is BoolExpr rightBoolean)
            {
                var value = _owner.Own(_context.MkAnd(leftBoolean, rightBoolean));
                var shortCircuitDefined = _owner.Own(_context.MkAnd(
                    left.Defined,
                    _owner.Own(_context.MkOr(
                        _owner.Own(_context.MkNot(leftBoolean)),
                        right.Defined))));
                return new EncodedValue(value, shortCircuitDefined);
            }

            if (binary.Operator == IrBinaryOperator.OrElse &&
                left.Value is BoolExpr leftOrBoolean &&
                right.Value is BoolExpr rightOrBoolean)
            {
                var value = _owner.Own(_context.MkOr(leftOrBoolean, rightOrBoolean));
                var shortCircuitDefined = _owner.Own(_context.MkAnd(
                    left.Defined,
                    _owner.Own(_context.MkOr(leftOrBoolean, right.Defined))));
                return new EncodedValue(value, shortCircuitDefined);
            }

            var defined = _owner.Own(_context.MkAnd(left.Defined, right.Defined));
            if (binary.Operator == IrBinaryOperator.Equal)
            {
                return new EncodedValue(_owner.Own(_context.MkEq(left.Value, right.Value)), defined);
            }

            if (binary.Operator == IrBinaryOperator.NotEqual)
            {
                return new EncodedValue(
                    _owner.Own(_context.MkNot(_owner.Own(_context.MkEq(left.Value, right.Value)))), defined);
            }

            var leftInteger = Integer(left);
            var rightInteger = Integer(right);
            return binary.Operator switch
            {
                IrBinaryOperator.Add => Bounded(_owner.Own(_context.MkAdd(leftInteger, rightInteger)), defined),
                IrBinaryOperator.Subtract => Bounded(_owner.Own(_context.MkSub(leftInteger, rightInteger)), defined),
                IrBinaryOperator.Multiply => Bounded(_owner.Own(_context.MkMul(leftInteger, rightInteger)), defined),
                IrBinaryOperator.Divide or IrBinaryOperator.Remainder =>
                    EncodeDivision(binary.Operator, leftInteger, rightInteger, defined),
                IrBinaryOperator.LessThan => new EncodedValue(_owner.Own(_context.MkLt(leftInteger, rightInteger)), defined),
                IrBinaryOperator.LessThanOrEqual => new EncodedValue(_owner.Own(_context.MkLe(leftInteger, rightInteger)), defined),
                IrBinaryOperator.GreaterThan => new EncodedValue(_owner.Own(_context.MkGt(leftInteger, rightInteger)), defined),
                IrBinaryOperator.GreaterThanOrEqual => new EncodedValue(_owner.Own(_context.MkGe(leftInteger, rightInteger)), defined),
                _ => throw new UnsupportedIrEncodingException()
            };
        }

        private EncodedValue EncodeDivision(
            IrBinaryOperator @operator, ArithExpr leftInteger,
            ArithExpr rightInteger, BoolExpr defined)
        {
            var quotient = DivideTowardZero(leftInteger, rightInteger);
            var result = @operator == IrBinaryOperator.Divide
                ? quotient
                : _owner.Own(_context.MkSub(
                    leftInteger,
                    _owner.Own(_context.MkMul(quotient, rightInteger))));
            return Bounded(result,
                _owner.Own(_context.MkAnd(defined, DivisionDefined(leftInteger, rightInteger))));
        }

        private EncodedValue EncodeConditional(IrConditionalTerm conditional)
        {
            var condition = EncodeBoolean(conditional.Condition);
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

            var value = _owner.Own(_context.MkITE(
                condition.Value,
                whenTrue.Value,
                whenFalse.Value));
            var branchDefined = (BoolExpr)_owner.Own(_context.MkITE(
                condition.Value,
                whenTrue.Defined,
                whenFalse.Defined));
            var defined = _owner.Own(_context.MkAnd(condition.Defined, branchDefined));
            return new EncodedValue(value, defined);
        }

        private EncodedValue Defined(Expr expression)
        {
            return new(expression, _defined ??= _owner.Own(_context.MkTrue()));
        }

        private EncodedValue Bounded(ArithExpr expression, BoolExpr defined)
        {
            var lowerBound = _owner.Own(_context.MkGe(
                expression, LongMin));
            var upperBound = _owner.Own(_context.MkLe(
                expression, LongMax));
            return new(expression, _owner.Own(_context.MkAnd(
                defined,
                lowerBound,
                upperBound)));
        }

        private static ArithExpr Integer(EncodedValue value)
        {
            return value.Value as ArithExpr ?? throw new UnsupportedIrEncodingException();
        }

        private ArithExpr DivideTowardZero(ArithExpr left, ArithExpr right)
        {
            var zero = Zero;
            var leftMagnitude = (ArithExpr)_owner.Own(_context.MkITE(
                _owner.Own(_context.MkGe(left, zero)),
                left,
                _owner.Own(_context.MkUnaryMinus(left))));
            var rightMagnitude = (ArithExpr)_owner.Own(_context.MkITE(
                _owner.Own(_context.MkGe(right, zero)),
                right,
                _owner.Own(_context.MkUnaryMinus(right))));
            var magnitude = _owner.Own(_context.MkDiv(leftMagnitude, rightMagnitude));
            var signsDiffer = _owner.Own(_context.MkXor(
                _owner.Own(_context.MkLt(left, zero)),
                _owner.Own(_context.MkLt(right, zero))));
            return (ArithExpr)_owner.Own(_context.MkITE(
                signsDiffer,
                _owner.Own(_context.MkUnaryMinus(magnitude)),
                magnitude));
        }

        private BoolExpr DivisionDefined(ArithExpr left, ArithExpr right)
        {
            var nonzero = _owner.Own(_context.MkNot(_owner.Own(_context.MkEq(
                right, Zero))));
            var notOverflow = _owner.Own(_context.MkNot(_owner.Own(_context.MkAnd(
                _owner.Own(_context.MkEq(
                    left, LongMin)),
                _owner.Own(_context.MkEq(
                    right, MinusOne))))));
            return _owner.Own(_context.MkAnd(nonzero, notOverflow));
        }
    }

    private readonly struct EncodedValue(Expr value, BoolExpr defined)
    {
        internal Expr Value { get; } = value;
        internal BoolExpr Defined { get; } = defined;
    }

    private readonly struct EncodedBoolean(BoolExpr value, BoolExpr defined)
    {
        internal BoolExpr Value { get; } = value;
        internal BoolExpr Defined { get; } = defined;
    }

}
