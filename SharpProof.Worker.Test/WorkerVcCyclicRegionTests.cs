using NUnit.Framework;
using SharpProof.Verify;
using SharpProof.Smt;
using SharpProof.Worker.Protocol;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Contracts;
using SharpProof.Ir;
using System.Text.Json;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcCyclicRegionTests
{
    internal const string RepeatedFilterSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 0);
            Contract.Ensures(Contract.Result<int>() == 1);
            Contract.Ensures(Contract.Result<int>() == 0);
            try { try { return 10 / x; } finally { x = 10 / x; } }
            catch (System.Exception) when (x == 0) { return 1; }
        } }
        """;

    internal const string CaughtLoopSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 0);
            Contract.Ensures(Contract.Result<int>() == x);
            Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
            int i = 0;
            while (i < 2) {
                try { x = 10 / x; }
                catch (System.DivideByZeroException) { x = 1; }
                finally { i++; }
            }
            return x;
        } }
        """;

    internal const string CapturedLoopSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 0); Contract.Ensures(Contract.Result<int>() == x);
            try {
                for (int i = 0; i < 2; i++) {
                    try { x = checked(x + 1); } finally { x = checked(x + 1); }
                }
                return x;
            } finally { x = checked(x + 10); }
        } }
        """;

    internal const string GuardedRethrowSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 0); Contract.Ensures(false);
            try { return 10 / x; }
            catch (System.DivideByZeroException) { if (x != 0) throw; return 1; }
        } }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task GuardedRethrowKeepsItsNormalCompletionAndConcreteRefutation(bool feasibility)
    {
        using var project = new ShadowTestProject(GuardedRethrowSource);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(project.Snapshot.Callables.Single())!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        if (feasibility)
        { Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible)); }
        else
        { Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<RefutedOutcome>()); }
    }

    [Test]
    public async Task CyclicFinallyCapturedReturnRefutesUsingOriginalPostState()
    {
        using var project = new ShadowTestProject(CapturedLoopSource);
        var preparation = project.Snapshot.Callables.Single();
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        var execution = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, result.EntryModel);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(4)));
        Assert.That(execution.GetCurrentValue(candidate.Parameters[0].Current)!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(14)));
    }

    internal static string ConditionalLoopSource => CaughtLoopSource
        .Replace("Contract.Requires(x == 0)", "Contract.Assume(x == 0)", StringComparison.Ordinal)
        .Replace("Contract.Result<int>() == x", "Contract.Result<int>() == x && Contract.Old(x) == 0", StringComparison.Ordinal);

    [TestCase(false)]
    [TestCase(true)]
    public async Task PrologueAssumeKeepsConditionalProofAndVacuityOutsideCyclicBody(bool contradictory)
    {
        using var project = new ShadowTestProject(contradictory ? ConditionalLoopSource.Replace("Contract.Assume(x == 0)", "Contract.Assume(false)", StringComparison.Ordinal) : ConditionalLoopSource);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(project.Snapshot.Callables.Single())!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var feasibility = await solver.VerifyFeasibilityAsync();
        Assert.That(feasibility.Kind, Is.EqualTo(contradictory ? PassiveCallableFeasibilityKind.NoModeledNormalReturn : PassiveCallableFeasibilityKind.Feasible));
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(result.BodyAssumptions, Has.Length.EqualTo(1));
        Assert.That(plan!.EntryQuery().Assumptions, Is.Empty);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        Assert.That(response.Errors, Is.Empty);
        var claimId = response.Manifest.Claims.Single(claim => claim.Kind == WorkerClaimKind.Postcondition && claim.Ordinal == 0).ClaimId;
        var claim = response.ClaimResults.Single(result => result.ClaimId == claimId);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(claim.Vacuity, Is.EqualTo(contradictory ? WorkerVacuityKind.NoModeledNormalReturn : WorkerVacuityKind.None));
        Assert.That(claim.Assumptions.Any(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume && assumption.Used), Is.True);
    }

    [Test]
    public async Task ExceptionLoopBeyondExactSearchKeepsSoundProofAndCompletedUnknown()
    {
        using var project = new ShadowTestProject(CaughtLoopSource.Replace("i < 2", "i < 8", StringComparison.Ordinal));
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(project.Snapshot.Callables.Single())!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Unknown));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var result = await solver.VerifyEnsuresAsync(1);
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.SolverIncomplete));
        Assert.That(result.QueryCompleted, Is.True);
        Assert.That(result.EntryModel, Is.Empty);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var before = solver.ConsumedResourceCount;
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () => await solver.VerifyEnsuresAsync(0, cancellation.Token)));
        Assert.That(solver.ConsumedResourceCount, Is.EqualTo(before));
    }

    [Test]
    public void CyclicArtifactStillRejectsExceptionalExitsWithoutPendingThrow()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(RepeatedFilterSource);
        var total = artifact.Callables.Single().Total!;
        var throws = total.Graph.Blocks.SelectMany(block => block.Instructions).Where(instruction => instruction.Kind == IrInstructionKind.Throw).ToArray();
        Assert.That(throws, Is.Not.Empty);
        foreach (var instruction in throws)
        {
            instruction.Kind = IrInstructionKind.Goto;
            instruction.A = instruction.B;
            instruction.B = -1;
        }
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OptionalCyclicEvidenceKeepsEveryLegacyByteAndFailureRow(bool repeated)
    {
        var compilation = TestCompilation.Create("CyclicLegacyPreservation", ("Subject.cs", repeated ? RepeatedFilterSource : CaughtLoopSource));
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();
        var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(target);
        Assert.That(preparation.Total, Is.Not.Null);
        var withTotal = CompilerLoweredArtifact.Encode(preparation);
        var legacy = CompilerLoweredArtifact.Encode(preparation with { Total = null });
        withTotal.Total = null;
        Assert.That(JsonSerializer.Serialize(withTotal, WorkerProtocolJson.SharedOptions), Is.EqualTo(JsonSerializer.Serialize(legacy, WorkerProtocolJson.SharedOptions)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SourceCyclicRegionUsesOwnedReplayForProofAndRefutation(bool repeated)
    {
        using var project = new ShadowTestProject(repeated ? RepeatedFilterSource : CaughtLoopSource, cacheEnabled: true);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out var reason),
            Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var proven = await solver.VerifyEnsuresAsync(0);
        if (repeated)
        {
            Assert.That(proven.Outcome, Is.Null);
            Assert.That(proven.Reason, Is.EqualTo(WorkerClaimReason.SolverIncomplete));
            Assert.That(proven.QueryCompleted, Is.True);
        }
        else
        { Assert.That(proven.Outcome, Is.TypeOf<ProvenOutcome>(), proven.Reason.ToString()); }
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), refuted.Reason.ToString());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(preparation.Total!.Parameters.Select(parameter => parameter.Entry)));
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var first = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        var cached = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(WorkerProtocolJson.Validate(first, project.Bind().InputHash, first.Manifest).IsValid, Is.True);
        Assert.That(WorkerProtocolJson.Validate(cached, project.Bind().InputHash, cached.Manifest).IsValid, Is.True);
        Assert.That(cached.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(first.Errors, Is.Empty);
        Assert.That(cached.Errors, Is.Empty);
        var ordinals = cached.Manifest.Claims.Where(claim => claim.Kind == WorkerClaimKind.Postcondition)
            .ToDictionary(claim => claim.ClaimId, claim => claim.Ordinal, StringComparer.Ordinal);
        var posts = cached.ClaimResults.Where(result => ordinals.ContainsKey(result.ClaimId)).OrderBy(result => ordinals[result.ClaimId]).ToArray();
        Assert.That(posts.Select(result => result.Outcome),
            Is.EqualTo(new[] { repeated ? WorkerClaimOutcome.Unknown : WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted }));
        Assert.That(posts[1].Model, Is.Not.Empty);
        Assert.That(JsonSerializer.Serialize(cached.ClaimResults, WorkerProtocolJson.SharedOptions),
            Is.EqualTo(JsonSerializer.Serialize(first.ClaimResults, WorkerProtocolJson.SharedOptions)));
    }

    [Test]
    public async Task RepeatedFilterAbstractionAndExactSearchKeepSeparateAuthority()
    {
        using var project = new ShadowTestProject(RepeatedFilterSource);
        var candidate = PassiveCallableArtifactAdapter.Enroll(project.Snapshot.Callables.Single())!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var session = new CallableSolverSession(candidate.Factory, new IrSmtBackendOptions(WorkerBudgets.DefaultQueryRlimit));
        var kernel = new ProofKernel(session);
        var abstractOutcome = await kernel.VerifyAsync(plan!.EnsuresQuery(0));
        var abstractCost = session.ConsumedResourceCount;
        var boundedOutcome = await kernel.VerifyCallableAsync(plan.LoopSearch!.EnsuresQuery(0), plan.LoopSearch.Replay(0));
        Assert.That(abstractOutcome, Is.TypeOf<RefutedOutcome>(), "This is a spurious abstract model, not original replay evidence.");
        Assert.That(boundedOutcome, Is.TypeOf<ProvenOutcome>(), "Only this finite search is UNSAT; it cannot prove the cyclic original.");
        await TestContext.Out.WriteLineAsync($"repeated-filter trace: abstract={abstractOutcome.GetType().Name} resources={abstractCost}; bounded={boundedOutcome.GetType().Name} resources={session.ConsumedResourceCount - abstractCost}");
    }

    [Test]
    public async Task HistoricalRepeatedFilterHasConcreteOriginalRefutationAndCache()
    {
        using var project = new ShadowTestProject(GoldenTest.Load("worker", "vc-shadow-exception-search").Source, cacheEnabled: true);
        var preparation = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("IRepeatedFilterClosed", StringComparison.Ordinal));
        Assert.That(preparation.Total, Is.Not.Null);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var refuted = await solver.VerifyEnsuresAsync(0);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), refuted.Reason.ToString());
        var execution = new SharpProof.Ir.IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, refuted.EntryModel);
        Assert.That(execution.Status, Is.EqualTo(SharpProof.Ir.IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.Not.EqualTo(System.Numerics.BigInteger.One));
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        for (var invocation = 0; invocation < 2; invocation++)
        {
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
            if (invocation == 1)
            { Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit)); }
            Assert.That(response.Errors, Is.Empty);
            var claimId = response.Manifest.Claims.Single(claim => claim.CallableId == preparation.Entry.CallableId && claim.Kind == WorkerClaimKind.Postcondition).ClaimId;
            var claim = response.ClaimResults.Single(result => result.ClaimId == claimId);
            Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(claim.Model, Is.Not.Empty);
        }
    }
}
