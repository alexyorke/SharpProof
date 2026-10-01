using NUnit.Framework;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeWorkerRoutingTests
{
    [TestCase("value", WorkerClaimOutcome.Proven)]
    [TestCase("0", WorkerClaimOutcome.Refuted)]
    public async Task NativeWorkerUsesCompanionClausesWithTheOriginalTargetBody(string returned, WorkerClaimOutcome outcome)
    {
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int value) { return {{returned}}; } }
            [ContractFor(typeof(Subject))] public static class SubjectContracts {
                public static int Target(int contractValue) {
                    Contract.Requires(contractValue > 0);
                    Contract.Ensures(Contract.Result<int>() == Contract.Old(contractValue));
                    return contractValue;
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(outcome));
        Assert.That(response.Manifest.Claims.Single().Evidence, Is.EqualTo(WorkerClaimEvidence.CompanionClause));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [TestCase("int", "Positive", "1", WorkerClaimOutcome.Proven)]
    [TestCase("int", "Positive", "0", WorkerClaimOutcome.Refuted)]
    [TestCase("ulong", "Positive", "18446744073709551615UL", WorkerClaimOutcome.Proven)]
    [TestCase("ulong", "Positive", "0UL", WorkerClaimOutcome.Refuted)]
    [TestCase("sbyte", "InRange(-129, 128)", "-128", WorkerClaimOutcome.Proven)]
    [TestCase("long", "InRange(-9223372036854775808L, 9223372036854775807L)", "-9223372036854775808L", WorkerClaimOutcome.Proven)]
    [TestCase("byte", "InRange(-1, 256)", "255", WorkerClaimOutcome.Proven)]
    [TestCase("byte", "InRange(256, 300)", "255", WorkerClaimOutcome.Refuted)]
    [TestCase("ulong", "InRange(-2, -1)", "0UL", WorkerClaimOutcome.Refuted)]
    [TestCase("string", "NotNull", "value", WorkerClaimOutcome.Proven)]
    [TestCase("string", "NotNull", "null!", WorkerClaimOutcome.Refuted)]
    public async Task NativeWorkerVerifiesTypedReturnAttributes(string type, string attribute, string value, WorkerClaimOutcome outcome)
    {
        var parameters = type == "string" ? "[NotNull] string value" : "";
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject { [return: {{attribute}}] public static {{type}} Target({{parameters}}) { return {{value}}; } }
            """;
        using var project = new ShadowTestProject(source);
        Assert.That(project.Snapshot.Callables.Single().Total, Is.Not.Null);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(outcome), response.ClaimResults.Single().Reason.ToString());
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [TestCase("int")]
    [TestCase("ulong")]
    public async Task NativeWorkerCombinesParameterAttributesDirectClausesAndReturnAttributes(string type)
    {
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject {
                [return: Positive] public static {{type}} Target([InRange(1, 10)] {{type}} value) {
                    Contract.Ensures(Contract.Result<{{type}}>() == value);
                    return value;
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
        Assert.That(response.ClaimResults.Select(result => result.Outcome), Is.All.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task NativeWorkerRetainsDirectVerifierOutcomesAcrossTheQualifiedUniverse()
    {
        var cases = WorkerVcShadowSourceGateTests.QualificationCases();
        var posts = 0;
        var known = 0;
        foreach (var sourceCase in cases)
        {
            using var project = new ShadowTestProject(WorkerVcShadowSourceGateTests.CreateGateArtifact(sourceCase));
            var expected = new Dictionary<string, WorkerClaimResult>(StringComparer.Ordinal);
            foreach (var preparation in project.Snapshot.Callables)
            {
                await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets, check =>
                    expected[check.ClaimId] = CallableClaimResultAssembler.FromTotal(preparation, check), CancellationToken.None);
            }
            using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True, sourceCase.Name);
            var postIds = response.Manifest.Claims.Where(claim => claim.Kind == WorkerClaimKind.Postcondition)
                .Select(claim => claim.ClaimId).ToHashSet(StringComparer.Ordinal);
            posts += postIds.Count;
            var actual = response.ClaimResults.Where(result => postIds.Contains(result.ClaimId)).ToDictionary(result => result.ClaimId, StringComparer.Ordinal);
            Assert.That(actual.Keys, Is.EquivalentTo(postIds), sourceCase.Name);
            foreach (var result in expected.Values.Where(result => result.Outcome is WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted))
            {
                known++;
                Assert.That((actual[result.ClaimId].Outcome, actual[result.ClaimId].Vacuity),
                    Is.EqualTo((result.Outcome, result.Vacuity)), sourceCase.Name + ":" + result.ClaimId);
            }
        }
        Assert.That(posts, Is.EqualTo(253));
        Assert.That(known, Is.EqualTo(236));
        await TestContext.Out.WriteLineAsync($"native-worker universe: fixtures={cases.Length} posts={posts} retained-known={known}");
    }

    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(1, true)]
    public async Task NativeWorkerVerifiesLegacyFailedCallablesAndReusesValidatedCache(int parallelism, bool cacheEnabled)
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static ulong A(ulong value) {
                    Contract.Requires(value == 18446744073709551615UL);
                    Contract.Ensures(Contract.Result<ulong>() == 0UL);
                    return unchecked(value + 1UL);
                }
                public static int B(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """, cacheEnabled);
        project.Request.Budgets.MaxParallelism = parallelism;
        Assert.That(project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains(".A(", StringComparison.Ordinal)).IsSuccess, Is.False);
        Assert.That(SharpProofWorker.CountSolverTargets(project.Snapshot.Callables, nativeAuthority: true), Is.EqualTo(2));
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        for (var run = 0; run < 2; run++)
        {
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
            Assert.That(response.ClaimResults.Select(result => result.Outcome), Is.All.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(response.CallableResults.Select(result => result.Coverage), Is.All.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(cacheEnabled
                ? run == 0 ? WorkerCacheStatus.Written : WorkerCacheStatus.Hit : WorkerCacheStatus.Disabled));
        }
    }

    [Test]
    public async Task EffectOnlyEntryWithoutPreconditionsDoesNotConsumeSolverQueries()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { [DoesNotThrow] public static void Target() { } }
            """);
        using var worker = new SharpProofWorker(new UnexpectedBackend(), null, nativeAuthority: true);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task ContradictoryNativeEntryMakesEffectProofVacuityExplicit()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { [DoesNotThrow] public static void Target() {
                Contract.Requires(false);
                throw null!;
            } }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        var result = response.ClaimResults.Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.ContradictoryPreconditions));
        Assert.That(result.ProofCore, Is.Not.Empty);
        Assert.That(result.Assumptions.Single().Used, Is.True);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task MethodTimeoutRetainsEarlierNativeProofAndMarksOnlyPendingClaim()
    {
        using var project = new ShadowTestProject(CompilerTotalCallableArtifactTests.DiamondSource);
        project.Request.Budgets.MethodWallTimeMilliseconds = 1500;
        var factory = project.Snapshot.Callables.Single().Total!.Program.Factory;
        using var worker = new SharpProofWorker(() => new StallAfterQueriesBackend(factory, project.Request.Budgets.QueryRlimit), nativeAuthority: true);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Count(result => result.Outcome == WorkerClaimOutcome.Proven), Is.EqualTo(1));
        var pending = response.ClaimResults.Single(result => result.Outcome == WorkerClaimOutcome.Unknown);
        Assert.That(pending.Reason, Is.EqualTo(WorkerClaimReason.MethodTimeout));
        Assert.That(response.CallableResults.Single().Reason, Is.EqualTo(WorkerCallableCoverageReason.MethodTimeout));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    private sealed class UnexpectedBackend : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        { throw new AssertionException("An unconstrained effect-only entry needs no SMT query."); }
    }

    [TestCase(false)]
    [TestCase(true)]
    [TestCase(false, true)]
    public async Task ProjectInterruptionRetainsEarlierNativeProof(bool projectTimeout, bool separateCallable = false)
    {
        using var project = new ShadowTestProject(separateCallable ? """
            using SharpProof.Attributes;
            public static class Subject {
                public static int A(int value) { Contract.Ensures(Contract.Result<int>() == value); return value; }
                public static int B(int value) { Contract.Ensures(Contract.Result<int>() == value); return value; }
            }
            """ : CompilerTotalCallableArtifactTests.DiamondSource);
        project.Request.Budgets.MaxParallelism = 1;
        using var cancellation = new CancellationTokenSource();
        if (projectTimeout)
        {
            project.Request.Budgets.ProjectWallTimeMilliseconds = 1500;
            project.Request.Budgets.MethodWallTimeMilliseconds = 1500;
        }
        var factory = project.Snapshot.Callables.OrderBy(callable => callable.Entry.CallableId, StringComparer.Ordinal).First().Total!.Program.Factory;
        using var worker = new SharpProofWorker(() => new StallAfterQueriesBackend(factory,
            project.Request.Budgets.QueryRlimit, projectTimeout ? null : cancellation.Cancel), nativeAuthority: true);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, cancellation.Token);
        Assert.That(response.ClaimResults.Count(result => result.Outcome == WorkerClaimOutcome.Proven), Is.EqualTo(1));
        var pending = response.ClaimResults.Single(result => result.Outcome == WorkerClaimOutcome.Unknown);
        Assert.That(pending.Reason, Is.EqualTo(projectTimeout ? WorkerClaimReason.ProjectTimeout : WorkerClaimReason.Canceled));
        Assert.That(response.RunStatus, Is.EqualTo(projectTimeout ? WorkerRunStatus.TimedOut : WorkerRunStatus.Canceled));
        if (separateCallable)
        {
            Assert.That(response.CallableResults.First().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(response.CallableResults.Last().Reason, Is.EqualTo(WorkerCallableCoverageReason.Canceled));
        }
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    private sealed class StallAfterQueriesBackend : ISmtBackend, IDisposable
    {
        private readonly CallableSolverSession _session;
        private readonly Action? _beforeStall;
        private int _queries;
        internal StallAfterQueriesBackend(IrFactory factory, uint queryRlimit, Action? beforeStall = null)
        {
            ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
            _session = new(factory, new IrSmtBackendOptions(queryRlimit));
            _beforeStall = beforeStall;
        }
        public async Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            if (++_queries == 4)
            {
                _beforeStall?.Invoke();
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            return await _session.CheckAsync(query, cancellationToken).ConfigureAwait(false);
        }
        public void Dispose()
        {
            _session.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
