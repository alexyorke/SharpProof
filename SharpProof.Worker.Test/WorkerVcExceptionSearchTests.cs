using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcExceptionSearchTests
{
    internal const string SearchSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 0);
            Contract.Ensures(Contract.Result<int>() == 123 && x == 12 && Contract.Old(x) == 0);
            Contract.Ensures(Contract.Result<int>() == 321);
            try { try { return 10 / x; } finally { x = x * 10 + 2; } }
            catch (System.DivideByZeroException) when ((x = x * 10 + 1) > 0) { return x * 10 + 3; }
        } }
        """;

    internal const string FilterFaultSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 0);
            Contract.Ensures(Contract.Result<int>() == 7 && x == 7 && Contract.Old(x) == 0);
            Contract.Ensures(Contract.Result<int>() == 1);
            try { return 10 / x; }
            catch (System.DivideByZeroException) when ((x = 7) / (x - 7) > 0) { return 1; }
            catch (System.DivideByZeroException) when (x == 7) { return x; }
        } }
        """;

    internal const string NestedReturnSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 3);
            Contract.Ensures(Contract.Result<int>() == Contract.Old(x) && x == 312);
            Contract.Ensures(Contract.Result<int>() == x);
            try { try { return x; } finally { x = x * 10 + 1; } }
            finally { x = x * 10 + 2; }
        } }
        """;

    internal const string ReplacementSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 0);
            Contract.Ensures(Contract.Result<int>() == 7 && x == 7);
            Contract.Ensures(Contract.Result<int>() == 1);
            try { try { try { return 10 / x; } finally { x = checked((byte)(x + 256)); } }
                catch (System.DivideByZeroException) when ((x = 7) == 7) { return 1; } }
            catch (System.OverflowException) { return x; }
        } }
        """;

    internal static string Source(string kind)
    {
        return kind switch
        {
            "search" => SearchSource,
            "filter-fault" => FilterFaultSource,
            "nested-return" => NestedReturnSource,
            "replacement" => ReplacementSource,
            "filter-throw" => FilterFaultSource.Replace("(x = 7) / (x - 7) > 0", "(x = 7) > 0 ? throw null! : true", StringComparison.Ordinal),
            "assume" => FilterFaultSource.Replace("Contract.Requires(x == 0)", "Contract.Assume(x == 0)", StringComparison.Ordinal),
            "nested-body-normal" => """
                using SharpProof.Attributes;
                public static class Subject { public static int Target(int x) {
                    Contract.Requires(x == 3);
                    Contract.Ensures(Contract.Result<int>() == 3 && x == 6);
                    Contract.Ensures(Contract.Result<int>() == 0);
                    try { return x; } finally { try { x++; } finally { x += 2; } }
                } }
                """,
            "nested-body-caught" => """
                using SharpProof.Attributes;
                public static class Subject { public static int Target(int x) {
                    Contract.Requires(x == 3);
                    Contract.Ensures(Contract.Result<int>() == 3 && x == 9);
                    Contract.Ensures(Contract.Result<int>() == 0);
                    try { return x; } finally {
                        try { try { x = 10 / (x - 3); } finally { x = 7; } }
                        catch (System.DivideByZeroException) { x = 9; }
                    }
                } }
                """,
            "nested-body-filtered" => Source("nested-body-caught").Replace("catch (System.DivideByZeroException)",
                "catch (System.DivideByZeroException) when (x == 3)", StringComparison.Ordinal),
            "nested-body-nonreturn" => """
                using SharpProof.Attributes;
                public static class Subject { public static int Target(int x) {
                    Contract.Requires(x == 0);
                    Contract.Ensures(Contract.Result<int>() == 3 && x == 3);
                    Contract.Ensures(Contract.Result<int>() == 0);
                    try { x++; } finally { try { x++; } finally { x++; } }
                    return x;
                } }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    [TestCase("search")]
    [TestCase("filter-fault")]
    [TestCase("nested-return")]
    [TestCase("replacement")]
    [TestCase("filter-throw")]
    [TestCase("assume")]
    [TestCase("nested-body-normal")]
    [TestCase("nested-body-caught")]
    [TestCase("nested-body-filtered")]
    [TestCase("nested-body-nonreturn")]
    public async Task NativeDecodedSearchAndUnwindPreserveProofReplayAndCache(string kind)
    {
        using var project = new ShadowTestProject(Source(kind), cacheEnabled: true);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(preparation.Total, Is.Not.Null, kind);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out var failure),
            Is.True, kind + " " + failure);
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), kind + " " + refuted.Reason);
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(preparation.Total!.Parameters.Select(parameter => parameter.Entry)));
        var replay = new IrProgramInterpreter(preparation.Total.Program.Factory).Execute(preparation.Total.Program, refuted.EntryModel);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        using var environment = new ShadowEnvironment("shadow");
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        for (var invocation = 0; invocation < 2; invocation++)
        {
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
            if (invocation == 1)
            { Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit)); }
            Assert.That(report!.Checked, Is.EqualTo(2));
            Assert.That(report.NewProven, Is.EqualTo(1));
            Assert.That(report.Rows.Select(row => row.NewOutcome), Does.Contain(WorkerClaimOutcome.Refuted));
            Assert.That(report.SoundnessDisagreements, Is.Zero);
            Assert.That(report.NewConditional, Is.EqualTo(kind == "assume" ? 1 : 0));
            if (kind == "assume")
            { Assert.That(report.Rows.Single(row => row.NewOutcome == WorkerClaimOutcome.Proven).NewAssumptions.Any(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume && assumption.Used), Is.True); }
        }
    }

    [TestCase("search")]
    [TestCase("filter-fault")]
    [TestCase("nested-return")]
    [TestCase("replacement")]
    public void OptionalRegionEvidencePreservesLegacyGraphAndRows(string kind)
    {
        var compilation = TestCompilation.Create("RegionLegacyPreservation", ("Subject.cs", Source(kind)));
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();
        var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(target);
        Assert.That(preparation.Total, Is.Not.Null);
        var withTotal = CompilerLoweredArtifact.Encode(preparation);
        var legacy = CompilerLoweredArtifact.Encode(preparation with { Total = null });
        withTotal.Total = null;
        Assert.That(JsonSerializer.Serialize(withTotal, WorkerProtocolJson.SharedOptions),
            Is.EqualTo(JsonSerializer.Serialize(legacy, WorkerProtocolJson.SharedOptions)));
    }

    [TestCase("naked-exit")]
    [TestCase("result-role")]
    [TestCase("throw-target")]
    public void MalformedRegionArtifactRejectsInsteadOfDroppingTotal(string mutation)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(SearchSource);
        var total = artifact.Callables.Single().Total!;
        var graph = total.Graph;
        if (mutation == "naked-exit")
        { graph.Entry = Array.FindIndex(graph.Blocks, block => block.Instructions.Last().Kind == IrInstructionKind.ExceptionalExit); }
        else if (mutation == "result-role")
        { total.Result = total.Parameters[0].Old; }
        else
        { graph.Blocks.SelectMany(block => block.Instructions).First(instruction => instruction.Kind == IrInstructionKind.Throw).B = graph.Blocks.Length; }
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _)));
    }

    [Test]
    public async Task HistoricalNestedFinallyPostStateDiffersFromCapturedReturn()
    {
        var fixture = GoldenTest.Load("worker", "vc-shadow-regions");
        using var project = new ShadowTestProject(fixture.Source, cacheEnabled: true);
        using var environment = new ShadowEnvironment("shadow");
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        for (var invocation = 0; invocation < 2; invocation++)
        {
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
            if (invocation == 1)
            { Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit)); }
            var claim = response.Manifest.Claims.Single(claim => claim.CallableId.Contains("GNestedFinallyClosed", StringComparison.Ordinal));
            var row = report!.Rows.Single(row => row.ClaimId == claim.ClaimId);
            Assert.That(row.NewOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(row.NewReason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(row.NewVacuity, Is.EqualTo(WorkerVacuityKind.None));
            Assert.That(row.Feasibility, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
            await TestContext.Out.WriteLineAsync($"historical nested finally: cache={response.Summary.CacheStatus} total={row.TotalPresent} enrolled={report.Enrolled} checked={report.Checked} unknown={report.Unknown} new={row.NewOutcome} newProven={report.NewProven} gains={report.PrecisionGains} coverage={report.CoverageComplete}");
        }
    }

    [Test]
    public async Task FaultingFilterKeepsOriginalUncaughtSiteAcrossTwoFinallyRegions()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Requires(x == 0); Contract.Ensures(false);
                try { try {
                    try { return 10 / x; }
                    catch (System.DivideByZeroException) when ((x = 7) / (x - 7) > 0) { return 1; }
                } finally { x = 9; } } finally { x = 11; }
            } }
            """;
        using var project = new ShadowTestProject(source);
        var preparation = project.Snapshot.Callables.Single();
        var total = preparation.Total!;
        Assert.That(total, Is.Not.Null);
        var factory = total.Program.Factory;
        var input = total.Parameters.Single().Entry;
        var replay = new IrProgramInterpreter(factory).Execute(total.Program,
            new Dictionary<IrVarId, IrValue> { [input] = factory.CreateIntegerValue(factory.GetVariableInfo(input).Type, 0) });
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        var span = factory.GetOperationInfo(replay.Instruction!.Operation).SourceSpan!;
        Assert.That(source.Substring(span.Start, span.Length), Is.EqualTo("10 / x"));
        Assert.That(replay.GetCurrentValue(total.Parameters.Single().Current)!.IntegerNumericValue,
            Is.EqualTo(new System.Numerics.BigInteger(11)));
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out var failure),
            Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.NoModeledNormalReturn));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }
}
