using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase(false)]
    [TestCase(true)]
    [TestCase(true, true)]
    public async Task ShippedConstructorSpecificationPreservesDeclaredCapabilityBound(bool synchronizing, bool explicitMessage = false)
    {
        var capability = synchronizing ? "SharpProofCapability.Synchronization" : "SharpProofCapability.None";
        var resourceMessage = synchronizing && !explicitMessage;
        var construction = resourceMessage ? "new System.InvalidOperationException()" : "new System.InvalidOperationException(\"failed\")";
        using var project = TestProject.Create("using SharpProof.Attributes; public static class Subject { " +
            "[AllowedCapabilities(" + capability + ")] public static void Caller() { " + construction + "; } }");
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(result.Outcome, Is.EqualTo(resourceMessage ? WorkerClaimOutcome.Unknown : WorkerClaimOutcome.Proven), WorkerProtocolJson.SerializeResponse(response));
            Assert.That(result.Reason, Is.EqualTo(resourceMessage ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
