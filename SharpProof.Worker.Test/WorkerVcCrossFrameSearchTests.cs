using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcCrossFrameSearchTests
{
    internal const string ReplacementSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 0);
                Contract.Ensures(Contract.Result<int>() == x);
                Contract.Ensures(Contract.Result<int>() == 0);
                try { return Callee(x); }
                catch (System.DivideByZeroException) when (++x > 100) { return 7; }
                catch (System.DivideByZeroException) when (++x > 0) { return x; }
                catch (System.OverflowException) when (++x > 0) { return x; }
            }
            private static int Callee(int value) {
                try { return 10 / value; }
                finally { value = checked((byte)(value + 256)); }
            }
        }
        """;

    internal const string FilterFaultSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 0);
                Contract.Ensures(Contract.Result<int>() == 1 && x == 1);
                Contract.Ensures(Contract.Result<int>() == 0);
                try { return 10 / x; }
                catch (System.DivideByZeroException) when (++x > 0 && Filter(x - 1)) { return 7; }
                catch (System.DivideByZeroException) { return x; }
            }
            private static bool Filter(int value) {
                try { return More(value); } finally { value++; }
            }
            private static bool More(int value) {
                try { return value > 0; } finally { value = checked((byte)(value + 256)); }
            }
        }
        """;

    internal const string CapturedReturnSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 3);
                Contract.Ensures(Contract.Result<int>() == Contract.Old(x) && x == unchecked(Contract.Old(x) + 10));
                Contract.Ensures(Contract.Result<int>() == x);
                try { return Callee(x); }
                catch (System.DivideByZeroException) when (++x > 0) { return x; }
                finally { x += 10; }
            }
            private static int Callee(int value) {
                try { return value; } finally { value += 100; }
            }
        }
        """;

    internal const string OwnFilterFaultSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 0);
                Contract.Ensures(Contract.Result<int>() == 2 && x == 0);
                Contract.Ensures(Contract.Result<int>() == 7);
                try { return Callee(x); }
                catch (System.DivideByZeroException) when (++x > 0) { return 7; }
            }
            private static int Callee(int value) {
                try { return 10 / value; }
                catch (System.DivideByZeroException) when (Filter(value)) { return 5; }
                catch (System.DivideByZeroException) { return 2; }
                finally { value++; }
            }
            private static bool Filter(int value) {
                try { return true; } finally { value = 10 / value; }
            }
        }
        """;

    internal const string RepeatedFilterSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 0);
                Contract.Ensures(Contract.Result<int>() == x);
                Contract.Ensures(Contract.Result<int>() == 0);
                try { return Callee(x); }
                catch (System.DivideByZeroException) when (++x > 0) { return x; }
            }
            private static int Callee(int value) {
                try { return 10 / value; } finally { value = 10 / value; }
            }
        }
        """;

    [Test]
    public async Task ReplacementSearchAbstractionAndBoundedReplayRetainSeparateAuthority()
    {
        using var project = new ShadowTestProject(ReplacementSource);
        var target = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("Target", StringComparison.Ordinal));
        var candidate = PassiveCallableArtifactAdapter.Enroll(target)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var session = new CallableSolverSession(candidate.Factory, new IrSmtBackendOptions(WorkerBudgets.DefaultQueryRlimit));
        var kernel = new ProofKernel(session);
        var abstractOutcome = await kernel.VerifyAsync(plan!.EnsuresQuery(0));
        var abstractCost = session.ConsumedResourceCount;
        var boundedOutcome = await kernel.VerifyCallableAsync(plan.LoopSearch!.EnsuresQuery(0), plan.LoopSearch.Replay(0));
        Assert.That(abstractOutcome, Is.TypeOf<RefutedOutcome>(), "An abstract model cannot refute the owned original.");
        Assert.That(boundedOutcome, Is.TypeOf<ProvenOutcome>(), "Bounded UNSAT cannot prove the owned cyclic original.");
        await TestContext.Out.WriteLineAsync($"cross-frame replacement trace: abstract={abstractOutcome.GetType().Name} resources={abstractCost}; bounded={boundedOutcome.GetType().Name} resources={session.ConsumedResourceCount - abstractCost}");
    }

    [TestCase(ReplacementSource, 3, 3, true)]
    [TestCase(FilterFaultSource, 1, 1, false)]
    [TestCase(CapturedReturnSource, 3, 13, false)]
    [TestCase(OwnFilterFaultSource, 2, 0, false)]
    [TestCase(RepeatedFilterSource, 2, 2, false)]
    public async Task OwnedSearchAndUnwindProveRefuteReplayAndCache(string source, int expected, int current, bool incomplete)
    {
        using var project = new ShadowTestProject(source, cacheEnabled: true);
        var target = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("Target", StringComparison.Ordinal));
        Assert.That(target.Total, Is.Not.Null);
        var candidate = PassiveCallableArtifactAdapter.Enroll(target)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var proven = await solver.VerifyEnsuresAsync(0);
        if (incomplete)
        {
            Assert.That(proven.Outcome, Is.Null);
            Assert.That(proven.Reason, Is.EqualTo(WorkerClaimReason.SolverIncomplete));
            Assert.That(proven.QueryCompleted, Is.True);
        }
        else
        { Assert.That(proven.Outcome, Is.TypeOf<ProvenOutcome>(), proven.Reason.ToString()); }
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), refuted.Reason.ToString());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(target.Total!.Parameters.Select(parameter => parameter.Entry)));
        var execution = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, refuted.EntryModel);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
        Assert.That(execution.GetCurrentValue(target.Total.Parameters[0].Current)!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(current)));
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        for (var invocation = 0; invocation < 2; invocation++)
        {
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
            if (invocation == 1)
            { Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit)); }
            Assert.That(response.Errors, Is.Empty);
            var ordinals = response.Manifest.Claims.Where(claim => claim.CallableId == target.Entry.CallableId && claim.Kind == WorkerClaimKind.Postcondition)
                .ToDictionary(claim => claim.ClaimId, claim => claim.Ordinal, StringComparer.Ordinal);
            var posts = response.ClaimResults.Where(result => ordinals.ContainsKey(result.ClaimId)).OrderBy(result => ordinals[result.ClaimId]).ToArray();
            Assert.That(posts.Select(result => result.Outcome),
                Is.EqualTo(new[] { incomplete ? WorkerClaimOutcome.Unknown : WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted }));
            Assert.That(posts.Select(result => result.Reason),
                Is.EqualTo(new[] { incomplete ? WorkerClaimReason.SolverIncomplete : WorkerClaimReason.None, WorkerClaimReason.None }));
            Assert.That(posts[1].Model, Is.Not.Empty);
        }
    }
}
