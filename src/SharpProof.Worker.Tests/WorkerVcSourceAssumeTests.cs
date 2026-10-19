using System.Collections.Immutable;
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
        var clause = total.Clauses.Single(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Assume));
        var point = total.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrAssumeInstruction>().Single();
        Assert.That(point.Operation, Is.EqualTo(clause.Operation));
        Assert.That(point.Condition.Id, Is.EqualTo(total.Program.Factory.Binary(IrBinaryOperator.AndAlso, clause.Safe, clause.Value).Id));
        Assert.That(clause.AssumptionId, Is.EqualTo(assumption.Id));
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var result = Postcondition(response);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
        var used = result.Assumptions.Single();
        Assert.That((used.Id, used.Kind, used.Used),
            Is.EqualTo((assumption.Id, WorkerAssumptionKind.UserAssume, true)));
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
        System.ArgumentNullException.ThrowIfNull(kind);
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
        Assert.That(total.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).Any(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction is IrThrowInstruction)), Is.False,
            "Compiler-elided assumption arguments must not emit runtime faults.");
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out _, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(plan!.EntryQuery().Assumptions.All(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Justification is not UserAssumedJustification)), Is.True);
        using var solver = new PassiveCallableSolver(plan);
        Assert.That((await solver.VerifyEntryAsync(cancellationToken: TestContext.CurrentContext.CancellationToken)).Outcome, Is.TypeOf<RefutedOutcome>());
        var feasibility = await solver.VerifyFeasibilityAsync(cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(feasibility.Kind, Is.EqualTo(vacuity == WorkerVacuityKind.None
            ? PassiveCallableFeasibilityKind.Feasible : PassiveCallableFeasibilityKind.NoModeledNormalReturn));
        var ensures = await solver.VerifyEnsuresAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(ensures.Outcome, expected == WorkerClaimOutcome.Refuted ? Is.TypeOf<RefutedOutcome>() : Is.TypeOf<ProvenOutcome>());
        if (expected == WorkerClaimOutcome.Refuted)
        { Assert.That(ensures.EntryModel.Single().Value.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(7))); }
        else
        { Assert.That(ensures.BodyAssumptions, Is.Not.Empty); }
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var result = Postcondition(response);
        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(result.Vacuity, Is.EqualTo(vacuity));
        if (expected == WorkerClaimOutcome.Proven)
        {
            Assert.That(result.Assumptions.Any(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind == WorkerAssumptionKind.UserAssume && assumption.Used)), Is.True);
        }
        else
        { Assert.That(result.Model, Is.Not.Empty); }
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
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var result = Postcondition(response);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.ContradictoryPreconditions));
        Assert.That(result.Assumptions.Single(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind == WorkerAssumptionKind.UserAssume)).Used, Is.False);
    }

    private static WorkerClaimResult Postcondition(WorkerVerifyResponse response)
    {
        System.ArgumentNullException.ThrowIfNull(response);
        Assert.That(response.Errors, Is.Empty);
        var id = response.Manifest.Claims.Single(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.Kind == WorkerClaimKind.Postcondition)).ClaimId;
        return response.ClaimResults.Single(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.ClaimId == id));
    }

    [TestCase("x++; Contract.Assume(x > 0); return x;")]
    [TestCase("if (x > 0) Contract.Assume(x > 0); return x;")]
    [TestCase("Contract.Assume(System.Math.Abs(x) > 0); return x;")]
    [TestCase("Contract.Assume(x++ > 0); return x;")]
    [TestCase("Contract.Assume(Contract.Old(x) > 0); return x;")]
    public void UnsupportedPlacementAndConditionKeepTotalAdmissionClosed(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
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
        System.ArgumentNullException.ThrowIfNull(mutation);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Assume(x > 0); Contract.Assume(x < 10);
                Contract.Ensures(Contract.Result<int>() > 0); return x;
            } }
            """);
        var total = artifact.Callables.Single().Total!;
        var originalClauses = total.Clauses.Where(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Assume)).ToImmutableArray();
        var clauses = originalClauses;
        var points = total.Graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).Where(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Kind == IrInstructionKind.Assume)).ToImmutableArray();
        switch (mutation)
        {
            case "id":
                { clauses = clauses.SetItem(0, clauses[0] with { AssumptionId = "foreign" }); }
                break;
            case "swap-ids":
                {
                    var changedFixture = (clauses[1].AssumptionId, clauses[0].AssumptionId);
                    var clauseBuilder = clauses.ToBuilder();
                    clauseBuilder[0] = clauseBuilder[0] with { AssumptionId = changedFixture.Item1 };
                    clauseBuilder[1] = clauseBuilder[1] with { AssumptionId = changedFixture.Item2 };
                    clauses = clauseBuilder.ToImmutable();
                }
                break;
            case "site":
                { clauses = clauses.SetItem(0, clauses[0] with { Operation = clauses[1].Operation }); }
                break;
            case "predicate":
                {
                    total = total with { Graph = total.Graph.ReplaceRow(points[0], points[0] with { A = points[1].A }) };
                    artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = total }) };
                }
                break;
            case "missing-point":
                {
                    total = total with { Graph = total.Graph.ReplaceRow(points[0], points[0] with { Kind = IrInstructionKind.Assert }) };
                    artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = total }) };
                }
                break;
            case "duplicate-point":
                {
                    total = total with { Graph = total.Graph.ReplaceRow(points[1], points[1] with { Operation = points[0].Operation }) };
                    artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = total }) };
                }
                break;
            case "canonical-role":
                var current = total.Parameters[0].Current;
                foreach (var term in total.Graph.Terms.Where(term => term is null ? throw new System.ArgumentNullException(nameof(term)) : (term.Kind == IrTermKind.Variable && term.A == current)))
                {
                    {
                        total = total with { Graph = total.Graph.ReplaceRow(term, term with { A = total.Parameters[0].Entry }) };
                        artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = total }) };
                    }
                }
                break;
        }
        for (var index = 0; index < clauses.Length; index++)
        {
            total = total with { Clauses = total.Clauses.ReplaceReference(originalClauses[index], clauses[index]) };
        }
        artifact = artifact with
        {
            Callables = artifact.Callables.ReplaceReference(
                artifact.Callables.Single(), artifact.Callables.Single() with { Total = total })
        };
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _, cancellationToken: TestContext.CurrentContext.CancellationToken)));
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
        System.ArgumentNullException.ThrowIfNull(source);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var total = preparations.Single().Total!;
        var builder = new IrProgramBuilder(total.Program.Factory);
        var blocks = ImmutableDictionaryCapture.Create(
            total.Program.Blocks,
            block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Id),
            block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (builder.CreateBlock()));
        builder.SetEntry(blocks[total.Program.Entry]);
        var point = total.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrAssumeInstruction>().Single();
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
                    case IrAllocationInstruction allocation:
                        builder.Allocate(destination, allocation.Operation, allocation.AllocatedType, allocation.Target, allocation.Length, allocation.InitialValues);
                        break;
                    case IrLockInstruction synchronization:
                        builder.Lock(destination, synchronization.Operation, synchronization.Receiver);
                        break;
                    case IrWriteInstruction write:
                        builder.Write(destination, write.Operation, write.Region);
                        break;
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
        { artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = CompilerTotalCallableArtifactCodec.Encode(total with { Program = builder.Build() }) }) }; }
        // Re-encoding keeps the graph canonical: this must reject placement,
        // rather than relying on a stale operation or term index.
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        try
        {
            CompilerManifestArtifactJson.DeserializePrepared(json, out _, cancellationToken: TestContext.CurrentContext.CancellationToken);
            return false;
        }
        catch (JsonException)
        { return true; }
    }
}
