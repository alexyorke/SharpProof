using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ActualWorkerHostLifecycleAdmissionTests
{
    [Test]
    public async Task OwnedHostExecutionRejectsNormallyCreatedWorkerAfterItsRealDisposal()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """;
        var compilation = TestCompilation.CreateExact("ActualWorkerHostLifecycleAdmission", [("Subject.cs", source)]);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        artifact = project.Artifact;
        var budgets = project.Request.Budgets;
        Assert.That(budgets.QueryRlimit, Is.EqualTo(3_000_000));
        Assert.That(budgets.MethodRlimit, Is.EqualTo(20_000_000));
        Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(64));
        Assert.That(budgets.MethodWallTimeMilliseconds, Is.EqualTo(10_000));
        Assert.That(budgets.ProjectWallTimeMilliseconds, Is.EqualTo(300_000));
        using var callerBoundary = new CancellationTokenSource();
        callerBoundary.CancelAfter(budgets.ProjectWallTimeMilliseconds);
        using var worker = SharpProofWorker.Create(budgets);
        // This proposed internal ownership boundary disposes supplied workers exactly as the default Host disposes its created worker.
        var healthy = await WorkerHost.VerifyAsync(project.Request, callerBoundary.Token,
            project.Snapshot, ownedWorker: worker);
        Assert.That(healthy.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        Assert.That(healthy.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
        Assert.That(healthy.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(healthy.ClaimResults.Single().Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(WorkerProtocolJson.Validate(healthy, project.Bind().InputHash,
            project.Snapshot.CompilerManifest.Manifest).IsValid, Is.True);
        var requestJson = WorkerProtocolJson.SerializeRequest(project.Request);
        // The same genuine worker was disposed by the maintained using scope above.
        // Its raw SDK ObjectDisposedException is caught by the original protected Host handler.
        var rejected = await WorkerHost.VerifyAsync(project.Request, callerBoundary.Token,
            project.Snapshot, ownedWorker: worker);
        Assert.That(rejected.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
        Assert.That(rejected.FailureReason, Is.EqualTo(WorkerRunFailureReason.InfrastructureFailure));
        Assert.That(rejected.InputHash, Is.EqualTo(WorkerResultAssembler.EmptyInputHash));
        Assert.That(rejected.Manifest.Callables, Is.Empty);
        Assert.That(rejected.Manifest.Claims, Is.Empty);
        Assert.That(rejected.CallableResults, Is.Empty);
        Assert.That(rejected.ClaimResults, Is.Empty);
        Assert.That(rejected.Errors, Has.Length.EqualTo(1));
        Assert.That(rejected.Errors.Single().Code, Is.EqualTo("worker.infrastructure"));
        Assert.That(rejected.Errors.Single().Message, Does.Contain(nameof(SharpProofWorker)));
        Assert.That(rejected.Summary.Budgets.QueryRlimit, Is.EqualTo(budgets.QueryRlimit));
        Assert.That(rejected.Summary.Budgets.MethodRlimit, Is.EqualTo(budgets.MethodRlimit));
        Assert.That(rejected.Summary.Budgets.MaximumExpressionDepth, Is.EqualTo(budgets.MaximumExpressionDepth));
        Assert.That(rejected.Summary.Budgets.MethodWallTimeMilliseconds, Is.EqualTo(budgets.MethodWallTimeMilliseconds));
        Assert.That(rejected.Summary.Budgets.ProjectWallTimeMilliseconds, Is.EqualTo(budgets.ProjectWallTimeMilliseconds));
        Assert.That(WorkerProtocolJson.SerializeRequest(project.Request), Is.EqualTo(requestJson));
        Assert.That(callerBoundary.IsCancellationRequested, Is.False);
        Assert.That(WorkerProtocolJson.Validate(rejected).IsValid, Is.True);
        var json = WorkerProtocolJson.SerializeCanonicalResponse(rejected);
        var roundTrip = WorkerProtocolJson.DeserializeResponse(json)
            ?? throw new AssertionException("The canonical disposed-worker rejection did not deserialize.");
        Assert.That(WorkerProtocolJson.Validate(roundTrip).IsValid, Is.True);
        Assert.That(WorkerProtocolJson.SerializeCanonicalResponse(roundTrip), Is.EqualTo(json));
    }
}
