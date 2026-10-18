using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task UnreadableCacheRecomputesWithoutChangingTheVerdict()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        using var backend = new CountingNativeBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var firstQueries = backend.CallCount;
        Assert.That(firstQueries, Is.GreaterThan(0));
        AssertClaimVerdict(first, WorkerClaimOutcome.Refuted);
        Assert.That(first.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Written));

        var cacheFile = Directory.GetFiles(
            project.CacheDirectory,
            "*.sharp-proof-cache.json").Single();
        WorkerVerifyResponse unavailable;
        var originalMode = File.GetUnixFileMode(project.CacheDirectory);
        try
        {
            File.SetUnixFileMode(project.CacheDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserExecute);
            using (File.Open(cacheFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAsync<IOException>((Func<Task>)(async () =>
                {
                    await File.ReadAllTextAsync(cacheFile);
                }));
                unavailable = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
                Assert.That(unavailable.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
            }
        }
        finally
        {
            File.SetUnixFileMode(project.CacheDirectory, originalMode);
        }

        var cached = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(firstQueries * 2));
            Assert.That(cached.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
            Assert.That(WorkerProtocolJson.Validate(unavailable).IsValid, Is.True);
            Assert.That(WorkerProtocolJson.Validate(cached).IsValid, Is.True);
        }
        AssertSemanticallyEquivalent(first, unavailable);
        AssertSemanticallyEquivalent(first, cached);
    }
}
