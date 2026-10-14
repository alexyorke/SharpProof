using System.Runtime.InteropServices;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerHostErrorProjectionTests
{
    [Test]
    public void ActualMissingFileErrorRetainsInfrastructureProjection()
    {
        var path = Path.Combine(Path.GetTempPath(), "sharpproof-missing-projection-" + Guid.NewGuid().ToString("N"));
        var exception = Assert.Throws<FileNotFoundException>(() =>
        {
            using var stream = File.OpenRead(path);
        });
        AssertProjection(exception!, WorkerRunFailureReason.InfrastructureFailure, "worker.infrastructure",
            "The worker failed before producing a semantic result.");
    }

    [Test]
    public void ActualMissingLibraryErrorRetainsUnavailableProjection()
    {
        var name = "sharpproof-missing-projection-" + Guid.NewGuid().ToString("N");
        var exception = Assert.Throws<DllNotFoundException>(() =>
        {
            var handle = NativeLibrary.Load(name);
            NativeLibrary.Free(handle);
        });
        AssertProjection(exception!, WorkerRunFailureReason.BackendUnavailable, "backend.unavailable",
            "The native SMT backend is unavailable.");
    }

    private static void AssertProjection(Exception exception, WorkerRunFailureReason reason, string code, string prefix)
    {
        var budgets = new WorkerBudgets();
        var response = WorkerHost.Failure(exception, budgets);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
        Assert.That(response.FailureReason, Is.EqualTo(reason));
        Assert.That(response.RequestHash, Is.EqualTo(WorkerProtocolVersions.EmptySha256));
        Assert.That(response.InputHash, Is.EqualTo(WorkerProtocolVersions.EmptySha256));
        Assert.That(response.Manifest.Callables, Is.Empty);
        Assert.That(response.Manifest.Claims, Is.Empty);
        Assert.That(response.CallableResults, Is.Empty);
        Assert.That(response.ClaimResults, Is.Empty);
        Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Disabled));
        Assert.That(response.Summary.CacheHit, Is.False);
        Assert.That(response.Summary.ElapsedMilliseconds, Is.Zero);
        Assert.That(response.Summary.Budgets.QueryRlimit, Is.EqualTo(budgets.QueryRlimit));
        Assert.That(response.Summary.Budgets.MethodRlimit, Is.EqualTo(budgets.MethodRlimit));
        Assert.That(response.Summary.Budgets.MethodWallTimeMilliseconds, Is.EqualTo(budgets.MethodWallTimeMilliseconds));
        Assert.That(response.Summary.Budgets.ProjectWallTimeMilliseconds, Is.EqualTo(budgets.ProjectWallTimeMilliseconds));
        Assert.That(response.Summary.Budgets.MaxParallelism, Is.EqualTo(budgets.MaxParallelism));
        Assert.That(response.Summary.Budgets.MaximumExpressionDepth, Is.EqualTo(budgets.MaximumExpressionDepth));
        Assert.That(response.Errors, Has.Length.EqualTo(1));
        Assert.That(response.Errors[0].Code, Is.EqualTo(code));
        Assert.That(response.Errors[0].Message, Is.EqualTo(prefix + " " + exception.GetBaseException().Message));
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var json = WorkerProtocolJson.SerializeResponse(response);
        var decoded = WorkerProtocolJson.DeserializeResponse(json);
        Assert.That(decoded, Is.Not.Null);
        Assert.That(WorkerProtocolJson.Validate(decoded!).IsValid, Is.True);
        Assert.That(WorkerProtocolJson.SerializeResponse(decoded!), Is.EqualTo(json));
    }
}
