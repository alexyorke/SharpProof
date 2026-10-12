using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task CompleteKnownEffectBoundAllowsActualUnspecifiedLibraryCall()
    {
        const string source = """
            using System.Linq;
            using SharpProof.Attributes;
            public static class Subject {
                [EffectContract(
                    SharpProofEffect.ReadsReceiverState | SharpProofEffect.ReadsArgumentState |
                    SharpProofEffect.ReadsCapturedState | SharpProofEffect.ReadsStaticState |
                    SharpProofEffect.ReadsAmbientState | SharpProofEffect.WritesReceiverState |
                    SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesCapturedState |
                    SharpProofEffect.WritesStaticState | SharpProofEffect.WritesAmbientState |
                    SharpProofEffect.Allocates | SharpProofEffect.Throws | SharpProofEffect.Synchronizes |
                    SharpProofEffect.UsesNondeterminism | SharpProofEffect.UsesNativeCode | SharpProofEffect.UsesReflection,
                    Capabilities = SharpProofCapability.IO | SharpProofCapability.FileRead |
                        SharpProofCapability.FileWrite | SharpProofCapability.Network |
                        SharpProofCapability.Console | SharpProofCapability.Process |
                        SharpProofCapability.Environment | SharpProofCapability.Registry |
                        SharpProofCapability.Clock | SharpProofCapability.Randomness |
                        SharpProofCapability.Reflection | SharpProofCapability.Synchronization |
                        SharpProofCapability.NativeInterop,
                    ThrownExceptions = new[] { typeof(System.Exception) }, Complete = true)]
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
        Assert.That(total.ValidEffectClaimIds, Does.Contain(claim.ClaimId));
        Assert.That(claim.Constraint.AllowedEffects, Is.EqualTo(WorkerEffectSet.AllKnown));
        Assert.That(claim.Constraint.AllowedCapabilities, Is.EqualTo(WorkerEffectCapabilitySet.AllKnown));
        Assert.That(total.IsBodyAbstraction, Is.False);
        Assert.That(total.EffectsCompleteAtEntry, Is.True);
        var instructions = total.Program.Blocks.SelectMany(block => block.Instructions).ToArray();
        var call = instructions.OfType<IrCallInstruction>().Single();
        Assert.That(IrOpaqueCallSite.Effects(total.Program.Factory, call.Operation), Is.EqualTo(IrOpaqueCallEffects.All));
        Assert.That(instructions.OfType<IrHavocInstruction>().Any(instruction =>
            instruction.Origin == IrHavocOrigin.Approximation), Is.True);
        Assert.That(total.ExceptionConstraints.Single().AllowedKinds, Does.Contain(IrExceptionKind.Unknown));
        var native = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation, new WorkerBudgets());
        Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>(), native.Reason.ToString());
        Assert.That(native.Reason, Is.EqualTo(WorkerClaimReason.None));

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
            Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
