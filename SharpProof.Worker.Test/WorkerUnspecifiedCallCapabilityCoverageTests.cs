using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task AllNamedCapabilitiesAllowAnUnspecifiedLibraryCall()
    {
        using var project = TestProject.Create("""
            using System.Linq;
            using SharpProof.Attributes;
            public static class Subject {
                [AllowedCapabilities(
                    SharpProofCapability.IO | SharpProofCapability.FileRead |
                    SharpProofCapability.FileWrite | SharpProofCapability.Network |
                    SharpProofCapability.Console | SharpProofCapability.Process |
                    SharpProofCapability.Environment | SharpProofCapability.Registry |
                    SharpProofCapability.Clock | SharpProofCapability.Randomness |
                    SharpProofCapability.Reflection | SharpProofCapability.Synchronization |
                    SharpProofCapability.NativeInterop)]
                public static void Caller() { Enumerable.Empty<int>(); }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request, cancellationToken: CancellationToken.None);
        var result = response.ClaimResults.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
                WorkerProtocolJson.SerializeResponse(response));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
