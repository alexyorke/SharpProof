using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CacheNullEvidenceBoundaryTests
{
    [TestCase("model")]
    [TestCase("claimAssumptions")]
    [TestCase("callableAssumptions")]
    [TestCase("valid")]
    public async Task CacheDoesNotRepairNullEvidenceIntoAHit(string shape)
    {
        using var directory = new TempDirectory("cache-null-evidence-");
        var location = new WorkerSourceLocation
        {
            Path = "Subject.cs",
            Start = 0,
            Length = 1,
            Line = 1,
            Column = 1
        };
        var manifest = new WorkerClaimManifest
        {
            Callables = [new WorkerCallableManifestEntry
            {
                CallableId = "M:Subject.Target",
                SelectedFeatures = [WorkerSelectedFeature.Contracts],
                SelectionReasons = [WorkerSelectionReason.DiscoveredPostcondition],
                Location = location,
                ClaimIds = ["claim"]
            }],
            Claims = [new WorkerClaimManifestEntry
            {
                ClaimId = "claim", CallableId = "M:Subject.Target", Ordinal = 0,
                Kind = WorkerClaimKind.Postcondition,
                Evidence = WorkerClaimEvidence.DirectClause, Location = location
            }]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var inputHash = new string('a', 64);
        var budgets = new WorkerBudgets();
        var response = WorkerResultAssembler.Create(
            inputHash, manifest, WorkerRunStatus.Complete, WorkerRunFailureReason.None,
            [new WorkerCallableResult
            {
                CallableId = "M:Subject.Target", Coverage = WorkerCallableCoverage.Complete, Reason = WorkerCallableCoverageReason.None
            }],
            [new WorkerClaimResult
            {
                ClaimId = "claim", Outcome = WorkerClaimOutcome.Proven, Reason = WorkerClaimReason.None, ProofCore = ["goal"]
            }], budgets, WorkerCacheStatus.Written, 0);
        Assert.That(VerificationCache.IsCacheable(response, inputHash, manifest), Is.True);
        var cache = new VerificationCache(directory.FullName, 100_000);
        Assert.That(await cache.TryWriteAsync(response, inputHash, manifest, CancellationToken.None), Is.True);
        var path = Path.Combine(directory.FullName, inputHash + VerificationCache.CacheFileSuffix);
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        switch (shape)
        {
            case "model":
                response.ClaimResults[0].Model = null!;
                envelope["claimResults"]![0]!["model"] = null;
                break;
            case "claimAssumptions":
                response.ClaimResults[0].Assumptions = null!;
                envelope["claimResults"]![0]!["assumptions"] = null;
                break;
            case "callableAssumptions":
                response.CallableResults[0].Assumptions = null!;
                envelope["callableResults"]![0]!["assumptions"] = null;
                break;
        }
        Assert.That(VerificationCache.IsCacheable(response, inputHash, manifest), Is.EqualTo(shape == "valid"));
        await File.WriteAllTextAsync(path, envelope.ToJsonString());
        var cached = await cache.TryReadAsync(inputHash, manifest, budgets, CancellationToken.None);
        if (shape == "valid")
        {
            Assert.That(cached, Is.Not.Null);
            Assert.That(cached!.ClaimResults[0].Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        }
        else
        {
            Assert.That(cached, Is.Null, "Malformed evidence must be a miss before canonicalization repairs it.");
            Assert.That(File.Exists(path), Is.False);
        }
    }
}
