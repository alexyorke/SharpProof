namespace SharpProof.Smt;

public sealed class IrSmtBackend : ISmtBackend, IDisposable
{
    private const int MaximumEncodingDepth = 256;
    private const int MaximumMalformedModelRetries = 4;
    private const int WellFormedUtf16PrefixLength = 1;
    internal const int MaximumDecodedStringLength = 1_000_000;
    private readonly Context _context;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly IrSmtBackendOptions _options;
    private readonly int _maximumDecodedStringLength;
    private readonly Action<int>? _stringLiteralProgress;
    private long _consumedResourceCount;
    private long _lastResourceSnapshot;
    private bool _resourceAccountingExhausted;
    private int _activeCheckCount;
    private bool _interrupted;
    private bool _disposed;

    public IrSmtBackend()
        : this(new IrSmtBackendOptions(), MaximumDecodedStringLength)
    {
    }

    public IrSmtBackend(IrSmtBackendOptions options)
        : this(options, MaximumDecodedStringLength)
    {
    }

    internal IrSmtBackend(
        IrSmtBackendOptions options,
        int maximumDecodedStringLength)
        : this(options, maximumDecodedStringLength, static () => new Context())
    {
    }

    internal IrSmtBackend(
        IrSmtBackendOptions options,
        int maximumDecodedStringLength,
        Func<Context> contextFactory,
        Action<int>? stringLiteralProgress = null)
    {
        _options = ArgumentNullGuard.NotNull(options, nameof(options));
        if (maximumDecodedStringLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDecodedStringLength));
        }

        contextFactory = ArgumentNullGuard.NotNull(contextFactory, nameof(contextFactory));
        _maximumDecodedStringLength = maximumDecodedStringLength;
        _stringLiteralProgress = stringLiteralProgress;
        _context = contextFactory();
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
        return CheckAsyncCore(query, cancellationToken);
    }

    private async Task<BackendCheckResult> CheckAsyncCore(
        VerificationQuery query,
        CancellationToken cancellationToken)
    {
        await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Keep the native Z3 call on a worker thread, but perform queue
            // admission asynchronously so canceled waiters never occupy one.
            return await Task.Run(() =>
            {
                Interlocked.Increment(ref _activeCheckCount);
                try
                {
                    lock (_gate)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Volatile.Write(ref _interrupted, false);
                        if (_disposed || Volatile.Read(ref _interrupted))
                        {
                            return BackendCheckResult.Unknown(BackendFailureReason.Unavailable);
                        }

                        if (_resourceAccountingExhausted)
                        {
                            return BackendCheckResult.Unknown(BackendFailureReason.ResourceLimit);
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
                        catch (UnsupportedIrEncodingException)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return BackendCheckResult.Unknown(BackendFailureReason.UnsupportedEncoding);
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
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private void Interrupt()
    {
        Volatile.Write(ref _interrupted, true);
        _context.Interrupt();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _context.Dispose();
        }
    }

    private BackendCheckResult CheckCore(
        VerificationQuery query,
        CancellationToken cancellationToken)
    {
        using var owner = new Z3ExpressionOwner();
        var encoder = new QueryEncoder(
            _context,
            query,
            owner,
            cancellationToken,
            _maximumDecodedStringLength,
            _stringLiteralProgress);
        using var solver = _context.MkSolver();
        using var parameters = _context.MkParams();
        using var rlimit = _context.MkSymbol("rlimit");
        parameters.Add(rlimit, _options.QueryRlimit);
        solver.Parameters = parameters;

        foreach (var variable in encoder.IntegerVariables)
        {
            var expression = (ArithExpr)encoder.GetVariable(variable);
            solver.Assert(owner.Own(_context.MkGe(
                expression,
                owner.Own(_context.MkInt(long.MinValue)))));
            solver.Assert(owner.Own(_context.MkLe(
                expression,
                owner.Own(_context.MkInt(long.MaxValue)))));
        }

        foreach (var variable in encoder.StringVariables)
        {
            solver.Assert(encoder.CreateWellFormedUtf16PrefixConstraint(variable));
        }

        var tracked = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < query.Assumptions.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var encoded = encoder.EncodeBoolean(query.Assumptions[index].Predicate);
            var labelName = "a" + index.ToString(CultureInfo.InvariantCulture);
            var label = owner.Own(_context.MkBoolConst(labelName));
            solver.AssertAndTrack(
                owner.Own(_context.MkAnd(encoded.Defined, encoded.Value)),
                label);
            tracked.Add(labelName, index);
        }

        var goal = encoder.EncodeBoolean(query.Goal.Predicate);
        var goalDefined = owner.Own((BoolExpr)goal.Defined.Simplify());
        var goalValue = owner.Own((BoolExpr)goal.Value.Simplify());
        solver.Assert(
            owner.Own(_context.MkNot(
                owner.Own(_context.MkAnd(goalDefined, goalValue)))));
        cancellationToken.ThrowIfCancellationRequested();
        var malformedModelRetries = 0;
        while (true)
        {
            var status = solver.Check();
            AccountResources(solver);
            cancellationToken.ThrowIfCancellationRequested();
            if (status == Status.UNSATISFIABLE)
            {
                return CreateUnsatisfiable(solver, tracked);
            }

            if (status != Status.SATISFIABLE)
            {
                return BackendCheckResult.Unknown(
                    ClassifyUnknown(solver.ReasonUnknown));
            }

            var result = CreateSatisfiable(
                query,
                encoder,
                solver,
                cancellationToken,
                out var excludedMalformedModel);
            if (!excludedMalformedModel)
            {
                return result;
            }

            malformedModelRetries++;
            if (malformedModelRetries >= MaximumMalformedModelRetries)
            {
                return BackendCheckResult.Unknown(
                    BackendFailureReason.MalformedResult);
            }
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

    private void AccountResources(Solver solver)
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

            var observed = entry.UIntValue;
            var delta = observed >= _lastResourceSnapshot
                ? observed - _lastResourceSnapshot
                : observed;
            if (!TryAddResourceCount(_consumedResourceCount, delta, out var total))
            {
                _consumedResourceCount = long.MaxValue;
                _resourceAccountingExhausted = true;
            }
            else
            {
                _consumedResourceCount = total;
            }

            _lastResourceSnapshot = observed;
            return;
        }
    }

    internal static long AddResourceCount(long consumed, long observed)
    {
        return TryAddResourceCount(consumed, observed, out var total)
            ? total
            : long.MaxValue;
    }

    private static bool TryAddResourceCount(
        long consumed,
        long observed,
        out long total)
    {
        if (consumed < 0 || observed < 0)
        {
            throw new ArgumentOutOfRangeException();
        }

        if (observed > long.MaxValue - consumed)
        {
            total = long.MaxValue;
            return false;
        }

        total = consumed + observed;
        return true;
    }

    internal static long AddResourceSnapshot(
        long consumed,
        long previousSnapshot,
        long observedSnapshot)
    {
        var delta = observedSnapshot >= previousSnapshot
            ? observedSnapshot - previousSnapshot
            : observedSnapshot;
        return AddResourceCount(consumed, delta);
    }

    private static BackendCheckResult CreateUnsatisfiable(
        Solver solver,
        Dictionary<string, int> tracked)
    {
        var expressions = solver.UnsatCore;
        return CreateUnsatisfiable(
            expressions,
            tracked,
            static expression => expression.ToString());
    }

    internal static BackendCheckResult CreateUnsatisfiable<T>(
        IReadOnlyList<T> expressions,
        IReadOnlyDictionary<string, int> tracked,
        Func<T, string> format)
        where T : IDisposable
    {
        var core = ImmutableArray.CreateBuilder<int>();
        try
        {
            foreach (var expression in expressions)
            {
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
        CancellationToken cancellationToken,
        out bool excludedMalformedModel)
    {
        excludedMalformedModel = false;
        using var model = solver.Model;
        var assignments = ImmutableArray.CreateBuilder<KeyValuePair<IrVarId, IrValue>>();
        foreach (var variable in encoder.Variables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var evaluated = model.Evaluate(encoder.GetVariable(variable), true);
            using var evaluatedNull = encoder.GetNullVariable(variable) is { } nullVariable
                ? model.Evaluate(nullVariable, true)
                : null;
            if (!encoder.TryCreateValue(
                    query.Factory,
                    variable,
                    evaluated,
                    evaluatedNull,
                model,
                cancellationToken,
                out var value,
                out var failureReason))
            {
                if (failureReason == BackendFailureReason.MalformedResult &&
                    query.Factory.GetVariableInfo(variable).Type ==
                    query.Factory.StringType)
                {
                    encoder.ExcludeModel(solver, model);
                    excludedMalformedModel = true;
                }
                return BackendCheckResult.Unknown(failureReason);
            }

            assignments.Add(new KeyValuePair<IrVarId, IrValue>(variable, value!));
        }
        return BackendCheckResult.Satisfiable(new BackendModel(assignments));
    }

    private sealed class QueryEncoder
    {
        private readonly Context _context;
        private readonly Z3ExpressionOwner _owner;
        private readonly Dictionary<IrId, EncodedValue> _encoded = [];
        private readonly Dictionary<IrVarId, Expr> _variables = [];
        private readonly Dictionary<IrVarId, BoolExpr> _nullVariables = [];
        private readonly IrFactory _factory;
        private readonly SeqSort _stringSort;
        private readonly int _maximumDecodedStringLength;
        private readonly CancellationToken _cancellationToken;
        private readonly Action<int>? _stringLiteralProgress;

        internal QueryEncoder(
            Context context,
            VerificationQuery query,
            Z3ExpressionOwner owner,
            CancellationToken cancellationToken,
            int maximumDecodedStringLength,
            Action<int>? stringLiteralProgress)
        {
            _context = context;
            _owner = owner;
            _factory = query.Factory;
            _maximumDecodedStringLength = maximumDecodedStringLength;
            _cancellationToken = cancellationToken;
            _stringLiteralProgress = stringLiteralProgress;
            _stringSort = owner.OwnSort(
                (SeqSort)_context.MkSeqSort(_context.IntSort));
            var maximumDepths = new Dictionary<IrId, int>();
            foreach (var assumption in query.Assumptions)
            {
                ValidateDepth(
                    assumption.Predicate,
                    cancellationToken,
                    maximumDepths);
            }
            ValidateDepth(
                query.Goal.Predicate,
                cancellationToken,
                maximumDepths);
            Variables = query.ModelVariables;
            IntegerVariables = [.. Variables.Where(variable =>
                _factory.GetVariableInfo(variable).Type == _factory.IntegerType)];
            foreach (var variable in Variables)
            {
                var type = _factory.GetVariableInfo(variable).Type;
                if (type != _factory.BooleanType &&
                    type != _factory.IntegerType &&
                    type != _factory.StringType)
                {
                    throw new UnsupportedIrEncodingException();
                }
            }
            for (var index = 0; index < Variables.Length; index++)
            {
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
                else if (type == _factory.StringType)
                {
                    expression = Own(_context.MkConst(name, _stringSort));
                    _nullVariables.Add(
                        variable,
                        Own(_context.MkBoolConst(name + "_null")));
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
            CancellationToken cancellationToken,
            Dictionary<IrId, int> maximumDepths)
        {
            var pending = new Stack<(IrTerm Term, int Depth)>();
            pending.Push((root, 1));
            while (pending.Count != 0)
            {
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

        internal BoolExpr? GetNullVariable(IrVarId variable)
        {
            return _nullVariables.TryGetValue(variable, out var value)
                ? value
                : null;
        }

        internal IEnumerable<IrVarId> StringVariables => _nullVariables.Keys;

        internal SeqExpr GetStringVariable(IrVarId variable)
        {
            return (SeqExpr)_variables[variable];
        }

        // Keep the first code unit in the same domain as IrFactory.  Z3's
        // sequence solver can exhaust its unfolding budget on a full regular
        // language over an unconstrained Seq<Int>; the model-replay guard below
        // rejects any malformed suffix that is not covered by this cheap prefix.
        internal BoolExpr CreateWellFormedUtf16PrefixConstraint(IrVarId variable)
        {
            var sequence = GetStringVariable(variable);
            var length = Own(_context.MkLength(sequence));
            var validUnits = new BoolExpr[WellFormedUtf16PrefixLength];
            for (var index = 0; index < validUnits.Length; index++)
            {
                var indexExpression = Own(_context.MkInt(index));
                var unit = Own((ArithExpr)_context.MkNth(sequence, indexExpression));
                var ordinaryLow = Own(_context.MkAnd(
                    Own(_context.MkGe(unit, Own(_context.MkInt(0)))),
                    Own(_context.MkLe(unit, Own(_context.MkInt(0xD7FF))))));
                var ordinaryHigh = Own(_context.MkAnd(
                    Own(_context.MkGe(unit, Own(_context.MkInt(0xE000)))),
                    Own(_context.MkLe(unit, Own(_context.MkInt(0xFFFF))))));
                var ordinary = Own(_context.MkOr(ordinaryLow, ordinaryHigh));
                var highLower = Own(_context.MkGe(
                    unit,
                    Own(_context.MkInt(0xD800))));
                var highUpper = Own(_context.MkLe(
                    unit,
                    Own(_context.MkInt(0xDBFF))));
                var high = Own(_context.MkAnd(highLower, highUpper));
                var lowLower = Own(_context.MkGe(
                    unit,
                    Own(_context.MkInt(0xDC00))));
                var lowUpper = Own(_context.MkLe(
                    unit,
                    Own(_context.MkInt(0xDFFF))));
                var low = Own(_context.MkAnd(lowLower, lowUpper));
                var pairStart = Own(_context.MkAnd(
                    high,
                    Own(_context.MkGt(
                        length,
                        Own(_context.MkInt(index + 1))))));
                var pairContinuation = index == 0
                    ? Own(_context.MkFalse())
                    : low;
                validUnits[index] = Own(_context.MkImplies(
                    Own(_context.MkGt(length, indexExpression)),
                    Own(_context.MkOr(ordinary, pairStart, pairContinuation))));
            }

            return Own(_context.MkImplies(
                Own(_context.MkNot(GetNullVariable(variable)!)),
                Own(_context.MkAnd(validUnits))));
        }

        internal bool TryCreateValue(
            IrFactory factory,
            IrVarId variable,
            Expr expression,
            Expr? nullExpression,
            Model model,
            CancellationToken cancellationToken,
            out IrValue? value,
            out BackendFailureReason failureReason)
        {
            failureReason = BackendFailureReason.MalformedResult;
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
            else if (type == factory.StringType &&
                     expression is SeqExpr sequence &&
                     nullExpression is BoolExpr nullTag)
            {
                value = DecodeString(
                    factory,
                    sequence,
                    nullTag,
                    model,
                    cancellationToken);
                if (value == null)
                {
                    using var lengthExpression = _context.MkLength(sequence);
                    using var evaluatedLengthExpression = model.Evaluate(
                        lengthExpression,
                        true);
                    if (evaluatedLengthExpression is IntNum lengthValue &&
                        int.TryParse(
                            lengthValue.ToString(),
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var length) &&
                        length > _maximumDecodedStringLength)
                    {
                        failureReason = BackendFailureReason.ResourceLimit;
                    }
                }
            }
            else
            {
                value = null;
            }

            return value != null;
        }

        internal void ExcludeModel(Solver solver, Model model)
        {
            var equalities = new List<BoolExpr>(Variables.Length * 2);
            foreach (var variable in Variables)
            {
                var expression = GetVariable(variable);
                using var evaluated = model.Evaluate(expression, true);
                equalities.Add(_context.MkEq(expression, evaluated));
                if (GetNullVariable(variable) is { } nullVariable)
                {
                    using var evaluatedNull = model.Evaluate(nullVariable, true);
                    equalities.Add(
                        _context.MkEq(nullVariable, evaluatedNull));
                }
            }

            using var conjunction = _context.MkAnd(equalities.ToArray());
            using var exclusion = _context.MkNot(conjunction);
            solver.Assert(exclusion);
            foreach (var equality in equalities)
            {
                equality.Dispose();
            }
        }

        private IrValue? DecodeString(
            IrFactory factory,
            SeqExpr sequence,
            BoolExpr nullTag,
            Model model,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var evaluatedNullExpression = model.Evaluate(nullTag, true);
            if (evaluatedNullExpression is not BoolExpr evaluatedNull)
            {
                return null;
            }

            if (evaluatedNull.IsTrue)
            {
                return factory.CreateNullValue(factory.StringType);
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var lengthExpression = _context.MkLength(sequence);
            using var evaluatedLengthExpression = model.Evaluate(
                lengthExpression,
                true);
            if (!evaluatedNull.IsFalse ||
                evaluatedLengthExpression is not IntNum lengthValue ||
                !int.TryParse(lengthValue.ToString(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var length) ||
                length < 0 ||
                length > _maximumDecodedStringLength)
            {
                return null;
            }

            var chars = new char[length];
            for (var index = 0; index < chars.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var indexExpression = _context.MkInt(index);
                using var element = _context.MkNth(sequence, indexExpression);
                using var evaluatedElementExpression = model.Evaluate(
                    element,
                    true);
                if (evaluatedElementExpression is not IntNum codeUnit ||
                    !int.TryParse(codeUnit.ToString(), NumberStyles.None,
                        CultureInfo.InvariantCulture, out var number) ||
                    number is < char.MinValue or > char.MaxValue)
                {
                    return null;
                }

                chars[index] = (char)number;
            }

            try
            {
                return factory.CreateStringValue(new string(chars));
            }
            catch (ArgumentException)
            {
                return null;
            }
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
                    Own(boolean.Value ? _context.MkTrue() : _context.MkFalse())),
                IrIntegerTerm integer => Defined(Own(_context.MkInt(integer.Value))),
                IrStringTerm text => Defined(
                    EncodeStringLiteral(_factory.GetString(text.Value)),
                    Own(_context.MkFalse())),
                IrNullTerm nullTerm when nullTerm.Type == _factory.StringType =>
                    new EncodedValue(
                        Own(_context.MkEmptySeq(_stringSort)),
                        Own(_context.MkTrue()),
                        Own(_context.MkTrue())),
                IrVariableTerm variable when variable.Type == _factory.StringType =>
                    Defined(GetVariable(variable.Variable), GetNullVariable(variable.Variable)),
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

            if (binary.Operator == IrBinaryOperator.StringConcat &&
                left.Value is SeqExpr leftString &&
                right.Value is SeqExpr rightString)
            {
                var leftOperand = StringConcatenationOperand(left, leftString);
                var rightOperand = StringConcatenationOperand(right, rightString);
                return new EncodedValue(
                    Own(_context.MkConcat(leftOperand, rightOperand)),
                    Own(_context.MkAnd(left.Defined, right.Defined)),
                    Own(_context.MkFalse()));
            }

            var defined = Own(_context.MkAnd(left.Defined, right.Defined));
            return binary.Operator switch
            {
                IrBinaryOperator.Add => Bounded(Own(_context.MkAdd(Integer(left), Integer(right))), defined),
                IrBinaryOperator.Subtract => Bounded(Own(_context.MkSub(Integer(left), Integer(right))), defined),
                IrBinaryOperator.Multiply => Bounded(Own(_context.MkMul(Integer(left), Integer(right))), defined),
                IrBinaryOperator.Divide or IrBinaryOperator.Remainder =>
                    EncodeDivision(binary.Operator, left, right, defined),
                IrBinaryOperator.Equal => EncodeEquality(left, right, defined),
                IrBinaryOperator.NotEqual => EncodeNotEquality(left, right, defined),
                IrBinaryOperator.LessThan => Comparison(Own(_context.MkLt(Integer(left), Integer(right))), defined),
                IrBinaryOperator.LessThanOrEqual => Comparison(Own(_context.MkLe(Integer(left), Integer(right))), defined),
                IrBinaryOperator.GreaterThan => Comparison(Own(_context.MkGt(Integer(left), Integer(right))), defined),
                IrBinaryOperator.GreaterThanOrEqual => Comparison(Own(_context.MkGe(Integer(left), Integer(right))), defined),
                _ => throw new UnsupportedIrEncodingException()
            };
        }

        private SeqExpr StringConcatenationOperand(
            EncodedValue value,
            SeqExpr sequence)
        {
            return value.IsNull == null
                ? sequence
                : (SeqExpr)Own(_context.MkITE(
                    value.IsNull,
                    Own(_context.MkEmptySeq(_stringSort)),
                    sequence));
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
            var operationDefined = @operator == IrBinaryOperator.Divide
                ? DivisionDefined(leftInteger, rightInteger)
                : RemainderDefined(leftInteger, rightInteger);
            return Bounded(result,
                Own(_context.MkAnd(defined, operationDefined)));
        }

        private EncodedValue EncodeConditional(IrConditionalTerm conditional)
        {
            var condition = EncodeBoolean(conditional.Condition);
            var whenTrue = Encode(conditional.WhenTrue);
            var whenFalse = Encode(conditional.WhenFalse);
            if (!whenTrue.Value.Sort.Equals(whenFalse.Value.Sort))
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
            var nullValue = whenTrue.IsNull != null || whenFalse.IsNull != null
                ? Own((BoolExpr)_context.MkITE(
                    condition.Value,
                    NullFlag(whenTrue),
                    NullFlag(whenFalse)))
                : null;
            return new EncodedValue(value, defined, nullValue);
        }

        private EncodedValue EncodeLength(IrLengthTerm length)
        {
            var value = Encode(length.Value);
            if (value.Value is not SeqExpr sequence)
            {
                throw new UnsupportedIrEncodingException();
            }

            var defined = length.Value.Type == _factory.StringType
                ? Own(_context.MkAnd(
                    value.Defined,
                    Own(_context.MkNot(NullFlag(value)))))
                : value.Defined;
            return Bounded(Own(_context.MkLength(sequence)), defined);
        }

        private SeqExpr EncodeStringLiteral(string value)
        {
            if (value.Length == 0)
            {
                return Own(_context.MkEmptySeq(_stringSort));
            }

            const int chunkSize = 256;
            var chunks = new List<SeqExpr>(
                (value.Length + chunkSize - 1) / chunkSize);
            for (var offset = 0; offset < value.Length; offset += chunkSize)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(chunkSize, value.Length - offset);
                var units = new SeqExpr[count];
                for (var index = 0; index < count; index++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    units[index] = (SeqExpr)Own(_context.MkUnit(
                        Own(_context.MkInt(value[offset + index]))));
                    _stringLiteralProgress?.Invoke(offset + index + 1);
                }

                chunks.Add(count == 1
                    ? units[0]
                    : Own(_context.MkConcat(units)));
            }

            return chunks.Count == 1
                ? chunks[0]
                : Own(_context.MkConcat(chunks.ToArray()));
        }

        private EncodedValue EncodeEquality(
            EncodedValue left,
            EncodedValue right,
            BoolExpr defined)
        {
            if (left.IsNull?.IsTrue == true)
            {
                return new EncodedValue(NullFlag(right), defined);
            }

            if (right.IsNull?.IsTrue == true)
            {
                return new EncodedValue(NullFlag(left), defined);
            }

            if (left.Value is SeqExpr && right.Value is SeqExpr)
            {
                var payloadEqual = Own(_context.MkEq(left.Value, right.Value));
                var bothNull = Own(_context.MkAnd(
                    NullFlag(left),
                    NullFlag(right)));
                var bothNonNull = Own(_context.MkAnd(
                    Own(_context.MkNot(NullFlag(left))),
                    Own(_context.MkNot(NullFlag(right))),
                    payloadEqual));
                return new EncodedValue(
                    Own(_context.MkOr(bothNull, bothNonNull)),
                    defined);
            }

            return new EncodedValue(
                Own(_context.MkEq(left.Value, right.Value)),
                defined);
        }

        private EncodedValue EncodeNotEquality(
            EncodedValue left,
            EncodedValue right,
            BoolExpr defined)
        {
            var equality = EncodeEquality(left, right, defined);
            return new EncodedValue(
                Own(_context.MkNot((BoolExpr)equality.Value)),
                equality.Defined);
        }

        private BoolExpr NullFlag(EncodedValue value)
        {
            return value.IsNull ?? Own(_context.MkFalse());
        }

        private EncodedValue Defined(Expr expression, BoolExpr? isNull = null)
        {
            return new(expression, Own(_context.MkTrue()), isNull);
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
            var nonzero = NonZero(right);
            var notOverflow = Own(_context.MkNot(Own(_context.MkAnd(
                Own(_context.MkEq(
                    left,
                    Own(_context.MkInt(long.MinValue)))),
                Own(_context.MkEq(
                    right,
                    Own(_context.MkInt(-1))))))));
            return Own(_context.MkAnd(nonzero, notOverflow));
        }

        private BoolExpr RemainderDefined(ArithExpr left, ArithExpr right)
        {
            return Own(_context.MkNot(Own(_context.MkOr(
                Own(_context.MkEq(right, Own(_context.MkInt(0)))),
                Own(_context.MkAnd(
                    Own(_context.MkEq(left, Own(_context.MkInt(long.MinValue)))),
                    Own(_context.MkEq(right, Own(_context.MkInt(-1))))))))));
        }

        private BoolExpr NonZero(ArithExpr right)
        {
            return Own(_context.MkNot(Own(_context.MkEq(
                right,
                Own(_context.MkInt(0))))));
        }
    }

    private sealed class EncodedValue(
        Expr value,
        BoolExpr defined,
        BoolExpr? isNull = null)
    {
        internal Expr Value { get; } = value;
        internal BoolExpr Defined { get; } = defined;
        internal BoolExpr? IsNull { get; } = isNull;
    }

    private readonly struct EncodedBoolean(BoolExpr value, BoolExpr defined)
    {
        internal BoolExpr Value { get; } = value;
        internal BoolExpr Defined { get; } = defined;
    }

    private sealed class UnsupportedIrEncodingException : Exception;
}
