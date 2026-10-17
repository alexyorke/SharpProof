using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase("")]
    [TestCase("not-a-sha256")]
    [TestCase("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public async Task CacheReadRejectsAnInvalidHashAndPreservesTheRealCachedResult(string inputHash)
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var original = await worker.VerifyAsync(request, cancellationToken: CancellationToken.None);
        AssertClaimVerdict(original, WorkerClaimOutcome.Refuted);
        Assert.That(original.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Written));

        var snapshot = WorkerInputSnapshot.Load(request, WorkerCacheIdentity.Current, CancellationToken.None);
        Assert.That(snapshot.InputHash, Is.EqualTo(original.InputHash));
        var manifest = snapshot.CompilerManifest.Manifest;
        var cache = new VerificationCache(project.CacheDirectory, request.Cache.MaximumBytes);
        var exception = Assert.ThrowsAsync<ArgumentException>((Func<Task>)(async () =>
        {
            await cache.TryReadAsync(inputHash, manifest, request.Budgets, CancellationToken.None);
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("inputHash"));
            Assert.That(exception.Message, Does.Contain("A SHA-256 input hash is required"));
            Assert.That(cache.LastReadUnavailable, Is.False);
        }

        var restored = await cache.TryReadAsync(snapshot.InputHash, manifest, request.Budgets,
            CancellationToken.None);
        Assert.That(restored, Is.Not.Null);
        AssertClaimVerdict(restored!, WorkerClaimOutcome.Refuted);
        Assert.That(WorkerProtocolJson.Validate(restored!, snapshot.InputHash, manifest).IsValid, Is.True);
    }
}
