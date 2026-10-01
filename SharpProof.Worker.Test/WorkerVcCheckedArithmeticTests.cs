using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcCheckedArithmeticTests
{
    internal const string CheckedReturnSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Ensures(Contract.Result<int>() == checked(Contract.Old(x) + 1));
            Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
            return checked(x + 1);
        } }
        """;

    internal const string CaughtEffectsSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 1073741824);
            Contract.Ensures(Contract.Result<int>() == 1073741825 && x == 7);
            Contract.Ensures(Contract.Result<int>() == 7);
            try { try { return checked(x += x++); } catch (System.OverflowException) { return x; } }
            finally { x = 7; }
        } }
        """;

    internal const string UnsafeClauseSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == int.MaxValue);
            Contract.Ensures(checked(x + 1) == unchecked(x + 1)); return x;
        } }
        """;

    internal const string FullUlongSource = """
        using SharpProof.Attributes;
        public static class Subject { public static ulong Target(ulong x) {
            Contract.Requires(x == ulong.MaxValue);
            Contract.Ensures(Contract.Result<ulong>() == 42UL && x == Contract.Old(x));
            Contract.Ensures(Contract.Result<ulong>() == 0UL);
            try { checked { x *= 2UL; } return 0UL; } catch (System.OverflowException) { return 42UL; }
        } }
        """;

    [TestCase("return")]
    [TestCase("caught-effects")]
    [TestCase("full-ulong")]
    [TestCase("narrow")]
    [TestCase("signed-multiply")]
    public async Task DecodedCheckedCandidateProvesAndReplaysFalseEnsuresWithCanonicalInputs(string kind)
    {
        var source = kind switch
        {
            "return" => CheckedReturnSource,
            "caught-effects" => CaughtEffectsSource,
            "full-ulong" => FullUlongSource,
            "signed-multiply" => """
                using SharpProof.Attributes;
                public static class Subject { public static long Target(long x) {
                    Contract.Requires(x == long.MinValue);
                    Contract.Ensures(Contract.Result<long>() == x);
                    Contract.Ensures(Contract.Result<long>() == 0L);
                    try { return checked(x * -1L); } catch (System.OverflowException) { return x; }
                } }
                """,
            _ => """
                using SharpProof.Attributes;
                public static class Subject { public static byte Target(byte x) {
                    Contract.Requires(x == byte.MaxValue);
                    Contract.Ensures(Contract.Result<byte>() == 0 && x == byte.MaxValue);
                    Contract.Ensures(Contract.Result<byte>() == 1);
                    try { checked { x++; } return x; } catch (System.OverflowException) { return 0; }
                } }
                """
        };
        using var project = new ShadowTestProject(source, cacheEnabled: true);
        var total = project.Snapshot.Callables.Single().Total;
        Assert.That(total, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(project.Snapshot.Callables.Single())!,
            out var plan, out var failure), Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var proven = await solver.VerifyEnsuresAsync(0);
        Assert.That(proven.Outcome, Is.TypeOf<ProvenOutcome>(), proven.Reason.ToString());
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), refuted.Reason.ToString());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(total!.Parameters.Select(parameter => parameter.Entry)));
        if (kind == "full-ulong")
        { Assert.That(refuted.EntryModel.Single().Value.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(ulong.MaxValue))); }
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker();
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        var first = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(WorkerProtocolJson.Validate(first, project.Bind().InputHash, first.Manifest).IsValid, Is.True);
        Assert.That(report!.Rows.Select(row => row.Checked), Is.All.True);
        var claimIds = total.Clauses.Where(clause => clause.Kind == SharpProof.CompilerArtifact.CompilerContractKind.Ensures).Select(clause => clause.ClaimId).ToArray();
        Assert.That(report.Rows.Single(row => row.ClaimId == claimIds[0]).NewOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(report.Rows.Single(row => row.ClaimId == claimIds[1]).NewOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        var hit = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(hit.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(report.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(report.Rows.Select(row => row.Checked), Is.All.True);
        Assert.That(report.Rows.Single(row => row.ClaimId == claimIds[1]).NewOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        await TestContext.Out.WriteLineAsync($"checked {kind}: resources={solver.ConsumedResourceCount} checked={report.Checked} proven={report.NewProven} unknown={report.Unknown}");
    }

    [Test]
    public async Task UnsafeCheckedClauseAbstainsWhileActualCheckedThrowHasNoNormalReturn()
    {
        using var environment = new ShadowEnvironment("shadow");
        using var unsafeProject = new ShadowTestProject(UnsafeClauseSource);
        using var unsafeWorker = unsafeProject.CreateLegacyWorker(unsafeProject.Request.Budgets);
        WorkerVcShadowReport? report = null;
        unsafeWorker.ShadowReportSink = value => report = value;
        await unsafeWorker.VerifyAsync(unsafeProject.Request, unsafeProject.Snapshot, CancellationToken.None);
        Assert.That(report!.Rows.Single().NewOutcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(report.Rows.Single().NewReason, Is.EqualTo(WorkerClaimReason.PostconditionMayBeUndefined));
        using var throwingProject = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Requires(x == int.MaxValue); Contract.Ensures(false); return checked(x + 1);
            } }
            """);
        using var throwingWorker = throwingProject.CreateLegacyWorker(throwingProject.Request.Budgets);
        throwingWorker.ShadowReportSink = value => report = value;
        await throwingWorker.VerifyAsync(throwingProject.Request, throwingProject.Snapshot, CancellationToken.None);
        Assert.That(report!.Rows.Single().NewOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(report.Rows.Single().NewVacuity, Is.EqualTo(WorkerVacuityKind.NoModeledNormalReturn));
    }
}
