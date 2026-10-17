using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
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
            request.Cache.MaximumBytes = entryBytes - 1;
            File.SetUnixFileMode(project.CacheDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.Throws<UnauthorizedAccessException>(new Action(() => File.Delete(cachePath)));
            unavailable = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        }
        finally
        {
            File.SetUnixFileMode(project.CacheDirectory, originalMode);
            request.Cache.MaximumBytes = originalCapacity;
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
