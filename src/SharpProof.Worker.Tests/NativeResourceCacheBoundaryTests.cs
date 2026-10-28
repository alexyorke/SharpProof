using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task CorruptResourceCacheEvidenceIsDeletedAndCanBeReplaced(bool nullAssumptionId)
    {
        var artifact = CreateArtifact(Source(nullAssumptionId
            ? "[ResourceUpperBound(ResourceMetric.AllocatedBytes, 40)]"
            : "[ResourceUpperBound(ResourceMetric.ArrayAllocations, 0)]", "var value = new int[3];"),
            reviewedRuntime: true, x64: true);
        var target = Read(artifact);
        var budgets = new WorkerBudgets();
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var claim = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, budgets, cancellation)).Single();
        Assert.That(claim.Outcome, Is.EqualTo(nullAssumptionId ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted),
            claim.ResourceBound?.UnknownReason);
        var inputHash = new string('a', 64);
        var response = WorkerResultAssembler.Create(inputHash, artifact.Manifest, WorkerRunStatus.Complete,
            WorkerRunFailureReason.None,
            [new WorkerCallableResult
            {
                CallableId = target.Entry.CallableId,
                Coverage = WorkerCallableCoverage.Complete,
                Reason = WorkerCallableCoverageReason.None,
                Assumptions = target.Entry.Assumptions
            }], [claim], budgets, WorkerCacheStatus.Written, 0);
        Assert.That(VerificationCache.IsCacheable(response, inputHash, artifact.Manifest), Is.True);
        using var directory = new TempDirectory("resource-cache-null-evidence-");
        var cache = new VerificationCache(directory.FullName, 100_000);
        Assert.That(await cache.TryWriteAsync(response, inputHash, artifact.Manifest, cancellation), Is.True);
        var path = Path.Combine(directory.FullName, inputHash + VerificationCache.CacheFileSuffix);
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        if (nullAssumptionId)
        {
            var assumptions = response.ClaimResults[0].Assumptions;
            var usedIndex = Enumerable.Range(0, assumptions.Length).Single(index => assumptions[index].Used);
            envelope["claimResults"]![0]!["assumptions"]![usedIndex]!["id"] = null;
        }
        else
        {
            envelope["claimResults"]![0]!["effectWitness"]!["exactExceptionTypeHierarchy"] = null;
        }
        await File.WriteAllTextAsync(path, envelope.ToJsonString());

        var cached = await cache.TryReadAsync(inputHash, artifact.Manifest, budgets, cancellation);

        Assert.That(cached, Is.Null);
        Assert.That(File.Exists(path), Is.False);
        Assert.That(cache.LastReadUnavailable, Is.False);
        Assert.That(await cache.TryWriteAsync(response, inputHash, artifact.Manifest, cancellation), Is.True);
        var repaired = await cache.TryReadAsync(inputHash, artifact.Manifest, budgets, cancellation);
        Assert.That(repaired, Is.Not.Null);
        Assert.That(repaired!.ClaimResults.Single().Outcome, Is.EqualTo(claim.Outcome));
    }
}
