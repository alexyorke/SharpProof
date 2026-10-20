using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class WorkerTests
{
    [Test]
    public async Task EvictionCanFinishAfterSkippingTheProtectedOldestEntry()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(response, WorkerClaimOutcome.Refuted);
        var original = CacheFiles(project).Single();
        var capacity = new FileInfo(original).Length;
        Assert.That(capacity, Is.GreaterThan(0));
        File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddDays(1));
        var snapshot = WorkerInputSnapshot.Load(request, WorkerCacheIdentity.Current, TestContext.CurrentContext.CancellationToken);
        var protectedHash = new string(response.InputHash[0] == 'a' ? 'b' : 'a', 64);
        var cache = new VerificationCache(project.CacheDirectory, capacity);
        Assert.That(await cache.TryWriteAsync(response, protectedHash, snapshot.CompilerManifest.Manifest,
            TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(File.Exists(original), Is.False);
        Assert.That(CacheFiles(project).Length, Is.EqualTo(1));
        var cached = await cache.TryReadAsync(protectedHash, snapshot.CompilerManifest.Manifest,
            request.Budgets, TestContext.CurrentContext.CancellationToken);
        Assert.That(cached, Is.Not.Null);
        AssertClaimVerdict(cached!, WorkerClaimOutcome.Refuted);
    }

    [Test]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task ReadOnlyCacheEvictionPreservesTheNativeRefutation()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        using var backend = new CountingNativeBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(first, WorkerClaimOutcome.Refuted);
        Assert.That(first.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Written));
        var firstQueries = backend.CallCount;
        Assert.That(firstQueries, Is.GreaterThan(0));
        var cachePath = CacheFiles(project).Single();
        var entryBytes = new FileInfo(cachePath).Length;
        Assert.That(entryBytes, Is.GreaterThan(1));
        var originalCapacity = request.Cache.MaximumBytes;
        var originalMode = File.GetUnixFileMode(project.CacheDirectory);
        WorkerVerifyResponse unavailable;
        try
        {
            { request = request with { Cache = request.Cache with { MaximumBytes = entryBytes - 1 } }; }
            File.SetUnixFileMode(project.CacheDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.Throws<UnauthorizedAccessException>(new Action(() => File.Delete(cachePath)));
            unavailable = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        }
        finally
        {
            File.SetUnixFileMode(project.CacheDirectory, originalMode);
            { request = request with { Cache = request.Cache with { MaximumBytes = originalCapacity } }; }
        }
        var cached = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unavailable.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
            Assert.That(cached.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
            Assert.That(backend.CallCount, Is.EqualTo(firstQueries * 2));
            Assert.That(File.Exists(cachePath), Is.True);
            Assert.That(WorkerProtocolJson.Validate(unavailable).IsValid, Is.True);
            Assert.That(WorkerProtocolJson.Validate(cached).IsValid, Is.True);
        }
        AssertSemanticallyEquivalent(first, unavailable);
        AssertSemanticallyEquivalent(first, cached);
    }
}
