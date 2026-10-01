using NUnit.Framework;
using SharpProof.Verify;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;
using System.Text.Json;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcSourceCallTests
{
    internal const string ScalarCallSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 0);
                Contract.Ensures(Contract.Result<int>() == 1);
                Contract.Ensures(Contract.Result<int>() == 0);
                return Increment(x);
            }
            private static int Increment(int value) { return value + 1; }
        }
        """;

    internal const string ArgumentCaptureSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 3);
                Contract.Ensures(Contract.Result<int>() == 33 && x == 4);
                Contract.Ensures(Contract.Result<int>() == x * 11);
                return Pack(first: x, second: x++);
            }
            private static int Pack(int first, int second) { return first * 10 + second; }
        }
        """;

    internal const string FilterUnwindSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 0); Contract.Ensures(Contract.Result<int>() == 0);
                try { return Callee(x); }
                catch (System.DivideByZeroException) when (++x > 0) { return 7; }
                catch (System.OverflowException) { return x; }
            }
            private static int Callee(int value) {
                try { return 10 / value; } finally { value = checked((byte)(value + 256)); }
            }
        }
        """;

    internal const string LoopCallSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 3);
                Contract.Ensures(Contract.Result<int>() == x);
                Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
                for (int i = 0; i < 2; i++) x = Increment(x); return x;
            }
            private static int Increment(int value) { return value + 1; }
        }
        """;

    internal const string FullUlongCallSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static ulong Target(ulong x) {
                Contract.Requires(x == 18446744073709551615UL);
                Contract.Ensures(Contract.Result<ulong>() == 0UL);
                Contract.Ensures(Contract.Result<ulong>() == 1UL);
                return Add(x);
            }
            private static ulong Add(ulong value, ulong step = 1UL) { return unchecked(value + step); }
        }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task CrossFrameFilterFinallyEvidenceRefutesTheFalseOriginalPostcondition(bool transitive)
    {
        var source = transitive ? FilterUnwindSource.Replace("return Callee(x);", "return Forward(x);", StringComparison.Ordinal)
            .Replace("private static int Callee", "private static int Forward(int value) { return Callee(value); } private static int Callee", StringComparison.Ordinal)
            : FilterUnwindSource;
        using var project = new ShadowTestProject(source, cacheEnabled: true);
        var target = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("Target", StringComparison.Ordinal));
        Assert.That(target.Total, Is.Not.Null);
        var candidate = PassiveCallableArtifactAdapter.Enroll(target)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var refuted = await solver.VerifyEnsuresAsync(0);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), refuted.Reason.ToString());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(target.Total!.Parameters.Select(parameter => parameter.Entry)));
        var execution = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, refuted.EntryModel);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(1)));
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
            var row = report!.Rows.Single();
            Assert.That(row.TotalPresent, Is.True);
            Assert.That(row.Enrolled, Is.True);
            Assert.That(row.Checked, Is.True);
            Assert.That(row.NewOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(report.CoverageComplete, Is.True);
            Assert.That(report.SoundnessDisagreements, Is.Zero);
        }
    }

    [Test]
    public void AsyncVoidRootDoesNotEmitSynchronousTotalEvidence()
    {
        // Do not invoke this async-void method in the test process: its exception
        // is posted to the synchronization context after the call returns.
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { public static async void Target(int x) {
                Contract.Ensures(false); throw null!;
            } }
            """);
        Assert.That(project.Snapshot.Callables.Single().Total, Is.Null);
    }

    [Test]
    public async Task OrdinaryConditionalVoidReturnProvesItsActualCurrentState()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { public static void Target(int x) {
                Contract.Requires(x == 0); Contract.Ensures(x == 1);
                if (x++ == 0) return; x++;
            } }
            """);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SourceCallUsesOwnedFramesForNativeProofRefutationAndCache(bool capture)
    {
        using var project = new ShadowTestProject(capture ? ArgumentCaptureSource : ScalarCallSource, cacheEnabled: true);
        var target = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("Target", StringComparison.Ordinal));
        Assert.That(target.Total, Is.Not.Null);
        var candidate = PassiveCallableArtifactAdapter.Enroll(target)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), refuted.Reason.ToString());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(target.Total!.Parameters.Select(parameter => parameter.Entry)));
        var execution = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, refuted.EntryModel);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(capture ? 33 : 1)));
        Assert.That(execution.GetCurrentValue(target.Total.Parameters[0].Current)!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(capture ? 4 : 0)));
        Assert.That(candidate.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrCallInstruction>(), Is.Empty);
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
            var ordinals = response.Manifest.Claims.ToDictionary(claim => claim.ClaimId, claim => claim.Ordinal, StringComparer.Ordinal);
            Assert.That(report!.Postconditions, Is.EqualTo(2));
            Assert.That(report.Enrolled, Is.EqualTo(2));
            Assert.That(report.Checked, Is.EqualTo(2));
            Assert.That(report.Rows.OrderBy(row => ordinals[row.ClaimId]).Select(row => row.NewOutcome),
                Is.EqualTo(new[] { WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted }));
            Assert.That(report.SoundnessDisagreements, Is.Zero);
        }
    }

    [Test]
    public async Task CalleeRequiresIsNotAnUnprovenEntryPremise()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int x) {
                    Contract.Requires(x == 0); Contract.Ensures(false); return Callee(x);
                }
                private static int Callee(int value) { Contract.Requires(false); return value + 1; }
            }
            """);
        var target = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("Target", StringComparison.Ordinal));
        Assert.That(target.Total, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(target)!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<RefutedOutcome>());
    }

    [TestCase(LoopCallSource, 5UL)]
    [TestCase(FullUlongCallSource, 0UL)]
    public async Task InternalFrameRolesInLoopsAndFullUlongDefaultsPreserveOriginalReplay(string source, ulong expected)
    {
        using var project = new ShadowTestProject(source, cacheEnabled: true);
        var target = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("Target", StringComparison.Ordinal));
        var candidate = PassiveCallableArtifactAdapter.Enroll(target)!;
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), refuted.Reason.ToString());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(target.Total!.Parameters.Select(parameter => parameter.Entry)));
        var execution = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, refuted.EntryModel);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
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
            Assert.That(report.Unknown, Is.Zero);
            Assert.That(report.SoundnessDisagreements, Is.Zero);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnsupportedCalleeContractAndAsyncCompletionRemainClosed(bool asynchronous)
    {
        var callee = asynchronous ? "private static async void Callee(int value) { throw null!; }"
            : "private static int Callee(int value) { Contract.Assume(value == 0); return value + 1; }";
        var call = asynchronous ? "Callee(x); return x;" : "return Callee(x);";
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { public static int Target(int x) { Contract.Ensures(false); " + call + " } " + callee + " }");
        var target = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("Target", StringComparison.Ordinal));
        Assert.That(target.Total, Is.Null);
    }

    [TestCase(ScalarCallSource)]
    [TestCase(FilterUnwindSource)]
    public void OptionalSourceCallEvidencePreservesEveryLegacySerializedByte(string source)
    {
        var compilation = TestCompilation.Create("SourceCallLegacyPreservation", ("Subject.cs", source));
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single(value => value.Entry.CallableId.Contains("Target", StringComparison.Ordinal));
        var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(target);
        var withTotal = CompilerLoweredArtifact.Encode(preparation);
        var legacy = CompilerLoweredArtifact.Encode(preparation with { Total = null });
        withTotal.Total = null;
        Assert.That(JsonSerializer.Serialize(withTotal, WorkerProtocolJson.SharedOptions),
            Is.EqualTo(JsonSerializer.Serialize(legacy, WorkerProtocolJson.SharedOptions)));
    }
}
