using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed class ResourceLockCacheBoundaryTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task RuntimeResourceWitnessesAreNotReusedAsContextIndependentCacheEntries(bool resource)
    {
        var body = resource ? "_ = new System.ArgumentOutOfRangeException(\"parameter\");" : "lock (new object()) { }";
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class Subject { [EnforcePure] public static void Target() { " + body + " } }");
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var snapshot = WorkerInputSnapshot.Load(request, WorkerCacheIdentity.Current, TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(second.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(first.ClaimResults.Single().EffectWitness!.Kind, Is.EqualTo("synchronization-lock"));
            Assert.That(first.ClaimResults.Single().EffectWitness!.Detail.StartsWith(IrOpaqueCallSite.ExceptionResourceLockPrefix, StringComparison.Ordinal), Is.EqualTo(resource));
            Assert.That(VerificationCache.IsCacheable(first, snapshot.InputHash, snapshot.CompilerManifest.Manifest), Is.EqualTo(!resource));
            Assert.That(second.Summary.CacheHit, Is.EqualTo(!resource));
            Assert.That(WorkerProtocolJson.Validate(first).IsValid, Is.True);
            Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.True);
        }
        if (resource)
        {
            var cache = new VerificationCache(project.CacheDirectory, request.Cache.MaximumBytes);
            // Simulate a copied cache envelope. The read boundary must reject
            // this context-specific evidence even when its protocol is valid.
            Assert.That(await cache.TryWriteAsync(first, snapshot.InputHash, snapshot.CompilerManifest.Manifest,
                TestContext.CurrentContext.CancellationToken), Is.True);
            Assert.That(await cache.TryReadAsync(snapshot.InputHash, snapshot.CompilerManifest.Manifest, request.Budgets,
                TestContext.CurrentContext.CancellationToken), Is.Null);
        }
    }
}
