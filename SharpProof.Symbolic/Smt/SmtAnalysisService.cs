namespace SharpProof.Symbolic.Smt;

internal sealed class SmtAnalysisService : IDisposable {
    private const int PreNormalizationFormulaDepthLimit = 1024;
    private static readonly Func<IAnalysisProofSearchSession> s_defaultProofSearchFactory =
        static () => new AnalysisProofSearch();

    private readonly SmtAnalysisBudget _budget;
    private readonly SmtProofResultCache _proofResults = new();
    private readonly SmtProofSearchSessionPool _proofSearchSessions;
    private readonly object _solverLock = new();
    private readonly object _healthLock = new();
    private bool _disposed;
    private int _consecutiveTransientFailureCount;
    private int _contextRecycleCount;
    private int _executedQueryCount;
    private int _healthState;
    private string _lastFailureCode = string.Empty;
    private int _recoveredTransientFailureCount;
    private int _transientRetryCount;

    public SmtAnalysisService(SmtAnalysisOptions options)
        : this(options, s_defaultProofSearchFactory) {
    }
    internal SmtAnalysisService(SmtAnalysisOptions options, Func<IAnalysisProofSearchSession> proofSearchFactory) {
        SmtNativeLibraryBootstrap.TryLoadAdjacentLibrary();
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _budget = new SmtAnalysisBudget(Options.MethodBudget);
        _proofSearchSessions = new SmtProofSearchSessionPool(proofSearchFactory);
        _healthState = (int)SmtAnalysisHealthState.Ready;
    }
    public SmtAnalysisOptions Options { get; }

    public int ExecutedQueryCount => _executedQueryCount;

    public int CacheEntryCount => _proofResults.LocalEntryCount;

    public long CacheHitCount => _proofResults.LocalHitCount;

    public long CacheMissCount => _proofResults.LocalMissCount;

    public long CacheEvictionCount => _proofResults.LocalEvictionCount;

    public bool IsPermanentlyUnavailable =>
        GetHealthState() == SmtAnalysisHealthState.PermanentlyUnavailable;

    public SmtAnalysisHealth Health {
        get {
            lock (_healthLock)
                return new SmtAnalysisHealth(
                    GetHealthState(),
                    _lastFailureCode,
                    _consecutiveTransientFailureCount,
                    _transientRetryCount,
                    _recoveredTransientFailureCount,
                    _contextRecycleCount);
        }
    }
    public void Dispose() {
        lock (_solverLock) {
            if (_disposed) return;

            _disposed = true;
            SetHealthState(SmtAnalysisHealthState.Disposed);
            Interlocked.Add(
                ref _contextRecycleCount,
                _proofSearchSessions.Dispose(Options.Lifecycle.DisposeCurrentThreadContextOnServiceDispose));
        }
    }
    internal AnalysisProofResult ClassifyPathFeasibility(IEnumerable<SmtFormula> pathConditions) => Classify(new AnalysisProofQuery(
            pathConditions.ToArray(),
            new AnalysisHazard(AnalysisHazardKind.BranchReachability, new SmtBooleanConstant(true))));

    internal AnalysisProofResult ClassifyImplication(IEnumerable<SmtFormula> pathConditions, SmtFormula factFormula) {
        if (factFormula == null) throw new ArgumentNullException(nameof(factFormula));

        return Classify(new AnalysisProofQuery(
            pathConditions.ToArray(),
            new AnalysisHazard(AnalysisHazardKind.BranchReachability, new SmtUnaryFormula(SmtUnaryOperator.Not, factFormula))));
    }
    internal AnalysisProofResult Classify(AnalysisProofQuery query) {
        if (_disposed) return Unknown("smt_disposed");

        if (IsPermanentlyUnavailable) return Unknown("smt_unavailable");

        if (!IsWithinFormulaDepthBudget(query.PathConditions, query.Hazard.TriggerCondition, PreNormalizationFormulaDepthLimit))
            return Unknown("smt_expression_budget_exceeded");

        var pathConditions = NormalizePathConditions(query.PathConditions);
        if (Options.QueryTimeout <= TimeSpan.Zero) return Unknown("smt_timeout");

        if (pathConditions.Length > Options.MaxPathConditions) return Unknown("smt_path_condition_budget_exceeded");

        if (!IsWithinFormulaNodeBudget(pathConditions, query.Hazard.TriggerCondition, Options.MaxExpressionNodes))
            return Unknown("smt_expression_budget_exceeded");

        var normalizedQuery = new AnalysisProofQuery(pathConditions, query.Hazard);
        var key = CreateQueryKey(normalizedQuery);
        if (_proofResults.TryGetLocal(key, out var cached)) return cached;

        if (_proofResults.TryGetShared(Options, key, out var sharedResult)) {
            _proofResults.AddLocal(key, sharedResult);
            return sharedResult;
        }
        if (Options.UseSharedResultCache) return ClassifyWithSharedQueryFlight(normalizedQuery, key);

        return ClassifyLocally(normalizedQuery, key);
    }
    private AnalysisProofResult ClassifyWithSharedQueryFlight(AnalysisProofQuery query, string queryKey) {
        var flight = _proofResults.AcquireSharedFlight(Options, queryKey, () => {
            if (_proofResults.TryGetShared(Options, queryKey, out var racedSharedResult))
                return racedSharedResult;

            return _budget.IsExceeded
                ? Unknown("smt_method_budget_exceeded")
                : ClassifyCore(query);
        });
        AnalysisProofResult result;
        try {
            result = flight.Result.Value;
            if (flight.OwnsFlight) {
                _proofResults.AddLocalIfCacheable(queryKey, result);
                _proofResults.AddSharedIfCacheable(Options, queryKey, result);
            }
            else if (SmtProofResultCache.IsShareable(result)) {
                _proofResults.AddLocal(queryKey, result);
            }
        }
        finally {
            _proofResults.ReleaseSharedFlight(flight);
        }
        return flight.OwnsFlight || SmtProofResultCache.IsShareable(result)
            ? result
            : ClassifyLocally(query, queryKey);
    }
    private AnalysisProofResult ClassifyLocally(AnalysisProofQuery query, string queryKey) {
        if (_proofResults.TryGetShared(Options, queryKey, out var sharedResult)) {
            _proofResults.AddLocal(queryKey, sharedResult);
            return sharedResult;
        }
        if (_budget.IsExceeded) return Unknown("smt_method_budget_exceeded");

        var result = ClassifyCore(query);
        _proofResults.AddLocalIfCacheable(queryKey, result);
        _proofResults.AddSharedIfCacheable(Options, queryKey, result);
        return result;
    }
    private AnalysisProofResult ClassifyCore(AnalysisProofQuery query) {
        var queryClock = Stopwatch.StartNew();
        try {
            lock (_solverLock) {
                if (_disposed) return Unknown("smt_disposed");
                if (IsPermanentlyUnavailable) return Unknown("smt_unavailable");

                for (var attempt = 0; ; attempt++) {
                    AnalysisProofResult result;
                    try {
                        Interlocked.Increment(ref _executedQueryCount);
                        var search = _proofSearchSessions.GetOrCreate();
                        var resourcesBefore = search.ConsumedResourceCount;
                        try {
                            result = search.Classify(query, Options.QueryTimeout);
                        }
                        finally {
                            _budget.RecordConsumedResources(search.ConsumedResourceCount - resourcesBefore);
                        }
                    }
                    catch (InvalidOperationException) {
                        RecordFailure("smt_encoding_failure", SmtAnalysisHealthState.Ready);
                        return Unknown("smt_encoding_failure");
                    }
                    catch (RegexMatchTimeoutException) {
                        RecordFailure("smt_timeout", SmtAnalysisHealthState.Ready);
                        return Unknown("smt_timeout");
                    }
                    catch (Exception ex) when (IsTransientSolverFailure(ex)) {
                        result = Unknown("smt_transient_failure");
                    }
                    catch (Exception ex) when (IsPermanentSolverFailure(ex)) {
                        MarkPermanentlyUnavailable(GetPermanentFailureCode(ex));
                        if (_proofSearchSessions.RecycleCurrentThread())
                            Interlocked.Increment(ref _contextRecycleCount);

                        return Unknown("smt_unavailable");
                    }
                    if (!SmtProofResultCache.IsTransientFailure(result)) {
                        RecordSolverSuccess();
                        return result;
                    }
                    RecordTransientFailure();
                    if (Options.Lifecycle.RecycleContextOnTransientFailure &&
                        _proofSearchSessions.RecycleCurrentThread())
                        Interlocked.Increment(ref _contextRecycleCount);

                    if (attempt >= Options.Lifecycle.MaxTransientRetries)
                        return Unknown("smt_transient_failure");

                    Interlocked.Increment(ref _transientRetryCount);
                }
            }
        }
        finally {
            queryClock.Stop();
            _budget.RecordQueryDuration(queryClock.ElapsedTicks);
        }
    }
    private static AnalysisProofResult Unknown(string reason) => new(
            AnalysisProofOutcome.Unknown,
            new ProofCheckInfo(false, Feasibility.Unknown),
            new ProofCheckInfo(false, Feasibility.Unknown),
            reason);

    private static bool IsTransientSolverFailure(Exception ex)
        => string.Equals(ex.GetType().FullName, "Microsoft.Z3.Z3Exception", StringComparison.Ordinal) ||
               string.Equals(ex.GetType().Name, "Z3Exception", StringComparison.Ordinal);

    private static bool IsPermanentSolverFailure(Exception ex) =>
        FindPermanentSolverFailure(ex) != null;

    private static string GetPermanentFailureCode(Exception ex) => FindPermanentSolverFailure(ex) switch {
        DllNotFoundException or FileNotFoundException => "smt_native_library_missing",
        BadImageFormatException or EntryPointNotFoundException => "smt_native_library_incompatible",
        PlatformNotSupportedException => "smt_platform_unsupported",
        _ => "smt_initialization_failure"
    };

    private static Exception? FindPermanentSolverFailure(Exception exception, int depth = 0) {
        if (depth >= 16) return null;

        if (exception is DllNotFoundException or
            BadImageFormatException or
            FileNotFoundException or
            EntryPointNotFoundException or
            PlatformNotSupportedException)
            return exception;

        if (exception is AggregateException aggregateException) {
            foreach (var innerException in aggregateException.Flatten().InnerExceptions) {
                var nestedFailure = FindPermanentSolverFailure(innerException, depth + 1);
                if (nestedFailure != null) return nestedFailure;
            }
        }
        else if (exception.InnerException != null) {
            var nestedFailure = FindPermanentSolverFailure(exception.InnerException, depth + 1);
            if (nestedFailure != null) return nestedFailure;
        }
        return exception is TypeInitializationException ? exception : null;
    }
    private SmtAnalysisHealthState GetHealthState() =>
        (SmtAnalysisHealthState)Volatile.Read(ref _healthState);

    private void SetHealthState(SmtAnalysisHealthState state) => Volatile.Write(ref _healthState, (int)state);

    private void RecordFailure(string failureCode, SmtAnalysisHealthState state) {
        lock (_healthLock) {
            _lastFailureCode = failureCode;
            if (!_disposed && GetHealthState() != SmtAnalysisHealthState.PermanentlyUnavailable)
                SetHealthState(state);
        }
    }
    private void RecordTransientFailure() {
        lock (_healthLock) {
            _lastFailureCode = "smt_transient_failure";
            _consecutiveTransientFailureCount++;
            if (!_disposed && GetHealthState() != SmtAnalysisHealthState.PermanentlyUnavailable)
                SetHealthState(SmtAnalysisHealthState.Degraded);
        }
    }
    private void RecordSolverSuccess() {
        lock (_healthLock) {
            if (GetHealthState() == SmtAnalysisHealthState.Degraded)
                _recoveredTransientFailureCount++;

            _consecutiveTransientFailureCount = 0;
            if (!_disposed &&
                GetHealthState() != SmtAnalysisHealthState.PermanentlyUnavailable)
                SetHealthState(SmtAnalysisHealthState.Ready);
        }
    }
    private void MarkPermanentlyUnavailable(string failureCode) {
        lock (_healthLock) {
            _lastFailureCode = failureCode;
            SetHealthState(SmtAnalysisHealthState.PermanentlyUnavailable);
        }
    }
    private static string CreateQueryKey(AnalysisProofQuery query) => CreateFormulaSequenceKey(query.PathConditions) +
               "|hazard=" + (int)query.Hazard.Kind +
               "|visibility=" + (int)query.Hazard.Visibility +
               "|trigger=" + SmtFormulaStructuralKey.Create(query.Hazard.TriggerCondition);

    private static ImmutableArray<SmtFormula> NormalizePathConditions(IEnumerable<SmtFormula> pathConditions) {
        var builder = ImmutableArray.CreateBuilder<SmtFormula>();
        var seen = new HashSet<SmtFormula>();
        foreach (var pathCondition in pathConditions) {
            if (pathCondition is SmtBooleanConstant { Value: true }) continue;

            if (seen.Add(pathCondition)) builder.Add(pathCondition);
        }
        return [.. builder.OrderBy(SmtFormulaStructuralKey.Create, StringComparer.Ordinal)];
    }
    private static string CreateFormulaSequenceKey(IEnumerable<SmtFormula> formulas) {
        var keys = formulas.Select(SmtFormulaStructuralKey.Create).ToArray();
        return string.Join(
            string.Empty,
            keys.Select(static key => key.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + key));
    }
    private static bool IsWithinFormulaNodeBudget(IEnumerable<SmtFormula> pathConditions, SmtFormula triggerCondition, int maxNodes) {
        var remaining = maxNodes;
        foreach (var formula in pathConditions)
            if (!TryConsumeFormulaNodeBudget(formula, ref remaining))
                return false;

        return TryConsumeFormulaNodeBudget(triggerCondition, ref remaining);
    }
    private static bool IsWithinFormulaDepthBudget(IEnumerable<SmtFormula> pathConditions, SmtFormula triggerCondition, int maxDepth) {
        foreach (var formula in pathConditions)
            if (!SmtFormulaTraversal.IsWithinDepth(formula, maxDepth))
                return false;

        return SmtFormulaTraversal.IsWithinDepth(triggerCondition, maxDepth);
    }
    private static bool TryConsumeFormulaNodeBudget(SmtFormula root, ref int remaining) {
        foreach (var formula in SmtFormulaTraversal.Enumerate(root)) {
            remaining -= formula is SmtRegexMatchFormula regexMatch
                ? 1 + Math.Max(1, regexMatch.Pattern.Length / 8)
                : 1;
            if (remaining < 0) return false;
        }
        return true;
    }
}
