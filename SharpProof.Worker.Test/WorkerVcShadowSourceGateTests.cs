using System.Collections.Immutable;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// This is an explicit native source-worker universe. The inherited analyzer
// corpus has no postconditions and cannot establish candidate shadow coverage.
// The gate consumes the direct sink; MSBuild stderr capture is capped at 1 MiB.
[TestFixture]
[NonParallelizable]
public sealed class WorkerVcShadowSourceGateTests
{
    internal static readonly ImmutableArray<ShadowSourceCase> Universe =
    [
        new("identity", WorkerVcShadowTests.IdentitySource, [WorkerClaimOutcome.Proven]),
        new("array-surrogate", GoldenTest.Load("worker", "vc-shadow-array-surrogate").Source, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("array-filter-return", GoldenTest.Load("worker", "vc-shadow-array-fault").Source, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("reference-length", """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(string text, int[] array, object unused) {
                    Contract.Requires(text != null && text.Length == 3 && array != null && array.Length == 3);
                    Contract.Ensures(Contract.Result<int>() == text.Length && array.Length == text.Length);
                    Contract.Ensures(Contract.Result<int>() == 0);
                    return Length(text);
                }
                private static int Length(string value) { return value.Length; }
            }
            """, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("reference-fault", """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(string text) {
                    Contract.Requires(text == null);
                    Contract.Ensures(Contract.Result<int>() == 1);
                    Contract.Ensures(Contract.Result<int>() == 0);
                    int seen = 0;
                    try { return Length(text); }
                    catch (System.NullReferenceException) when (++seen == 1) { return seen; }
                    finally { seen += 10; }
                }
                private static int Length(string value) { return value.Length; }
            }
            """, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("diamond-old", CompilerTotalCallableArtifactTests.DiamondSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("all-throw", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int d) {
                Contract.Requires(d == 0);
                Contract.Ensures(Contract.Result<int>() == 7); return 10 / d;
            } }
            """, [WorkerClaimOutcome.Proven], Vacuity: WorkerVacuityKind.NoModeledNormalReturn),
        new("impossible-entry", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Requires(x > 0); Contract.Requires(x < 0);
                Contract.Ensures(Contract.Result<int>() == 7); return x;
            } }
            """, [WorkerClaimOutcome.Proven], Vacuity: WorkerVacuityKind.ContradictoryPreconditions),
        new("guarded-return", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int d) {
                Contract.Requires(d == 0 || d == 1);
                Contract.Ensures(d == 0 ? Contract.Result<int>() == 0 : Contract.Result<int>() == 10 / d);
                return 10 / d;
            } }
            """, [WorkerClaimOutcome.Proven]),
        new("unsafe-ensures", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int d) {
                Contract.Ensures(Contract.Result<int>() / d == 0); return 0;
            } }
            """, [WorkerClaimOutcome.Unknown], Reason: WorkerClaimReason.PostconditionMayBeUndefined),
        new("ulong-legacy-failure", """
            using SharpProof.Attributes;
            public static class Subject { public static ulong Target(ulong x) {
                Contract.Requires(x == 18446744073709551615UL);
                Contract.Ensures(Contract.Result<ulong>() == 0UL); return unchecked(x + 1UL);
            } }
            """, [WorkerClaimOutcome.Proven]),
        new("source-assume-prologue", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Ensures(Contract.Result<int>() > 0); Contract.Assume(x > 0); return x;
            } }
            """, [WorkerClaimOutcome.Proven], Conditional: true),
        new("source-scalar-loop", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Ensures(Contract.Result<int>() == x); while (x < 0) x++; return x;
            } }
            """, [WorkerClaimOutcome.Proven]),
        new("source-call-closed", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Ensures(Contract.Result<int>() == x); return System.Math.Abs(x);
            } }
            """, [WorkerClaimOutcome.Unknown], TotalPresent: false, Checked: false, Reason: WorkerClaimReason.UnsupportedBody),
        new("general-division-budget", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int d) {
                Contract.Ensures(Contract.Result<int>() == 10 / d); return 10 / d;
            } }
            """, [WorkerClaimOutcome.Proven]),
        new("region-captured-return", WorkerVcRegionTests.CapturedReturnSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("region-mixed-finally", WorkerVcRegionTests.MixedFinallySource, [WorkerClaimOutcome.Proven]),
        new("region-nested-rethrow", WorkerVcRegionTests.NestedRethrowSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("region-assume", WorkerVcRegionTests.AssumeRegionSource, [WorkerClaimOutcome.Proven], Conditional: true),
        new("region-all-throw", WorkerVcRegionTests.AllThrowSource, [WorkerClaimOutcome.Proven], Vacuity: WorkerVacuityKind.NoModeledNormalReturn),
        new("region-normal-local", WorkerVcRegionTests.LocalJoinSource, [WorkerClaimOutcome.Proven]),
        new("checked-normal-return", WorkerVcCheckedArithmeticTests.CheckedReturnSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("checked-caught-effects", WorkerVcCheckedArithmeticTests.CaughtEffectsSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("checked-full-ulong", WorkerVcCheckedArithmeticTests.FullUlongSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("checked-unsafe-clause", WorkerVcCheckedArithmeticTests.UnsafeClauseSource, [WorkerClaimOutcome.Unknown], Reason: WorkerClaimReason.PostconditionMayBeUndefined),
        new("filter-search-before-unwind", WorkerVcExceptionSearchTests.SearchSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("filter-fault-side-effects", WorkerVcExceptionSearchTests.FilterFaultSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("nested-finally-captured-return", WorkerVcExceptionSearchTests.NestedReturnSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("finally-replaces-selected-handler", WorkerVcExceptionSearchTests.ReplacementSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("cyclic-caught-loop", WorkerVcCyclicRegionTests.CaughtLoopSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("cyclic-repeated-filter-completed", WorkerVcCyclicRegionTests.RepeatedFilterSource.Replace(
            "Contract.Ensures(Contract.Result<int>() == 0);", "", StringComparison.Ordinal), [WorkerClaimOutcome.Unknown], Reason: WorkerClaimReason.SolverIncomplete),
        new("guarded-rethrow-normal-return", WorkerVcCyclicRegionTests.GuardedRethrowSource, [WorkerClaimOutcome.Refuted]),
        new("source-call-scalar", WorkerVcSourceCallTests.ScalarCallSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("source-call-argument-capture", WorkerVcSourceCallTests.ArgumentCaptureSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("source-call-loop-frames", WorkerVcSourceCallTests.LoopCallSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("source-call-ulong-default", WorkerVcSourceCallTests.FullUlongCallSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("source-call-cross-filter-finally-closed", WorkerVcSourceCallTests.FilterUnwindSource,
            [WorkerClaimOutcome.Refuted]),
        new("cross-frame-replacement-search", WorkerVcCrossFrameSearchTests.ReplacementSource,
            [WorkerClaimOutcome.Unknown, WorkerClaimOutcome.Refuted], Reasons: [WorkerClaimReason.SolverIncomplete, WorkerClaimReason.None]),
        new("cross-frame-filter-fault", WorkerVcCrossFrameSearchTests.FilterFaultSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("cross-frame-captured-return", WorkerVcCrossFrameSearchTests.CapturedReturnSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("cross-frame-own-filter-fault", WorkerVcCrossFrameSearchTests.OwnFilterFaultSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("cross-frame-repeated-filter", WorkerVcCrossFrameSearchTests.RepeatedFilterSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted]),
        new("metadata-source-private-chain", WorkerVcMetadataCallTests.IncrementSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted], LibrarySource: WorkerVcMetadataCallTests.LibrarySource),
        new("metadata-argument-capture", WorkerVcMetadataCallTests.ArgumentSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted], LibrarySource: WorkerVcMetadataCallTests.LibrarySource),
        new("metadata-reverse-named-order", WorkerVcMetadataCallTests.ReverseArgumentSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted], LibrarySource: WorkerVcMetadataCallTests.LibrarySource),
        new("metadata-narrow-starg", WorkerVcMetadataCallTests.StoreSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted], LibrarySource: WorkerVcMetadataCallTests.LibrarySource),
        new("metadata-full-ulong", WorkerVcMetadataCallTests.UlongSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted], LibrarySource: WorkerVcMetadataCallTests.LibrarySource),
        new("metadata-caller-filter-finally", WorkerVcMetadataCallTests.FaultSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted], LibrarySource: WorkerVcMetadataCallTests.LibrarySource),
        new("metadata-filter-fault-swallowed", WorkerVcMetadataCallTests.FilterFaultSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted], LibrarySource: WorkerVcMetadataCallTests.LibrarySource),
        new("metadata-private-overflow-finally", WorkerVcMetadataCallTests.OverflowSource, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted], LibrarySource: WorkerVcMetadataCallTests.LibrarySource),
        new("metadata-reference-copy", """
            using SharpProof.Attributes;
            public static class Subject { public static object Target(object x) {
                Contract.Requires(x != null);
                Contract.Ensures(Contract.Result<object>() == x);
                Contract.Ensures(Contract.Result<object>() == null);
                return Library.Target(x);
            } }
            """, [WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted],
            LibrarySource: "public static class Library { public static object Target(object value) { return Again(value); } private static object Again(object value) { return value; } }"),
        new("metadata-eh-closed", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Ensures(true); return Library.Target(x);
            } }
            """, [WorkerClaimOutcome.Unknown], TotalPresent: false, Checked: false, Reason: WorkerClaimReason.UnsupportedBody,
            LibrarySource: "public static class Library { public static int Target(int x) { try { return 10 / x; } catch (System.DivideByZeroException) { return 1; } } }"),
        new("metadata-module-init-closed", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Ensures(true); return Library.Target(x);
            } }
            """, [WorkerClaimOutcome.Unknown], TotalPresent: false, Checked: false, Reason: WorkerClaimReason.UnsupportedBody,
            LibrarySource: "public static class Boot { public static int State; [System.Runtime.CompilerServices.ModuleInitializer] public static void Initialize() { State = 5; } } public static class Library { public static int Target(int x) { return x; } }"),
        new("async-root-closed", """
            using SharpProof.Attributes;
            public static class Subject { public static async void Target(int x) {
                Contract.Ensures(false); throw null!;
            } }
            """, [WorkerClaimOutcome.Unknown], TotalPresent: false, Checked: false, Reason: WorkerClaimReason.UnsupportedBody)
    ];

    [Test]
    public async Task DefinedSourceWorkerUniverseAccountsForEveryPostconditionWithoutEmptyExitSuccess()
    {
        Assert.That(Universe.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(Universe.Length));
        Assert.That(Universe.Length, Is.EqualTo(54));
        using var environment = new ShadowEnvironment("shadow");
        var rows = new List<WorkerVcShadowRow>();
        var manifestPostconditions = 0;
        foreach (var sourceCase in Universe)
        {
            using var project = new ShadowTestProject(CreateGateArtifact(sourceCase));
            using var worker = SharpProofWorker.Create(project.Request.Budgets);
            WorkerVcShadowReport? report = null;
            worker.ShadowReportSink = observed => report = observed;
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(report, Is.Not.Null, sourceCase.Name);
            AssertReport(report!, response, project, sourceCase);
            manifestPostconditions += response.Manifest.Claims.Count(claim => claim.Kind == WorkerClaimKind.Postcondition);
            rows.AddRange(report!.Rows);
            await TestContext.Out.WriteLineAsync($"source-worker {sourceCase.Name}: posts={report.Postconditions} enrolled={report.Enrolled} checked={report.Checked} unknown={report.Unknown} oldProven={report.OldProven} newProven={report.NewProven} disagreements={report.SoundnessDisagreements}");
        }
        Assert.That(manifestPostconditions, Is.EqualTo(87));
        Assert.That(rows, Has.Count.EqualTo(manifestPostconditions));
        var aggregate = new WorkerVcShadowReport("source-universe", "source-universe", WorkerCacheStatus.Disabled, [.. rows]);
        Assert.That(aggregate.Enrolled, Is.EqualTo(83));
        Assert.That(aggregate.Unenrolled, Is.EqualTo(4));
        Assert.That(aggregate.Checked, Is.EqualTo(83));
        Assert.That(aggregate.Unknown, Is.EqualTo(8));
        Assert.That(aggregate.NewConditional, Is.EqualTo(2));
        Assert.That(aggregate.SoundnessDisagreements, Is.Zero);
        Assert.That(aggregate.CoverageComplete, Is.False);
        await TestContext.Out.WriteLineAsync($"source-worker universe: sources={Universe.Length} posts={aggregate.Postconditions} enrolled={aggregate.Enrolled} checked={aggregate.Checked} unchecked={aggregate.Unchecked} unknown={aggregate.Unknown} disagreements={aggregate.SoundnessDisagreements} full-exit={aggregate.CoverageComplete}");
    }

    private static SharpProof.CompilerArtifact.CompilerManifestArtifact CreateGateArtifact(ShadowSourceCase sourceCase)
    {
        if (sourceCase.LibrarySource is not { } library)
        { return CompilerTotalCallableArtifactTests.CreateArtifact(sourceCase.Source); }
        using var metadata = new MetadataTestSubject(library, sourceCase.Source);
        return metadata.CreateArtifact();
    }

    [Test]
    public async Task EffectOnlyCallableHasExplicitEmptyPostconditionCoverage()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { [EnforcePure] public static int Target(int x) { return x; } }
            """);
        using var environment = new ShadowEnvironment("shadow");
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Manifest.Claims, Is.Not.Empty);
        Assert.That(report, Is.Not.Null);
        Assert.That(report!.Postconditions, Is.Zero);
        Assert.That(report.Enrolled, Is.Zero);
        Assert.That(report.Checked, Is.Zero);
        Assert.That(report.CoverageComplete, Is.False);
    }

    private static void AssertReport(WorkerVcShadowReport report, WorkerVerifyResponse response, ShadowTestProject project, ShadowSourceCase sourceCase)
    {
        var validation = WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest);
        Assert.That(validation.IsValid, Is.True, string.Join(", ", validation.Errors.Select(error => error.Code)));
        Assert.That(report.InputHash, Is.EqualTo(response.InputHash), sourceCase.Name);
        Assert.That(report.RequestHash, Is.EqualTo(response.RequestHash), sourceCase.Name);
        Assert.That(report.Rows.Select(row => row.ClaimId), Is.EquivalentTo(response.Manifest.Claims.Where(claim => claim.Kind == WorkerClaimKind.Postcondition).Select(claim => claim.ClaimId)));
        var ordinals = response.Manifest.Claims.ToDictionary(claim => claim.ClaimId, claim => claim.Ordinal, StringComparer.Ordinal);
        Assert.That(report.Rows.OrderBy(row => ordinals[row.ClaimId]).Select(row => row.NewOutcome), Is.EqualTo(sourceCase.Outcomes), sourceCase.Name);
        if (sourceCase.Reasons.IsDefault)
        { Assert.That(report.Rows.Select(row => row.NewReason), Is.All.EqualTo(sourceCase.Reason), sourceCase.Name); }
        else
        { Assert.That(report.Rows.OrderBy(row => ordinals[row.ClaimId]).Select(row => row.NewReason), Is.EqualTo(sourceCase.Reasons), sourceCase.Name); }
        Assert.That(report.Rows.Select(row => row.NewVacuity), Is.All.EqualTo(sourceCase.Vacuity), sourceCase.Name);
        Assert.That(report.Rows.Select(row => row.TotalPresent), Is.All.EqualTo(sourceCase.TotalPresent), sourceCase.Name);
        Assert.That(report.Rows.Select(row => row.NewConditional), Is.All.EqualTo(sourceCase.Conditional), sourceCase.Name);
        if (sourceCase.Checked is { } checkedValue)
        { Assert.That(report.Rows.Select(row => row.Checked), Is.All.EqualTo(checkedValue), sourceCase.Name); }
        foreach (var row in report.Rows)
        {
            var old = response.ClaimResults.Single(claim => claim.ClaimId == row.ClaimId);
            Assert.That(row.OldOutcome, Is.EqualTo(old.Outcome));
            Assert.That(row.OldVacuity, Is.EqualTo(old.Vacuity));
            Assert.That(row.OldAssumptions.Select(assumption => (assumption.Id, assumption.Kind, assumption.Used)),
                Is.EqualTo(old.Assumptions.OrderBy(assumption => assumption.Id, StringComparer.Ordinal).Select(assumption => (assumption.Id, assumption.Kind, assumption.Used))));
        }
        using var json = JsonDocument.Parse(report.Serialize()[WorkerVcShadowReport.Prefix.Length..]);
        Assert.That(json.RootElement.GetProperty("authority").GetString(), Is.EqualTo("legacy"));
        Assert.That(report.Serialize(), Does.Not.Contain("entryModel").And.Not.Contain("sourceText").And.Not.Contain("program"));
    }
}

internal sealed record ShadowSourceCase(string Name, string Source, ImmutableArray<WorkerClaimOutcome> Outcomes,
    bool TotalPresent = true, bool? Checked = true, WorkerVacuityKind Vacuity = WorkerVacuityKind.None,
    WorkerClaimReason Reason = WorkerClaimReason.None, bool Conditional = false, ImmutableArray<WorkerClaimReason> Reasons = default,
    string? LibrarySource = null);
