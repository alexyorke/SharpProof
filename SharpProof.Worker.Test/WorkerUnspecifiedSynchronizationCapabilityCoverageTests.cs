using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task UnspecifiedCallMayCapabilitiesRemainUnknownWhenOnlySynchronizationIsAllowed()
    {
        const string source = """
            using System.Linq;
            using SharpProof.Attributes;
            public static class Subject {
                [AllowedCapabilities(SharpProofCapability.Synchronization)]
                public static void Target() { Enumerable.Empty<int>(); }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var preparation = preparations.Single();
        Assert.That(preparation.IsSuccess, Is.True, preparation.FailureReason.ToString());
        Assert.That(preparation.Total, Is.Not.Null);
        var total = preparation.Total!;
        var claim = preparation.EffectClaims.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.ContractKind, Is.EqualTo(WorkerEffectContractKind.AllowedCapabilities));
            Assert.That(claim.Constraint.AllowedCapabilities, Is.EqualTo(WorkerEffectCapabilitySet.Synchronization));
            Assert.That(total.ValidEffectClaimIds, Does.Contain(claim.ClaimId));
            Assert.That(total.IsBodyAbstraction, Is.False);
            Assert.That(total.EffectsCompleteAtEntry, Is.True);
        }
        var instructions = total.Program.Blocks.SelectMany(block => block.Instructions).ToArray();
        var call = instructions.OfType<IrCallInstruction>().Single();
        Assert.That(IrOpaqueCallSite.Effects(total.Program.Factory, call.Operation), Is.EqualTo(IrOpaqueCallEffects.All));
        Assert.That(instructions.OfType<IrHavocInstruction>().Any(instruction =>
            instruction.Origin == IrHavocOrigin.Approximation), Is.True);
        var native = await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(native.Outcome, Is.Null);
            Assert.That(native.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(native.LockWitness, Is.Null);
            Assert.That(native.Core, Is.Empty);
        }

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        var result = AssertClaimVerdict(response, WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedBody,
            WorkerVacuityKind.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(result.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(result.ProofCore, Is.Empty);
            Assert.That(result.EffectWitness, Is.Null);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
