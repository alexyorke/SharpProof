using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public async Task SarifPresentationRejectsInvalidRawOutcomeWithoutChangingRealEvidence(int rawOutcome)
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: CancellationToken.None);
        AssertClaimVerdict(response, WorkerClaimOutcome.Proven);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var evidence = WorkerProtocolJson.SerializeResponse(response);
        var actual = response.ClaimResults.Single();
        var presentation = Launcher.SarifProjection.PresentOutcome(actual.Outcome, actual.Vacuity, request.VerifyPolicy);
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, response, project.DirectoryPath));
        var projected = sarif.RootElement.GetProperty("runs")[0].GetProperty("results")
            .EnumerateArray().Single(entry => entry.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(presentation, Is.EqualTo(("pass", "none")));
            Assert.That(projected.GetProperty("kind").GetString(), Is.EqualTo(presentation.Kind));
            Assert.That(projected.GetProperty("level").GetString(), Is.EqualTo(presentation.Level));
        }
        var exception = Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            Launcher.SarifProjection.PresentOutcome((WorkerClaimOutcome)rawOutcome, actual.Vacuity, request.VerifyPolicy)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("result"));
            Assert.That(WorkerProtocolJson.SerializeResponse(response), Is.EqualTo(evidence));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
