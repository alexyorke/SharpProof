using System.Text;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Smt;
using SharpProof.Host;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcShadowTests
{
    internal const string IdentitySource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x, int unused) {
            Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
            return x;
        } }
        """;

    [Test]
    public async Task ActualMeterRefusalAfterNormalWitnessLeavesEnsuresUnchecked()
    {
        var report = await MeterRefusal(IdentitySource);
        var row = report.Rows.Single();
        Assert.That(row.Feasibility, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That(row.NewReason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
        Assert.That(row.Enrolled, Is.True);
        Assert.That(row.Checked, Is.False);
        Assert.That(report.CoverageComplete, Is.False);
    }

    internal static async Task<WorkerVcShadowReport> MeterRefusal(string source)
    {
        using var project = new ShadowTestProject(source);
        var candidate = PassiveCallableArtifactAdapter.Enroll(project.Snapshot.Callables.Single())!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out _), Is.True);
        long entryCost;
        long normalCost;
        using (var probe = new PassiveCallableSolver(plan!))
        {
            Assert.That((await probe.VerifyEntryAsync()).Outcome, Is.TypeOf<RefutedOutcome>());
            entryCost = probe.ConsumedResourceCount;
            Assert.That((await probe.VerifyNormalCompletionAsync()).Outcome, Is.TypeOf<RefutedOutcome>());
            normalCost = probe.ConsumedResourceCount - entryCost;
        }
        Assert.That(normalCost, Is.GreaterThan(0));
        // Reserve room for entry and normal queries, but less than a whole
        // next query after the actual normal-witness consumption.
        var budgets = project.Request.Budgets;
        budgets.MethodRlimit = checked((uint)(budgets.QueryRlimit + entryCost + normalCost / 2));
        using (var bounded = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit))
        {
            Assert.That((await bounded.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
            var skipped = await bounded.VerifyEnsuresAsync(0);
            Assert.That(skipped.Outcome, Is.Null);
            Assert.That(skipped.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
        }
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker(budgets);
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        var bound = project.Bind();
        Assert.That(WorkerProtocolJson.Validate(response, bound.InputHash, response.Manifest).IsValid, Is.True);
        Assert.That(report, Is.Not.Null);
        Assert.That(report!.InputHash, Is.EqualTo(response.InputHash));
        Assert.That(report.RequestHash, Is.EqualTo(response.RequestHash));
        return report;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ShadowBoundaryCancellationDoesNotRetireCleanInjectedBackend(bool projectTimeout)
    {
        await CompletedBoundary(IdentitySource, projectTimeout);
    }

    internal static async Task<(WorkerVerifyResponse Completed, WorkerVerifyResponse Next, WorkerVcShadowReport Report, int Checks)> CompletedBoundary(string source, bool projectTimeout)
    {
        using var project = new ShadowTestProject(source);
        var budgets = project.Request.Budgets;
        budgets.ProjectWallTimeMilliseconds = 4_000;
        budgets.MethodWallTimeMilliseconds = 2_000;
        using var cancellation = new CancellationTokenSource();
        var backend = new UnknownBackend();
        using var worker = new SharpProofWorker(backend);
        WorkerVcShadowReport? observed = null;
        worker.ShadowReportSink = report =>
        {
            observed = report;
            if (projectTimeout)
            { Task.Delay(budgets.ProjectWallTimeMilliseconds).GetAwaiter().GetResult(); }
            else
            { cancellation.Cancel(); }
        };
        WorkerVerifyResponse completed;
        using (var environment = new ShadowEnvironment("shadow"))
        {
            completed = await worker.VerifyAsync(project.Request, project.Snapshot, cancellation.Token);
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.Checked, Is.EqualTo(1));
            Assert.That(completed.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(completed.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(WorkerProtocolJson.Validate(completed, project.Bind().InputHash, completed.Manifest).IsValid, Is.True);
        }
        worker.ShadowReportSink = null;
        using (var environment = new ShadowEnvironment(null))
        {
            var next = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(next.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(next.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(backend.Checks, Is.EqualTo(2));
            return (completed, next, observed!, backend.Checks);
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task ValidatedResponseSurvivesObservationSinkFailureOrCallerRace(bool cacheHit, bool ioFailure)
    {
        using var project = new ShadowTestProject(IdentitySource, cacheEnabled: true);
        using var worker = project.CreateLegacyWorker();
        if (cacheHit)
        {
            using var disabled = new ShadowEnvironment(null);
            Assert.That((await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None)).Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
        }
        using var cancellation = new CancellationTokenSource();
        WorkerVcShadowReport? observed = null;
        worker.ShadowReportSink = report =>
        {
            observed = report;
            cancellation.Cancel();
            if (ioFailure)
            { throw new IOException("diagnostic sink failed"); }
        };
        using var shadow = new ShadowEnvironment("shadow");
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, cancellation.Token);
        Assert.That(observed, Is.Not.Null);
        Assert.That(cancellation.IsCancellationRequested, Is.True);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(response.Summary.CacheStatus, Is.EqualTo(cacheHit ? WorkerCacheStatus.Hit : WorkerCacheStatus.Written));
        Assert.That(observed!.CacheStatus, Is.EqualTo(response.Summary.CacheStatus));
        Assert.That(observed.InputHash, Is.EqualTo(response.InputHash));
        Assert.That(observed.RequestHash, Is.EqualTo(response.RequestHash));
        Assert.That(observed.CoverageComplete, Is.True);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task CacheHitObservesPreparedIrAndLeavesAuthoritativeCacheBytesUnchanged()
    {
        using var project = new ShadowTestProject(CompilerTotalCallableArtifactTests.DiamondSource, cacheEnabled: true);
        using var worker = project.CreateLegacyWorker();
        WorkerVerifyResponse first;
        using (var disabled = new ShadowEnvironment(null))
        { first = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None); }
        Assert.That(first.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Written));
        var files = Directory.GetFiles(project.Request.Cache.Directory!, "*", SearchOption.AllDirectories);
        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in files)
        { bytes.Add(path, await File.ReadAllBytesAsync(path)); }
        WorkerVcShadowReport? observed = null;
        worker.ShadowReportSink = value => observed = value;
        using var shadow = new ShadowEnvironment("shadow");
        var cached = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(cached.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(observed, Is.Not.Null);
        Assert.That(observed!.Checked, Is.EqualTo(2));
        Assert.That(observed.Rows.Select(row => row.NewOutcome),
            Is.EqualTo((WorkerClaimOutcome[])[WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]));
        Assert.That(cached.ClaimResults.Select(claim => (claim.ClaimId, claim.Outcome, claim.Reason, claim.Vacuity)),
            Is.EqualTo(first.ClaimResults.Select(claim => (claim.ClaimId, claim.Outcome, claim.Reason, claim.Vacuity))));
        Assert.That(Directory.GetFiles(project.Request.Cache.Directory!, "*", SearchOption.AllDirectories), Is.EquivalentTo(files));
        foreach (var path in files)
        { Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes[path])); }
    }

    [TestCase(null)]
    [TestCase("other")]
    [TestCase("SHADOW")]
    public async Task LegacyQualificationProfileIgnoresUnrecognizedModes(string? mode)
    {
        using var project = new ShadowTestProject(IdentitySource);
        using var environment = new ShadowEnvironment(mode);
        using var worker = project.CreateLegacyWorker();
        var reports = 0;
        worker.ShadowReportSink = _ => reports++;
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(reports, Is.Zero);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [Test]
    public async Task InterruptedShadowReportsRemainUncheckedAndRetainExactReason()
    {
        using var project = new ShadowTestProject(IdentitySource);
        using var legacyWorker = project.CreateLegacyWorker();
        WorkerVerifyResponse authoritative;
        using (var disabled = new ShadowEnvironment(null))
        { authoritative = await legacyWorker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None); }
        using var projectBoundary = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        await projectBoundary.CancelAsync();
        var timedOut = await WorkerVcShadowObserver.ObserveAsync(authoritative, project.Snapshot.Callables, project.Request.Budgets,
            projectBoundary.Token, caller.Token);
        Assert.That(timedOut.Rows.Single().NewReason, Is.EqualTo(WorkerClaimReason.ProjectTimeout));
        Assert.That(timedOut.Checked, Is.Zero);
        await caller.CancelAsync();
        var canceled = await WorkerVcShadowObserver.ObserveAsync(authoritative, project.Snapshot.Callables, project.Request.Budgets,
            projectBoundary.Token, caller.Token);
        Assert.That(canceled.Rows.Single().NewReason, Is.EqualTo(WorkerClaimReason.Canceled));
        Assert.That(canceled.Checked, Is.Zero);
        Assert.That(canceled.CoverageComplete, Is.False);
    }

    [Test]
    public async Task ShadowMethodTimeoutKeepsRemainingClausesUncheckedAndPreservesLegacyResponse()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x, int d) {
                Contract.Ensures(unchecked(Contract.Result<int>() * d + x % d) == x);
                Contract.Ensures(unchecked(Contract.Result<int>() * d + x % d) == x);
                return x / d;
            } }
            """);
        var candidate = PassiveCallableArtifactAdapter.Enroll(project.Snapshot.Callables.Single())!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out _), Is.True);
        using (var native = new PassiveCallableSolver(plan!))
        {
            Assert.That((await native.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
            var beforeEnsures = native.ConsumedResourceCount;
            var hardQuery = await native.VerifyEnsuresAsync(0);
            Assert.That(hardQuery.Outcome, Is.TypeOf<UnknownOutcome>());
            Assert.That(hardQuery.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(native.ConsumedResourceCount - beforeEnsures, Is.EqualTo(WorkerBudgets.DefaultQueryRlimit));
        }
        var backend = new UnknownBackend();
        using var worker = new SharpProofWorker(backend);
        WorkerVerifyResponse legacy;
        using (var disabled = new ShadowEnvironment(null))
        { legacy = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None); }
        Assert.That(legacy.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        project.Request.Budgets.MethodWallTimeMilliseconds = 30;
        WorkerVcShadowReport? observed = null;
        worker.ShadowReportSink = report => observed = report;
        using var shadow = new ShadowEnvironment("shadow");
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        Assert.That(response.ClaimResults.Select(claim => (claim.ClaimId, claim.Outcome, claim.Reason, claim.Vacuity)),
            Is.EqualTo(legacy.ClaimResults.Select(claim => (claim.ClaimId, claim.Outcome, claim.Reason, claim.Vacuity))));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        Assert.That(observed, Is.Not.Null);
        Assert.That(observed!.Rows, Has.Length.EqualTo(2));
        Assert.That(observed.Checked, Is.Zero);
        Assert.That(observed.Rows.Select(row => row.NewReason), Is.All.EqualTo(WorkerClaimReason.MethodTimeout));
        Assert.That(observed.CoverageComplete, Is.False);
    }

    [Test]
    public void ClassificationRequiresOpposedConcreteOutcomesAndPreservesMetadata()
    {
        var row = new WorkerVcShadowRow("callable", "claim", WorkerClaimOutcome.Proven, WorkerVacuityKind.None, [],
            true, true, true, WorkerClaimOutcome.Refuted, WorkerClaimReason.None, WorkerVacuityKind.None,
            PassiveCallableFeasibilityKind.Feasible, [], false);
        Assert.That(row.SoundnessDisagreement, Is.True);
        Assert.That((row with { OldOutcome = WorkerClaimOutcome.Refuted, NewOutcome = WorkerClaimOutcome.Proven }).SoundnessDisagreement, Is.True);
        Assert.That((row with { OldVacuity = WorkerVacuityKind.ContradictoryPreconditions }).SoundnessDisagreement, Is.False);
        Assert.That((row with { NewVacuity = WorkerVacuityKind.NoModeledNormalReturn }).SoundnessDisagreement, Is.False);
        Assert.That((row with { HasBodyAssumptions = true }).SoundnessDisagreement, Is.False);
        Assert.That((row with { OldAssumptions = [new("assume", WorkerAssumptionKind.UserAssume, true)] }).SoundnessDisagreement, Is.False);
        Assert.That((row with { NewAssumptions = [new("api", WorkerAssumptionKind.ApiSpecification, true)] }).SoundnessDisagreement, Is.False);
        Assert.That((row with { OldOutcome = WorkerClaimOutcome.Unknown }).PrecisionGain, Is.True);
        Assert.That((row with { NewOutcome = WorkerClaimOutcome.Unknown }).PrecisionLoss, Is.True);
        Assert.That((row with { Checked = false }).PrecisionLoss, Is.True);
        Assert.That((row with { NewOutcome = WorkerClaimOutcome.Unknown }).SoundnessDisagreement, Is.False);
        var mixed = new WorkerVcShadowReport(new string('a', 64), new string('b', 64), WorkerCacheStatus.Disabled,
            [row, row with { ClaimId = "unsupported", Enrolled = false, Checked = false, NewOutcome = WorkerClaimOutcome.Unknown }]);
        Assert.That(mixed.Postconditions, Is.EqualTo(2));
        Assert.That(mixed.Checked, Is.EqualTo(1));
        Assert.That(mixed.Unchecked, Is.EqualTo(1));
        Assert.That(mixed.Unenrolled, Is.EqualTo(1));
        Assert.That(mixed.SoundnessDisagreements, Is.EqualTo(1));
        Assert.That(mixed.CoverageComplete, Is.False);
        Assert.That((mixed with { Rows = [] }).CoverageComplete, Is.False);
    }

    private sealed class UnknownBackend : ISmtBackend
    {
        internal int Checks { get; private set; }
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checks++;
            return Task.FromResult(BackendCheckResult.Unknown(BackendFailureReason.ResourceLimit));
        }
    }
}

internal sealed class ShadowEnvironment : IDisposable
{
    private readonly string? _previous = Environment.GetEnvironmentVariable("SHARPPROOF_VC");
    internal ShadowEnvironment(string? value)
    { Environment.SetEnvironmentVariable("SHARPPROOF_VC", value); }
    public void Dispose()
    { Environment.SetEnvironmentVariable("SHARPPROOF_VC", _previous); }
}

internal sealed class ShadowTestProject : IDisposable
{
    private readonly TempDirectory _directory = new("sharpproof-shadow-");
    internal WorkerVerifyRequest Request { get; }
    internal WorkerInputSnapshot Snapshot { get; }
    internal ShadowTestProject(string source, bool cacheEnabled = false)
        : this(CompilerTotalCallableArtifactTests.CreateArtifact(source), cacheEnabled)
    {
    }
    internal ShadowTestProject(CompilerManifestArtifact artifact, bool cacheEnabled = false)
    {
        try
        {
            artifact.Compilation.ProjectDirectory = _directory.FullName;
            var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
            var path = Path.Combine(_directory.FullName, "compiler-manifest.json");
            File.WriteAllBytes(path, bytes);
            Request = new WorkerVerifyRequest
            {
                CompilerManifest = new WorkerFileReference { Path = path, Sha256 = WorkerProtocolJson.ComputeSha256(bytes) },
                Cache = new WorkerCacheOptions { Enabled = cacheEnabled, Directory = Path.Combine(_directory.FullName, "cache") },
                Budgets = new WorkerBudgets()
            };
            Snapshot = WorkerInputSnapshot.Load(Request, WorkerCacheIdentity.Current, CancellationToken.None);
            // Both authoritative and shadow routes must reuse the prepared
            // immutable artifact, including a later validated cache hit.
            File.Delete(path);
        }
        catch
        { _directory.Dispose(); throw; }
    }
    public void Dispose()
    { _directory.Dispose(); }

    internal WorkerInputSnapshot Bind()
    {
        return ArtifactValidator.Bind(Request,
            new ValidatedArtifact(Snapshot.CompilerManifest, Snapshot.Callables, Snapshot.ArtifactDigest), WorkerCacheIdentity.Current);
    }

    internal SharpProofWorker CreateLegacyWorker(WorkerBudgets? budgets = null)
    {
        var selected = budgets ?? Request.Budgets;
        return new SharpProofWorker(() =>
        {
            ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
            return new IrSmtBackend(new IrSmtBackendOptions(selected.QueryRlimit));
        });
    }
}
