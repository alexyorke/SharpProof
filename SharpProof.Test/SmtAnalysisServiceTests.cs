using NUnit.Framework;
using SearchLib.Purity;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
[Category("SmtHeavy")]
public class SmtAnalysisServiceTests
{
    [Test]
    public void ForMode_Deep_ReturnsExpandedBudgetPreset()
    {
        var options = SmtAnalysisOptions.ForMode(SmtAnalysisMode.Deep);

        Assert.That(options.Mode, Is.EqualTo(SmtAnalysisMode.Deep));
        Assert.That(options.QueryTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(2000)));
        Assert.That(options.MethodBudget, Is.EqualTo(TimeSpan.FromMilliseconds(15000)));
        Assert.That(options.MaxPathConditions, Is.EqualTo(512));
        Assert.That(options.MaxExpressionNodes, Is.EqualTo(8192));
        Assert.That(options.UseSharedResultCache, Is.False);
    }

    [Test]
    public void DefaultPreset_DisablesSharedResultCache()
    {
        Assert.That(SmtAnalysisOptions.Default.UseSharedResultCache, Is.False);
    }

    [Test]
    public void WithOverrides_PreservesModeAndAppliesExplicitBudgets()
    {
        var options = SmtAnalysisOptions.ForMode(SmtAnalysisMode.Deep).WithOverrides(
            TimeSpan.FromMilliseconds(123),
            TimeSpan.FromMilliseconds(456),
            7,
            89);

        Assert.That(options.Mode, Is.EqualTo(SmtAnalysisMode.Deep));
        Assert.That(options.QueryTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(123)));
        Assert.That(options.MethodBudget, Is.EqualTo(TimeSpan.FromMilliseconds(456)));
        Assert.That(options.MaxPathConditions, Is.EqualTo(7));
        Assert.That(options.MaxExpressionNodes, Is.EqualTo(89));
    }

    [Test]
    public void LifecycleOptions_DefaultsAndOverridesAreStable()
    {
        var defaults = SmtSolverLifecycleOptions.Default;

        Assert.That(defaults.MaxTransientRetries, Is.EqualTo(1));
        Assert.That(defaults.RecycleContextOnTransientFailure, Is.True);
        Assert.That(defaults.DisposeCurrentThreadContextOnServiceDispose, Is.False);
        Assert.That(
            () => new SmtSolverLifecycleOptions(maxTransientRetries: -1),
            Throws.TypeOf<ArgumentOutOfRangeException>());

        var lifecycle = new SmtSolverLifecycleOptions(3, false, true);
        var options = SmtAnalysisOptions.Default.WithLifecycle(lifecycle);

        Assert.That(options.Lifecycle, Is.SameAs(lifecycle));
        Assert.That(options.WithOverrides(queryTimeout: TimeSpan.FromMilliseconds(123)).Lifecycle,
            Is.SameAs(lifecycle));
    }

    [Test]
    public void Classify_TransientFailure_RecyclesRetriesAndRecovers()
    {
        var attempts = 0;
        var disposedSessions = 0;
        var options = SmtAnalysisOptions.Default.WithLifecycle(
            new SmtSolverLifecycleOptions(maxTransientRetries: 1));
        using var service = new SmtAnalysisService(
            options,
            () => new StubProofSearchSession(
                (_, _) => Interlocked.Increment(ref attempts) == 1
                    ? CreateTransientFailure()
                    : CreateImpureResult(),
                () => Interlocked.Increment(ref disposedSessions)));

        var result = service.Classify(CreateSolverQuery("transient_recovery"));
        var health = service.Health;
        var diagnostics = SymbolicSmtDiagnostics.FromService(service);
        var compactDiagnostics = SymbolicCompactSmtDiagnostics.FromDiagnostics(diagnostics);
        var compactHazardDiagnostics = SymbolicCompactRuntimeHazardSmtDiagnostics.FromDiagnostics(diagnostics);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
        Assert.That(attempts, Is.EqualTo(2));
        Assert.That(disposedSessions, Is.EqualTo(1));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(2));
        Assert.That(health.State, Is.EqualTo(SmtAnalysisHealthState.Ready));
        Assert.That(health.LastFailureCode, Is.EqualTo("smt_transient_failure"));
        Assert.That(health.TransientRetryCount, Is.EqualTo(1));
        Assert.That(health.RecoveredTransientFailureCount, Is.EqualTo(1));
        Assert.That(health.ConsecutiveTransientFailureCount, Is.Zero);
        Assert.That(health.ContextRecycleCount, Is.EqualTo(1));
        Assert.That(diagnostics.Health.State, Is.EqualTo(SmtAnalysisHealthState.Ready));
        Assert.That(diagnostics.Lifecycle, Is.SameAs(options.Lifecycle));
        Assert.That(compactDiagnostics.Health.TransientRetryCount, Is.EqualTo(1));
        Assert.That(compactDiagnostics.Lifecycle, Is.SameAs(options.Lifecycle));
        Assert.That(compactHazardDiagnostics.Health.RecoveredTransientFailureCount, Is.EqualTo(1));
        Assert.That(compactHazardDiagnostics.Lifecycle, Is.SameAs(options.Lifecycle));
    }

    [Test]
    public void Classify_ExhaustedTransientFailure_IsNotCached()
    {
        var attempts = 0;
        var options = SmtAnalysisOptions.Default.WithLifecycle(
            new SmtSolverLifecycleOptions(maxTransientRetries: 0));
        using var service = new SmtAnalysisService(
            options,
            () => new StubProofSearchSession(
                (_, _) =>
                {
                    Interlocked.Increment(ref attempts);
                    return CreateTransientFailure();
                }));
        var query = CreateSolverQuery("transient_not_cached");

        var first = service.Classify(query);
        var second = service.Classify(query);

        Assert.That(first.Reason, Is.EqualTo("smt_transient_failure"));
        Assert.That(second.Reason, Is.EqualTo("smt_transient_failure"));
        Assert.That(attempts, Is.EqualTo(2));
        Assert.That(service.CacheEntryCount, Is.Zero);
        Assert.That(service.Health.State, Is.EqualTo(SmtAnalysisHealthState.Degraded));
        Assert.That(service.Health.ConsecutiveTransientFailureCount, Is.EqualTo(2));
    }

    [Test]
    public void Classify_NativeLoadFailure_IsPermanentlyUnavailable()
    {
        var factoryCalls = 0;
        using var service = new SmtAnalysisService(
            SmtAnalysisOptions.Default,
            () =>
            {
                Interlocked.Increment(ref factoryCalls);
                throw new DllNotFoundException("missing test solver");
            });
        var query = CreateSolverQuery("permanent_failure");

        var first = service.Classify(query);
        var second = service.Classify(query);
        var health = service.Health;

        Assert.That(first.Reason, Is.EqualTo("smt_unavailable"));
        Assert.That(second.Reason, Is.EqualTo("smt_unavailable"));
        Assert.That(factoryCalls, Is.EqualTo(1));
        Assert.That(service.IsPermanentlyUnavailable, Is.True);
        Assert.That(health.State, Is.EqualTo(SmtAnalysisHealthState.PermanentlyUnavailable));
        Assert.That(health.LastFailureCode, Is.EqualTo("smt_native_library_missing"));
    }

    [Test]
    public void Classify_WrappedNativeFailures_PreserveStableFallbackCodes()
    {
        AssertPermanentFailureCode(
            new TypeInitializationException(
                "Microsoft.Z3.Native",
                new DllNotFoundException("missing test solver")),
            "smt_native_library_missing");
        AssertPermanentFailureCode(
            new TypeInitializationException(
                "Microsoft.Z3.Native",
                new BadImageFormatException("incompatible test solver")),
            "smt_native_library_incompatible");
        AssertPermanentFailureCode(
            new TypeInitializationException(
                "Microsoft.Z3.Native",
                new PlatformNotSupportedException("unsupported test platform")),
            "smt_platform_unsupported");
    }

    [Test]
    public void RequestGlobalSolverContextRecycle_PreservesLocalAndSharedCaches()
    {
        var firstFactoryCalls = 0;
        var firstDisposedSessions = 0;
        var options = new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(1000),
                4,
                32,
                true)
            .WithLifecycle(SmtSolverLifecycleOptions.Default);
        var query = CreateSolverQuery("recycle_cache_" + Guid.NewGuid().ToString("N"));
        using var firstService = new SmtAnalysisService(
            options,
            () =>
            {
                Interlocked.Increment(ref firstFactoryCalls);
                return new StubProofSearchSession(
                    (_, _) => CreateImpureResult(),
                    () => Interlocked.Increment(ref firstDisposedSessions));
            });

        var first = firstService.Classify(query);
        var recycle = firstService.RequestGlobalSolverContextRecycle();
        var localCached = firstService.Classify(query);
        var secondFactoryCalls = 0;
        using var secondService = new SmtAnalysisService(
            options,
            () =>
            {
                Interlocked.Increment(ref secondFactoryCalls);
                return new StubProofSearchSession((_, _) => CreateImpureResult());
            });
        var sharedCached = secondService.Classify(query);

        Assert.That(first.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
        Assert.That(localCached.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(sharedCached.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(recycle.Scope, Is.EqualTo(SmtSolverContextRecycleScope.AllThreadsOnNextUse));
        Assert.That(recycle.DisposedCurrentThreadContext, Is.True);
        Assert.That(recycle.LocalCacheEntryCount, Is.EqualTo(1));
        Assert.That(recycle.SharedCacheEntryCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(firstFactoryCalls, Is.EqualTo(1));
        Assert.That(firstDisposedSessions, Is.EqualTo(1));
        Assert.That(secondFactoryCalls, Is.Zero);
        Assert.That(firstService.CacheEntryCount, Is.EqualTo(1));
        Assert.That(secondService.CacheEntryCount, Is.EqualTo(1));
    }

    [Test]
    public void RequestGlobalSolverContextRecycle_RecyclesOtherThreadOnNextUse()
    {
        var factoryCalls = 0;
        var disposedSessions = 0;
        Exception? workerException = null;
        SmtAnalysisHealth? healthAfterRecycle = null;
        using var contextReady = new ManualResetEventSlim();
        using var recycleRequested = new ManualResetEventSlim();
        var options = SmtAnalysisOptions.Default.WithLifecycle(
            new SmtSolverLifecycleOptions(disposeCurrentThreadContextOnServiceDispose: true));
        var worker = new Thread(() =>
        {
            try
            {
                using var service = new SmtAnalysisService(
                    options,
                    () =>
                    {
                        Interlocked.Increment(ref factoryCalls);
                        return new StubProofSearchSession(
                            (_, _) => CreateImpureResult(),
                            () => Interlocked.Increment(ref disposedSessions));
                    });
                _ = service.Classify(CreateSolverQuery("global_recycle_first"));
                contextReady.Set();
                recycleRequested.Wait();
                _ = service.Classify(CreateSolverQuery("global_recycle_second"));
                healthAfterRecycle = service.Health;
            }
            catch (Exception ex)
            {
                workerException = ex;
                contextReady.Set();
            }
        });
        worker.Start();
        Assert.That(contextReady.Wait(TimeSpan.FromSeconds(10)), Is.True);

        using (var controller = new SmtAnalysisService(options))
        {
            var recycle = controller.RequestGlobalSolverContextRecycle();
            Assert.That(recycle.Scope, Is.EqualTo(SmtSolverContextRecycleScope.AllThreadsOnNextUse));
        }

        recycleRequested.Set();
        Assert.That(worker.Join(TimeSpan.FromSeconds(10)), Is.True);

        Assert.That(workerException, Is.Null);
        Assert.That(factoryCalls, Is.EqualTo(2));
        Assert.That(disposedSessions, Is.EqualTo(2));
        Assert.That(healthAfterRecycle, Is.Not.Null);
        Assert.That(healthAfterRecycle!.ContextRecycleCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_ConfiguredLifecycle_DisposesCurrentThreadContext()
    {
        var disposedSessions = 0;
        var options = SmtAnalysisOptions.Default.WithLifecycle(
            new SmtSolverLifecycleOptions(disposeCurrentThreadContextOnServiceDispose: true));
        var service = new SmtAnalysisService(
            options,
            () => new StubProofSearchSession(
                (_, _) => CreateImpureResult(),
                () => Interlocked.Increment(ref disposedSessions)));

        _ = service.Classify(CreateSolverQuery("dispose_context"));
        service.Dispose();

        Assert.That(disposedSessions, Is.EqualTo(1));
        Assert.That(service.Health.State, Is.EqualTo(SmtAnalysisHealthState.Disposed));
        Assert.That(service.Health.ContextRecycleCount, Is.EqualTo(1));
    }

    [Test]
    public void Classify_OffMode_ReturnsConservativeUnknown()
    {
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Off,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            4,
            16));

        var result = service.Classify(CreateQuery(Array.Empty<SmtFormula>(), new SmtBooleanConstant(true)));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_disabled"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_PathConditionBudgetExceeded_ReturnsConservativeUnknownWithoutSolver()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            1,
            32));

        var result = service.Classify(CreateQuery(
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(10))
            },
            new SmtBooleanConstant(true)));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_path_condition_budget_exceeded"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_ZeroTimeout_ReturnsConservativeTimeoutWithoutSolver()
    {
        var value = new SmtVariable("timeout_value_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var containsNeedle = new SmtStringContainsFormula(value, new SmtStringConstant("needle"));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(500),
            4,
            32));

        var result = service.Classify(CreateQuery(Array.Empty<SmtFormula>(), containsNeedle));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_timeout"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_DuplicateAndTruePathConditions_AreNormalizedBeforeBudgetAndCache()
    {
        var x = new SmtVariable("normalized_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var y = new SmtVariable("normalized_y_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var xAtLeastZero = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            x,
            new SmtIntegerConstant(0));
        var yAtLeastZero = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            y,
            new SmtIntegerConstant(0));
        var fact = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, y),
            new SmtIntegerConstant(0));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(1000),
            2,
            64));

        var first = service.ClassifyImplication(
            new SmtFormula[] { xAtLeastZero, yAtLeastZero, new SmtBooleanConstant(true), xAtLeastZero },
            fact);
        var second = service.ClassifyImplication(new[] { yAtLeastZero, xAtLeastZero }, fact);

        Assert.That(first.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
        Assert.That(service.CacheEntryCount, Is.EqualTo(1));
    }

    [Test]
    public void Classify_EquivalentPathConditionOrder_UsesSameCacheEntry()
    {
        var x = new SmtVariable("ordered_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var y = new SmtVariable("ordered_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var xAtLeastZero = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            x,
            new SmtIntegerConstant(0));
        var yAtLeastZero = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            y,
            new SmtIntegerConstant(0));
        var fact = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, y),
            new SmtIntegerConstant(0));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(1000),
            4,
            64));

        var first = service.ClassifyImplication(new[] { xAtLeastZero, yAtLeastZero }, fact);
        var second = service.ClassifyImplication(new[] { yAtLeastZero, xAtLeastZero }, fact);

        Assert.That(first.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
        Assert.That(service.CacheEntryCount, Is.EqualTo(1));
    }

    [Test]
    public void Classify_SyntacticPathContradiction_BypassesBudgetsAndSolver()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            1,
            3));

        var result = service.Classify(CreateQuery(
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, x, new SmtIntegerConstant(0))
            },
            new SmtBooleanConstant(true)));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_SyntacticIntegerIntervalContradiction_BypassesBudgetsAndSolver()
    {
        var x = new SmtVariable("interval_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            1,
            3));

        var result = service.Classify(CreateQuery(
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(10)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(10))
            },
            new SmtBooleanConstant(true)));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_SyntacticDisjunctionOfKnownComparisonComplements_BypassesSolver()
    {
        var values = new SmtVariable("values_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var index = new SmtVariable("index_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var length = new SmtVariable(values.Name + ".Length", SmtValueKind.Int);
        var valuesIsNotNull = new SmtBinaryFormula(
            SmtBinaryOperator.NotEqual,
            values,
            new SmtNullConstant());
        var indexIsNonNegative = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            index,
            new SmtIntegerConstant(0));
        var indexIsInBounds = new SmtBinaryFormula(
            SmtBinaryOperator.LessThan,
            index,
            length);
        var contradiction = new SmtBinaryFormula(
            SmtBinaryOperator.Or,
            new SmtBinaryFormula(SmtBinaryOperator.Equal, values, new SmtNullConstant()),
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, index, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, index, length)));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1),
            4,
            64));

        var result = service.ClassifyPathFeasibility(new SmtFormula[]
        {
            valuesIsNotNull,
            indexIsNonNegative,
            indexIsInBounds,
            contradiction
        });

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_NestedConjunctIntegerContradiction_BypassesSolver()
    {
        var x = new SmtVariable("nested_interval_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var nestedBounds = new SmtBinaryFormula(
            SmtBinaryOperator.And,
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(3)),
            new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, x, new SmtIntegerConstant(3)));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.Classify(CreateQuery(
            new[] { nestedBounds },
            new SmtBooleanConstant(true)));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_DirectPathFact_BypassesSolver()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyImplication(new[] { xIsZero }, xIsZero);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.PathConditionsImply(new[] { xIsZero }, xIsZero), Is.True);
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_IntegerIntervalEntailment_BypassesSolver()
    {
        var x = new SmtVariable("interval_entailment_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var xAtMostNine = new SmtBinaryFormula(
            SmtBinaryOperator.LessThanOrEqual,
            x,
            new SmtIntegerConstant(9));
        var xLessThanTen = new SmtBinaryFormula(
            SmtBinaryOperator.LessThan,
            x,
            new SmtIntegerConstant(10));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyImplication(new[] { xAtMostNine }, xLessThanTen);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_ExactStringLengthEntailment_BypassesSolver()
    {
        var text = new SmtVariable("exact_string_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var textIsAbc = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            text,
            new SmtStringConstant("ABC"));
        var lengthAtLeastThree = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            new SmtStringLengthTerm(text),
            new SmtIntegerConstant(3));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyImplication(new[] { textIsAbc }, lengthAtLeastThree);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_ExactStringPredicateEntailment_BypassesSolver()
    {
        var text = new SmtVariable("exact_predicate_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var textIsAbc = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            text,
            new SmtStringConstant("ABC"));
        var startsWithA = new SmtStringStartsWithFormula(text, new SmtStringConstant("A"));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyImplication(new[] { textIsAbc }, startsWithA);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_ExactStringNegatedPredicateEntailment_BypassesSolver()
    {
        var text = new SmtVariable("exact_negated_predicate_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var textIsAbc = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            text,
            new SmtStringConstant("ABC"));
        var doesNotEndWithZ = new SmtUnaryFormula(
            SmtUnaryOperator.Not,
            new SmtStringEndsWithFormula(text, new SmtStringConstant("Z")));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyImplication(new[] { textIsAbc }, doesNotEndWithZ);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_ContradictoryExactStringValues_BypassesSolver()
    {
        var text = new SmtVariable("exact_contradiction_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            1,
            3));

        var result = service.ClassifyPathFeasibility(new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC")),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("XYZ"))
        });

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_DivideByZeroContradictedByGuard_BypassesSolver()
    {
        var divisor = new SmtVariable("divisor", SmtValueKind.Int);
        var divisorIsNotZero = new SmtBinaryFormula(
            SmtBinaryOperator.NotEqual,
            divisor,
            new SmtIntegerConstant(0));
        var divisorIsZero = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            divisor,
            new SmtIntegerConstant(0));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.Classify(new PurityProofQuery(
            new[] { divisorIsNotZero },
            new PurityHazard(PurityHazardKind.DivideByZero, divisorIsZero)));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("divide_by_zero_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_DivideByZeroContradictedByPositiveInterval_BypassesSolver()
    {
        var divisor = new SmtVariable("positive_divisor_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var divisorIsPositive = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThan,
            divisor,
            new SmtIntegerConstant(0));
        var divisorIsZero = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            divisor,
            new SmtIntegerConstant(0));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.Classify(new PurityProofQuery(
            new[] { divisorIsPositive },
            new PurityHazard(PurityHazardKind.DivideByZero, divisorIsZero)));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("divide_by_zero_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_ReusedSolverContext_DistinguishesSameNamedVariablesByKind()
    {
        var intX = new SmtVariable("x", SmtValueKind.Int);
        var stringX = new SmtVariable("x", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var intResult = service.ClassifyPathFeasibility(new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, intX, new SmtIntegerConstant(1))
        });
        var stringResult = service.ClassifyPathFeasibility(new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, stringX, new SmtStringConstant("A"))
        });

        Assert.That(intResult.PathFeasibility, Is.EqualTo(Feasibility.Satisfiable));
        Assert.That(stringResult.PathFeasibility, Is.EqualTo(Feasibility.Satisfiable));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(2));
    }

    [Test]
    public void Classify_ExpressionNodeBudgetExceeded_ReturnsConservativeUnknownWithoutSolver()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var trigger = new SmtBinaryFormula(
            SmtBinaryOperator.And,
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(0)),
            new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(10)));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            4,
            3));

        var result = service.Classify(CreateQuery(Array.Empty<SmtFormula>(), trigger));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_expression_budget_exceeded"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_DeepPathFormulaOverBudget_ReturnsBeforeNormalization()
    {
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            4,
            128));

        var result = service.Classify(CreateQuery(
            new[] { CreateNestedNegation(4096) },
            new SmtBooleanConstant(true)));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_expression_budget_exceeded"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Classify_MethodBudgetDoesNotExpireBeforeFirstSolverQueryByWallClock()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(1),
            4,
            32));

        await Task.Delay(20);

        var result = service.Classify(CreateQuery(new[] { xIsZero }, xIsZero));

        Assert.That(result.Reason, Is.Not.EqualTo("smt_method_budget_exceeded"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
    }

    [Test]
    public void Classify_MethodBudgetExceededAfterSolverTime_ReturnsConservativeUnknownWithoutSolver()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
        var xIsPositive = new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(0));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromTicks(1),
            4,
            32));

        _ = service.Classify(CreateQuery(new[] { xIsZero }, xIsZero));
        var result = service.Classify(CreateQuery(new[] { xIsPositive }, xIsPositive));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_method_budget_exceeded"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
    }

    [Test]
    public void Classify_RepeatedEquivalentQuery_UsesCache()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(1000),
            4,
            32));
        var query = CreateQuery(new[] { xIsZero }, xIsZero);

        var first = service.Classify(query);
        var second = service.Classify(query);

        Assert.That(first.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
        Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
        Assert.That(service.CacheEntryCount, Is.EqualTo(1));
    }

    [Test]
    public void Classify_CacheHitsBypassExhaustedMethodBudget()
    {
        var variableName = "budget_cache_" + Guid.NewGuid().ToString("N");
        var x = new SmtVariable(variableName, SmtValueKind.Int);
        var y = new SmtVariable(variableName + "_other", SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
        var yIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, y, new SmtIntegerConstant(0));
        var options = new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromTicks(1),
            4,
            32,
            true);
        var cachedQuery = CreateQuery(new[] { xIsZero }, xIsZero);
        var budgetBurnQuery = CreateQuery(new[] { yIsZero }, yIsZero);
        using var firstService = new SmtAnalysisService(options);
        using var secondService = new SmtAnalysisService(options);

        var first = firstService.Classify(cachedQuery);
        var localCached = firstService.Classify(cachedQuery);
        _ = secondService.Classify(budgetBurnQuery);
        var sharedCached = secondService.Classify(cachedQuery);

        Assert.That(first.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
        Assert.That(localCached.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(sharedCached.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(localCached.Reason, Is.Not.EqualTo("smt_method_budget_exceeded"));
        Assert.That(sharedCached.Reason, Is.Not.EqualTo("smt_method_budget_exceeded"));
        Assert.That(firstService.ExecutedQueryCount, Is.EqualTo(1));
        Assert.That(secondService.ExecutedQueryCount, Is.EqualTo(1));
        Assert.That(firstService.CacheEntryCount, Is.EqualTo(1));
        Assert.That(secondService.CacheEntryCount, Is.EqualTo(2));
    }

    [Test]
    public void Classify_SharedResultCacheEnabled_ReusesResultAcrossServices()
    {
        var variableName = "shared_" + Guid.NewGuid().ToString("N");
        var x = new SmtVariable(variableName, SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
        var options = new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(1000),
            4,
            32,
            true);
        var query = CreateQuery(new[] { xIsZero }, xIsZero);
        using var firstService = new SmtAnalysisService(options);
        using var secondService = new SmtAnalysisService(options);

        var first = firstService.Classify(query);
        var second = secondService.Classify(query);

        Assert.That(first.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
        Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(firstService.ExecutedQueryCount, Is.EqualTo(1));
        Assert.That(secondService.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(secondService.CacheEntryCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Classify_SharedResultCacheEnabled_CoalescesConcurrentQueries()
    {
        var variableName = "shared_concurrent_" + Guid.NewGuid().ToString("N");
        var x = new SmtVariable(variableName, SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
        var options = new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(1000),
            4,
            32,
            true);
        var query = CreateQuery(new[] { xIsZero }, xIsZero);
        const int serviceCount = 8;
        var services = Enumerable.Range(0, serviceCount)
            .Select(_ => new SmtAnalysisService(options))
            .ToArray();
        using var startGate = new Barrier(serviceCount);

        try
        {
            var tasks = services
                .Select(service => Task.Run(() =>
                {
                    startGate.SignalAndWait();
                    return service.Classify(query);
                }))
                .ToArray();
            var results = await Task.WhenAll(tasks);

            Assert.That(
                results.Select(result => result.Outcome),
                Is.All.EqualTo(PurityProofOutcome.ProvablyImpure));
            Assert.That(services.Sum(service => service.ExecutedQueryCount), Is.EqualTo(1));
            Assert.That(services.Sum(service => service.CacheEntryCount), Is.EqualTo(serviceCount));
        }
        finally
        {
            foreach (var service in services) service.Dispose();
        }
    }

    [Test]
    public void SymbolicBudgetDiagnostics_LargeBudgetsClampMilliseconds()
    {
        var options = new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds((double)int.MaxValue + 1),
            TimeSpan.FromMilliseconds((double)int.MaxValue + 2),
            4,
            32);
        using var smtAnalysis = new SmtAnalysisService(options);

        var diagnostics = SymbolicSmtDiagnostics.FromService(smtAnalysis);
        var proof = SymbolicReachabilityService.ClassifyFormulaReachability(
            new[] { new SmtBooleanConstant(false) },
            smtAnalysis);

        Assert.That(diagnostics.QueryTimeoutMs, Is.EqualTo(int.MaxValue));
        Assert.That(diagnostics.MethodBudgetMs, Is.EqualTo(int.MaxValue));
        Assert.That(proof.Info.Budget, Is.Not.Null);
        Assert.That(proof.Info.Budget!.TimeoutMilliseconds, Is.EqualTo(int.MaxValue));
        Assert.That(proof.Info.Budget.MethodBudgetMilliseconds, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void ClassifyImplication_ProvesFactFromPathConditions()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0))
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, x, new SmtIntegerConstant(1));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(service.PathConditionsImply(pathConditions, fact), Is.True);
    }

    [Test]
    public void ClassifyImplication_ReturnsReachableWhenFactDoesNotFollow()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(0))
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(0));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Satisfiable));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Satisfiable));
        Assert.That(service.PathConditionsImply(pathConditions, fact), Is.False);
    }

    [Test]
    public void ClassifyImplication_ProvesStrictRegexLiteralLengthFact()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, @"\A[A-Z][0-9]\z")
        };
        var fact = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtStringLengthTerm(text),
            new SmtIntegerConstant(2));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void ClassifyPathFeasibility_DollarAnchorAllowsTrailingNewline()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, "^AB$"),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AB\n"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Satisfiable));
    }

    [Test]
    public void ClassifyPathFeasibility_CombinesStrictRegexAndStringEquality()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, @"\AAB\z"),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("AB"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void ClassifyPathFeasibility_CombinesNonCapturingRegexGroupAndStringEquality()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, @"\A(?:AB|CD)\z"),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("EF"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void ClassifyPathFeasibility_CombinesNegatedRegexClassAndStringEquality()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, @"\A[^A]\z"),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("A"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void ClassifyPathFeasibility_CombinesRegexHexEscapesAndStringEquality()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, "\\A\\u0041\\x42\\z"),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("AB"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void ClassifyImplication_ProvesShorthandRegexLengthFact()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, @"\A\d\s\w\z")
        };
        var fact = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtStringLengthTerm(text),
            new SmtIntegerConstant(3));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void ClassifyPathFeasibility_NegatedShorthandRegexClassConcreteMatchIsSelfVerified()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, @"\A[^\d]\z"),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("A"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Satisfiable));
    }

    [Test]
    public void ClassifyPathFeasibility_ShorthandRegexConcreteMismatchIsRejectedByDotNetValidation()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, @"\A\d\z"),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("A"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void ClassifyImplication_ProvesCategoryRegexLengthFact()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, @"\A\p{Lu}\P{Ll}\z")
        };
        var fact = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtStringLengthTerm(text),
            new SmtIntegerConstant(2));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void ClassifyImplication_WordBoundaryRegexLengthFactRemainsConservative()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, @"\A\bAB\B?\z")
        };
        var fact = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtStringLengthTerm(text),
            new SmtIntegerConstant(2));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unknown));
    }

    [Test]
    public void ClassifyPathFeasibility_NegatedCategoryRegexClassConcreteMismatchIsRejectedByDotNetValidation()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, @"\A[^\p{Lu}]\z"),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("A"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void ClassifyPathFeasibility_InvalidConcreteRegexRemainsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtRegexMatchFormula(text, "("),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("A"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unknown));
    }

    [Test]
    public void ClassifyPathFeasibility_CombinesStringContainsAndEquality()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtStringContainsFormula(text, new SmtStringConstant("Z")),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void ClassifyPathFeasibility_ConcreteStringStartsWithMismatchIsRejected()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtStringStartsWithFormula(text, new SmtStringConstant("AB")),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ZAB"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void ClassifyPathFeasibility_ConcreteNegatedStringEndsWithMismatchIsRejected()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtUnaryFormula(
                SmtUnaryOperator.Not,
                new SmtStringEndsWithFormula(text, new SmtStringConstant("BC"))),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void ClassifyPathFeasibility_CombinesStringConcatAndEquality()
    {
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                new SmtStringConcatTerm(new SmtStringConstant("A"), new SmtStringConstant("B")),
                new SmtStringConstant("AB"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void ClassifyPathFeasibility_ReportsContradictoryPathUnsatisfiable()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(0)),
            new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(0))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void ClassifyPathFeasibility_BooleanAliasComparisonContradiction_IsUnsatisfiable()
    {
        var value = new SmtVariable("value", SmtValueKind.Int);
        var isZero = new SmtVariable("isZero", SmtValueKind.Bool);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                isZero,
                new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtIntegerConstant(0))),
            isZero,
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, value, new SmtIntegerConstant(0))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void ClassifyImplication_TransitiveBooleanEquivalenceEntailment_BypassesSolver()
    {
        var left = new SmtVariable("bool_equiv_left_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var middle = new SmtVariable("bool_equiv_middle_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var right = new SmtVariable("bool_equiv_right_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, left, middle),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, middle, right)
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right);

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_TransitiveBooleanNegationEntailment_BypassesSolver()
    {
        var left = new SmtVariable("bool_neg_left_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var middle = new SmtVariable("bool_neg_middle_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var right = new SmtVariable("bool_neg_right_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, middle),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, middle, right)
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right);

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_NegatedBooleanRelationEntailment_BypassesSolver()
    {
        var left = new SmtVariable("bool_not_rel_left_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var middle = new SmtVariable("bool_not_rel_middle_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var right = new SmtVariable("bool_not_rel_right_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                left,
                new SmtUnaryFormula(SmtUnaryOperator.Not, middle)),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, middle, right)
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, right);

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_NegatedBooleanRelationParityContradiction_BypassesSolver()
    {
        var left = new SmtVariable("bool_not_parity_left_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var middle = new SmtVariable("bool_not_parity_middle_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var right = new SmtVariable("bool_not_parity_right_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                left,
                new SmtUnaryFormula(SmtUnaryOperator.Not, middle)),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, middle, right),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right)
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_BooleanEquivalenceParityContradiction_BypassesSolver()
    {
        var left = new SmtVariable("bool_parity_left_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var middle = new SmtVariable("bool_parity_middle_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var right = new SmtVariable("bool_parity_right_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, left, middle),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, middle, right),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right)
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_IntegerAliasIntervalContradiction_BypassesSolver()
    {
        var x = new SmtVariable("alias_int_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var y = new SmtVariable("alias_int_y_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(10)),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, x, y),
            new SmtBinaryFormula(SmtBinaryOperator.LessThan, y, new SmtIntegerConstant(10))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_IntegerAliasIntervalEntailment_BypassesSolver()
    {
        var x = new SmtVariable("alias_entail_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var y = new SmtVariable("alias_entail_y_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, x, y),
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(3))
        };
        var fact = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            y,
            new SmtIntegerConstant(3));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_AffineOffsetAliasIntervalContradiction_BypassesSolver()
    {
        var x = new SmtVariable("affine_offset_alias_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var y = new SmtVariable("affine_offset_alias_y_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var xPlusTwo = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(2));
        var yPlusFour = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, y, new SmtIntegerConstant(4));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(5)),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, xPlusTwo, yPlusFour),
            new SmtBinaryFormula(SmtBinaryOperator.LessThan, y, new SmtIntegerConstant(3))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_AffineOffsetAliasIntervalEntailment_BypassesSolver()
    {
        var x = new SmtVariable("affine_offset_entail_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var y = new SmtVariable("affine_offset_entail_y_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var xPlusTwo = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(2));
        var yPlusFour = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, y, new SmtIntegerConstant(4));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, xPlusTwo, yPlusFour),
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(5))
        };
        var fact = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            y,
            new SmtIntegerConstant(3));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_SameBaseAffineEqualityContradiction_BypassesSolver()
    {
        var x = new SmtVariable("same_base_affine_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var xPlusTwo = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(2));
        var xPlusThree = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(3));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, xPlusTwo, xPlusThree)
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_SameBaseAffineOrderingContradiction_BypassesSolver()
    {
        var x = new SmtVariable("same_base_affine_order_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var xPlusTwo = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(2));
        var xPlusThree = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(3));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.LessThan, xPlusThree, xPlusTwo)
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_SameBaseAffineOrderingTautology_BypassesSolver()
    {
        var x = new SmtVariable("same_base_affine_tautology_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var xPlusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(1));
        var xPlusTwo = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(2));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var fact = new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, xPlusOne, xPlusTwo);

        var result = service.ClassifyImplication(Array.Empty<SmtFormula>(), fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_AffineComparisonAgainstExactTerm_BypassesSolver()
    {
        var x = new SmtVariable("affine_exact_compare_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var y = new SmtVariable("affine_exact_compare_y_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var xPlusTwo = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(2));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, y, new SmtIntegerConstant(10)),
            new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, x, new SmtIntegerConstant(8))
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, xPlusTwo, y);

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_StringAliasContradiction_BypassesSolver()
    {
        var left = new SmtVariable("alias_text_left_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var right = new SmtVariable("alias_text_right_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, left, new SmtStringConstant("ABC")),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, right, new SmtStringConstant("ABC"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_ReferenceAliasNullContradiction_BypassesSolver()
    {
        var left = new SmtVariable("alias_ref_left_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var right = new SmtVariable("alias_ref_right_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, left, new SmtNullConstant()),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, right, new SmtNullConstant())
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_DisjunctionReferenceNullContradiction_BypassesSolver()
    {
        var value = new SmtVariable("disjunction_ref_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var guard = new SmtVariable("disjunction_guard_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var valueIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtNullConstant());
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Or, valueIsNull, guard),
            new SmtUnaryFormula(SmtUnaryOperator.Not, guard),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, value, new SmtNullConstant())
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_NegatedDisjunctionReferenceNullContradiction_BypassesSolver()
    {
        var value = new SmtVariable("negated_disjunction_ref_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var guard = new SmtVariable("negated_disjunction_guard_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var valueIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtNullConstant());
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtUnaryFormula(
                SmtUnaryOperator.Not,
                new SmtBinaryFormula(SmtBinaryOperator.Or, valueIsNull, guard)),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtNullConstant())
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_ReferenceAliasNonNullEntailment_BypassesSolver()
    {
        var left = new SmtVariable("alias_ref_entail_left_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var right = new SmtVariable("alias_ref_entail_right_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, new SmtNullConstant())
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, right, new SmtNullConstant());

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_NullConstantInequality_BypassesSolver()
    {
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, new SmtNullConstant(), new SmtNullConstant())
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_AliasInsideIntegerExpressionIntervalContradiction_BypassesSolver()
    {
        var x = new SmtVariable("expr_alias_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var y = new SmtVariable("expr_alias_y_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var xPlusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(1));
        var yPlusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, y, new SmtIntegerConstant(1));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, x, y),
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, xPlusOne, new SmtIntegerConstant(5)),
            new SmtBinaryFormula(SmtBinaryOperator.LessThan, yPlusOne, new SmtIntegerConstant(5))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_SubtractionIntervalContradiction_BypassesSolver()
    {
        var x = new SmtVariable("subtract_interval_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var xMinusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, x, new SmtIntegerConstant(1));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(5)),
            new SmtBinaryFormula(SmtBinaryOperator.LessThan, xMinusOne, new SmtIntegerConstant(4))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_PositiveConstantMultiplyIntervalEntailment_BypassesSolver()
    {
        var x = new SmtVariable("multiply_interval_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var twiceX = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, x, new SmtIntegerConstant(2));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(3))
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, twiceX, new SmtIntegerConstant(6));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_AliasInsideStringLengthEntailment_BypassesSolver()
    {
        var left = new SmtVariable("length_alias_left_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var right = new SmtVariable("length_alias_right_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var leftLength = new SmtStringLengthTerm(left);
        var rightLength = new SmtStringLengthTerm(right);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, leftLength, new SmtIntegerConstant(3))
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, rightLength, new SmtIntegerConstant(3));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_AliasInsideStringConcatContradiction_BypassesSolver()
    {
        var left = new SmtVariable("concat_alias_left_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var right = new SmtVariable("concat_alias_right_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var leftConcat = new SmtStringConcatTerm(left, new SmtStringConstant("!"));
        var rightConcat = new SmtStringConcatTerm(right, new SmtStringConstant("!"));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, leftConcat, new SmtStringConstant("A!")),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, rightConcat, new SmtStringConstant("A!"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_StringConcatKnownOperandLengths_BypassesSolver()
    {
        var left = new SmtVariable("concat_length_left_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var right = new SmtVariable("concat_length_right_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var concatLength = new SmtStringLengthTerm(new SmtStringConcatTerm(left, right));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(left),
                new SmtIntegerConstant(2)),
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(right),
                new SmtIntegerConstant(3))
        };
        var fact = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            concatLength,
            new SmtIntegerConstant(5));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_NegativeStringLength_BypassesSolver()
    {
        var text = new SmtVariable("negative_length_" + Guid.NewGuid().ToString("N"), SmtValueKind.String);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(0))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_ConditionalIntegerComparisonWithKnownGuard_BypassesSolver()
    {
        var guard = new SmtVariable("conditional_int_guard_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var selected = new SmtVariable("conditional_int_selected_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var fallback = new SmtVariable("conditional_int_fallback_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var conditional = new SmtConditionalFormula(guard, selected, fallback, SmtValueKind.Int);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            guard,
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, conditional, new SmtIntegerConstant(10)),
            new SmtBinaryFormula(SmtBinaryOperator.LessThan, selected, new SmtIntegerConstant(10))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_ConditionalReferenceNullFactWithKnownGuard_BypassesSolver()
    {
        var guard = new SmtVariable("conditional_ref_guard_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var left = new SmtVariable("conditional_ref_left_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var right = new SmtVariable("conditional_ref_right_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var conditional = new SmtConditionalFormula(guard, left, right, SmtValueKind.Reference);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtUnaryFormula(SmtUnaryOperator.Not, guard),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, right, new SmtNullConstant())
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, conditional, new SmtNullConstant());

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_ConditionalReferenceAliasNullStatePropagatesToSelectedBranch_BypassesSolver()
    {
        var guard = new SmtVariable("conditional_ref_alias_guard_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var selected = new SmtVariable("conditional_ref_alias_selected_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Reference);
        var whenTrue = new SmtVariable("conditional_ref_alias_true_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Reference);
        var whenFalse = new SmtVariable("conditional_ref_alias_false_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Reference);
        var conditional = new SmtConditionalFormula(guard, whenTrue, whenFalse, SmtValueKind.Reference);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            guard,
            new SmtBinaryFormula(SmtBinaryOperator.Equal, selected, conditional),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, selected, new SmtNullConstant())
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, whenTrue, new SmtNullConstant());

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_ConditionalBooleanWithKnownGuard_BypassesSolver()
    {
        var guard = new SmtVariable("conditional_bool_guard_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var selected = new SmtVariable("conditional_bool_selected_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var fallback = new SmtVariable("conditional_bool_fallback_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var conditional = new SmtConditionalFormula(guard, selected, fallback, SmtValueKind.Bool);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            guard,
            conditional,
            new SmtUnaryFormula(SmtUnaryOperator.Not, selected)
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_ConditionalWithEqualBranchesCollapsesBeforeSolver()
    {
        var guard = new SmtVariable("conditional_equal_branch_guard_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Bool);
        var value = new SmtVariable("conditional_equal_branch_value_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var conditional = new SmtConditionalFormula(guard, value, value, SmtValueKind.Int);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, conditional, new SmtIntegerConstant(7)),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, value, new SmtIntegerConstant(7))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_ConditionalIntegerBranchImplications_BypassesSolver()
    {
        var guard = new SmtVariable("conditional_branch_int_guard_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var whenTrue = new SmtVariable("conditional_branch_int_true_" + Guid.NewGuid().ToString("N"), SmtValueKind.Int);
        var whenFalse = new SmtVariable("conditional_branch_int_false_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Int);
        var conditional = new SmtConditionalFormula(guard, whenTrue, whenFalse, SmtValueKind.Int);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtUnaryFormula(SmtUnaryOperator.Not, guard),
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, whenTrue, new SmtIntegerConstant(0))),
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                guard,
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, whenFalse, new SmtIntegerConstant(0)))
        };
        var fact = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            conditional,
            new SmtIntegerConstant(0));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_ConditionalReferenceBranchImplications_BypassesSolver()
    {
        var guard = new SmtVariable("conditional_branch_ref_guard_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var whenTrue = new SmtVariable("conditional_branch_ref_true_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Reference);
        var whenFalse = new SmtVariable("conditional_branch_ref_false_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Reference);
        var conditional = new SmtConditionalFormula(guard, whenTrue, whenFalse, SmtValueKind.Reference);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtUnaryFormula(SmtUnaryOperator.Not, guard),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, whenTrue, new SmtNullConstant())),
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                guard,
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, whenFalse, new SmtNullConstant())),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, conditional, new SmtNullConstant())
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_ConditionalBooleanBranchImplications_BypassesSolver()
    {
        var guard = new SmtVariable("conditional_branch_bool_guard_" + Guid.NewGuid().ToString("N"), SmtValueKind.Bool);
        var whenTrue = new SmtVariable("conditional_branch_bool_true_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Bool);
        var whenFalse = new SmtVariable("conditional_branch_bool_false_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Bool);
        var conditional = new SmtConditionalFormula(guard, whenTrue, whenFalse, SmtValueKind.Bool);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtUnaryFormula(SmtUnaryOperator.Not, guard),
                whenTrue),
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                guard,
                whenFalse),
            new SmtUnaryFormula(SmtUnaryOperator.Not, conditional)
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_ConditionalSelectedReferenceNullBranchImplication_BypassesSolver()
    {
        var guard = new SmtVariable("conditional_selected_ref_guard_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Bool);
        var first = new SmtVariable("conditional_selected_ref_first_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Reference);
        var second = new SmtVariable("conditional_selected_ref_second_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Reference);
        var resultReference = new SmtVariable("conditional_selected_ref_result_" + Guid.NewGuid().ToString("N"),
            SmtValueKind.Reference);
        var resultIsNonNull = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, resultReference, new SmtNullConstant());
        var firstIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, first, new SmtNullConstant());
        var secondIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, second, new SmtNullConstant());
        var selectedReference = new SmtConditionalFormula(guard, first, second, SmtValueKind.Reference);
        var selectedIsNull = new SmtConditionalFormula(guard, firstIsNull, secondIsNull, SmtValueKind.Bool);
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.Equal, resultReference, selectedReference),
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtBinaryFormula(SmtBinaryOperator.Equal, resultReference, new SmtNullConstant()),
                selectedIsNull)
        };
        var fact = new SmtBinaryFormula(
            SmtBinaryOperator.And,
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtUnaryFormula(SmtUnaryOperator.Not, guard),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Or,
                    resultIsNonNull,
                    firstIsNull)),
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                guard,
                new SmtBinaryFormula(
                    SmtBinaryOperator.Or,
                    resultIsNonNull,
                    secondIsNull)));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_RuntimeTypeTestPredicateIsCongruentUnderReferenceEquality()
    {
        var x = new SmtVariable("runtime_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var y = new SmtVariable("runtime_y_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var xEqualsY = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, y);
        var xIsString = new SmtRuntimeTypeTestFormula(x, "System.String");
        var yIsString = new SmtRuntimeTypeTestFormula(y, "System.String");
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyImplication(new SmtFormula[] { xEqualsY, xIsString }, yIsString);

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
    }

    [Test]
    public void ClassifyPathFeasibility_RuntimeTypeTestPredicateContradictsItsNegationThroughReferenceEquality()
    {
        var x = new SmtVariable("runtime_x_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var y = new SmtVariable("runtime_y_" + Guid.NewGuid().ToString("N"), SmtValueKind.Reference);
        var xEqualsY = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, y);
        var xIsString = new SmtRuntimeTypeTestFormula(x, "System.String");
        var yIsNotString = new SmtUnaryFormula(
            SmtUnaryOperator.Not,
            new SmtRuntimeTypeTestFormula(y, "System.String"));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyPathFeasibility(new SmtFormula[] { xEqualsY, xIsString, yIsNotString });

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    private static void AssertPermanentFailureCode(Exception exception, string expectedCode)
    {
        var factoryCalls = 0;
        using var service = new SmtAnalysisService(
            SmtAnalysisOptions.Default,
            () =>
            {
                Interlocked.Increment(ref factoryCalls);
                throw exception;
            });

        var result = service.Classify(CreateSolverQuery("wrapped_native_" + Guid.NewGuid().ToString("N")));

        Assert.That(result.Reason, Is.EqualTo("smt_unavailable"));
        Assert.That(factoryCalls, Is.EqualTo(1));
        Assert.That(service.IsPermanentlyUnavailable, Is.True);
        Assert.That(service.Health.State, Is.EqualTo(SmtAnalysisHealthState.PermanentlyUnavailable));
        Assert.That(service.Health.LastFailureCode, Is.EqualTo(expectedCode));
    }

    private static PurityProofQuery CreateQuery(
        IReadOnlyList<SmtFormula> pathConditions,
        SmtFormula triggerCondition)
    {
        return new PurityProofQuery(
            pathConditions,
            new PurityHazard(
                PurityHazardKind.ImpureCallReachability,
                triggerCondition));
    }

    private static PurityProofQuery CreateSolverQuery(string name)
    {
        var value = new SmtVariable(name, SmtValueKind.Int);
        var valueIsZero = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            value,
            new SmtIntegerConstant(0));
        return CreateQuery(new[] { valueIsZero }, valueIsZero);
    }

    private static PurityProofResult CreateTransientFailure()
    {
        return new PurityProofResult(
            PurityProofOutcome.Unknown,
            Feasibility.Unknown,
            Feasibility.Unknown,
            "path_feasibility_unknown",
            new SmtSatisfyingWitness(
                SmtWitnessStatus.Unsupported,
                "z3_transient_failure",
                Array.Empty<SmtModelAssignment>()));
    }

    private static PurityProofResult CreateImpureResult()
    {
        return new PurityProofResult(
            PurityProofOutcome.ProvablyImpure,
            Feasibility.Satisfiable,
            Feasibility.Satisfiable,
            "impure_call_reachable");
    }

    private sealed class StubProofSearchSession : ISmtProofSearchSession
    {
        private readonly Func<PurityProofQuery, TimeSpan, PurityProofResult> _classify;
        private readonly Action? _dispose;

        public StubProofSearchSession(
            Func<PurityProofQuery, TimeSpan, PurityProofResult> classify,
            Action? dispose = null)
        {
            _classify = classify;
            _dispose = dispose;
        }

        public long ConsumedResourceCount => 0;

        public PurityProofResult Classify(PurityProofQuery query, TimeSpan timeout)
        {
            return _classify(query, timeout);
        }

        public void Dispose()
        {
            _dispose?.Invoke();
        }
    }

    private static SmtFormula CreateNestedNegation(int depth)
    {
        SmtFormula formula = new SmtBooleanConstant(true);
        for (var index = 0; index < depth; index++) formula = new SmtUnaryFormula(SmtUnaryOperator.Not, formula);

        return formula;
    }
}
