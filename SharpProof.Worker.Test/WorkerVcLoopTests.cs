using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcLoopTests
{
    internal const string AssumeLoopSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Assume(x >= 0); Contract.Ensures(Contract.Result<int>() == x);
            while (x < 3) x++; return x;
        } }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public void CyclicArtifactCannotReenterTheSourcePrologue(bool initialization)
    { Assert.That(PrologueReentryRejected(initialization), Is.True); }

    internal static bool PrologueReentryRejected(bool initialization, string source = AssumeLoopSource)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var total = preparations.Single().Total!;
        var builder = new IrProgramBuilder(total.Program.Factory);
        var blocks = total.Program.Blocks.ToDictionary(block => block.Id, block => builder.CreateBlock());
        var pointBlock = total.Program.Blocks.Single(block => block.Instructions.Any(instruction => instruction is IrAssumeInstruction));
        builder.SetEntry(blocks[total.Program.Entry]);
        foreach (var block in total.Program.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                var destination = blocks[block.Id];
                switch (instruction)
                {
                    case IrAssignInstruction assign:
                        builder.Assign(destination, assign.Operation, assign.Target, assign.Value);
                        break;
                    case IrAssumeInstruction assume:
                        builder.Assume(destination, assume.Operation, assume.Condition);
                        break;
                    case IrGotoInstruction go:
                        builder.Goto(destination, go.Operation, blocks[go.Target]);
                        break;
                    case IrBranchInstruction branch:
                        builder.Branch(destination, branch.Operation, branch.Condition, blocks[branch.WhenTrue], blocks[branch.WhenFalse]);
                        break;
                    case IrExceptionalExitInstruction exit:
                        builder.ExceptionalExit(destination, exit.Operation);
                        break;
                    case IrReturnInstruction returned:
                        var exitBlock = builder.CreateBlock();
                        builder.Return(exitBlock, returned.Operation, returned.Value);
                        builder.Branch(destination, returned.Operation, total.Program.Factory.Boolean(false),
                            blocks[initialization ? total.Program.Entry : pointBlock.Id], exitBlock);
                        break;
                    default:
                        Assert.Fail("Unexpected loop fixture instruction.");
                        break;
                }
            }
        }
        artifact.Callables.Single().Total = CompilerTotalCallableArtifactCodec.Encode(total with { Program = builder.Build() });
        try
        {
            CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
            return false;
        }
        catch (System.Text.Json.JsonException) { return true; }
    }

    internal const string LoopSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Ensures(Contract.Result<int>() == x);
            Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
            while (x < 3) x++; return x;
        } }
        """;

    internal const string BeyondSearchSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Requires(x == 0);
            Contract.Ensures(Contract.Result<int>() == x);
            Contract.Ensures(Contract.Result<int>() == 0);
            while (x < 6) x++; return x;
        } }
        """;

    [TestCase("zero-trip")]
    [TestCase("do-entry-mutation")]
    [TestCase("continue-break")]
    [TestCase("nested-shared")]
    [TestCase("ulong-wrap")]
    [TestCase("long-wrap")]
    public async Task SourceLoopNativeAndCachePreserveOriginalWitness(string kind)
    {
        var (type, requirement, body) = kind switch
        {
            "zero-trip" => ("int", "x == 8", "while (x < 3) x++; return x;"),
            "do-entry-mutation" => ("int", "x == 0", "do { x++; } while (x < 3); return x;"),
            "continue-break" => ("int", "x == 0", "while (x < 3) { x++; if (x == 1) continue; break; } return x;"),
            "nested-shared" => ("int", "x == 0", "for (int i = 0; i < 1; i++) { for (int j = 0; j < 2; j++) x++; } return x;"),
            "ulong-wrap" => ("ulong", "x == ulong.MaxValue", "do { unchecked { x++; } } while (x != 0UL); return x;"),
            _ => ("long", "x == long.MaxValue", "do { unchecked { x++; } } while (x > 0L); return x;")
        };
        var falseGoal = kind == "zero-trip" ? "Contract.Result<int>() == 0" : $"Contract.Result<{type}>() == Contract.Old(x)";
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject { public static {{type}} Target({{type}} x) {
                Contract.Requires({{requirement}});
                Contract.Ensures(Contract.Result<{{type}}>() == x);
                Contract.Ensures({{falseGoal}});
                {{body}}
            } }
            """, cacheEnabled: true);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(preparation.Total, Is.Not.Null, kind);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out var failure), Is.True, failure.ToString());
        Assert.That(plan!.LoopSearch, Is.Not.Null);
        Assert.Throws<ArgumentException>(new Action(() => new PassiveCallableSolver(plan.LoopSearch!)));
        Assert.That(plan.EnsuresQuery(0).Assumptions.All(assumption => assumption.Justification is not UserAssumedJustification), Is.True);
        using var solver = new PassiveCallableSolver(plan);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible), kind);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>(), kind);
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), kind + ": " + refuted.Reason);
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(preparation.Total!.Parameters.Select(parameter => parameter.Entry)));
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker();
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        for (var invocation = 0; invocation < 2; invocation++)
        {
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
            if (invocation == 1)
            { Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit)); }
            Assert.That(report!.Checked, Is.EqualTo(2), kind);
            Assert.That(report.NewProven, Is.EqualTo(1), kind);
            Assert.That(report.Rows.Select(row => row.NewOutcome), Does.Contain(WorkerClaimOutcome.Refuted));
            Assert.That(report.NewConditional, Is.Zero);
            Assert.That(report.SoundnessDisagreements, Is.Zero);
        }
    }

    [Test]
    public async Task FiniteReturnBeyondSearchCannotProveFalseOrInventVacuity()
    {
        using var project = new ShadowTestProject(BeyondSearchSource);
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker();
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        var proven = report!.Rows.Single(row => row.NewOutcome == WorkerClaimOutcome.Proven);
        Assert.That(proven.Feasibility, Is.EqualTo(PassiveCallableFeasibilityKind.Unknown));
        Assert.That(proven.Checked, Is.True);
        Assert.That(proven.NewVacuity, Is.EqualTo(WorkerVacuityKind.None));
        var unknown = report.Rows.Single(row => row.NewOutcome == WorkerClaimOutcome.Unknown);
        Assert.That(unknown.NewReason, Is.EqualTo(WorkerClaimReason.SolverIncomplete));
        Assert.That(unknown.Checked, Is.True);
        Assert.That(unknown.NewVacuity, Is.EqualTo(WorkerVacuityKind.None));
        Assert.That(report.CoverageComplete, Is.True, "This finite fixture is fully checked; bounded Unknown is not a full Phase 2 exit.");
    }

    [TestCase("assume", WorkerVacuityKind.NoModeledNormalReturn, true)]
    [TestCase("entry", WorkerVacuityKind.ContradictoryPreconditions, false)]
    [TestCase("infinite", WorkerVacuityKind.NoModeledNormalReturn, false)]
    public async Task AbstractNormalVacuityRetainsItsOriginalProvenance(string kind, WorkerVacuityKind expected, bool conditional)
    {
        var prologue = kind == "assume" ? "Contract.Assume(false);" : kind == "entry" ? "Contract.Requires(x > 0); Contract.Requires(x < 0);" : "";
        var body = kind == "infinite" ? "while (true) { x++; }" : "while (x < 3) x++; return x;";
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                {{prologue}} Contract.Ensures(false); {{body}}
            } }
            """);
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker();
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(report!.Rows.Single().NewOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(report.Rows.Single().NewVacuity, Is.EqualTo(expected));
        Assert.That(report.Rows.Single().NewConditional, Is.EqualTo(conditional));
    }

    [Test]
    public async Task ActualSourceLoopProvesCutAndRefutesOnlyOriginalReplay()
    {
        using var project = new ShadowTestProject(LoopSource);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out var failure), Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), refuted.Reason.ToString());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(preparation.Total!.Parameters.Select(parameter => parameter.Entry)));
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker();
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(report!.Postconditions, Is.EqualTo(2));
        Assert.That(report.Unenrolled, Is.Zero);
        Assert.That(report.Checked, Is.EqualTo(2));
        Assert.That(report.NewProven, Is.EqualTo(1));
        Assert.That(report.Rows.Select(row => row.NewOutcome), Does.Contain(WorkerClaimOutcome.Refuted));
    }
}
