namespace SharpProof.Smt;

public sealed class IrSmtBackend(IrSmtBackendOptions options) : ISmtBackend, IDisposable {
    private readonly Context _context = new();
    private readonly object _gate = new();
    private readonly IrSmtBackendOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));
    private long _consumedResourceCount;
    private long _lastObservedResourceCount;
    private bool _disposed;

    public IrSmtBackend()
        : this(new IrSmtBackendOptions()) {
    }

    public long ConsumedResourceCount {
        get {
            lock (_gate)
                return _consumedResourceCount;
        }
    }

    public Task<BackendCheckResult> CheckAsync(
        VerificationQuery query,
        CancellationToken cancellationToken) {
        if (query == null) throw new ArgumentNullException(nameof(query));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) {
            if (_disposed) return Task.FromResult(
                BackendCheckResult.Unknown(BackendFailureReason.Unavailable));
            using var registration = cancellationToken.Register(
                static state => ((Context)state!).Interrupt(),
                _context);
            try {
                var result = CheckCore(query, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(result);
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (UnsupportedIrEncodingException) {
                return Task.FromResult(
                    BackendCheckResult.Unknown(BackendFailureReason.UnsupportedEncoding));
            }
            catch (Exception exception) when (exception is
                Z3Exception or
                InvalidOperationException or
                ArgumentException or
                ArithmeticException) {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(
                    BackendCheckResult.Unknown(BackendFailureReason.InfrastructureFailure));
            }
        }
    }

    public void Dispose() {
        lock (_gate) {
            if (_disposed) return;
            _disposed = true;
            _context.Dispose();
        }
    }

    private BackendCheckResult CheckCore(
        VerificationQuery query,
        CancellationToken cancellationToken) {
        using var encoder = new QueryEncoder(_context, query);
        using var solver = _context.MkSolver();
        using var parameters = _context.MkParams();
        parameters.Add("rlimit", _options.QueryRlimit);
        solver.Parameters = parameters;

        foreach (var variable in encoder.IntegerVariables) {
            var expression = (ArithExpr)encoder.GetVariable(variable);
            solver.Assert(_context.MkGe(expression, _context.MkInt(long.MinValue)));
            solver.Assert(_context.MkLe(expression, _context.MkInt(long.MaxValue)));
        }

        var tracked = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < query.Assumptions.Length; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            var encoded = encoder.EncodeBoolean(query.Assumptions[index].Predicate);
            var labelName = "a" + index.ToString(CultureInfo.InvariantCulture);
            using var label = _context.MkBoolConst(labelName);
            solver.AssertAndTrack(
                _context.MkAnd(encoded.Defined, encoded.Value),
                label);
            tracked.Add(labelName, index);
        }

        var goal = encoder.EncodeBoolean(query.Goal.Predicate);
        solver.Assert(
            _context.MkNot(
                _context.MkAnd(goal.Defined, goal.Value)));
        var status = solver.Check();
        AccountResources(solver);
        cancellationToken.ThrowIfCancellationRequested();
        return status switch {
            Status.UNSATISFIABLE => CreateUnsatisfiable(solver, tracked),
            Status.SATISFIABLE => CreateSatisfiable(query, encoder, solver),
            _ => BackendCheckResult.Unknown(BackendFailureReason.ResourceLimit)
        };
    }

    private void AccountResources(Solver solver) {
        foreach (var entry in solver.Statistics.Entries) {
            if (!string.Equals(
                    entry.Key,
                    "rlimit count",
                    StringComparison.Ordinal) ||
                !entry.IsUInt)
                continue;
            long observed = entry.UIntValue;
            _consumedResourceCount += observed >= _lastObservedResourceCount
                ? observed - _lastObservedResourceCount
                : (1L << 32) - _lastObservedResourceCount + observed;
            _lastObservedResourceCount = observed;
            return;
        }
    }

    private static BackendCheckResult CreateUnsatisfiable(
        Solver solver,
        IReadOnlyDictionary<string, int> tracked) {
        var core = ImmutableArray.CreateBuilder<int>();
        foreach (var expression in solver.UnsatCore) {
            if (!tracked.TryGetValue(expression.ToString(), out var index))
                return BackendCheckResult.Unknown(BackendFailureReason.MalformedResult);
            core.Add(index);
        }
        return BackendCheckResult.Unsatisfiable(
            core.Distinct().OrderBy(static index => index));
    }

    private static BackendCheckResult CreateSatisfiable(
        VerificationQuery query,
        QueryEncoder encoder,
        Solver solver) {
        using var model = solver.Model;
        var assignments = ImmutableArray.CreateBuilder<KeyValuePair<IrVarId, IrValue>>();
        foreach (var variable in encoder.Variables) {
            using var evaluated = model.Evaluate(encoder.GetVariable(variable), true);
            if (!TryCreateValue(query.Factory, variable, evaluated, out var value))
                return BackendCheckResult.Unknown(BackendFailureReason.MalformedResult);
            assignments.Add(new KeyValuePair<IrVarId, IrValue>(variable, value!));
        }
        return BackendCheckResult.Satisfiable(new BackendModel(assignments));
    }

    private static bool TryCreateValue(
        IrFactory factory,
        IrVarId variable,
        Expr expression,
        out IrValue? value) {
        var type = factory.GetVariableInfo(variable).Type;
        if (type == factory.BooleanType) {
            if (expression.IsTrue) {
                value = factory.CreateBooleanValue(true);
                return true;
            }
            if (expression.IsFalse) {
                value = factory.CreateBooleanValue(false);
                return true;
            }
        }
        else if (type == factory.IntegerType && expression is IntNum integer) {
            if (long.TryParse(
                    integer.ToString(),
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var number)) {
                value = factory.CreateIntegerValue(number);
                return true;
            }
        }
        else if (type == factory.StringType && expression.IsString) {
            value = factory.CreateStringValue(expression.String);
            return true;
        }
        value = null;
        return false;
    }

    private sealed class QueryEncoder : IDisposable {
        private readonly Context _context;
        private readonly Dictionary<IrId, EncodedValue> _encoded = [];
        private readonly Dictionary<IrVarId, Expr> _variables = [];
        private readonly IrFactory _factory;

        internal QueryEncoder(Context context, VerificationQuery query) {
            _context = context;
            _factory = query.Factory;
            Variables = [.. CollectVariables(query)
                .OrderBy(static variable => variable.Value)];
            IntegerVariables = [.. Variables.Where(variable =>
                _factory.GetVariableInfo(variable).Type == _factory.IntegerType)];
            for (var index = 0; index < Variables.Length; index++) {
                var variable = Variables[index];
                var name = "v" + index.ToString(CultureInfo.InvariantCulture);
                var type = _factory.GetVariableInfo(variable).Type;
                Expr expression;
                if (type == _factory.BooleanType)
                    expression = _context.MkBoolConst(name);
                else if (type == _factory.IntegerType)
                    expression = _context.MkIntConst(name);
                else
                    throw new UnsupportedIrEncodingException();
                _variables.Add(variable, expression);
            }
        }

        internal ImmutableArray<IrVarId> Variables { get; }
        internal ImmutableArray<IrVarId> IntegerVariables { get; }

        public void Dispose() {
            foreach (var encoded in _encoded.Values) encoded.Dispose();
            foreach (var variable in _variables.Values) variable.Dispose();
        }

        internal Expr GetVariable(IrVarId variable) => _variables[variable];

        internal EncodedBoolean EncodeBoolean(IrTerm term) {
            var encoded = Encode(term);
            if (encoded.Value is not BoolExpr boolean)
                throw new UnsupportedIrEncodingException();
            return new EncodedBoolean(boolean, encoded.Defined);
        }

        private EncodedValue Encode(IrTerm term) {
            if (_encoded.TryGetValue(term.Id, out var existing)) return existing;
            var encoded = term switch {
                IrBooleanTerm boolean => Defined(
                    boolean.Value ? _context.MkTrue() : _context.MkFalse()),
                IrIntegerTerm integer => Defined(_context.MkInt(integer.Value)),
                IrStringTerm text => Defined(_context.MkString(_factory.GetString(text.Value))),
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

        private EncodedValue EncodeUnary(IrUnaryTerm unary) {
            var operand = Encode(unary.Operand);
            return unary.Operator switch {
                IrUnaryOperator.Not when operand.Value is BoolExpr boolean =>
                    new EncodedValue(_context.MkNot(boolean), operand.Defined),
                IrUnaryOperator.Negate when operand.Value is ArithExpr integer =>
                    Bounded(_context.MkUnaryMinus(integer), operand.Defined),
                _ => throw new UnsupportedIrEncodingException()
            };
        }

        private EncodedValue EncodeBinary(IrBinaryTerm binary) {
            var left = Encode(binary.Left);
            var right = Encode(binary.Right);
            if (binary.Operator == IrBinaryOperator.AndAlso &&
                left.Value is BoolExpr leftBoolean &&
                right.Value is BoolExpr rightBoolean)
                return new EncodedValue(
                    _context.MkAnd(leftBoolean, rightBoolean),
                    _context.MkAnd(
                        left.Defined,
                        _context.MkOr(_context.MkNot(leftBoolean), right.Defined)));
            if (binary.Operator == IrBinaryOperator.OrElse &&
                left.Value is BoolExpr leftOrBoolean &&
                right.Value is BoolExpr rightOrBoolean)
                return new EncodedValue(
                    _context.MkOr(leftOrBoolean, rightOrBoolean),
                    _context.MkAnd(
                        left.Defined,
                        _context.MkOr(leftOrBoolean, right.Defined)));
            var defined = _context.MkAnd(left.Defined, right.Defined);
            return binary.Operator switch {
                IrBinaryOperator.Add => Bounded(
                    _context.MkAdd(Integer(left), Integer(right)),
                    defined),
                IrBinaryOperator.Subtract => Bounded(
                    _context.MkSub(Integer(left), Integer(right)),
                    defined),
                IrBinaryOperator.Multiply => Bounded(
                    _context.MkMul(Integer(left), Integer(right)),
                    defined),
                IrBinaryOperator.Divide => EncodeDivide(left, right, defined),
                IrBinaryOperator.Remainder => EncodeRemainder(left, right, defined),
                IrBinaryOperator.Equal => new EncodedValue(
                    _context.MkEq(left.Value, right.Value),
                    defined),
                IrBinaryOperator.NotEqual => new EncodedValue(
                    _context.MkNot(_context.MkEq(left.Value, right.Value)),
                    defined),
                IrBinaryOperator.LessThan => Comparison(
                    _context.MkLt(Integer(left), Integer(right)),
                    defined),
                IrBinaryOperator.LessThanOrEqual => Comparison(
                    _context.MkLe(Integer(left), Integer(right)),
                    defined),
                IrBinaryOperator.GreaterThan => Comparison(
                    _context.MkGt(Integer(left), Integer(right)),
                    defined),
                IrBinaryOperator.GreaterThanOrEqual => Comparison(
                    _context.MkGe(Integer(left), Integer(right)),
                    defined),
                _ => throw new UnsupportedIrEncodingException()
            };
        }

        private EncodedValue EncodeDivide(
            EncodedValue left,
            EncodedValue right,
            BoolExpr defined) {
            var leftInteger = Integer(left);
            var rightInteger = Integer(right);
            var quotient = DivideTowardZero(leftInteger, rightInteger);
            return Bounded(
                quotient,
                _context.MkAnd(defined, DivisionDefined(leftInteger, rightInteger)));
        }

        private EncodedValue EncodeRemainder(
            EncodedValue left,
            EncodedValue right,
            BoolExpr defined) {
            var leftInteger = Integer(left);
            var rightInteger = Integer(right);
            var quotient = DivideTowardZero(leftInteger, rightInteger);
            var remainder = _context.MkSub(
                leftInteger,
                _context.MkMul(quotient, rightInteger));
            return Bounded(
                remainder,
                _context.MkAnd(defined, DivisionDefined(leftInteger, rightInteger)));
        }

        private EncodedValue EncodeConditional(IrConditionalTerm conditional) {
            var condition = EncodeBoolean(conditional.Condition);
            var whenTrue = Encode(conditional.WhenTrue);
            var whenFalse = Encode(conditional.WhenFalse);
            if (!whenTrue.Value.Sort.Equals(whenFalse.Value.Sort))
                throw new UnsupportedIrEncodingException();
            return new EncodedValue(
                _context.MkITE(condition.Value, whenTrue.Value, whenFalse.Value),
                _context.MkAnd(
                    condition.Defined,
                    (BoolExpr)_context.MkITE(
                        condition.Value,
                        whenTrue.Defined,
                        whenFalse.Defined)));
        }

        private EncodedValue EncodeLength(IrLengthTerm length) {
            if (length.Value.Type == _factory.StringType)
                throw new UnsupportedIrEncodingException();
            var value = Encode(length.Value);
            if (value.Value is not SeqExpr sequence)
                throw new UnsupportedIrEncodingException();
            return Bounded(_context.MkLength(sequence), value.Defined);
        }

        private EncodedValue Defined(Expr expression) =>
            new(expression, _context.MkTrue());

        private EncodedValue Bounded(ArithExpr expression, BoolExpr defined) =>
            new(
                expression,
                _context.MkAnd(
                    defined,
                    _context.MkGe(expression, _context.MkInt(long.MinValue)),
                    _context.MkLe(expression, _context.MkInt(long.MaxValue))));

        private static EncodedValue Comparison(BoolExpr expression, BoolExpr defined) =>
            new(expression, defined);

        private static ArithExpr Integer(EncodedValue value) =>
            value.Value as ArithExpr ?? throw new UnsupportedIrEncodingException();

        private ArithExpr DivideTowardZero(ArithExpr left, ArithExpr right) {
            var zero = _context.MkInt(0);
            var leftMagnitude = (ArithExpr)_context.MkITE(
                _context.MkGe(left, zero),
                left,
                _context.MkUnaryMinus(left));
            var rightMagnitude = (ArithExpr)_context.MkITE(
                _context.MkGe(right, zero),
                right,
                _context.MkUnaryMinus(right));
            var magnitude = _context.MkDiv(leftMagnitude, rightMagnitude);
            var signsDiffer = _context.MkXor(
                _context.MkLt(left, zero),
                _context.MkLt(right, zero));
            return (ArithExpr)_context.MkITE(
                signsDiffer,
                _context.MkUnaryMinus(magnitude),
                magnitude);
        }

        private BoolExpr DivisionDefined(ArithExpr left, ArithExpr right) =>
            _context.MkAnd(
                _context.MkNot(_context.MkEq(right, _context.MkInt(0))),
                _context.MkNot(_context.MkAnd(
                    _context.MkEq(left, _context.MkInt(long.MinValue)),
                    _context.MkEq(right, _context.MkInt(-1)))));

        private static IEnumerable<IrVarId> CollectVariables(VerificationQuery query) {
            var seenTerms = new HashSet<IrId>();
            var variables = new HashSet<IrVarId>();
            foreach (var root in query.Assumptions
                         .Select(static assumption => assumption.Predicate)
                         .Append(query.Goal.Predicate))
                CollectVariables(root, seenTerms, variables);
            return variables;
        }

        private static void CollectVariables(
            IrTerm term,
            ISet<IrId> seenTerms,
            ISet<IrVarId> variables) {
            if (!seenTerms.Add(term.Id)) return;
            switch (term) {
                case IrVariableTerm variable:
                    variables.Add(variable.Variable);
                    break;
                case IrOpaqueTerm opaque:
                    if (opaque.Receiver != null)
                        CollectVariables(opaque.Receiver, seenTerms, variables);
                    foreach (var argument in opaque.Arguments)
                        CollectVariables(argument, seenTerms, variables);
                    break;
                case IrUnaryTerm unary:
                    CollectVariables(unary.Operand, seenTerms, variables);
                    break;
                case IrBinaryTerm binary:
                    CollectVariables(binary.Left, seenTerms, variables);
                    CollectVariables(binary.Right, seenTerms, variables);
                    break;
                case IrConditionalTerm conditional:
                    CollectVariables(conditional.Condition, seenTerms, variables);
                    CollectVariables(conditional.WhenTrue, seenTerms, variables);
                    CollectVariables(conditional.WhenFalse, seenTerms, variables);
                    break;
                case IrCastTerm cast:
                    CollectVariables(cast.Operand, seenTerms, variables);
                    break;
                case IrLengthTerm length:
                    CollectVariables(length.Value, seenTerms, variables);
                    break;
                case IrSequenceAccessTerm access:
                    CollectVariables(access.Sequence, seenTerms, variables);
                    CollectVariables(access.Index, seenTerms, variables);
                    break;
            }
        }
    }

    private sealed class EncodedValue : IDisposable {
        internal EncodedValue(Expr value, BoolExpr defined) {
            Value = value;
            Defined = defined;
        }

        internal Expr Value { get; }
        internal BoolExpr Defined { get; }

        public void Dispose() {
            Value.Dispose();
            Defined.Dispose();
        }
    }

    private readonly struct EncodedBoolean(BoolExpr value, BoolExpr defined) {
        internal BoolExpr Value { get; } = value;
        internal BoolExpr Defined { get; } = defined;
    }

    private sealed class UnsupportedIrEncodingException : Exception;
}
