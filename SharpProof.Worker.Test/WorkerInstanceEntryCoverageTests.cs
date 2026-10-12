using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task InstanceAllocationClaimUsesActualReceiverEntryPrecondition()
    {
        const string source = """
            using SharpProof.Attributes;
            public sealed class Subject {
                [ZeroAllocations]
                public void Target(int value) { Contract.Requires(value > 0); }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var preparation = preparations.Single();
        Assert.That(preparation.IsSuccess, Is.True, preparation.FailureReason.ToString());
        Assert.That(preparation.TotalEntry, Is.Not.Null);
        Assert.That(preparation.Total, Is.Not.Null);
        var entry = preparation.TotalEntry!;
        var total = preparation.Total!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.HasReceiver, Is.True);
            Assert.That(entry.Parameters, Has.Length.EqualTo(2));
            Assert.That(entry.Factory.GetTypeInfo(entry.Factory.GetVariableInfo(entry.Parameters[^1].Entry).Type).Kind,
                Is.EqualTo(IrTypeKind.Reference));
            Assert.That(entry.Clauses, Has.Length.EqualTo(1));
            Assert.That(preparation.Entry.Assumptions.Count(item => item.Kind == WorkerAssumptionKind.Precondition),
                Is.EqualTo(1));
            Assert.That(total.HasReceiver, Is.True);
            Assert.That(total.IsBodyAbstraction, Is.False);
            Assert.That(total.EffectsCompleteAtEntry, Is.True);
            Assert.That(total.ValidEffectClaimIds, Does.Contain(preparation.EffectClaims.Single().ClaimId));
            Assert.That(total.Program.Blocks.SelectMany(block => block.Instructions)
                .OfType<IrHavocInstruction>().Any(instruction => instruction.Origin == IrHavocOrigin.Approximation), Is.False);
        }
        var nativeEntry = await TotalCallableVerifier.VerifyEntryAsync(preparation, new WorkerBudgets(), default);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(nativeEntry.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Feasible));
            Assert.That(nativeEntry.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(nativeEntry.ProofCore, Is.Empty);
            Assert.That(nativeEntry.UsedAssumptionIds, Is.Empty);
        }

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        var result = AssertClaimVerdict(response, WorkerClaimOutcome.Proven, WorkerClaimReason.None,
            WorkerVacuityKind.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(result.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            Assert.That(result.ProofCore, Is.Not.Empty);
            Assert.That(result.EffectWitness, Is.Null);
            Assert.That(result.Assumptions.Count(item => item.Kind == WorkerAssumptionKind.Precondition), Is.EqualTo(1));
            Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
