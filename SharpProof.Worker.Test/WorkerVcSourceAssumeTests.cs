using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcSourceAssumeTests
{
    internal const string Source = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x) {
            Contract.Ensures(Contract.Result<int>() > 0);
            Contract.Assume(x > 0);
            return x;
        } }
        """;

    [Test]
    public async Task SourceAssumeAdmissionRetainsItsManifestAndConditionalEvidence()
    {
        using var project = new ShadowTestProject(Source);
        var assumption = project.Snapshot.Callables.Single().Entry.Assumptions.Single();
        Assert.That(assumption.Kind, Is.EqualTo(WorkerAssumptionKind.UserAssume));
        var total = project.Snapshot.Callables.Single().Total!;
        Assert.That(total, Is.Not.Null);
        var clause = total.Clauses.Single(clause => clause.Kind == CompilerContractKind.Assume);
        var point = total.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssumeInstruction>().Single();
        Assert.That(point.Operation, Is.EqualTo(clause.Operation));
        Assert.That(point.Condition.Id, Is.EqualTo(total.Program.Factory.Binary(IrBinaryOperator.AndAlso, clause.Safe, clause.Value).Id));
        Assert.That(clause.AssumptionId, Is.EqualTo(assumption.Id));
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker();
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var row = report!.Rows.Single();
        Assert.That(row.OldOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(row.OldConditional, Is.True);
        Assert.That(row.OldAssumptions.Single().Used, Is.True);
        Assert.That(row.TotalPresent, Is.True);
        Assert.That(row.Checked, Is.True);
        Assert.That(row.NewOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(row.NewReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(row.NewConditional, Is.True);
        Assert.That(row.NewAssumptions.Single(), Is.EqualTo(new WorkerVcShadowAssumption(assumption.Id, WorkerAssumptionKind.UserAssume, true)));
        await TestContext.Out.WriteLineAsync($"source Assume: old={row.OldOutcome} conditional={row.OldConditional} total={row.TotalPresent} checked={row.Checked} new={row.NewOutcome}/{row.NewReason}");
    }

    [TestCase("mutation", WorkerClaimOutcome.Proven, WorkerVacuityKind.None)]
    [TestCase("wrong-return", WorkerClaimOutcome.Refuted, WorkerVacuityKind.None)]
    [TestCase("contradictory", WorkerClaimOutcome.Proven, WorkerVacuityKind.NoModeledNormalReturn)]
    [TestCase("undefined", WorkerClaimOutcome.Proven, WorkerVacuityKind.NoModeledNormalReturn)]
    [TestCase("lazy", WorkerClaimOutcome.Proven, WorkerVacuityKind.None)]
    [TestCase("zero-parameters", WorkerClaimOutcome.Proven, WorkerVacuityKind.NoModeledNormalReturn)]
    public async Task PointFilterKeepsEntryFeasibilityMutationAndOriginalReplay(string kind,
        WorkerClaimOutcome expected, WorkerVacuityKind vacuity)
    {
        var body = kind switch
        {
            "mutation" => "Contract.Assume(x == 7); Contract.Ensures(Contract.Result<int>() == 8 && Contract.Old(x) == 7); x++; return x;",
            "wrong-return" => "Contract.Assume(x == 7); Contract.Ensures(Contract.Result<int>() == 7); x = 0; return x;",
            "contradictory" => "Contract.Assume(x > 0); Contract.Assume(x < 0); Contract.Ensures(Contract.Result<int>() == 42); return x;",
            "undefined" => "Contract.Requires(x == 0); Contract.Assume(10 / x == 10 / x); Contract.Ensures(Contract.Result<int>() == 42); return x;",
            "zero-parameters" => "Contract.Assume(false); Contract.Ensures(Contract.Result<int>() == 42); return 0;",
            _ => "Contract.Assume(x == 0 || 10 / x > 0); Contract.Ensures(Contract.Result<int>() == 0 || 10 / Contract.Result<int>() > 0); return x;"
        };
        var signature = kind == "zero-parameters" ? "" : "int x";
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject { public static int Target({{signature}}) { {{body}} } }
            """);
        var preparation = project.Snapshot.Callables.Single();
        var total = preparation.Total!;
        Assert.That(total, Is.Not.Null);
        Assert.That(total.Program.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction is IrThrowInstruction), Is.False,
            "Compiler-elided assumption arguments must not emit runtime faults.");
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out _), Is.True);
        Assert.That(plan!.EntryQuery().Assumptions.All(assumption => assumption.Justification is not UserAssumedJustification), Is.True);
        using var solver = new PassiveCallableSolver(plan);
        Assert.That((await solver.VerifyEntryAsync()).Outcome, Is.TypeOf<RefutedOutcome>());
        var feasibility = await solver.VerifyFeasibilityAsync();
        Assert.That(feasibility.Kind, Is.EqualTo(vacuity == WorkerVacuityKind.None
            ? PassiveCallableFeasibilityKind.Feasible : PassiveCallableFeasibilityKind.NoModeledNormalReturn));
        var ensures = await solver.VerifyEnsuresAsync(0);
        Assert.That(ensures.Outcome, expected == WorkerClaimOutcome.Refuted ? Is.TypeOf<RefutedOutcome>() : Is.TypeOf<ProvenOutcome>());
        if (expected == WorkerClaimOutcome.Refuted)
        { Assert.That(ensures.EntryModel.Single().Value.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(7))); }
        else
        { Assert.That(ensures.BodyAssumptions, Is.Not.Empty); }
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker();
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var row = report!.Rows.Single();
        Assert.That(row.NewOutcome, Is.EqualTo(expected));
        Assert.That(row.NewVacuity, Is.EqualTo(vacuity));
        if (expected == WorkerClaimOutcome.Proven)
        {
            Assert.That(row.NewConditional, Is.True);
            Assert.That(row.NewAssumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume).Any(assumption => assumption.Used), Is.True);
        }
    }

    [Test]
    public async Task EntryContradictionDoesNotMarkADeclaredPointAssumptionUsed()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Requires(x > 0); Contract.Requires(x < 0); Contract.Assume(x == 7);
                Contract.Ensures(Contract.Result<int>() == 42); return x;
            } }
            """);
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker();
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var row = report!.Rows.Single();
        Assert.That(row.NewOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(row.NewVacuity, Is.EqualTo(WorkerVacuityKind.ContradictoryPreconditions));
        Assert.That(row.NewConditional, Is.False);
        Assert.That(row.NewAssumptions.Single(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume).Used, Is.False);
    }

    [TestCase("x++; Contract.Assume(x > 0); return x;")]
    [TestCase("if (x > 0) Contract.Assume(x > 0); return x;")]
    [TestCase("Contract.Assume(System.Math.Abs(x) > 0); return x;")]
    [TestCase("Contract.Assume(x++ > 0); return x;")]
    [TestCase("Contract.Assume(Contract.Old(x) > 0); return x;")]
    public void UnsupportedPlacementAndConditionKeepTotalAdmissionClosed(string body)
    {
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) { Contract.Ensures(true); {{body}} } }
            """);
        Assert.That(project.Snapshot.Callables.Single().Total, Is.Null);
    }

    [TestCase("id")]
    [TestCase("swap-ids")]
    [TestCase("site")]
    [TestCase("predicate")]
    [TestCase("missing-point")]
    [TestCase("duplicate-point")]
    [TestCase("canonical-role")]
    public void MalformedPointEvidenceRejectsTheWholeArtifact(string mutation)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Assume(x > 0); Contract.Assume(x < 10);
                Contract.Ensures(Contract.Result<int>() > 0); return x;
            } }
            """);
        var total = artifact.Callables.Single().Total!;
        var clauses = total.Clauses.Where(clause => clause.Kind == CompilerContractKind.Assume).ToArray();
        var points = total.Graph.Blocks.SelectMany(block => block.Instructions).Where(instruction => instruction.Kind == IrInstructionKind.Assume).ToArray();
        switch (mutation)
        {
            case "id":
                clauses[0].AssumptionId = "foreign";
                break;
            case "swap-ids":
                (clauses[0].AssumptionId, clauses[1].AssumptionId) = (clauses[1].AssumptionId, clauses[0].AssumptionId);
                break;
            case "site":
                clauses[0].Operation = clauses[1].Operation;
                break;
            case "predicate":
                points[0].A = points[1].A;
                break;
            case "missing-point":
                points[0].Kind = IrInstructionKind.Assert;
                break;
            case "duplicate-point":
                points[1].Operation = points[0].Operation;
                break;
            case "canonical-role":
                var current = total.Parameters[0].Current;
                foreach (var term in total.Graph.Terms.Where(term => term.Kind == IrTermKind.Variable && term.A == current))
                { term.A = total.Parameters[0].Entry; }
                break;
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CanonicalRelocatedAssumptionCannotMasqueradeAsASourcePrologue(bool unreachable)
    {
        Assert.That(RelocatedAssumptionRejected("""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Assume(x == 7); Contract.Ensures(Contract.Result<int>() == 7);
                x = 0; return x;
            } }
            """, unreachable), Is.True);
    }

    internal static bool RelocatedAssumptionRejected(string source, bool unreachable)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var total = preparations.Single().Total!;
        var builder = new IrProgramBuilder(total.Program.Factory);
        var blocks = total.Program.Blocks.ToDictionary(block => block.Id, block => builder.CreateBlock());
        builder.SetEntry(blocks[total.Program.Entry]);
        var point = total.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssumeInstruction>().Single();
        var moved = false;
        foreach (var block in total.Program.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                if (ReferenceEquals(instruction, point))
                { continue; }
                var destination = blocks[block.Id];
                switch (instruction)
                {
                    case IrAssignInstruction assign:
                        builder.Assign(destination, assign.Operation, assign.Target, assign.Value);
                        if (!unreachable && assign.Target == total.Parameters[0].Current &&
                            total.Program.Factory.GetOperationInfo(assign.Operation).SourceSpan != null)
                        {
                            builder.Assume(destination, point.Operation, point.Condition);
                            moved = true;
                        }
                        break;
                    case IrGotoInstruction go:
                        builder.Goto(destination, go.Operation, blocks[go.Target]);
                        break;
                    case IrReturnInstruction returned:
                        builder.Return(destination, returned.Operation, returned.Value);
                        break;
                    case IrExceptionalExitInstruction exit:
                        builder.ExceptionalExit(destination, exit.Operation);
                        break;
                    default:
                        Assert.Fail("Unexpected instruction in the relocation fixture.");
                        break;
                }
            }
        }
        if (unreachable)
        {
            var unused = builder.CreateBlock();
            builder.Assume(unused, point.Operation, point.Condition);
            builder.Return(unused, point.Operation, total.Program.Factory.Integer(0));
            moved = true;
        }
        Assert.That(moved, Is.True);
        artifact.Callables.Single().Total = CompilerTotalCallableArtifactCodec.Encode(total with { Program = builder.Build() });
        // Re-encoding keeps the graph canonical: this must reject placement,
        // rather than relying on a stale operation or term index.
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        try
        {
            CompilerManifestArtifactJson.DeserializePrepared(json, out _);
            return false;
        }
        catch (JsonException)
        { return true; }
    }
}
