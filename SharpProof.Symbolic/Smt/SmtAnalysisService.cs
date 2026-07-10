using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;
using SearchLib.Purity;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Smt;

public sealed class SmtAnalysisService : IDisposable
{
    private const int PreNormalizationFormulaDepthLimit = 1024;
    private const int SharedQueryCacheEntryLimit = 4096;

    private static readonly ConcurrentDictionary<string, PurityProofResult> s_sharedQueryCache =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentQueue<string> s_sharedQueryCacheOrder = new();

    private static readonly ConcurrentDictionary<string, SharedQueryFlight> s_sharedQueryFlights =
        new(StringComparer.Ordinal);

    private static readonly Func<ISmtProofSearchSession> s_defaultProofSearchFactory =
        static () => new SearchLibProofSearchSession();

    [ThreadStatic] private static SharedProofSearchContext? t_sharedProofSearchContext;

    private static long s_solverContextGeneration;

    private readonly ConcurrentDictionary<string, PurityProofResult> _queryCache = new(StringComparer.Ordinal);
    private readonly Func<ISmtProofSearchSession> _proofSearchFactory;
    private readonly object _solverLock = new();
    private readonly object _healthLock = new();
    private long _consumedQueryTicks;
    private long _consumedResourceCount;
    private bool _disposed;
    private int _consecutiveTransientFailureCount;
    private int _contextRecycleCount;
    private int _executedQueryCount;
    private int _healthState;
    private string _lastFailureCode = string.Empty;
    private int _recoveredTransientFailureCount;
    private int _transientRetryCount;

    public SmtAnalysisService(SmtAnalysisOptions options)
        : this(options, s_defaultProofSearchFactory)
    {
    }

    internal SmtAnalysisService(
        SmtAnalysisOptions options,
        Func<ISmtProofSearchSession> proofSearchFactory)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _proofSearchFactory = proofSearchFactory ?? throw new ArgumentNullException(nameof(proofSearchFactory));
        _healthState = (int)(Options.IsEnabled
            ? SmtAnalysisHealthState.Ready
            : SmtAnalysisHealthState.Disabled);
    }

    public SmtAnalysisOptions Options { get; }

    public int ExecutedQueryCount => _executedQueryCount;

    public int CacheEntryCount => _queryCache.Count;

    public bool IsPermanentlyUnavailable =>
        GetHealthState() == SmtAnalysisHealthState.PermanentlyUnavailable;

    public SmtAnalysisHealth Health
    {
        get
        {
            lock (_healthLock)
                return new SmtAnalysisHealth(
                    GetHealthState(),
                    _lastFailureCode,
                    _consecutiveTransientFailureCount,
                    _transientRetryCount,
                    _recoveredTransientFailureCount,
                    _contextRecycleCount,
                    Interlocked.Read(ref s_solverContextGeneration));
        }
    }

    public void Dispose()
    {
        lock (_solverLock)
        {
            if (_disposed) return;

            _disposed = true;
            SetHealthState(SmtAnalysisHealthState.Disposed);
            if (Options.Lifecycle.DisposeCurrentThreadContextOnServiceDispose &&
                DisposeCurrentThreadProofSearch())
                Interlocked.Increment(ref _contextRecycleCount);
        }
    }

    public SmtSolverContextRecycleResult RecycleCurrentThreadSolverContext()
    {
        lock (_solverLock)
        {
            var disposed = DisposeCurrentThreadProofSearch();
            if (disposed) Interlocked.Increment(ref _contextRecycleCount);

            ResetDegradedHealth();
            return CreateRecycleResult(
                SmtSolverContextRecycleScope.CurrentThread,
                disposed,
                Interlocked.Read(ref s_solverContextGeneration));
        }
    }

    public SmtSolverContextRecycleResult RequestGlobalSolverContextRecycle()
    {
        var generation = Interlocked.Increment(ref s_solverContextGeneration);
        lock (_solverLock)
        {
            var disposed = DisposeCurrentThreadProofSearch();
            if (disposed) Interlocked.Increment(ref _contextRecycleCount);

            ResetDegradedHealth();
            return CreateRecycleResult(
                SmtSolverContextRecycleScope.AllThreadsOnNextUse,
                disposed,
                generation);
        }
    }

    internal PurityProofResult ClassifyPathFeasibility(IEnumerable<SmtFormula> pathConditions)
    {
        return Classify(new PurityProofQuery(
            pathConditions.ToArray(),
            new PurityHazard(
                PurityHazardKind.BranchReachability,
                new SmtBooleanConstant(true))));
    }

    internal PurityProofResult ClassifyImplication(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula factFormula)
    {
        if (factFormula == null) throw new ArgumentNullException(nameof(factFormula));

        return Classify(new PurityProofQuery(
            pathConditions.ToArray(),
            new PurityHazard(
                PurityHazardKind.BranchReachability,
                new SmtUnaryFormula(SmtUnaryOperator.Not, factFormula))));
    }

    internal bool PathConditionsImply(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula factFormula)
    {
        return ClassifyImplication(pathConditions, factFormula).Outcome == PurityProofOutcome.ProvablyPure;
    }

    internal PurityProofResult Classify(PurityProofQuery query)
    {
        if (_disposed) return Unknown("smt_disposed");

        if (!Options.IsEnabled) return Unknown("smt_disabled");

        if (IsPermanentlyUnavailable) return Unknown("smt_unavailable");

        if (!IsWithinFormulaDepthBudget(
                query.PathConditions,
                query.Hazard.TriggerCondition,
                PreNormalizationFormulaDepthLimit))
            return Unknown("smt_expression_budget_exceeded");

        var pathConditions = NormalizePathConditions(query.PathConditions);
        if (TryClassifySyntactically(query, pathConditions, out var syntacticResult)) return syntacticResult;

        if (Options.QueryTimeout <= TimeSpan.Zero) return Unknown("smt_timeout");

        if (pathConditions.Length > Options.MaxPathConditions) return Unknown("smt_path_condition_budget_exceeded");

        if (!IsWithinFormulaNodeBudget(
                pathConditions,
                query.Hazard.TriggerCondition,
                Options.MaxExpressionNodes))
            return Unknown("smt_expression_budget_exceeded");

        var normalizedQuery = new PurityProofQuery(pathConditions, query.Hazard);
        var key = CreateQueryKey(normalizedQuery);
        if (_queryCache.TryGetValue(key, out var cached)) return cached;

        if (TryGetSharedResult(key, out var sharedResult))
        {
            _queryCache.TryAdd(key, sharedResult);
            return sharedResult;
        }

        if (Options.UseSharedResultCache) return ClassifyWithSharedQueryFlight(normalizedQuery, key);

        return ClassifyLocally(normalizedQuery, key);
    }

    private PurityProofResult ClassifyWithSharedQueryFlight(
        PurityProofQuery query,
        string queryKey)
    {
        var sharedKey = CreateSharedQueryKey(Options, queryKey);
        var candidate = new SharedQueryFlight(() =>
        {
            if (TryGetSharedResult(queryKey, out var racedSharedResult)) return racedSharedResult;

            return IsMethodBudgetExceeded()
                ? Unknown("smt_method_budget_exceeded")
                : ClassifyCore(query);
        });
        var flight = s_sharedQueryFlights.GetOrAdd(sharedKey, candidate);
        var ownsFlight = ReferenceEquals(flight, candidate);
        PurityProofResult result;
        try
        {
            result = flight.Result.Value;
            if (ownsFlight)
            {
                if (IsLocallyCacheableResult(result)) _queryCache.TryAdd(queryKey, result);
                AddSharedResult(queryKey, result);
            }
            else if (IsShareableResult(result))
            {
                _queryCache.TryAdd(queryKey, result);
            }
        }
        finally
        {
            if (ownsFlight) s_sharedQueryFlights.TryRemove(sharedKey, out _);
        }

        return ownsFlight || IsShareableResult(result)
            ? result
            : ClassifyLocally(query, queryKey);
    }

    private PurityProofResult ClassifyLocally(
        PurityProofQuery query,
        string queryKey)
    {
        if (TryGetSharedResult(queryKey, out var sharedResult))
        {
            _queryCache.TryAdd(queryKey, sharedResult);
            return sharedResult;
        }

        if (IsMethodBudgetExceeded()) return Unknown("smt_method_budget_exceeded");

        var result = ClassifyCore(query);
        if (IsLocallyCacheableResult(result)) _queryCache.TryAdd(queryKey, result);
        AddSharedResult(queryKey, result);
        return result;
    }

    private PurityProofResult ClassifyCore(PurityProofQuery query)
    {
        var queryClock = Stopwatch.StartNew();
        try
        {
            lock (_solverLock)
            {
                if (_disposed) return Unknown("smt_disposed");
                if (IsPermanentlyUnavailable) return Unknown("smt_unavailable");

                for (var attempt = 0;; attempt++)
                {
                    PurityProofResult result;
                    try
                    {
                        Interlocked.Increment(ref _executedQueryCount);
                        var search = GetOrCreateProofSearch();
                        var resourcesBefore = search.ConsumedResourceCount;
                        try
                        {
                            result = search.Classify(query, Options.QueryTimeout);
                        }
                        finally
                        {
                            Interlocked.Add(
                                ref _consumedResourceCount,
                                search.ConsumedResourceCount - resourcesBefore);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        RecordFailure("smt_encoding_failure", SmtAnalysisHealthState.Ready);
                        return Unknown("smt_encoding_failure");
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        RecordFailure("smt_timeout", SmtAnalysisHealthState.Ready);
                        return Unknown("smt_timeout");
                    }
                    catch (Exception ex) when (IsTransientSolverFailure(ex))
                    {
                        result = Unknown("smt_transient_failure");
                    }
                    catch (Exception ex) when (IsPermanentSolverFailure(ex))
                    {
                        MarkPermanentlyUnavailable(GetPermanentFailureCode(ex));
                        if (DisposeCurrentThreadProofSearch())
                            Interlocked.Increment(ref _contextRecycleCount);

                        return Unknown("smt_unavailable");
                    }

                    if (!IsTransientSolverFailure(result))
                    {
                        RecordSolverSuccess();
                        return result;
                    }

                    RecordTransientFailure();
                    if (Options.Lifecycle.RecycleContextOnTransientFailure &&
                        DisposeCurrentThreadProofSearch())
                        Interlocked.Increment(ref _contextRecycleCount);

                    if (attempt >= Options.Lifecycle.MaxTransientRetries)
                        return Unknown("smt_transient_failure");

                    Interlocked.Increment(ref _transientRetryCount);
                }
            }
        }
        finally
        {
            queryClock.Stop();
            Interlocked.Add(ref _consumedQueryTicks, queryClock.ElapsedTicks);
        }
    }

    private ISmtProofSearchSession GetOrCreateProofSearch()
    {
        var generation = Interlocked.Read(ref s_solverContextGeneration);
        if (t_sharedProofSearchContext != null &&
            (t_sharedProofSearchContext.Generation != generation ||
             !ReferenceEquals(t_sharedProofSearchContext.Factory, _proofSearchFactory)))
        {
            var belongsToServiceFactory =
                ReferenceEquals(t_sharedProofSearchContext.Factory, _proofSearchFactory);
            if (DisposeCurrentThreadProofSearch() && belongsToServiceFactory)
                Interlocked.Increment(ref _contextRecycleCount);
        }

        if (t_sharedProofSearchContext == null)
            t_sharedProofSearchContext = new SharedProofSearchContext(
                _proofSearchFactory(),
                _proofSearchFactory,
                generation);

        return t_sharedProofSearchContext.Search;
    }

    private static bool DisposeCurrentThreadProofSearch()
    {
        if (t_sharedProofSearchContext == null) return false;

        try
        {
            t_sharedProofSearchContext.Search.Dispose();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Disposal is best effort for a failed or stale native context.
        }

        t_sharedProofSearchContext = null;
        return true;
    }

    private bool IsMethodBudgetExceeded()
    {
        // Primary budget is deterministic solver work (rlimit units), so the
        // cutoff does not depend on machine speed or CPU load. Wall-clock stays
        // only as a scaled-up safety net against a wedged solver.
        if (Interlocked.Read(ref _consumedResourceCount) >
            SmtResourceBudget.GetMethodRlimitBudget(Options.MethodBudget)) return true;

        var safetyNetTicks = Options.MethodBudget.TotalSeconds * Stopwatch.Frequency *
                             SmtResourceBudget.WallClockSafetyFactor;
        return Interlocked.Read(ref _consumedQueryTicks) > safetyNetTicks;
    }

    private bool TryGetSharedResult(string queryKey, out PurityProofResult result)
    {
        if (Options.UseSharedResultCache)
            return s_sharedQueryCache.TryGetValue(CreateSharedQueryKey(Options, queryKey), out result);

        result = default!;
        return false;
    }

    private void AddSharedResult(string queryKey, PurityProofResult result)
    {
        if (!Options.UseSharedResultCache ||
            !IsShareableResult(result))
            return;

        var sharedKey = CreateSharedQueryKey(Options, queryKey);
        if (!s_sharedQueryCache.TryAdd(sharedKey, result)) return;

        s_sharedQueryCacheOrder.Enqueue(sharedKey);
        while (s_sharedQueryCache.Count > SharedQueryCacheEntryLimit &&
               s_sharedQueryCacheOrder.TryDequeue(out var oldestKey))
            s_sharedQueryCache.TryRemove(oldestKey, out _);
    }

    private static bool IsShareableResult(PurityProofResult result)
    {
        return result.Outcome is PurityProofOutcome.ProvablyPure or PurityProofOutcome.ProvablyImpure;
    }

    private static bool IsLocallyCacheableResult(PurityProofResult result)
    {
        return !IsTransientSolverFailure(result);
    }

    private static PurityProofResult Unknown(string reason)
    {
        return new PurityProofResult(
            PurityProofOutcome.Unknown,
            Feasibility.Unknown,
            Feasibility.Unknown,
            reason);
    }

    private static bool IsTransientSolverFailure(PurityProofResult result)
    {
        return string.Equals(result.Reason, "smt_transient_failure", StringComparison.Ordinal) ||
               string.Equals(result.PathWitness?.Reason, "z3_transient_failure", StringComparison.Ordinal) ||
               string.Equals(result.TriggerWitness?.Reason, "z3_transient_failure", StringComparison.Ordinal);
    }

    private static bool IsTransientSolverFailure(Exception ex)
    {
        return string.Equals(ex.GetType().FullName, "Microsoft.Z3.Z3Exception", StringComparison.Ordinal) ||
               string.Equals(ex.GetType().Name, "Z3Exception", StringComparison.Ordinal);
    }

    private static bool IsPermanentSolverFailure(Exception ex)
    {
        return ex is DllNotFoundException or
            BadImageFormatException or
            FileNotFoundException or
            TypeInitializationException or
            EntryPointNotFoundException or
            PlatformNotSupportedException;
    }

    private static string GetPermanentFailureCode(Exception ex)
    {
        return ex switch
        {
            DllNotFoundException or FileNotFoundException => "smt_native_library_missing",
            BadImageFormatException or EntryPointNotFoundException => "smt_native_library_incompatible",
            PlatformNotSupportedException => "smt_platform_unsupported",
            _ => "smt_initialization_failure"
        };
    }

    private SmtSolverContextRecycleResult CreateRecycleResult(
        SmtSolverContextRecycleScope scope,
        bool disposedCurrentThreadContext,
        long requestedGeneration)
    {
        return new SmtSolverContextRecycleResult(
            scope,
            disposedCurrentThreadContext,
            requestedGeneration,
            _queryCache.Count,
            s_sharedQueryCache.Count);
    }

    private SmtAnalysisHealthState GetHealthState()
    {
        return (SmtAnalysisHealthState)Volatile.Read(ref _healthState);
    }

    private void SetHealthState(SmtAnalysisHealthState state)
    {
        Volatile.Write(ref _healthState, (int)state);
    }

    private void RecordFailure(string failureCode, SmtAnalysisHealthState state)
    {
        lock (_healthLock)
        {
            _lastFailureCode = failureCode;
            if (!_disposed && GetHealthState() != SmtAnalysisHealthState.PermanentlyUnavailable)
                SetHealthState(state);
        }
    }

    private void RecordTransientFailure()
    {
        lock (_healthLock)
        {
            _lastFailureCode = "smt_transient_failure";
            _consecutiveTransientFailureCount++;
            if (!_disposed && GetHealthState() != SmtAnalysisHealthState.PermanentlyUnavailable)
                SetHealthState(SmtAnalysisHealthState.Degraded);
        }
    }

    private void RecordSolverSuccess()
    {
        lock (_healthLock)
        {
            if (GetHealthState() == SmtAnalysisHealthState.Degraded)
                _recoveredTransientFailureCount++;

            _consecutiveTransientFailureCount = 0;
            if (!_disposed && Options.IsEnabled &&
                GetHealthState() != SmtAnalysisHealthState.PermanentlyUnavailable)
                SetHealthState(SmtAnalysisHealthState.Ready);
        }
    }

    private void ResetDegradedHealth()
    {
        lock (_healthLock)
        {
            _consecutiveTransientFailureCount = 0;
            if (!_disposed && Options.IsEnabled &&
                GetHealthState() != SmtAnalysisHealthState.PermanentlyUnavailable)
                SetHealthState(SmtAnalysisHealthState.Ready);
        }
    }

    private void MarkPermanentlyUnavailable(string failureCode)
    {
        lock (_healthLock)
        {
            _lastFailureCode = failureCode;
            SetHealthState(SmtAnalysisHealthState.PermanentlyUnavailable);
        }
    }

    private static string CreateQueryKey(PurityProofQuery query)
    {
        return string.Join(";", query.PathConditions.Select(static condition => condition.ToString())) +
               "|hazard=" +
               query.Hazard.Kind +
               "|" +
               query.Hazard.Visibility +
               "|" +
               query.Hazard.TriggerCondition;
    }

    private static string CreateSharedQueryKey(SmtAnalysisOptions options, string queryKey)
    {
        return options.Mode +
               "|timeout_ms=" +
               (long)options.QueryTimeout.TotalMilliseconds +
               "|max_path=" +
               options.MaxPathConditions +
               "|max_expr=" +
               options.MaxExpressionNodes +
               "|" +
               queryKey;
    }

    private static ImmutableArray<SmtFormula> NormalizePathConditions(IEnumerable<SmtFormula> pathConditions)
    {
        var builder = ImmutableArray.CreateBuilder<SmtFormula>();
        var seen = new HashSet<SmtFormula>();
        foreach (var pathCondition in pathConditions)
        {
            if (pathCondition is SmtBooleanConstant { Value: true }) continue;

            if (seen.Add(pathCondition)) builder.Add(pathCondition);
        }

        return builder
            .OrderBy(static condition => condition.ToString(), StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static bool TryClassifySyntactically(
        PurityProofQuery query,
        ImmutableArray<SmtFormula> pathConditions,
        out PurityProofResult result)
    {
        return SmtSyntacticClassifier.TryClassify(query, pathConditions, out result);
    }

    private static bool IsWithinFormulaNodeBudget(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula triggerCondition,
        int maxNodes)
    {
        var remaining = maxNodes;
        foreach (var formula in pathConditions)
            if (!TryConsumeFormulaNodeBudget(formula, ref remaining))
                return false;

        return TryConsumeFormulaNodeBudget(triggerCondition, ref remaining);
    }

    private static bool IsWithinFormulaDepthBudget(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula triggerCondition,
        int maxDepth)
    {
        foreach (var formula in pathConditions)
            if (!IsWithinFormulaDepthBudget(formula, maxDepth))
                return false;

        return IsWithinFormulaDepthBudget(triggerCondition, maxDepth);
    }

    private static bool IsWithinFormulaDepthBudget(SmtFormula root, int maxDepth)
    {
        var stack = new Stack<(SmtFormula Formula, int Depth)>();
        stack.Push((root, 1));
        while (stack.Count != 0)
        {
            var (formula, depth) = stack.Pop();
            if (depth > maxDepth) return false;

            var childDepth = depth + 1;
            switch (formula)
            {
                case SmtUnaryFormula unary:
                    stack.Push((unary.Operand, childDepth));
                    break;
                case SmtBinaryFormula binary:
                    stack.Push((binary.Left, childDepth));
                    stack.Push((binary.Right, childDepth));
                    break;
                case SmtIntegerUnaryTerm unary:
                    stack.Push((unary.Operand, childDepth));
                    break;
                case SmtIntegerBinaryTerm binary:
                    stack.Push((binary.Left, childDepth));
                    stack.Push((binary.Right, childDepth));
                    break;
                case SmtStringLengthTerm stringLength:
                    stack.Push((stringLength.Value, childDepth));
                    break;
                case SmtStringConcatTerm stringConcat:
                    stack.Push((stringConcat.Left, childDepth));
                    stack.Push((stringConcat.Right, childDepth));
                    break;
                case SmtStringContainsFormula stringContains:
                    stack.Push((stringContains.Value, childDepth));
                    stack.Push((stringContains.Search, childDepth));
                    break;
                case SmtStringStartsWithFormula stringStartsWith:
                    stack.Push((stringStartsWith.Value, childDepth));
                    stack.Push((stringStartsWith.Prefix, childDepth));
                    break;
                case SmtStringEndsWithFormula stringEndsWith:
                    stack.Push((stringEndsWith.Value, childDepth));
                    stack.Push((stringEndsWith.Suffix, childDepth));
                    break;
                case SmtRegexMatchFormula regexMatch:
                    stack.Push((regexMatch.Value, childDepth));
                    break;
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    stack.Push((runtimeTypeTest.Value, childDepth));
                    break;
                case SmtConditionalFormula conditional:
                    stack.Push((conditional.Condition, childDepth));
                    stack.Push((conditional.WhenTrue, childDepth));
                    stack.Push((conditional.WhenFalse, childDepth));
                    break;
            }
        }

        return true;
    }

    private static bool TryConsumeFormulaNodeBudget(SmtFormula root, ref int remaining)
    {
        var stack = new Stack<SmtFormula>();
        stack.Push(root);
        while (stack.Count != 0)
        {
            var formula = stack.Pop();
            var weight = formula is SmtRegexMatchFormula regexMatch
                ? 1 + Math.Max(1, regexMatch.Pattern.Length / 8)
                : 1;
            remaining -= weight;
            if (remaining < 0) return false;

            switch (formula)
            {
                case SmtUnaryFormula unary:
                    stack.Push(unary.Operand);
                    break;
                case SmtBinaryFormula binary:
                    stack.Push(binary.Left);
                    stack.Push(binary.Right);
                    break;
                case SmtIntegerUnaryTerm unary:
                    stack.Push(unary.Operand);
                    break;
                case SmtIntegerBinaryTerm binary:
                    stack.Push(binary.Left);
                    stack.Push(binary.Right);
                    break;
                case SmtStringLengthTerm stringLength:
                    stack.Push(stringLength.Value);
                    break;
                case SmtStringConcatTerm stringConcat:
                    stack.Push(stringConcat.Left);
                    stack.Push(stringConcat.Right);
                    break;
                case SmtStringContainsFormula stringContains:
                    stack.Push(stringContains.Value);
                    stack.Push(stringContains.Search);
                    break;
                case SmtStringStartsWithFormula stringStartsWith:
                    stack.Push(stringStartsWith.Value);
                    stack.Push(stringStartsWith.Prefix);
                    break;
                case SmtStringEndsWithFormula stringEndsWith:
                    stack.Push(stringEndsWith.Value);
                    stack.Push(stringEndsWith.Suffix);
                    break;
                case SmtRegexMatchFormula regexFormula:
                    stack.Push(regexFormula.Value);
                    break;
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    stack.Push(runtimeTypeTest.Value);
                    break;
                case SmtConditionalFormula conditional:
                    stack.Push(conditional.Condition);
                    stack.Push(conditional.WhenTrue);
                    stack.Push(conditional.WhenFalse);
                    break;
            }
        }

        return true;
    }

    private sealed class SharedProofSearchContext
    {
        public SharedProofSearchContext(
            ISmtProofSearchSession search,
            Func<ISmtProofSearchSession> factory,
            long generation)
        {
            Search = search;
            Factory = factory;
            Generation = generation;
        }

        public ISmtProofSearchSession Search { get; }

        public Func<ISmtProofSearchSession> Factory { get; }

        public long Generation { get; }
    }

    private sealed class SharedQueryFlight
    {
        public SharedQueryFlight(Func<PurityProofResult> classify)
        {
            Result = new Lazy<PurityProofResult>(
                classify,
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public Lazy<PurityProofResult> Result { get; }
    }
}
