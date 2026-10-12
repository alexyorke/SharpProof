using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task SarifRejectsInvalidRequestPolicyWithoutChangingWorkerEvidence()
    {
        using var project = TestProject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                [AllowedCapabilities(SharpProofCapability.None)]
                public static int Target(int value) => (int)((decimal)value * 2m);
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.VerifyPolicy = WorkerVerifyPolicy.WarnOnUnknown;
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        AssertClaimVerdict(response, WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedBody);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var originalEvidence = WorkerProtocolJson.SerializeResponse(response);

        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, response, project.DirectoryPath));
        var result = sarif.RootElement.GetProperty("runs")[0].GetProperty("results")
            .EnumerateArray().Single(entry => entry.GetProperty("ruleId").GetString() == "SharpProof.Unknown");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("kind").GetString(), Is.EqualTo("fail"));
            Assert.That(result.GetProperty("level").GetString(), Is.EqualTo("warning"));
            Assert.That(WorkerProtocolJson.SerializeResponse(response), Is.EqualTo(originalEvidence));
        }

        request.VerifyPolicy = (WorkerVerifyPolicy)int.MaxValue;
        var exception = Assert.Throws<InvalidOperationException>(new Action(() =>
            Launcher.SarifProjection.Serialize(request, response, project.DirectoryPath)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("The verifier policy was not validated."));
            Assert.That(WorkerProtocolJson.SerializeResponse(response), Is.EqualTo(originalEvidence));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
