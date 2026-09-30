namespace SharpProof.Smt;

// Candidate-only total scalar solver. Each query activates its own assumption
// subset and goal; inactive body facts cannot alter an entry feasibility check.
public sealed class CallableSolverSession : ISmtBackend, IDisposable
{
    private readonly IrFactory _factory;
    private readonly IrSmtBackendOptions _options;
    private readonly SmtNativeRunner _runner;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213", Justification = "The native runner drains checks and invokes DisposeOwned before releasing its context.")]
    private readonly Solver _solver;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213", Justification = "The native runner drains checks and invokes DisposeOwned before releasing its context.")]
    private readonly Z3ExpressionOwner _owner = new();
    private readonly BvEncoder _encoder;
    private readonly Dictionary<Assumption, BoolExpr> _assumptions = [];
    private readonly Dictionary<VerificationQuery, BoolExpr> _goals = [];
    private long _consumedResourceCount;

    public CallableSolverSession(IrFactory factory, IrSmtBackendOptions options)
        : this(factory, options, static () => new Context()) { }

    internal CallableSolverSession(IrFactory factory, IrSmtBackendOptions options, Func<Context> createContext)
    {
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        _options = ArgumentNullGuard.NotNull(options, nameof(options));
        if (factory.Semantics != IrExecutionSemantics.Total)
        {
            throw new ArgumentException("A callable bitvector session requires Total IR semantics.", nameof(factory));
        }
        _runner = new SmtNativeRunner(createContext, retireAfterFailure: true, disposeOwned: DisposeOwned);
        try
        {
            _solver = _runner.Context.MkSolver();
            _encoder = new BvEncoder(_runner.Context, factory, _owner);
        }
        catch { _runner.Dispose(); throw; }
    }

    public long ConsumedResourceCount => Interlocked.Read(ref _consumedResourceCount);

    public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(query, nameof(query));
        return _runner.CheckAsync(() => CheckCore(query, cancellationToken), cancellationToken);
    }

    private BackendCheckResult CheckCore(VerificationQuery query, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(query.Factory, _factory))
        {
            return BackendCheckResult.Unknown(BackendFailureReason.UnsupportedEncoding);
        }
        var meter = new SmtQueryResourceMeter(_options.QueryRlimit, cancellationToken);
        try
        {
            _encoder.ValidateQuery(query, meter, cancellationToken);
            var active = new Dictionary<string, int>(StringComparer.Ordinal);
            var selectors = new List<Expr>(query.Assumptions.Length + 1);
            for (var index = 0; index < query.Assumptions.Length; index++)
            {
                meter.Consume();
                var assumption = query.Assumptions[index];
                if (!_assumptions.TryGetValue(assumption, out var selector))
                {
                    var predicate = _encoder.EncodeBoolean(assumption.Predicate, meter);
                    selector = _owner.Own(_runner.Context.MkBoolConst("a" + _assumptions.Count.ToString(CultureInfo.InvariantCulture)));
                    _solver.Assert(_owner.Own(_runner.Context.MkImplies(selector, predicate)));
                    _assumptions.Add(assumption, selector);
                }
                var name = selector.ToString();
                if (!active.ContainsKey(name))
                {
                    active.Add(name, index);
                    selectors.Add(selector);
                }
            }
            if (!_goals.TryGetValue(query, out var goalSelector))
            {
                var goal = _encoder.EncodeBoolean(query.Goal.Predicate, meter);
                meter.Consume();
                goalSelector = _owner.Own(_runner.Context.MkBoolConst("g" + _goals.Count.ToString(CultureInfo.InvariantCulture)));
                _solver.Assert(_owner.Own(_runner.Context.MkImplies(goalSelector,
                    _owner.Own(_runner.Context.MkNot(goal)))));
                _goals.Add(query, goalSelector);
            }
            selectors.Add(goalSelector);
            using var parameters = _runner.Context.MkParams();
            IrSmtBackend.AddOwnedParameter(parameters, _runner.Context.MkSymbol("rlimit"), meter.GetRemainingBudget());
            _solver.Parameters = parameters;
            var status = SmtNativeCheck.Run(_solver, selectors.ToArray(), meter);
            return status switch
            {
                Status.UNSATISFIABLE => DecodeCore(_solver.UnsatCore, active, goalSelector.ToString(), meter),
                Status.SATISFIABLE => CreateModel(query, meter),
                _ => BackendCheckResult.Unknown(IrSmtBackend.ClassifyUnknown(_solver.ReasonUnknown))
            };
        }
        finally
        {
            Interlocked.Exchange(ref _consumedResourceCount,
                checked(Interlocked.Read(ref _consumedResourceCount) + meter.Consumed));
        }
    }

    internal static BackendCheckResult DecodeCore<T>(IReadOnlyList<T> core, IReadOnlyDictionary<string, int> active,
        string goal, SmtQueryResourceMeter meter) where T : IDisposable
    {
        var indices = new HashSet<int>();
        try
        {
            foreach (var expression in core)
            {
                meter.Consume();
                var name = expression.ToString()!;
                if (string.Equals(name, goal, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!active.TryGetValue(name, out var index))
                {
                    return BackendCheckResult.Unknown(BackendFailureReason.MalformedResult);
                }
                indices.Add(index);
            }
            return BackendCheckResult.Unsatisfiable(indices.OrderBy(static index => index));
        }
        finally
        {
            foreach (var expression in core)
            {
                expression.Dispose();
            }
        }
    }

    private BackendCheckResult CreateModel(VerificationQuery query, SmtQueryResourceMeter meter)
    {
        using var model = _solver.Model;
        var values = new Dictionary<IrVarId, IrValue>();
        foreach (var variable in query.ModelVariables)
        {
            meter.Consume();
            using var evaluated = model.Evaluate(_encoder.GetVariable(variable, meter), true);
            var value = BvEncoder.CreateValue(_factory, _factory.GetVariableInfo(variable).Type, evaluated);
            if (value == null)
            {
                return BackendCheckResult.Unknown(BackendFailureReason.MalformedResult);
            }
            values.Add(variable, value);
        }
        return BackendCheckResult.Satisfiable(new BackendModel(values));
    }

    private void DisposeOwned()
    {
        try
        {
            _solver?.Dispose();
        }
        finally
        {
            _owner.Dispose();
        }
    }

    public void Dispose()
    {
        _runner.Dispose();
    }
}
