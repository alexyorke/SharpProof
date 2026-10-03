using System.Collections.Immutable;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// This is an explicit native source-worker universe. The inherited analyzer
// corpus has no postconditions and cannot establish native postcondition coverage.
[TestFixture]
[NonParallelizable]
public sealed class WorkerVcShadowSourceGateTests
{
    internal static readonly ImmutableArray<ShadowSourceCase> Universe =
    [
        new("identity", ShadowTestProject.IdentitySource, [WorkerClaimOutcome.Proven]),
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
            """, [WorkerClaimOutcome.Refuted]),
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
            """, [WorkerClaimOutcome.Proven],
            LibrarySource: "public static class Library { public static int Target(int x) { try { return 10 / x; } catch (System.DivideByZeroException) { return 1; } } }"),
        new("metadata-module-init-closed", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Ensures(true); return Library.Target(x);
            } }
            """, [WorkerClaimOutcome.Proven],
            LibrarySource: "public static class Boot { public static int State; [System.Runtime.CompilerServices.ModuleInitializer] public static void Initialize() { State = 5; } } public static class Library { public static int Target(int x) { return x; } }"),
        new("async-root-closed", """
            using SharpProof.Attributes;
            public static class Subject { public static async void Target(int x) {
                Contract.Ensures(false); throw null!;
            } }
            """, [WorkerClaimOutcome.Unknown], TotalPresent: false, Checked: false, Reason: WorkerClaimReason.UnsupportedCallable)
    ];

    [Test]
    public async Task DefinedSourceWorkerUniverseAccountsForEveryPostconditionWithoutEmptyExitSuccess()
    {
        Assert.That(Universe.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(Universe.Length));
        Assert.That(Universe.Length, Is.EqualTo(54));
        var results = new List<WorkerClaimResult>();
        var checks = new List<TotalCallableClaimCheck>();
        var manifestPostconditions = 0;
        foreach (var sourceCase in Universe)
        {
            using var project = new ShadowTestProject(CreateGateArtifact(sourceCase));
            var sourceChecks = new Dictionary<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
            foreach (var preparation in project.Snapshot.Callables)
            {
                await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets,
                    check => sourceChecks[check.ClaimId] = check, CancellationToken.None);
            }
            using var worker = SharpProofWorker.Create(project.Request.Budgets);
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            var posts = AssertNativeResults(response, project, sourceCase, sourceChecks);
            manifestPostconditions += response.Manifest.Claims.Count(claim => claim.Kind == WorkerClaimKind.Postcondition);
            results.AddRange(posts);
            checks.AddRange(sourceChecks.Values);
        }
        Assert.That(manifestPostconditions, Is.EqualTo(87));
        Assert.That(results, Has.Count.EqualTo(manifestPostconditions));
        Assert.That(checks.Count(check => check.Enrolled), Is.EqualTo(86));
        Assert.That(results.Count - checks.Count(check => check.Enrolled), Is.EqualTo(1));
        Assert.That(checks.Count(check => check.Checked), Is.EqualTo(86));
        Assert.That(results.Count(result => result.Outcome == WorkerClaimOutcome.Unknown), Is.EqualTo(5));
        Assert.That(results.Count(result => result.Outcome == WorkerClaimOutcome.Proven &&
            result.Assumptions.Any(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume && assumption.Used)), Is.EqualTo(2));
        await TestContext.Out.WriteLineAsync($"native source-worker universe: sources={Universe.Length} posts={results.Count} enrolled={checks.Count(check => check.Enrolled)} checked={checks.Count(check => check.Checked)} unknown={results.Count(result => result.Outcome == WorkerClaimOutcome.Unknown)}");
    }

    [Test]
    public async Task NativeEntryQualificationAccountsForEveryCallableInTheSourceAndGoldenUniverse()
    {
        var cases = QualificationCases();
        var baseline = QualificationBaseline("entries", "CallableId");
        var sourceCount = 0;
        var callableCount = 0;
        var enrolledCount = 0;
        var oldKnown = 0;
        var nativeKnown = 0;
        var unknownCount = 0;
        var disagreements = 0;
        var degradations = 0;
        foreach (var sourceCase in cases)
        {
            sourceCount++;
            using var project = new ShadowTestProject(CreateGateArtifact(sourceCase));
            if (project.Snapshot.Callables.IsEmpty)
            { await TestContext.Out.WriteLineAsync($"entry-worker {sourceCase.Name}: callables=0"); }
            foreach (var preparation in project.Snapshot.Callables)
            {
                callableCount++;
                if (preparation.TotalEntry != null)
                { enrolledCount++; }
                var native = await TotalCallableVerifier.VerifyEntryAsync(preparation, project.Request.Budgets, CancellationToken.None);
                var legacyKind = Enum.Parse<CallableEntryFeasibilityKind>(baseline[sourceCase.Name + ":" + preparation.Entry.CallableId].GetProperty("Kind").GetString()!);
                if (legacyKind != CallableEntryFeasibilityKind.Unknown)
                { oldKnown++; }
                if (!native.IsUnknown)
                { nativeKnown++; }
                else
                { unknownCount++; }
                if (legacyKind != CallableEntryFeasibilityKind.Unknown && native.IsUnknown)
                { degradations++; }
                if (legacyKind != CallableEntryFeasibilityKind.Unknown && !native.IsUnknown && legacyKind != native.Kind)
                { disagreements++; }
                Assert.That(native.UsedAssumptionIds,
                    Is.SubsetOf(preparation.Entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.Precondition).Select(assumption => assumption.Id)), sourceCase.Name);
                await TestContext.Out.WriteLineAsync($"entry-worker {sourceCase.Name}: old={legacyKind} native={native.Kind} enrolled={preparation.TotalEntry != null}");
            }
        }
        Assert.That(sourceCount, Is.EqualTo(cases.Length));
        Assert.That(callableCount, Is.GreaterThanOrEqualTo(Universe.Length));
        Assert.That(nativeKnown + unknownCount, Is.EqualTo(callableCount));
        Assert.That(callableCount, Is.EqualTo(baseline.Count));
        Assert.That(oldKnown, Is.GreaterThan(0));
        Assert.That(oldKnown, Is.EqualTo(30));
        Assert.That(nativeKnown, Is.GreaterThanOrEqualTo(oldKnown));
        Assert.That(disagreements, Is.Zero);
        Assert.That(degradations, Is.Zero);
        await TestContext.Out.WriteLineAsync($"entry-worker universe: sources={sourceCount} callables={callableCount} enrolled={enrolledCount} old-known={oldKnown} native-known={nativeKnown} unknown={unknownCount} disagreements={disagreements} degradations={degradations}");
    }

    internal static ShadowSourceCase[] QualificationCases()
    {
        // Reachable-source, synchronization projection, and metadata Requires cases qualify through
        // dedicated drivers. They are outside the frozen Phase 2 callable
        // authority baseline; retain every original baseline case.
        var goldenNames = GoldenTest.Cases("worker").Where(name =>
        {
            var source = GoldenTest.Load("worker", name).Source;
            return !source.StartsWith("// golden-scenario: reachable-source\n", StringComparison.Ordinal) &&
                !source.StartsWith("// golden-scenario: synchronization-projection\n", StringComparison.Ordinal) &&
                !source.StartsWith("// golden-scenario: typed-il-call-requires\n", StringComparison.Ordinal) &&
                !source.StartsWith("// golden-scenario: shadow-call-ancestry-tampered-predicate\n", StringComparison.Ordinal);
        }).ToArray();
        Assert.That(goldenNames, Has.Length.EqualTo(64));
        var cases = Universe.Concat(goldenNames.Select(name =>
        {
            var source = GoldenTest.Load("worker", name).Source;
            const string marker = "// metadata-library";
            var boundary = source.IndexOf(marker, StringComparison.Ordinal);
            return boundary < 0 ? new ShadowSourceCase("golden:" + name, source, [])
                : new ShadowSourceCase("golden:" + name, source[..boundary], [], LibrarySource: source[(boundary + marker.Length)..]);
        })).ToArray();
        Assert.That(cases.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(cases.Length));
        Assert.That(Universe, Has.Length.EqualTo(54));
        return cases;
    }

    [Test]
    public async Task NativePostconditionQualificationRetainsProofsAcrossTheSourceAndGoldenUniverse()
    {
        var cases = QualificationCases();
        var baseline = QualificationBaseline("postconditions", "CallableId");
        var proofLosses = new List<string>();
        var callableCount = 0;
        var postconditions = 0;
        var oldProven = 0;
        var nativeProven = 0;
        var enrolled = 0;
        var checkedCount = 0;
        var unknown = 0;
        var disagreements = 0;
        foreach (var sourceCase in cases)
        {
            using var project = new ShadowTestProject(CreateGateArtifact(sourceCase));
            callableCount += project.Snapshot.Callables.Length;
            var checks = new Dictionary<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
            foreach (var preparation in project.Snapshot.Callables)
            {
                await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets,
                    check => checks[check.ClaimId] = check, CancellationToken.None);
            }
            var owned = project.Snapshot.Callables.ToDictionary(preparation => preparation.Entry.CallableId, StringComparer.Ordinal);
            var expected = project.Snapshot.CompilerManifest.Manifest.Claims.Where(claim => claim.Kind == WorkerClaimKind.Postcondition).ToArray();
            Assert.That(checks.Keys, Is.SubsetOf(expected.Select(claim => claim.ClaimId)), sourceCase.Name);
            postconditions += expected.Length;
            foreach (var claim in expected)
            {
                var old = baseline[sourceCase.Name + ":" + claim.CallableId + ":" + claim.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)];
                var oldOutcome = Enum.Parse<WorkerClaimOutcome>(old.GetProperty("Outcome").GetString()!);
                var newOutcome = WorkerClaimOutcome.Unknown;
                var comparable = false;
                if (checks.TryGetValue(claim.ClaimId, out var check))
                {
                    var result = CallableClaimResultAssembler.FromTotal(owned[claim.CallableId], check);
                    newOutcome = result.Outcome;
                    if (check.Enrolled)
                    { enrolled++; }
                    if (check.Checked)
                    { checkedCount++; }
                    comparable = check.Checked && result.Vacuity == WorkerVacuityKind.None &&
                        check.Evidence.BodyAssumptions.IsEmpty && !result.Assumptions.Any(assumption => assumption.Used &&
                            assumption.Kind is WorkerAssumptionKind.UserAssume or WorkerAssumptionKind.TrustedBoundary or WorkerAssumptionKind.ApiSpecification);
                }
                if (oldOutcome == WorkerClaimOutcome.Proven)
                {
                    oldProven++;
                    if (newOutcome != WorkerClaimOutcome.Proven)
                    { proofLosses.Add(sourceCase.Name + ":" + claim.ClaimId); }
                }
                if (newOutcome == WorkerClaimOutcome.Proven)
                { nativeProven++; }
                if (newOutcome == WorkerClaimOutcome.Unknown)
                { unknown++; }
                if (comparable && old.GetProperty("Vacuity").GetString() == "None" && !old.GetProperty("Conditional").GetBoolean() &&
                    (oldOutcome == WorkerClaimOutcome.Proven && newOutcome == WorkerClaimOutcome.Refuted ||
                     oldOutcome == WorkerClaimOutcome.Refuted && newOutcome == WorkerClaimOutcome.Proven))
                { disagreements++; }
            }
        }
        await TestContext.Out.WriteLineAsync($"post-worker universe: sources={cases.Length} callables={callableCount} posts={postconditions} enrolled={enrolled} checked={checkedCount} unknown={unknown} old-proven={oldProven} native-proven={nativeProven} disagreements={disagreements} proof-losses={proofLosses.Count}");
        Assert.That(postconditions, Is.EqualTo(baseline.Count));
        Assert.That(oldProven, Is.EqualTo(18));
        Assert.That(nativeProven, Is.GreaterThanOrEqualTo(oldProven));
        Assert.That(disagreements, Is.Zero);
        Assert.That(proofLosses, Is.Empty, string.Join(", ", proofLosses));
    }

    private static Dictionary<string, JsonElement> QualificationBaseline(string section, string id)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestRepository.FindRoot(),
            "tests", "qualification", "phase2-native-authority-baseline.json")));
        Assert.That(document.RootElement.GetProperty("commit").GetString(), Is.EqualTo("86fa6ea9039a0883e1a8f2baf492738b8224fba6"));
        return document.RootElement.GetProperty(section).EnumerateArray().ToDictionary(
            item => item.GetProperty("Fixture").GetString() + ":" + item.GetProperty(id).GetString() +
                (section == "postconditions" ? ":" + item.GetProperty("Ordinal").GetRawText() : ""),
            item => item.Clone(), StringComparer.Ordinal);
    }

    internal static SharpProof.CompilerArtifact.CompilerManifestArtifact CreateGateArtifact(ShadowSourceCase sourceCase)
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
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Manifest.Claims, Is.Not.Empty);
        Assert.That(response.Manifest.Claims.Any(claim => claim.Kind == WorkerClaimKind.Postcondition), Is.False);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var checks = new List<TotalCallableClaimCheck>();
        foreach (var preparation in project.Snapshot.Callables)
        {
            await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets, checks.Add, CancellationToken.None);
        }
        Assert.That(checks, Is.Empty);
    }

    private static WorkerClaimResult[] AssertNativeResults(WorkerVerifyResponse response, ShadowTestProject project, ShadowSourceCase sourceCase,
        IReadOnlyDictionary<string, TotalCallableClaimCheck> checks)
    {
        var validation = WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest);
        Assert.That(validation.IsValid, Is.True, string.Join(", ", validation.Errors.Select(error => error.Code)));
        Assert.That(response.Errors, Is.Empty, sourceCase.Name);
        var ordinals = response.Manifest.Claims.Where(claim => claim.Kind == WorkerClaimKind.Postcondition)
            .ToDictionary(claim => claim.ClaimId, claim => claim.Ordinal, StringComparer.Ordinal);
        if (sourceCase.TotalPresent)
        { Assert.That(checks.Keys, Is.EquivalentTo(ordinals.Keys), sourceCase.Name); }
        else
        { Assert.That(checks, Is.Empty, sourceCase.Name); }
        var posts = response.ClaimResults.Where(result => ordinals.ContainsKey(result.ClaimId)).OrderBy(result => ordinals[result.ClaimId]).ToArray();
        Assert.That(posts.Select(result => result.ClaimId), Is.EquivalentTo(ordinals.Keys), sourceCase.Name);
        Assert.That(posts.Select(result => result.Outcome), Is.EqualTo(sourceCase.Outcomes), sourceCase.Name);
        if (sourceCase.Reasons.IsDefault)
        { Assert.That(posts.Select(result => result.Reason), Is.All.EqualTo(sourceCase.Reason), sourceCase.Name); }
        else
        { Assert.That(posts.Select(result => result.Reason), Is.EqualTo(sourceCase.Reasons), sourceCase.Name); }
        Assert.That(posts.Select(result => result.Vacuity), Is.All.EqualTo(sourceCase.Vacuity), sourceCase.Name);
        Assert.That(project.Snapshot.Callables.Select(preparation => preparation.Total != null), Is.All.EqualTo(sourceCase.TotalPresent), sourceCase.Name);
        Assert.That(posts.Select(result => result.Outcome == WorkerClaimOutcome.Proven &&
            result.Assumptions.Any(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume && assumption.Used)),
            Is.All.EqualTo(sourceCase.Conditional), sourceCase.Name);
        if (sourceCase.Checked is { } checkedValue)
        { Assert.That(checks.Values.Select(check => check.Checked), Is.All.EqualTo(checkedValue), sourceCase.Name); }
        foreach (var post in posts)
        {
            if (checks.TryGetValue(post.ClaimId, out var check))
            {
                var preparation = project.Snapshot.Callables.Single(callable => callable.Entry.ClaimIds.Contains(post.ClaimId));
                var expected = CallableClaimResultAssembler.FromTotal(preparation, check);
                Assert.That((post.Outcome, post.Reason, post.Vacuity), Is.EqualTo((expected.Outcome, expected.Reason, expected.Vacuity)), sourceCase.Name);
            }
            else
            {
                Assert.That(post.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), sourceCase.Name);
                Assert.That(post.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedCallable), sourceCase.Name);
            }
            if (post.Outcome == WorkerClaimOutcome.Refuted)
            { Assert.That(post.Model, Is.Not.Empty, sourceCase.Name); }
        }
        return posts;
    }
}

internal sealed record ShadowSourceCase(string Name, string Source, ImmutableArray<WorkerClaimOutcome> Outcomes,
    bool TotalPresent = true, bool? Checked = true, WorkerVacuityKind Vacuity = WorkerVacuityKind.None,
    WorkerClaimReason Reason = WorkerClaimReason.None, bool Conditional = false, ImmutableArray<WorkerClaimReason> Reasons = default,
    string? LibrarySource = null);
