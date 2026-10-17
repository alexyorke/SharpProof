using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase("ReadsAmbientState", "None")]
    [TestCase("WritesAmbientState", "None")]
    [TestCase("Allocates", "None")]
    [TestCase("Synchronizes", "Synchronization")]
    [TestCase("UsesNondeterminism", "Randomness")]
    [TestCase("UsesNativeCode", "NativeInterop")]
    [TestCase("UsesReflection", "Reflection")]
    public async Task CompleteEffectContractAllowsReviewedOpaqueCall(
        string effect, string capability)
    {
        System.ArgumentNullException.ThrowIfNull(capability);
        System.ArgumentNullException.ThrowIfNull(effect);
        var deterministic = effect == "UsesNondeterminism" ? "false" : "true";
        using var project = TestProject.Create($$"""
            using SharpProof.Attributes;
            public class Subject {
                [EffectContract(
                    SharpProofEffect.ReadsReceiverState | SharpProofEffect.ReadsArgumentState |
                    SharpProofEffect.ReadsCapturedState | SharpProofEffect.ReadsStaticState |
                    SharpProofEffect.ReadsAmbientState | SharpProofEffect.WritesReceiverState |
                    SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesCapturedState |
                    SharpProofEffect.WritesStaticState | SharpProofEffect.WritesAmbientState |
                    SharpProofEffect.Allocates | SharpProofEffect.Synchronizes |
                    SharpProofEffect.UsesNondeterminism | SharpProofEffect.UsesNativeCode |
                    SharpProofEffect.UsesReflection,
                    Capabilities = SharpProofCapability.{{capability}}, Complete = true)]
                public void Caller(object argument) { ReviewedBoundary.Invoke(); }
            }
            """);
        project.AddImplementationReference($$"""
            using SharpProof.Attributes;
            public static class ReviewedBoundary {
                [SharpProofTrusted("Reviewed external boundary for this test.")]
                [EffectContract(SharpProofEffect.{{effect}},
                    Capabilities = SharpProofCapability.{{capability}},
                    IsDeterministic = {{deterministic}}, PreconditionFree = true, Complete = true)]
                public static extern void Invoke();
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);
        var result = response.ClaimResults.Single(claim =>
            claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (GetCallableId(response, claim).Contains(".Caller(", StringComparison.Ordinal)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
                WorkerProtocolJson.SerializeResponse(response));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
