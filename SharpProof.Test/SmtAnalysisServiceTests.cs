using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.ProofCore.Analysis;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using static SharpProof.Test.SmtTestFormula;

namespace SharpProof.Test;

[TestFixture]
[Category("SmtHeavy")]
public class SmtAnalysisServiceTests {
    [Test]
    public void ProjectConfiguration_HonorsBuildPropertyPrefix() {
        var options = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new ProjectConfigOptionsProvider(
                ImmutableDictionary<string, string>.Empty
                    .Add("build_property.sharpproof_smt_timeout_ms", "123")
                    .Add("build_property.sharpproof_analysis_max_merged_if_else_facts", "17")));

        var configuration = SymbolicProjectConfiguration.FromAnalyzerOptions(options);

        Assert.That(configuration.SmtOptions.QueryTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(123)));
        Assert.That(configuration.AnalysisLimits.MaxMergedIfElseFacts, Is.EqualTo(17));
    }

    [Test]
    public void NativeLibraryBootstrap_RecognizesSupportedX64Platforms() {
        Assert.That(
            SmtNativeLibraryBootstrap.GetNativeLibraryFileName(OSPlatform.Windows, Architecture.X64),
            Is.EqualTo("libz3.dll"));
        Assert.That(
            SmtNativeLibraryBootstrap.GetNativeLibraryFileName(OSPlatform.OSX, Architecture.X64),
            Is.EqualTo("libz3.dylib"));
        Assert.That(
            SmtNativeLibraryBootstrap.GetNativeLibraryFileName(OSPlatform.Linux, Architecture.X64),
            Is.EqualTo("libz3.so"));
        Assert.That(
            SmtNativeLibraryBootstrap.GetNativeLibraryFileName(OSPlatform.Linux, Architecture.Arm64),
            Is.Null);
    }

    [Test]
    public void ForMode_Deep_ReturnsExpandedBudgetPreset() {
        var options = SmtAnalysisOptions.ForMode(SmtAnalysisMode.Deep);

        Assert.That(options.Mode, Is.EqualTo(SmtAnalysisMode.Deep));
        Assert.That(options.QueryTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(2000)));
        Assert.That(options.MethodBudget, Is.EqualTo(TimeSpan.FromMilliseconds(15000)));
        Assert.That(options.MaxPathConditions, Is.EqualTo(512));
        Assert.That(options.MaxExpressionNodes, Is.EqualTo(8192));
        Assert.That(options.UseSharedResultCache, Is.False);
    }

    [Test]
    public void DefaultPreset_DisablesSharedResultCache() {
        Assert.That(SmtAnalysisOptions.Default.UseSharedResultCache, Is.False);
    }

    [Test]
    public void WithOverrides_PreservesModeAndAppliesExplicitBudgets() {
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
    public void LifecycleOptions_DefaultsAndOverridesAreStable() {
        var defaults = SmtSolverLifecycleOptions.Default;

        Assert.That(defaults.MaxTransientRetries, Is.EqualTo(1));
        Assert.That(defaults.RecycleContextOnTransientFailure, Is.True);
        Assert.That(defaults.DisposeCurrentThreadContextOnServiceDispose, Is.True);
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
    public void Classify_TransientFailure_RecyclesRetriesAndRecovers() {
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
        var snapshot = diagnostics.Snapshot;

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Disproven));
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
        Assert.That(snapshot.Health.TransientRetryCount, Is.EqualTo(1));
        Assert.That(snapshot.Lifecycle, Is.SameAs(options.Lifecycle));
        Assert.That(snapshot.Health.RecoveredTransientFailureCount, Is.EqualTo(1));
    }

    [Test]
    public void Classify_ExhaustedTransientFailure_IsNotCached() {
        var attempts = 0;
        var options = SmtAnalysisOptions.Default.WithLifecycle(
            new SmtSolverLifecycleOptions(maxTransientRetries: 0));
        using var service = new SmtAnalysisService(
            options,
            () => new StubProofSearchSession(
                (_, _) => {
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
    public void Classify_NativeLoadFailure_IsPermanentlyUnavailable() {
        var factoryCalls = 0;
        using var service = new SmtAnalysisService(
            SmtAnalysisOptions.Default,
            () => {
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
    public void Classify_WrappedNativeFailures_PreserveStableFallbackCodes() {
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
    public void TransientSolverContextRecycle_PreservesLocalAndSharedCaches() {
        var attempts = 0;
        var firstFactoryCalls = 0;
        var firstDisposedSessions = 0;
        var options = new SmtAnalysisOptions(
                SmtAnalysisMode.Bounded,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromMilliseconds(1000),
                4,
                32,
                true)
            .WithLifecycle(SmtSolverLifecycleOptions.Default);
        var query = CreateSolverQuery("recycle_cache_" + Guid.NewGuid().ToString("N"));
        using var firstService = new SmtAnalysisService(
            options,
            () => {
                Interlocked.Increment(ref firstFactoryCalls);
                return new StubProofSearchSession(
                    (_, _) => Interlocked.Increment(ref attempts) == 1
                        ? CreateTransientFailure()
                        : CreateImpureResult(),
                    () => Interlocked.Increment(ref firstDisposedSessions));
            });

        var first = firstService.Classify(query);
        var localCached = firstService.Classify(query);
        var secondFactoryCalls = 0;
        using var secondService = new SmtAnalysisService(
            options,
            () => {
                Interlocked.Increment(ref secondFactoryCalls);
                return new StubProofSearchSession((_, _) => CreateImpureResult());
            });
        var sharedCached = secondService.Classify(query);

        Assert.That(first.Outcome, Is.EqualTo(AnalysisProofOutcome.Disproven));
        Assert.That(localCached.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(sharedCached.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(firstFactoryCalls, Is.EqualTo(2));
        Assert.That(firstDisposedSessions, Is.EqualTo(1));
        Assert.That(secondFactoryCalls, Is.Zero);
        Assert.That(firstService.Health.ContextRecycleCount, Is.EqualTo(1));
        Assert.That(firstService.CacheEntryCount, Is.EqualTo(1));
        Assert.That(secondService.CacheEntryCount, Is.EqualTo(1));
    }

    [Test]
    public void TransientSolverContextRecycle_DoesNotRecycleAnotherServiceSession() {
        var firstAttempts = 0;
        var firstFactoryCalls = 0;
        var firstDisposedSessions = 0;
        var secondFactoryCalls = 0;
        var secondDisposedSessions = 0;
        var options = SmtAnalysisOptions.Default.WithLifecycle(new SmtSolverLifecycleOptions(maxTransientRetries: 1));
        using var firstService = new SmtAnalysisService(
            options,
            () => {
                Interlocked.Increment(ref firstFactoryCalls);
                return new StubProofSearchSession(
                    (_, _) => Interlocked.Increment(ref firstAttempts) == 1
                        ? CreateTransientFailure()
                        : CreateImpureResult(),
                    () => Interlocked.Increment(ref firstDisposedSessions));
            });
        using var secondService = new SmtAnalysisService(
            options,
            () => {
                Interlocked.Increment(ref secondFactoryCalls);
                return new StubProofSearchSession(
                    (_, _) => CreateImpureResult(),
                    () => Interlocked.Increment(ref secondDisposedSessions));
            });

        _ = secondService.Classify(CreateSolverQuery("isolated_second_before"));
        _ = firstService.Classify(CreateSolverQuery("isolated_first"));
        _ = secondService.Classify(CreateSolverQuery("isolated_second_after"));

        Assert.That(firstFactoryCalls, Is.EqualTo(2));
        Assert.That(firstDisposedSessions, Is.EqualTo(1));
        Assert.That(firstService.Health.ContextRecycleCount, Is.EqualTo(1));
        Assert.That(secondFactoryCalls, Is.EqualTo(1));
        Assert.That(secondDisposedSessions, Is.Zero);
        Assert.That(secondService.Health.ContextRecycleCount, Is.Zero);
    }

    [Test]
    public void PermanentFailureRecycle_IsolatesServiceOwnedSessions() {
        var firstFactoryCalls = 0;
        var secondFactoryCalls = 0;
        var firstDisposedSessions = 0;
        var secondDisposedSessions = 0;
        using var firstService = new SmtAnalysisService(
            SmtAnalysisOptions.Default,
            () => {
                Interlocked.Increment(ref firstFactoryCalls);
                return new StubProofSearchSession(
                    (_, _) => throw new DllNotFoundException("missing test solver"),
                    () => Interlocked.Increment(ref firstDisposedSessions));
            });
        using var secondService = new SmtAnalysisService(
            SmtAnalysisOptions.Default,
            () => {
                Interlocked.Increment(ref secondFactoryCalls);
                return new StubProofSearchSession(
                    (_, _) => CreateImpureResult(),
                    () => Interlocked.Increment(ref secondDisposedSessions));
            });

        var first = firstService.Classify(CreateSolverQuery("first_service_session"));
        _ = secondService.Classify(CreateSolverQuery("second_service_session_before_recycle"));
        _ = secondService.Classify(CreateSolverQuery("second_service_session_after_recycle"));

        Assert.That(first.Reason, Is.EqualTo("smt_unavailable"));
        Assert.That(firstFactoryCalls, Is.EqualTo(1));
        Assert.That(firstDisposedSessions, Is.EqualTo(1));
        Assert.That(firstService.Health.ContextRecycleCount, Is.EqualTo(1));
        Assert.That(secondFactoryCalls, Is.EqualTo(1));
        Assert.That(secondDisposedSessions, Is.Zero);
        Assert.That(secondService.Health.ContextRecycleCount, Is.Zero);
    }

    [Test]
    public void Dispose_DefaultLifecycle_DisposesCurrentThreadContext() {
        var disposedSessions = 0;
        var service = new SmtAnalysisService(
            SmtAnalysisOptions.Default,
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
    public void Dispose_ConfiguredLifecycle_DisposesContextsCreatedOnAllThreads() {
        const int threadCount = 4;
        var disposedSessions = 0;
        var factoryCalls = 0;
        var service = new SmtAnalysisService(
            SmtAnalysisOptions.Default.WithLifecycle(
                new SmtSolverLifecycleOptions(disposeCurrentThreadContextOnServiceDispose: true)),
            () => {
                Interlocked.Increment(ref factoryCalls);
                return new StubProofSearchSession(
                    (_, _) => CreateImpureResult(),
                    () => Interlocked.Increment(ref disposedSessions));
            });
        var threads = Enumerable.Range(0, threadCount)
            .Select(index => new Thread(() =>
                service.Classify(CreateSolverQuery("thread_owned_context_" + index))))
            .ToArray();

        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads)
            Assert.That(thread.Join(TimeSpan.FromSeconds(10)), Is.True);

        service.Dispose();

        Assert.That(factoryCalls, Is.EqualTo(threadCount));
        Assert.That(disposedSessions, Is.EqualTo(threadCount));
        Assert.That(service.Health.ContextRecycleCount, Is.EqualTo(threadCount));
    }

    [Test]
    public void Classify_OffMode_ReturnsConservativeUnknown() {
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Off,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            4,
            16));

        var result = service.Classify(CreateQuery(Array.Empty<SmtFormula>(), Boolean(true)));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_disabled"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_PathConditionBudgetExceeded_ReturnsConservativeUnknownWithoutSolver() {
        var x = Int("x");
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            1,
            32));

        var result = service.Classify(CreateQuery(
            new SmtFormula[]
            {
                GreaterThanOrEqual(x, Integer(0)),
                LessThan(x, Integer(10))
            },
            Boolean(true)));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_path_condition_budget_exceeded"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_ZeroTimeout_ReturnsConservativeTimeoutWithoutSolver() {
        var value = String("timeout_value_" + Guid.NewGuid().ToString("N"));
        var containsNeedle = new SmtStringContainsFormula(value, Text("needle"));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(500),
            4,
            32));

        var result = service.Classify(CreateQuery(Array.Empty<SmtFormula>(), containsNeedle));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_timeout"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [TestCase(
        true,
        2,
        TestName = "Classify_DuplicateAndTruePathConditions_AreNormalizedBeforeBudgetAndCache")]
    [TestCase(
        false,
        4,
        TestName = "Classify_EquivalentPathConditionOrder_UsesSameCacheEntry")]
    public void Classify_NormalizedPathConditionsUseSameCacheEntry(
        bool includeDuplicateAndTrueConditions,
        int maxPathConditions) {
        var prefix = includeDuplicateAndTrueConditions ? "normalized_" : "ordered_";
        var x = Int(prefix + "x_" + Guid.NewGuid().ToString("N"));
        var y = Int(prefix + "y_" + Guid.NewGuid().ToString("N"));
        var xAtLeastZero = GreaterThanOrEqual(x, Integer(0));
        var yAtLeastZero = GreaterThanOrEqual(y, Integer(0));
        var fact = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, y),
            Integer(0));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(1000),
            maxPathConditions,
            64));

        var firstPath = includeDuplicateAndTrueConditions
            ? new SmtFormula[] { xAtLeastZero, yAtLeastZero, Boolean(true), xAtLeastZero }
            : new SmtFormula[] { xAtLeastZero, yAtLeastZero };
        var first = service.ClassifyImplication(
            firstPath,
            fact);
        var second = service.ClassifyImplication(new[] { yAtLeastZero, xAtLeastZero }, fact);

        Assert.That(first.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
        Assert.That(service.CacheEntryCount, Is.EqualTo(1));
    }

    [Test]
    public void Classify_SyntacticPathContradiction_BypassesBudgetsAndSolver() {
        var x = Int("x");
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            1,
            3));

        var result = service.Classify(CreateQuery(
            new SmtFormula[]
            {
                Equal(x, Integer(0)),
                NotEqual(x, Integer(0))
            },
            Boolean(true)));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.PathCheck.WasAttempted, Is.True);
        Assert.That(result.HazardCheck.WasAttempted, Is.False);
        Assert.That(result.HazardCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_SyntacticIntegerIntervalContradiction_BypassesBudgetsAndSolver() {
        var x = Int("interval_" + Guid.NewGuid().ToString("N"));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            1,
            3));

        var result = service.Classify(CreateQuery(
            new SmtFormula[]
            {
                GreaterThanOrEqual(x, Integer(10)),
                LessThan(x, Integer(10))
            },
            Boolean(true)));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.PathCheck.WasAttempted, Is.True);
        Assert.That(result.HazardCheck.WasAttempted, Is.False);
        Assert.That(result.HazardCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_SyntacticDisjunctionOfKnownComparisonComplements_BypassesSolver() {
        var values = Reference("values_" + Guid.NewGuid().ToString("N"));
        var index = Int("index_" + Guid.NewGuid().ToString("N"));
        var length = Int(values.Name + ".Length");
        var valuesIsNotNull = new SmtBinaryFormula(
            SmtBinaryOperator.NotEqual,
            values,
            new SmtNullConstant());
        var indexIsNonNegative = GreaterThanOrEqual(index, Integer(0));
        var indexIsInBounds = LessThan(index, length);
        var contradiction = new SmtBinaryFormula(
            SmtBinaryOperator.Or,
            new SmtBinaryFormula(SmtBinaryOperator.Equal, values, new SmtNullConstant()),
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                LessThan(index, Integer(0)),
                GreaterThanOrEqual(index, length)));
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

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_NestedConjunctIntegerContradiction_BypassesSolver() {
        var x = Int("nested_interval_" + Guid.NewGuid().ToString("N"));
        var nestedBounds = new SmtBinaryFormula(
            SmtBinaryOperator.And,
            GreaterThan(x, Integer(3)),
            LessThanOrEqual(x, Integer(3)));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.Classify(CreateQuery(
            new[] { nestedBounds },
            Boolean(true)));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_DirectPathFact_BypassesSolver() {
        var x = Int("x");
        var xIsZero = Equal(x, Integer(0));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyImplication(new[] { xIsZero }, xIsZero);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.HazardCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(
            service.ClassifyImplication(new[] { xIsZero }, xIsZero).Outcome,
            Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_IntegerIntervalEntailment_BypassesSolver() {
        var x = Int("interval_entailment_" + Guid.NewGuid().ToString("N"));
        var xAtMostNine = LessThanOrEqual(x, Integer(9));
        var xLessThanTen = LessThan(x, Integer(10));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyImplication(new[] { xAtMostNine }, xLessThanTen);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.HazardCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_ExactStringLengthEntailment_BypassesSolver() {
        var text = String("exact_string_" + Guid.NewGuid().ToString("N"));
        var textIsAbc = Equal(text, Text("ABC"));
        var lengthAtLeastThree = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            new SmtStringLengthTerm(text),
            Integer(3));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyImplication(new[] { textIsAbc }, lengthAtLeastThree);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_ExactStringPredicateEntailment_BypassesSolver() {
        var text = String("exact_predicate_" + Guid.NewGuid().ToString("N"));
        var textIsAbc = Equal(text, Text("ABC"));
        var startsWithA = new SmtStringStartsWithFormula(text, Text("A"));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyImplication(new[] { textIsAbc }, startsWithA);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyImplication_ExactStringNegatedPredicateEntailment_BypassesSolver() {
        var text = String("exact_negated_predicate_" + Guid.NewGuid().ToString("N"));
        var textIsAbc = Equal(text, Text("ABC"));
        var doesNotEndWithZ = new SmtUnaryFormula(
            SmtUnaryOperator.Not,
            new SmtStringEndsWithFormula(text, Text("Z")));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyImplication(new[] { textIsAbc }, doesNotEndWithZ);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.Reason, Is.EqualTo("branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void ClassifyPathFeasibility_ContradictoryExactStringValues_BypassesSolver() {
        var text = String("exact_contradiction_" + Guid.NewGuid().ToString("N"));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            1,
            3));

        var result = service.ClassifyPathFeasibility(new SmtFormula[]
        {
            Equal(text, Text("ABC")),
            Equal(text, Text("XYZ"))
        });

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_DivideByZeroContradictedByGuard_BypassesSolver() {
        var divisor = Int("divisor");
        var divisorIsNotZero = NotEqual(divisor, Integer(0));
        var divisorIsZero = Equal(divisor, Integer(0));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.Classify(new AnalysisProofQuery(
            new[] { divisorIsNotZero },
            new AnalysisHazard(AnalysisHazardKind.DivideByZero, divisorIsZero)));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.HazardCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("divide_by_zero_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_DivideByZeroContradictedByPositiveInterval_BypassesSolver() {
        var divisor = Int("positive_divisor_" + Guid.NewGuid().ToString("N"));
        var divisorIsPositive = GreaterThan(divisor, Integer(0));
        var divisorIsZero = Equal(divisor, Integer(0));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.Classify(new AnalysisProofQuery(
            new[] { divisorIsPositive },
            new AnalysisHazard(AnalysisHazardKind.DivideByZero, divisorIsZero)));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.HazardCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("divide_by_zero_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_ReusedSolverContext_DistinguishesSameNamedVariablesByKind() {
        var intX = Int("x");
        var stringX = String("x");
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var intResult = service.ClassifyPathFeasibility(new SmtFormula[]
        {
            Equal(intX, Integer(1))
        });
        var stringResult = service.ClassifyPathFeasibility(new SmtFormula[]
        {
            Equal(stringX, Text("A"))
        });

        Assert.That(intResult.PathCheck.Feasibility, Is.EqualTo(Feasibility.Satisfiable));
        Assert.That(stringResult.PathCheck.Feasibility, Is.EqualTo(Feasibility.Satisfiable));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(2));
    }

    [Test]
    public void Classify_ExpressionNodeBudgetExceeded_ReturnsConservativeUnknownWithoutSolver() {
        var x = Int("x");
        var trigger = new SmtBinaryFormula(
            SmtBinaryOperator.And,
            GreaterThanOrEqual(x, Integer(0)),
            LessThan(x, Integer(10)));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            4,
            3));

        var result = service.Classify(CreateQuery(Array.Empty<SmtFormula>(), trigger));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_expression_budget_exceeded"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public void Classify_DeepPathFormulaOverBudget_ReturnsBeforeNormalization() {
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500),
            4,
            128));

        var result = service.Classify(CreateQuery(
            new[] { CreateNestedNegation(4096) },
            Boolean(true)));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_expression_budget_exceeded"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Classify_MethodBudgetDoesNotExpireBeforeFirstSolverQueryByWallClock() {
        var x = Int("x");
        var xIsZero = Equal(x, Integer(0));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(1),
            4,
            32));

        await Task.Delay(20);

        var result = service.Classify(CreateQuery(new[] { xIsZero }, xIsZero));

        Assert.That(result.Reason, Is.Not.EqualTo("smt_method_budget_exceeded"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
    }

    [Test]
    public void Classify_MethodBudgetExceededAfterSolverTime_ReturnsConservativeUnknownWithoutSolver() {
        var x = Int("x");
        var xIsZero = Equal(x, Integer(0));
        var xIsPositive = GreaterThan(x, Integer(0));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromTicks(1),
            4,
            32));

        _ = service.Classify(CreateQuery(new[] { xIsZero }, xIsZero));
        var result = service.Classify(CreateQuery(new[] { xIsPositive }, xIsPositive));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_method_budget_exceeded"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
    }

    [Test]
    public void Classify_RepeatedEquivalentQuery_UsesCache() {
        var x = Int("x");
        var xIsZero = Equal(x, Integer(0));
        var service = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(1000),
            4,
            32));
        var query = CreateQuery(new[] { xIsZero }, xIsZero);

        var first = service.Classify(query);
        var second = service.Classify(query);

        Assert.That(first.Outcome, Is.EqualTo(AnalysisProofOutcome.Disproven));
        Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(1));
        Assert.That(service.CacheEntryCount, Is.EqualTo(1));
    }

    [Test]
    public void Classify_CacheHitsBypassExhaustedMethodBudget() {
        var variableName = "budget_cache_" + Guid.NewGuid().ToString("N");
        var x = Int(variableName);
        var y = Int(variableName + "_other");
        var xIsZero = Equal(x, Integer(0));
        var yIsZero = Equal(y, Integer(0));
        var options = new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromSeconds(2),
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

        Assert.That(first.Outcome, Is.EqualTo(AnalysisProofOutcome.Disproven));
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
    public void Classify_SharedResultCacheEnabled_ReusesResultAcrossServices() {
        var variableName = "shared_" + Guid.NewGuid().ToString("N");
        var x = Int(variableName);
        var xIsZero = Equal(x, Integer(0));
        var options = new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(1000),
            4,
            32,
            true);
        var query = CreateQuery(new[] { xIsZero }, xIsZero);
        using var firstService = new SmtAnalysisService(options);
        using var secondService = new SmtAnalysisService(options);

        var first = firstService.Classify(query);
        var second = secondService.Classify(query);

        Assert.That(first.Outcome, Is.EqualTo(AnalysisProofOutcome.Disproven));
        Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(firstService.ExecutedQueryCount, Is.EqualTo(1));
        Assert.That(secondService.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(secondService.CacheEntryCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Classify_SharedResultCacheEnabled_CoalescesConcurrentQueries() {
        var variableName = "shared_concurrent_" + Guid.NewGuid().ToString("N");
        var x = Int(variableName);
        var xIsZero = Equal(x, Integer(0));
        var options = new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromSeconds(2),
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

        try {
            var tasks = services
                .Select(service => Task.Run(() => {
                    startGate.SignalAndWait();
                    return service.Classify(query);
                }))
                .ToArray();
            var results = await Task.WhenAll(tasks);

            Assert.That(
                results.Select(result => result.Outcome),
                Is.All.EqualTo(AnalysisProofOutcome.Disproven));
            Assert.That(services.Sum(service => service.ExecutedQueryCount), Is.EqualTo(1));
            Assert.That(services.Sum(service => service.CacheEntryCount), Is.EqualTo(serviceCount));
        }
        finally {
            foreach (var service in services) service.Dispose();
        }
    }

    [Test]
    public void SymbolicBudgetDiagnostics_LargeBudgetsClampMilliseconds() {
        var options = new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds((double)int.MaxValue + 1),
            TimeSpan.FromMilliseconds((double)int.MaxValue + 2),
            4,
            32);
        using var smtAnalysis = new SmtAnalysisService(options);

        var diagnostics = SymbolicSmtDiagnostics.FromService(smtAnalysis);
        var budgetFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicVariableTerm("budget_value", SmtValueKind.Int),
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("budget_value == 0"),
            "test.budget");
        var proof = new SymbolicProofService(smtAnalysis).ClassifyReachability(
            new SymbolicState(pathConditions: new[] { new SymbolicFactCondition(budgetFact) }));

        Assert.That(diagnostics.QueryTimeoutMs, Is.EqualTo(int.MaxValue));
        Assert.That(diagnostics.MethodBudgetMs, Is.EqualTo(int.MaxValue));
        Assert.That(proof.Budget, Is.Not.Null);
        Assert.That(proof.Budget!.TimeoutMilliseconds, Is.EqualTo(int.MaxValue));
        Assert.That(proof.Budget.MethodBudgetMilliseconds, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void ClassifyImplication_ProvesFactFromPathConditions() {
        var x = Int("x");
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            Equal(x, Integer(0))
        };
        var fact = LessThanOrEqual(x, Integer(1));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.HazardCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(
            service.ClassifyImplication(pathConditions, fact).Outcome,
            Is.EqualTo(AnalysisProofOutcome.Proven));
    }

    [Test]
    public void ClassifyImplication_ReturnsReachableWhenFactDoesNotFollow() {
        var x = Int("x");
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            GreaterThanOrEqual(x, Integer(0))
        };
        var fact = GreaterThan(x, Integer(0));

        var result = service.ClassifyImplication(pathConditions, fact);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Disproven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Satisfiable));
        Assert.That(result.HazardCheck.Feasibility, Is.EqualTo(Feasibility.Satisfiable));
        Assert.That(
            service.ClassifyImplication(pathConditions, fact).Outcome,
            Is.Not.EqualTo(AnalysisProofOutcome.Proven));
    }

    private sealed record ServiceRegexCase(
        string Pattern,
        string? Text,
        bool TextEquality,
        int? ImpliedLength,
        AnalysisProofOutcome? Outcome,
        Feasibility Feasibility,
        string? Reason = null);

    private static IEnumerable<TestCaseData> ServiceRegexCases() {
        yield return CreateServiceRegexCase("ClassifyImplication_ProvesStrictRegexLiteralLengthFact", @"\A[A-Z][0-9]\z", null, true, 2, AnalysisProofOutcome.Proven, Feasibility.Unsatisfiable);
        yield return CreateServiceRegexCase("ClassifyPathFeasibility_DollarAnchorAllowsTrailingNewline", "^AB$", "AB\n", true, null, null, Feasibility.Satisfiable);
        yield return CreateServiceRegexCase("ClassifyPathFeasibility_CombinesStrictRegexAndStringEquality", @"\AAB\z", "AB", false, null, AnalysisProofOutcome.Proven, Feasibility.Unsatisfiable);
        yield return CreateServiceRegexCase("ClassifyPathFeasibility_CombinesNonCapturingRegexGroupAndStringEquality", @"\A(?:AB|CD)\z", "EF", true, null, AnalysisProofOutcome.Proven, Feasibility.Unsatisfiable);
        yield return CreateServiceRegexCase("ClassifyPathFeasibility_CombinesNegatedRegexClassAndStringEquality", @"\A[^A]\z", "A", true, null, AnalysisProofOutcome.Proven, Feasibility.Unsatisfiable);
        yield return CreateServiceRegexCase("ClassifyPathFeasibility_CombinesRegexHexEscapesAndStringEquality", "\\A\\u0041\\x42\\z", "AB", false, null, AnalysisProofOutcome.Proven, Feasibility.Unsatisfiable);
        yield return CreateServiceRegexCase("ClassifyImplication_ProvesShorthandRegexLengthFact", @"\A\d\s\w\z", null, true, 3, AnalysisProofOutcome.Proven, Feasibility.Unsatisfiable);
        yield return CreateServiceRegexCase("ClassifyPathFeasibility_NegatedShorthandRegexClassConcreteMatchIsSelfVerified", @"\A[^\d]\z", "A", true, null, null, Feasibility.Satisfiable);
        yield return CreateServiceRegexCase("ClassifyPathFeasibility_ShorthandRegexConcreteMismatchIsRejectedByDotNetValidation", @"\A\d\z", "A", true, null, AnalysisProofOutcome.Proven, Feasibility.Unsatisfiable, "path_unsatisfiable");
        yield return CreateServiceRegexCase("ClassifyImplication_ProvesCategoryRegexLengthFact", @"\A\p{Lu}\P{Ll}\z", null, true, 2, AnalysisProofOutcome.Proven, Feasibility.Unsatisfiable);
        yield return CreateServiceRegexCase("ClassifyImplication_WordBoundaryRegexLengthFactRemainsConservative", @"\A\bAB\B?\z", null, true, 2, AnalysisProofOutcome.Unknown, Feasibility.Unknown);
        yield return CreateServiceRegexCase("ClassifyPathFeasibility_NegatedCategoryRegexClassConcreteMismatchIsRejectedByDotNetValidation", @"\A[^\p{Lu}]\z", "A", true, null, AnalysisProofOutcome.Proven, Feasibility.Unsatisfiable, "path_unsatisfiable");
        yield return CreateServiceRegexCase("ClassifyPathFeasibility_InvalidConcreteRegexRemainsUnknown", "(", "A", true, null, AnalysisProofOutcome.Unknown, Feasibility.Unknown);
    }

    [TestCaseSource(nameof(ServiceRegexCases))]
    public void ServiceRegexMatrix(object value) {
        var testCase = (ServiceRegexCase)value;
        var text = String("text");
        using var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var regex = new SmtRegexMatchFormula(text, testCase.Pattern);
        AnalysisProofResult result;
        if (testCase.ImpliedLength is { } length) {
            result = service.ClassifyImplication([regex], Equal(new SmtStringLengthTerm(text), Integer(length)));
            Assert.That(result.HazardCheck.Feasibility, Is.EqualTo(testCase.Feasibility));
        }
        else {
            var comparison = testCase.TextEquality ? Equal(text, Text(testCase.Text!)) : NotEqual(text, Text(testCase.Text!));
            result = service.ClassifyPathFeasibility([regex, comparison]);
            Assert.That(result.PathCheck.Feasibility, Is.EqualTo(testCase.Feasibility));
        }
        if (testCase.Outcome is { } outcome) Assert.That(result.Outcome, Is.EqualTo(outcome));
        if (testCase.Reason != null) Assert.That(result.Reason, Is.EqualTo(testCase.Reason));
    }

    private static TestCaseData CreateServiceRegexCase(
        string name,
        string pattern,
        string? text,
        bool textEquality,
        int? impliedLength,
        AnalysisProofOutcome? outcome,
        Feasibility feasibility,
        string? reason = null) => new TestCaseData(
        new ServiceRegexCase(pattern, text, textEquality, impliedLength, outcome, feasibility, reason)).SetName(name);
    [Test]
    public void ConcreteRegexValidationCache_IsBounded() {
        using var solver = new SmtSolver();
        var text = String("text");
        for (var index = 0; index < SmtSolver.MaxRegexValidationCacheEntries * 2; index++) {
            var pathConditions = new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, "^value[0-9]+$"),
                Equal(text, Text("value" + index))
            };

            _ = solver.CheckSatisfiability(pathConditions, TimeSpan.FromMilliseconds(50));
        }

        Assert.That(solver.RegexValidationCacheCount,
            Is.LessThanOrEqualTo(SmtSolver.MaxRegexValidationCacheEntries));
    }

    [Test]
    public void ClassifyPathFeasibility_CombinesStringContainsAndEquality() {
        var text = String("text");
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtStringContainsFormula(text, Text("Z")),
            Equal(text, Text("ABC"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void ClassifyPathFeasibility_ConcreteStringStartsWithMismatchIsRejected() {
        var text = String("text");
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtStringStartsWithFormula(text, Text("AB")),
            Equal(text, Text("ZAB"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void ClassifyPathFeasibility_ConcreteNegatedStringEndsWithMismatchIsRejected() {
        var text = String("text");
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtUnaryFormula(
                SmtUnaryOperator.Not,
                new SmtStringEndsWithFormula(text, Text("BC"))),
            Equal(text, Text("ABC"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void ClassifyPathFeasibility_CombinesStringConcatAndEquality() {
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                new SmtStringConcatTerm(Text("A"), new SmtStringConstant("B")),
                Text("AB"))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void ClassifyPathFeasibility_ReportsContradictoryPathUnsatisfiable() {
        var x = Int("x");
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            GreaterThan(x, Integer(0)),
            LessThan(x, Integer(0))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void ClassifyPathFeasibility_BooleanAliasComparisonContradiction_IsUnsatisfiable() {
        var value = Int("value");
        var isZero = Bool("isZero");
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                isZero,
                Equal(value, Integer(0))),
            isZero,
            NotEqual(value, Integer(0))
        };

        var result = service.ClassifyPathFeasibility(pathConditions);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void ClassifyImplication_TransitiveBooleanEquivalenceEntailment_BypassesSolver() {
        var left = Bool("bool_equiv_left_" + Guid.NewGuid().ToString("N"));
        var middle = Bool("bool_equiv_middle_" + Guid.NewGuid().ToString("N"));
        var right = Bool("bool_equiv_right_" + Guid.NewGuid().ToString("N"));
        var pathConditions = new SmtFormula[]
        {
            Equal(left, middle),
            Equal(middle, right)
        };
        var fact = Equal(left, right);

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyImplication_TransitiveBooleanNegationEntailment_BypassesSolver() {
        var left = Bool("bool_neg_left_" + Guid.NewGuid().ToString("N"));
        var middle = Bool("bool_neg_middle_" + Guid.NewGuid().ToString("N"));
        var right = Bool("bool_neg_right_" + Guid.NewGuid().ToString("N"));
        var pathConditions = new SmtFormula[]
        {
            NotEqual(left, middle),
            NotEqual(middle, right)
        };
        var fact = Equal(left, right);

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyImplication_NegatedBooleanRelationEntailment_BypassesSolver() {
        var left = Bool("bool_not_rel_left_" + Guid.NewGuid().ToString("N"));
        var middle = Bool("bool_not_rel_middle_" + Guid.NewGuid().ToString("N"));
        var right = Bool("bool_not_rel_right_" + Guid.NewGuid().ToString("N"));
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                left,
                new SmtUnaryFormula(SmtUnaryOperator.Not, middle)),
            Equal(middle, right)
        };
        var fact = NotEqual(left, right);

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyPathFeasibility_NegatedBooleanRelationParityContradiction_BypassesSolver() {
        var left = Bool("bool_not_parity_left_" + Guid.NewGuid().ToString("N"));
        var middle = Bool("bool_not_parity_middle_" + Guid.NewGuid().ToString("N"));
        var right = Bool("bool_not_parity_right_" + Guid.NewGuid().ToString("N"));
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                left,
                new SmtUnaryFormula(SmtUnaryOperator.Not, middle)),
            Equal(middle, right),
            Equal(left, right)
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyPathFeasibility_BooleanEquivalenceParityContradiction_BypassesSolver() {
        var left = Bool("bool_parity_left_" + Guid.NewGuid().ToString("N"));
        var middle = Bool("bool_parity_middle_" + Guid.NewGuid().ToString("N"));
        var right = Bool("bool_parity_right_" + Guid.NewGuid().ToString("N"));
        var pathConditions = new SmtFormula[]
        {
            Equal(left, middle),
            NotEqual(middle, right),
            Equal(left, right)
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyPathFeasibility_IntegerAliasIntervalContradiction_BypassesSolver() {
        var x = Int("alias_int_x_" + Guid.NewGuid().ToString("N"));
        var y = Int("alias_int_y_" + Guid.NewGuid().ToString("N"));
        var pathConditions = new SmtFormula[]
        {
            GreaterThanOrEqual(x, Integer(10)),
            Equal(x, y),
            LessThan(y, Integer(10))
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyImplication_IntegerAliasIntervalEntailment_BypassesSolver() {
        var x = Int("alias_entail_x_" + Guid.NewGuid().ToString("N"));
        var y = Int("alias_entail_y_" + Guid.NewGuid().ToString("N"));
        var pathConditions = new SmtFormula[]
        {
            Equal(x, y),
            GreaterThanOrEqual(x, Integer(3))
        };
        var fact = GreaterThanOrEqual(y, Integer(3));

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyPathFeasibility_AffineOffsetAliasIntervalContradiction_BypassesSolver() {
        var x = Int("affine_offset_alias_x_" + Guid.NewGuid().ToString("N"));
        var y = Int("affine_offset_alias_y_" + Guid.NewGuid().ToString("N"));
        var xPlusTwo = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, Integer(2));
        var yPlusFour = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, y, Integer(4));
        var pathConditions = new SmtFormula[]
        {
            GreaterThanOrEqual(x, Integer(5)),
            Equal(xPlusTwo, yPlusFour),
            LessThan(y, Integer(3))
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyImplication_AffineOffsetAliasIntervalEntailment_BypassesSolver() {
        var x = Int("affine_offset_entail_x_" + Guid.NewGuid().ToString("N"));
        var y = Int("affine_offset_entail_y_" + Guid.NewGuid().ToString("N"));
        var xPlusTwo = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, Integer(2));
        var yPlusFour = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, y, Integer(4));
        var pathConditions = new SmtFormula[]
        {
            Equal(xPlusTwo, yPlusFour),
            GreaterThanOrEqual(x, Integer(5))
        };
        var fact = GreaterThanOrEqual(y, Integer(3));

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyPathFeasibility_SameBaseAffineEqualityContradiction_BypassesSolver() {
        var x = Int("same_base_affine_x_" + Guid.NewGuid().ToString("N"));
        var xPlusTwo = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, Integer(2));
        var xPlusThree = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, Integer(3));
        var pathConditions = new SmtFormula[]
        {
            Equal(xPlusTwo, xPlusThree)
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyPathFeasibility_SameBaseAffineOrderingContradiction_BypassesSolver() {
        var x = Int("same_base_affine_order_x_" + Guid.NewGuid().ToString("N"));
        var xPlusTwo = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, Integer(2));
        var xPlusThree = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, Integer(3));
        var pathConditions = new SmtFormula[]
        {
            LessThan(xPlusThree, xPlusTwo)
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyImplication_SameBaseAffineOrderingTautology_BypassesSolver() {
        var x = Int("same_base_affine_tautology_x_" + Guid.NewGuid().ToString("N"));
        var xPlusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, Integer(1));
        var xPlusTwo = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, Integer(2));
        var fact = LessThanOrEqual(xPlusOne, xPlusTwo);

        AssertPreprocessed(Array.Empty<SmtFormula>(), fact);
    }

    [Test]
    public void ClassifyImplication_AffineComparisonAgainstExactTerm_BypassesSolver() {
        var x = Int("affine_exact_compare_x_" + Guid.NewGuid().ToString("N"));
        var y = Int("affine_exact_compare_y_" + Guid.NewGuid().ToString("N"));
        var xPlusTwo = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, Integer(2));
        var pathConditions = new SmtFormula[]
        {
            Equal(y, Integer(10)),
            LessThanOrEqual(x, Integer(8))
        };
        var fact = LessThanOrEqual(xPlusTwo, y);

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyPathFeasibility_StringAliasContradiction_BypassesSolver() {
        var left = String("alias_text_left_" + Guid.NewGuid().ToString("N"));
        var right = String("alias_text_right_" + Guid.NewGuid().ToString("N"));
        var pathConditions = new SmtFormula[]
        {
            Equal(left, right),
            Equal(left, Text("ABC")),
            NotEqual(right, Text("ABC"))
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyPathFeasibility_ReferenceAliasNullContradiction_BypassesSolver() {
        var left = Reference("alias_ref_left_" + Guid.NewGuid().ToString("N"));
        var right = Reference("alias_ref_right_" + Guid.NewGuid().ToString("N"));
        var pathConditions = new SmtFormula[]
        {
            Equal(left, right),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, left, new SmtNullConstant()),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, right, new SmtNullConstant())
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyPathFeasibility_DisjunctionReferenceNullContradiction_BypassesSolver() {
        var value = Reference("disjunction_ref_" + Guid.NewGuid().ToString("N"));
        var guard = Bool("disjunction_guard_" + Guid.NewGuid().ToString("N"));
        var valueIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtNullConstant());
        var pathConditions = new SmtFormula[]
        {
            Or(valueIsNull, guard),
            new SmtUnaryFormula(SmtUnaryOperator.Not, guard),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, value, new SmtNullConstant())
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyPathFeasibility_NegatedDisjunctionReferenceNullContradiction_BypassesSolver() {
        var value = Reference("negated_disjunction_ref_" + Guid.NewGuid().ToString("N"));
        var guard = Bool("negated_disjunction_guard_" + Guid.NewGuid().ToString("N"));
        var valueIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtNullConstant());
        var pathConditions = new SmtFormula[]
        {
            new SmtUnaryFormula(
                SmtUnaryOperator.Not,
                Or(valueIsNull, guard)),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtNullConstant())
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyImplication_ReferenceAliasNonNullEntailment_BypassesSolver() {
        var left = Reference("alias_ref_entail_left_" + Guid.NewGuid().ToString("N"));
        var right = Reference("alias_ref_entail_right_" + Guid.NewGuid().ToString("N"));
        var pathConditions = new SmtFormula[]
        {
            Equal(left, right),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, new SmtNullConstant())
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, right, new SmtNullConstant());

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyPathFeasibility_NullConstantInequality_BypassesSolver() {
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, new SmtNullConstant(), new SmtNullConstant())
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyPathFeasibility_AliasInsideIntegerExpressionIntervalContradiction_BypassesSolver() {
        var x = Int("expr_alias_x_" + Guid.NewGuid().ToString("N"));
        var y = Int("expr_alias_y_" + Guid.NewGuid().ToString("N"));
        var xPlusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, Integer(1));
        var yPlusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, y, Integer(1));
        var pathConditions = new SmtFormula[]
        {
            Equal(x, y),
            GreaterThanOrEqual(xPlusOne, Integer(5)),
            LessThan(yPlusOne, Integer(5))
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyPathFeasibility_SubtractionIntervalContradiction_BypassesSolver() {
        var x = Int("subtract_interval_x_" + Guid.NewGuid().ToString("N"));
        var xMinusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, x, Integer(1));
        var pathConditions = new SmtFormula[]
        {
            GreaterThanOrEqual(x, Integer(5)),
            LessThan(xMinusOne, Integer(4))
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyImplication_PositiveConstantMultiplyIntervalEntailment_BypassesSolver() {
        var x = Int("multiply_interval_x_" + Guid.NewGuid().ToString("N"));
        var twiceX = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, x, Integer(2));
        var pathConditions = new SmtFormula[]
        {
            GreaterThanOrEqual(x, Integer(3))
        };
        var fact = GreaterThanOrEqual(twiceX, Integer(6));

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyImplication_AliasInsideStringLengthEntailment_BypassesSolver() {
        var left = String("length_alias_left_" + Guid.NewGuid().ToString("N"));
        var right = String("length_alias_right_" + Guid.NewGuid().ToString("N"));
        var leftLength = new SmtStringLengthTerm(left);
        var rightLength = new SmtStringLengthTerm(right);
        var pathConditions = new SmtFormula[]
        {
            Equal(left, right),
            Equal(leftLength, Integer(3))
        };
        var fact = Equal(rightLength, Integer(3));

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyPathFeasibility_AliasInsideStringConcatContradiction_BypassesSolver() {
        var left = String("concat_alias_left_" + Guid.NewGuid().ToString("N"));
        var right = String("concat_alias_right_" + Guid.NewGuid().ToString("N"));
        var leftConcat = new SmtStringConcatTerm(left, Text("!"));
        var rightConcat = new SmtStringConcatTerm(right, Text("!"));
        var pathConditions = new SmtFormula[]
        {
            Equal(left, right),
            Equal(leftConcat, Text("A!")),
            NotEqual(rightConcat, Text("A!"))
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyImplication_StringConcatKnownOperandLengths_BypassesSolver() {
        var left = String("concat_length_left_" + Guid.NewGuid().ToString("N"));
        var right = String("concat_length_right_" + Guid.NewGuid().ToString("N"));
        var concatLength = new SmtStringLengthTerm(new SmtStringConcatTerm(left, right));
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(left),
                Integer(2)),
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(right),
                Integer(3))
        };
        var fact = Equal(concatLength, Integer(5));

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyPathFeasibility_NegativeStringLength_BypassesSolver() {
        var text = String("negative_length_" + Guid.NewGuid().ToString("N"));
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                new SmtStringLengthTerm(text),
                Integer(0))
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyPathFeasibility_ConditionalIntegerComparisonWithKnownGuard_BypassesSolver() {
        var guard = Bool("conditional_int_guard_" + Guid.NewGuid().ToString("N"));
        var selected = Int("conditional_int_selected_" + Guid.NewGuid().ToString("N"));
        var fallback = Int("conditional_int_fallback_" + Guid.NewGuid().ToString("N"));
        var conditional = new SmtConditionalFormula(guard, selected, fallback, SmtValueKind.Int);
        var pathConditions = new SmtFormula[]
        {
            guard,
            GreaterThanOrEqual(conditional, Integer(10)),
            LessThan(selected, Integer(10))
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyImplication_ConditionalReferenceNullFactWithKnownGuard_BypassesSolver() {
        var guard = Bool("conditional_ref_guard_" + Guid.NewGuid().ToString("N"));
        var left = Reference("conditional_ref_left_" + Guid.NewGuid().ToString("N"));
        var right = Reference("conditional_ref_right_" + Guid.NewGuid().ToString("N"));
        var conditional = new SmtConditionalFormula(guard, left, right, SmtValueKind.Reference);
        var pathConditions = new SmtFormula[]
        {
            new SmtUnaryFormula(SmtUnaryOperator.Not, guard),
            new SmtBinaryFormula(SmtBinaryOperator.NotEqual, right, new SmtNullConstant())
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, conditional, new SmtNullConstant());

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyImplication_ConditionalReferenceAliasNullStatePropagatesToSelectedBranch_BypassesSolver() {
        var guard = Bool("conditional_ref_alias_guard_" + Guid.NewGuid().ToString("N"));
        var selected = Reference("conditional_ref_alias_selected_" + Guid.NewGuid().ToString("N"));
        var whenTrue = Reference("conditional_ref_alias_true_" + Guid.NewGuid().ToString("N"));
        var whenFalse = Reference("conditional_ref_alias_false_" + Guid.NewGuid().ToString("N"));
        var conditional = new SmtConditionalFormula(guard, whenTrue, whenFalse, SmtValueKind.Reference);
        var pathConditions = new SmtFormula[]
        {
            guard,
            Equal(selected, conditional),
            new SmtBinaryFormula(SmtBinaryOperator.Equal, selected, new SmtNullConstant())
        };
        var fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, whenTrue, new SmtNullConstant());

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyPathFeasibility_ConditionalBooleanWithKnownGuard_BypassesSolver() {
        var guard = Bool("conditional_bool_guard_" + Guid.NewGuid().ToString("N"));
        var selected = Bool("conditional_bool_selected_" + Guid.NewGuid().ToString("N"));
        var fallback = Bool("conditional_bool_fallback_" + Guid.NewGuid().ToString("N"));
        var conditional = new SmtConditionalFormula(guard, selected, fallback, SmtValueKind.Bool);
        var pathConditions = new SmtFormula[]
        {
            guard,
            conditional,
            new SmtUnaryFormula(SmtUnaryOperator.Not, selected)
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyPathFeasibility_ConditionalWithEqualBranchesCollapsesBeforeSolver() {
        var guard = Bool("conditional_equal_branch_guard_" + Guid.NewGuid().ToString("N"));
        var value = Int("conditional_equal_branch_value_" + Guid.NewGuid().ToString("N"));
        var conditional = new SmtConditionalFormula(guard, value, value, SmtValueKind.Int);
        var pathConditions = new SmtFormula[]
        {
            Equal(conditional, Integer(7)),
            NotEqual(value, Integer(7))
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyImplication_ConditionalIntegerBranchImplications_BypassesSolver() {
        var guard = Bool("conditional_branch_int_guard_" + Guid.NewGuid().ToString("N"));
        var whenTrue = Int("conditional_branch_int_true_" + Guid.NewGuid().ToString("N"));
        var whenFalse = Int("conditional_branch_int_false_" + Guid.NewGuid().ToString("N"));
        var conditional = new SmtConditionalFormula(guard, whenTrue, whenFalse, SmtValueKind.Int);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtUnaryFormula(SmtUnaryOperator.Not, guard),
                GreaterThanOrEqual(whenTrue, Integer(0))),
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                guard,
                GreaterThanOrEqual(whenFalse, Integer(0)))
        };
        var fact = GreaterThanOrEqual(conditional, Integer(0));

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyPathFeasibility_ConditionalReferenceBranchImplications_BypassesSolver() {
        var guard = Bool("conditional_branch_ref_guard_" + Guid.NewGuid().ToString("N"));
        var whenTrue = Reference("conditional_branch_ref_true_" + Guid.NewGuid().ToString("N"));
        var whenFalse = Reference("conditional_branch_ref_false_" + Guid.NewGuid().ToString("N"));
        var conditional = new SmtConditionalFormula(guard, whenTrue, whenFalse, SmtValueKind.Reference);
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

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyPathFeasibility_ConditionalBooleanBranchImplications_BypassesSolver() {
        var guard = Bool("conditional_branch_bool_guard_" + Guid.NewGuid().ToString("N"));
        var whenTrue = Bool("conditional_branch_bool_true_" + Guid.NewGuid().ToString("N"));
        var whenFalse = Bool("conditional_branch_bool_false_" + Guid.NewGuid().ToString("N"));
        var conditional = new SmtConditionalFormula(guard, whenTrue, whenFalse, SmtValueKind.Bool);
        var pathConditions = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtUnaryFormula(SmtUnaryOperator.Not, guard),
                whenTrue),
            Or(guard, whenFalse),
            new SmtUnaryFormula(SmtUnaryOperator.Not, conditional)
        };

        AssertPreprocessed(pathConditions);
    }

    [Test]
    public void ClassifyImplication_ConditionalSelectedReferenceNullBranchImplication_BypassesSolver() {
        var guard = Bool("conditional_selected_ref_guard_" + Guid.NewGuid().ToString("N"));
        var first = Reference("conditional_selected_ref_first_" + Guid.NewGuid().ToString("N"));
        var second = Reference("conditional_selected_ref_second_" + Guid.NewGuid().ToString("N"));
        var resultReference = Reference("conditional_selected_ref_result_" + Guid.NewGuid().ToString("N"));
        var resultIsNonNull = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, resultReference, new SmtNullConstant());
        var firstIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, first, new SmtNullConstant());
        var secondIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, second, new SmtNullConstant());
        var selectedReference = new SmtConditionalFormula(guard, first, second, SmtValueKind.Reference);
        var selectedIsNull = new SmtConditionalFormula(guard, firstIsNull, secondIsNull, SmtValueKind.Bool);
        var pathConditions = new SmtFormula[]
        {
            Equal(resultReference, selectedReference),
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
                Or(resultIsNonNull, firstIsNull)),
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                guard,
                Or(resultIsNonNull, secondIsNull)));

        AssertPreprocessed(pathConditions, fact);
    }

    [Test]
    public void ClassifyImplication_RuntimeTypeTestPredicateIsCongruentUnderReferenceEquality() {
        var x = Reference("runtime_x_" + Guid.NewGuid().ToString("N"));
        var y = Reference("runtime_y_" + Guid.NewGuid().ToString("N"));
        var xEqualsY = Equal(x, y);
        var xIsString = new SmtRuntimeTypeTestFormula(x, "System.String");
        var yIsString = new SmtRuntimeTypeTestFormula(y, "System.String");
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyImplication(new SmtFormula[] { xEqualsY, xIsString }, yIsString);

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
    }

    [Test]
    public void ClassifyPathFeasibility_RuntimeTypeTestPredicateContradictsItsNegationThroughReferenceEquality() {
        var x = Reference("runtime_x_" + Guid.NewGuid().ToString("N"));
        var y = Reference("runtime_y_" + Guid.NewGuid().ToString("N"));
        var xEqualsY = Equal(x, y);
        var xIsString = new SmtRuntimeTypeTestFormula(x, "System.String");
        var yIsNotString = new SmtUnaryFormula(
            SmtUnaryOperator.Not,
            new SmtRuntimeTypeTestFormula(y, "System.String"));
        var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = service.ClassifyPathFeasibility(new SmtFormula[] { xEqualsY, xIsString, yIsNotString });

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    private static void AssertPreprocessed(IReadOnlyList<SmtFormula> pathConditions, SmtFormula? conclusion = null) {
        using var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = conclusion == null
            ? service.ClassifyPathFeasibility(pathConditions)
            : service.ClassifyImplication(pathConditions, conclusion);
        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(conclusion == null ? result.PathCheck.Feasibility : result.HazardCheck.Feasibility,
            Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.Reason, Is.EqualTo(conclusion == null ? "path_unsatisfiable" : "branch_unreachable"));
        Assert.That(service.ExecutedQueryCount, Is.EqualTo(0));
        Assert.That(service.CacheEntryCount, Is.EqualTo(0));
    }
    private static void AssertPermanentFailureCode(Exception exception, string expectedCode) {
        var factoryCalls = 0;
        using var service = new SmtAnalysisService(
            SmtAnalysisOptions.Default,
            () => {
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

    private static AnalysisProofQuery CreateQuery(
        IReadOnlyList<SmtFormula> pathConditions,
        SmtFormula triggerCondition) {
        return new AnalysisProofQuery(
            pathConditions,
            new AnalysisHazard(
                AnalysisHazardKind.EffectViolationReachability,
                triggerCondition));
    }

    private static AnalysisProofQuery CreateSolverQuery(string name) {
        var value = Int(name);
        var valueIsZero = Equal(value, Integer(0));
        return CreateQuery(new[] { valueIsZero }, valueIsZero);
    }

    private static AnalysisProofResult CreateTransientFailure() {
        return new AnalysisProofResult(
            AnalysisProofOutcome.Unknown,
            new ProofCheckInfo(
                true,
                Feasibility.Unknown,
                new SmtSatisfyingWitness(
                    SmtWitnessStatus.Unsupported,
                    "z3_transient_failure",
                    Array.Empty<SmtModelAssignment>())),
            new ProofCheckInfo(false, Feasibility.Unknown),
            "path_feasibility_unknown");
    }

    private static AnalysisProofResult CreateImpureResult() {
        return new AnalysisProofResult(
            AnalysisProofOutcome.Disproven,
            new ProofCheckInfo(true, Feasibility.Satisfiable),
            new ProofCheckInfo(true, Feasibility.Satisfiable),
            "impure_call_reachable");
    }

    private sealed class StubProofSearchSession : IAnalysisProofSearchSession {
        private readonly Func<AnalysisProofQuery, TimeSpan, AnalysisProofResult> _classify;
        private readonly Action? _dispose;

        public StubProofSearchSession(
            Func<AnalysisProofQuery, TimeSpan, AnalysisProofResult> classify,
            Action? dispose = null) {
            _classify = classify;
            _dispose = dispose;
        }

        public long ConsumedResourceCount => 0;

        public AnalysisProofResult Classify(AnalysisProofQuery query, TimeSpan timeout) {
            return _classify(query, timeout);
        }

        public void Dispose() {
            _dispose?.Invoke();
        }
    }

    private sealed class ProjectConfigOptionsProvider : AnalyzerConfigOptionsProvider {
        private readonly AnalyzerConfigOptions _empty = new ProjectConfigOptions(
            ImmutableDictionary<string, string>.Empty);

        internal ProjectConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions) {
            GlobalOptions = new ProjectConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _empty;
    }

    private sealed class ProjectConfigOptions : AnalyzerConfigOptions {
        private readonly ImmutableDictionary<string, string> _values;

        internal ProjectConfigOptions(ImmutableDictionary<string, string> values) {
            _values = values;
        }

        public override bool TryGetValue(string key, out string value) {
            if (_values.TryGetValue(key, out var found)) {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }

    private static SmtFormula CreateNestedNegation(int depth) {
        SmtFormula formula = Boolean(true);
        for (var index = 0; index < depth; index++) formula = new SmtUnaryFormula(SmtUnaryOperator.Not, formula);

        return formula;
    }
}
