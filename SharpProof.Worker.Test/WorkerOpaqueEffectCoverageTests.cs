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
        var deterministic = effect == "UsesNondeterminism" ? "false" : "true";
        using var project = TestProject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject {
                [SharpProofTrusted("Reviewed external boundary for this test.")]
                [EffectContract(SharpProofEffect.{{effect}},
                    Capabilities = SharpProofCapability.{{capability}},
                    IsDeterministic = {{deterministic}}, PreconditionFree = true, Complete = true)]
                public static extern void Boundary();

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
                public static void Caller() { Boundary(); }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);
        var result = response.ClaimResults.Single(claim =>
            GetCallableId(response, claim).Contains(".Caller(", StringComparison.Ordinal));

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
