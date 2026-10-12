using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task ActualCallPreconditionProofRetainsItsOwnedUsedSourceAssumption()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() > 0);
                    Contract.Assume(value > 0);
                    return Helper(value);
                }
                private static int Helper(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var preparation = preparations.Single(item => item.Total is { CallPreconditions.Length: 1 });
        Assert.That(preparation.IsSuccess, Is.True, preparation.FailureReason.ToString());
        var total = preparation.Total!;
        var assumption = preparation.Entry.Assumptions.Single(item => item.Kind == WorkerAssumptionKind.UserAssume);
        var clause = total.Clauses.Single(item => item.Kind == CompilerContractKind.Assume);
        var instructions = total.Program.Blocks.SelectMany(block => block.Instructions).ToArray();
        var point = instructions.OfType<IrAssumeInstruction>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(total.IsBodyAbstraction, Is.False);
            Assert.That(total.EffectsCompleteAtEntry, Is.True);
            Assert.That(instructions.OfType<IrHavocInstruction>().Any(instruction =>
                instruction.Origin == IrHavocOrigin.Approximation), Is.False);
            Assert.That(clause.AssumptionId, Is.EqualTo(assumption.Id));
            Assert.That(point.Operation, Is.EqualTo(clause.Operation));
            Assert.That(point.Condition.Id, Is.EqualTo(total.Program.Factory.Binary(
                IrBinaryOperator.AndAlso, clause.Safe, clause.Value).Id));
        }
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(candidate!.CallPreconditions, Has.Length.EqualTo(1));
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure), Is.True,
            failure.ToString());
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var solver = new PassiveCallableSolver(plan!);
        var checkedCall = await solver.VerifyCallPreconditionAsync(0);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(checkedCall.Outcome, Is.TypeOf<ProvenOutcome>(), checkedCall.Reason.ToString());
            Assert.That(checkedCall.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(checkedCall.Core, Is.Not.Empty);
            Assert.That(checkedCall.BodyAssumptions, Is.EquivalentTo(new[] { point.Operation }));
            Assert.That(checkedCall.CallPreconditionWitness, Is.Null);
        }

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        var result = AssertClaimVerdict(response, WorkerClaimOutcome.Proven, WorkerClaimReason.None,
            WorkerVacuityKind.None);
        var used = result.Assumptions.Single(item => item.Kind == WorkerAssumptionKind.UserAssume);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(used.Used, Is.True);
            Assert.That(result.ProofCore, Is.Not.Empty);
            Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
