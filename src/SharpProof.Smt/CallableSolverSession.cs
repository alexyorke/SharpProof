namespace SharpProof.Smt;

// Total scalar solver. Each query activates its own assumption
// subset and goal; inactive body facts cannot alter an entry feasibility check.
internal sealed class CallableSolverSession : ISmtBackend, IDisposable
{
    private readonly IrFactory _factory;
    private readonly IrSmtBackendOptions _options;
    private readonly SmtNativeRunner _runner;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213", Justification = "The native runner drains checks and invokes DisposeOwned before releasing its context.")]
    private readonly Solver _solver;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213", Justification = "The native runner drains checks and invokes DisposeOwned before releasing its context.")]
    private readonly Z3ExpressionOwner _owner = new();
    private readonly BvEncoder _encoder;
    private readonly ImmutableDictionary<Assumption, BoolExpr>.Builder _assumptions = ImmutableDictionary.CreateBuilder<Assumption, BoolExpr>();
    private readonly ImmutableDictionary<VerificationQuery, BoolExpr>.Builder _goals = ImmutableDictionary.CreateBuilder<VerificationQuery, BoolExpr>();
    private readonly ImmutableDictionary<VerificationQuery, BoolExpr>.Builder _smallReferences = ImmutableDictionary.CreateBuilder<VerificationQuery, BoolExpr>();
    private readonly ImmutableDictionary<VerificationQuery, BoolExpr>.Builder _nominalReferences = ImmutableDictionary.CreateBuilder<VerificationQuery, BoolExpr>();
    private long _consumedResourceCount;
    private int _assertedReferenceFacts;

    public CallableSolverSession(IrFactory factory, IrSmtBackendOptions options)
        : this(factory, options, static () => new Context()) { factory = RepositoryParameterGuard.NotNull(factory, nameof(factory)); options = RepositoryParameterGuard.NotNull(options, nameof(options)); }

    internal CallableSolverSession(IrFactory factory, IrSmtBackendOptions options, Func<Context> createContext)
    {
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        _options = ArgumentNullGuard.NotNull(options, nameof(options));
        if (factory.Semantics != IrExecutionSemantics.Total)
        {
            throw new ArgumentException("A callable bitvector session requires Total IR semantics.", nameof(factory));
        }
        _runner = new SmtNativeRunner(ArgumentNullGuard.NotNull(createContext, nameof(createContext)), retireAfterFailure: true, disposeOwned: DisposeOwned);
        try
        {
            _solver = _runner.Context.MkSolver();
            _encoder = new BvEncoder(_runner.Context, factory, _owner, concreteOpaqueValueProvider: term => term is null ? throw new ArgumentNullException(nameof(term)) : ConcreteOpaqueValueProvider?.Invoke(term));
        }
        catch { _runner.Dispose(); throw; }
    }

    public long ConsumedResourceCount => Interlocked.Read(ref _consumedResourceCount);
    internal Func<IrOpaqueTerm, IrValue?>? ConcreteOpaqueValueProvider { get; init; }

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
            var active = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
            var selectors = ImmutableArray.CreateBuilder<BoolExpr>();
            for (var index = 0; index < query.Assumptions.Length; index++)
            {
                meter.Consume();
                var assumption = query.Assumptions[index];
                if (!_assumptions.TryGetValue(assumption, out var selector))
                {
                    var predicate = _encoder.EncodeBoolean(assumption.Predicate, meter);
                    selector = _owner.Own(_runner.Context.MkBoolConst("a" + _assumptions.Count.ToString(CultureInfo.InvariantCulture)));
                    _solver.Assert(_owner.Own(_runner.Context.MkImplies(selector, predicate)));
                    _assumptions.AddUnique(assumption, selector);
                }
                var name = selector.ToString();
                if (!active.ContainsKey(name))
                {
                    active.AddUnique(name, index);
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
                _goals.AddUnique(query, goalSelector);
            }
            selectors.Add(goalSelector);
            while (_assertedReferenceFacts < _encoder.ReferenceFacts.Count)
            {
                meter.Consume();
                _solver.Assert(_encoder.ReferenceFacts[_assertedReferenceFacts++]);
            }
            var status = Check(selectors.ToImmutable(), meter);
            if (status == Status.SATISFIABLE && SmallReferenceWitnesses(query, meter) is { } smallReferences)
            {
                // Prefer replayable string/array witnesses after the original
                // query is SAT. A failed preference retries the unbounded
                // query, so this bound can never prove a claim.
                selectors.Add(smallReferences);
                status = Check(selectors.ToImmutable(), meter);
                if (status != Status.SATISFIABLE)
                {
                    selectors.RemoveAt(selectors.Count - 1);
                    status = Check(selectors.ToImmutable(), meter);
                }
            }
            if (status == Status.SATISFIABLE)
            {
                try
                { return CreateModel(query, meter); }
                catch (UnsupportedIrEncodingException)
                {
                    // Keep already-decodable witnesses unchanged. A rejected
                    // SAT model may get one metered nominal-membership retry;
                    // failure of that preference never becomes a proof.
                    if (NominalReferenceWitnesses(query, meter) is { } nominal)
                    {
                        selectors.Add(nominal);
                        var retry = Check(selectors.ToImmutable(), meter);
                        if (retry == Status.SATISFIABLE)
                        { return CreateModel(query, meter); }
                        if (retry == Status.UNKNOWN)
                        { return BackendCheckResult.Unknown(SmtNativeUtilities.ClassifyUnknown(_solver.ReasonUnknown)); }
                    }
                    return BackendCheckResult.Unknown(BackendFailureReason.UnsupportedEncoding);
                }
            }
            return status switch
            {
                Status.UNSATISFIABLE => DecodeCore(_solver.UnsatCore, active.ToImmutable(), goalSelector.ToString(), meter),
                _ => BackendCheckResult.Unknown(SmtNativeUtilities.ClassifyUnknown(_solver.ReasonUnknown))
            };
        }
        finally
        {
            Interlocked.Exchange(ref _consumedResourceCount,
                checked(Interlocked.Read(ref _consumedResourceCount) + meter.Consumed));
        }
    }

    private Status Check(ImmutableArray<BoolExpr> selectors, SmtQueryResourceMeter meter)
    {
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        if (selectors.IsDefault)
        { throw new ArgumentException("Array must be initialized.", nameof(selectors)); }
        using var parameters = _runner.Context.MkParams();
        SmtNativeUtilities.AddOwnedParameter(parameters, _runner.Context.MkSymbol("rlimit"), meter.GetRemainingBudget());
        // The host owns SIGINT and forwards cancellation through Context.Interrupt().
        using var interruptParameter = _runner.Context.MkSymbol("ctrl_c");
        parameters.Add(interruptParameter, false);
        _solver.Parameters = parameters;
        return SmtNativeCheck.Run(_solver, selectors, meter);
    }

    // A model preference for string and array lengths, inactive in proof checks.
    private BoolExpr? SmallReferenceWitnesses(VerificationQuery query, SmtQueryResourceMeter meter)
    {
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        if (_smallReferences.TryGetValue(query, out var existing))
        { return existing; }
        var bound = _encoder.SmallReferenceBound(query, meter);
        if (bound == null)
        { return null; }
        var selector = _owner.Own(_runner.Context.MkBoolConst("s" + _smallReferences.Count.ToString(CultureInfo.InvariantCulture)));
        _solver.Assert(_owner.Own(_runner.Context.MkImplies(selector, bound)));
        _smallReferences.AddUnique(query, selector);
        return selector;
    }

    private BoolExpr? NominalReferenceWitnesses(VerificationQuery query, SmtQueryResourceMeter meter)
    {
        query = RepositoryParameterGuard.NotNull(query, nameof(query));
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        if (_nominalReferences.TryGetValue(query, out var existing))
        { return existing; }
        var bound = _encoder.NominalReferenceBound(query, meter);
        if (bound == null)
        { return null; }
        var selector = _owner.Own(_runner.Context.MkBoolConst("n" + _nominalReferences.Count.ToString(CultureInfo.InvariantCulture)));
        _solver.Assert(_owner.Own(_runner.Context.MkImplies(selector, bound)));
        _nominalReferences.AddUnique(query, selector);
        return selector;
    }
    internal static BackendCheckResult DecodeCore<T>(IReadOnlyList<T> core, ImmutableDictionary<string, int> active,
        string goal, SmtQueryResourceMeter meter) where T : IDisposable
    {
        active = RepositoryParameterGuard.NotNull(active, nameof(active));
        core = RepositoryParameterGuard.NotNull(core, nameof(core));
        goal = RepositoryParameterGuard.NotNull(goal, nameof(goal));
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        var indices = ImmutableHashSet.CreateBuilder<int>();
        try
        {
            foreach (var expression in core)
            {
                meter.Consume();
                var name = expression.ToString();
                if (name is not null)
                {
                    if (string.Equals(name, goal, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (active.TryGetValue(name, out var index))
                    {
                        indices.Add(index);
                        continue;
                    }
                }
                return BackendCheckResult.Unknown(BackendFailureReason.MalformedResult);
            }
            return BackendCheckResult.Unsatisfiable(indices.ToImmutable().OrderBy(static index => index));
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
        meter = RepositoryParameterGuard.NotNull(meter, nameof(meter));
        query = RepositoryParameterGuard.NotNull(query, nameof(query));
        using var model = _solver.Model;
        // Z3 reports SAT without a retrievable model only on a malformed backend result.
        var values = model is null ? null : _encoder.DecodeModel(query, model, meter);
        return values == null ? BackendCheckResult.Unknown(BackendFailureReason.MalformedResult)
            : BackendCheckResult.Satisfiable(new BackendModel(values));
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
            _encoder?.DisposeReferences();
        }
    }

    public void Dispose()
    {
        _runner.Dispose();
    }
}
