using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcRegionTests
{
    internal const string CapturedReturnSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 3);
            Contract.Ensures(Contract.Result<int>() == Contract.Old(x) && x == 7);
            Contract.Ensures(Contract.Result<int>() == 7);
            try { return x; } finally { x = 7; }
        } }
        """;

    internal const string MixedFinallySource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 0 || x == 1);
            Contract.Ensures(Contract.Result<int>() == 7);
            try {
                try { if (x == 0) return 10 / x; x = 3; }
                finally { x = 7; }
            } catch (System.DivideByZeroException) { return x; }
            return x;
        } }
        """;

    internal const string NestedRethrowSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 0 || x == 1);
            Contract.Ensures(Contract.Result<int>() == 7);
            Contract.Ensures(Contract.Result<int>() == 9);
            try {
                try { if (x == 0) return 10 / x; return 20 / (x - 1); }
                catch (System.DivideByZeroException) {
                    try { x = checked((byte)(unchecked(x + 256))); }
                    catch (System.OverflowException) { }
                    throw;
                }
            } catch (System.DivideByZeroException) { return 7; }
            catch (System.OverflowException) { return 9; }
        } }
        """;

    internal const string AssumeRegionSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Assume(x == 0);
            Contract.Ensures(Contract.Result<int>() == 7 && x == 7 && Contract.Old(x) == 0);
            try { try { return 10 / x; } finally { x = 7; } }
            catch (System.DivideByZeroException) { return x; }
        } }
        """;

    internal const string AllThrowSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 0);
            Contract.Ensures(false);
            try { return 10 / x; } finally { x = 7; }
        } }
        """;

    internal const string LocalJoinSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int d) {
            Contract.Ensures(Contract.Result<int>() == 10 / Contract.Old(d));
            int y;
            try { y = 10 / d; } finally { d = 1; }
            return y;
        } }
        """;

    [TestCase("normal")]
    [TestCase("initialized")]
    [TestCase("goto")]
    [TestCase("assume")]
    public async Task NormalOnlyLocalAssignmentSurvivesExceptionalFinallyJoin(string kind)
    {
        var source = kind switch
        {
            "initialized" => LocalJoinSource.Replace("int y;", "int y = 0;", StringComparison.Ordinal),
            "goto" => LocalJoinSource.Replace("int y;", "int y; goto L; d = 2; L:", StringComparison.Ordinal),
            "assume" => LocalJoinSource.Replace("Contract.Ensures(", "Contract.Assume(d == 2); Contract.Ensures(Contract.Result<int>() == 5); Contract.Ensures(", StringComparison.Ordinal),
            _ => LocalJoinSource
        };
        using var project = new ShadowTestProject(source);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out var failure),
            Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var check = await solver.VerifyEnsuresAsync(0);
        Assert.That(check.Outcome, Is.TypeOf<ProvenOutcome>(), check.Reason.ToString());
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker();
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        Assert.That(report!.Rows.Select(row => row.NewOutcome), Is.All.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(report.Rows.Select(row => row.Checked), Is.All.True);
        if (kind == "assume")
        { Assert.That(report.Rows.Any(row => row.NewConditional && row.NewAssumptions.Any(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume && assumption.Used)), Is.True); }
    }

    [TestCase("captured-return")]
    [TestCase("mixed-finally")]
    [TestCase("nested-rethrow")]
    [TestCase("assume-region")]
    [TestCase("all-throw")]
    public async Task DecodedRegionCandidatePreservesNormalResultsReplayAndConditionalEvidence(string kind)
    {
        var source = kind switch
        {
            "captured-return" => CapturedReturnSource,
            "mixed-finally" => MixedFinallySource,
            "nested-rethrow" => NestedRethrowSource,
            "assume-region" => AssumeRegionSource,
            _ => AllThrowSource
        };
        using var project = new ShadowTestProject(source);
        var preparation = project.Snapshot.Callables.Single();
        var total = preparation.Total!;
        Assert.That(total, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out var failure),
            Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var feasibility = await solver.VerifyFeasibilityAsync();
        Assert.That(feasibility.Kind, Is.EqualTo(kind == "all-throw"
            ? PassiveCallableFeasibilityKind.NoModeledNormalReturn : PassiveCallableFeasibilityKind.Feasible));
        var proven = await solver.VerifyEnsuresAsync(0);
        Assert.That(proven.Outcome, Is.TypeOf<ProvenOutcome>(), proven.Reason.ToString());
        if (kind is "captured-return" or "nested-rethrow")
        {
            var refuted = await solver.VerifyEnsuresAsync(1);
            Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), refuted.Reason.ToString());
            Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(total.Parameters.Select(parameter => parameter.Entry)));
            Assert.That(refuted.EntryModel.Single().Value.IntegerNumericValue, kind == "captured-return"
                ? Is.EqualTo(new System.Numerics.BigInteger(3)) : Is.AnyOf(new System.Numerics.BigInteger(0), new System.Numerics.BigInteger(1)));
        }
        if (kind == "assume-region")
        {
            Assert.That(total.Program.GetBlock(total.Program.Entry).Instructions.OfType<IrAssignInstruction>().Select(assign => assign.Target),
                Is.EqualTo(total.Parameters.SelectMany(parameter => new[] { parameter.Current, parameter.Old })));
            var point = total.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssumeInstruction>().Single();
            Assert.That(proven.BodyAssumptions, Does.Contain(point.Operation));
        }
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker();
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        Assert.That(report!.Rows.Select(row => row.Checked), Is.All.True);
        var claims = total.Clauses.Where(clause => clause.Kind == SharpProof.CompilerArtifact.CompilerContractKind.Ensures).ToArray();
        var first = report.Rows.Single(row => row.ClaimId == claims[0].ClaimId);
        Assert.That(first.NewOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        if (kind is "captured-return" or "nested-rethrow")
        { Assert.That(report.Rows.Single(row => row.ClaimId == claims[1].ClaimId).NewOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted)); }
        if (kind == "assume-region")
        {
            Assert.That(first.NewConditional, Is.True);
            Assert.That(first.NewAssumptions.Single(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume).Used, Is.True);
        }
        Assert.That(first.NewVacuity, Is.EqualTo(kind == "all-throw"
            ? WorkerVacuityKind.NoModeledNormalReturn : WorkerVacuityKind.None));
        await TestContext.Out.WriteLineAsync($"region {kind}: enrolled={report.Enrolled} checked={report.Checked} proven={report.NewProven} vacuous={report.NewVacuous} conditional={report.NewConditional}");
    }
}
