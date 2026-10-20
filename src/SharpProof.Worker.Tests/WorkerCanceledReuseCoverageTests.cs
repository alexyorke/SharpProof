using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class WorkerTests
{
    [Test]
    public async Task CanceledInjectedBackendCannotProduceEvidenceOnReuse()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backendStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = new SharpProofWorker(
            new SignalingDelayingBackend(backendStarted));
        using var cancellation = new CancellationTokenSource();

        var firstRun = worker.VerifyAsync(request, cancellation.Token);
        await backendStarted.Task;
        await cancellation.CancelAsync();
        var canceled = await firstRun;
        var reused = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(canceled.RunStatus, Is.EqualTo(WorkerRunStatus.Canceled));
            Assert.That(reused.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(reused.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.InfrastructureFailure));
            Assert.That(reused.Errors.Single().Code, Is.EqualTo("worker.infrastructure"));
            Assert.That(reused.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(reused.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(reused.ClaimResults.Single().Reason,
                Is.EqualTo(WorkerClaimReason.InfrastructureFailure));
            Assert.That(WorkerProtocolJson.Validate(canceled).IsValid, Is.True);
            Assert.That(WorkerProtocolJson.Validate(reused).IsValid, Is.True);
            Assert.That(CacheFiles(project), Is.Empty);
        }
    }
}
