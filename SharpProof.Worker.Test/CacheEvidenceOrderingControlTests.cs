using NUnit.Framework;
using SharpProof.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CacheEvidenceOrderingControlTests
{
    private static readonly string[] s_unordered = ["z", "a"];
    private static readonly string[] s_ordered = ["a", "z"];
    private static readonly string[] s_orderedClaims = ["a.claim", "z.claim"];

    [Test]
    public async Task ValidUnorderedEvidenceIsCanonicalizedAfterAdmission()
    {
        using var directory = new TempDirectory("cache-ordering-control-");
        var manifest = new WorkerClaimManifest
        {
            Callables = [.. s_unordered.Select(id => new WorkerCallableManifestEntry
            {
                CallableId = id,
                SelectedFeatures = [WorkerSelectedFeature.Contracts],
                SelectionReasons = [WorkerSelectionReason.DiscoveredPostcondition],
                Location = new WorkerSourceLocation
                {
                    Path = "Subject.cs",
                    Start = 0,
                    Length = 1,
                    Line = 1,
                    Column = 1
                },
                ClaimIds = [id + ".claim"]
            })],
            Claims = [.. s_unordered.Select(id => new WorkerClaimManifestEntry
            {
                ClaimId = id + ".claim", CallableId = id, Ordinal = 0,
                Kind = WorkerClaimKind.Postcondition, Evidence = WorkerClaimEvidence.DirectClause,
                Location = new WorkerSourceLocation
                {
                    Path = "Subject.cs",
                    Start = 0,
                    Length = 1,
                    Line = 1,
                    Column = 1
                }
            })]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var inputHash = new string('b', 64);
        var budgets = new WorkerBudgets();
        var response = WorkerResultAssembler.Create(
            inputHash, manifest, WorkerRunStatus.Complete, WorkerRunFailureReason.None,
            manifest.Callables.Select(callable => new WorkerCallableResult
            {
                CallableId = callable.CallableId,
                Coverage = WorkerCallableCoverage.Complete,
                Reason = WorkerCallableCoverageReason.None
            }),
            manifest.Claims.Select(claim => new WorkerClaimResult
            {
                ClaimId = claim.ClaimId,
                Outcome = WorkerClaimOutcome.Proven,
                Reason = WorkerClaimReason.None,
                ProofCore = ["z", "a"]
            }), budgets, WorkerCacheStatus.Written, 0);
        Array.Reverse(response.CallableResults);
        Array.Reverse(response.ClaimResults);
        foreach (var claim in response.ClaimResults)
        {
            Array.Reverse(claim.ProofCore);
        }
        Assert.That(VerificationCache.IsCacheable(response, inputHash, manifest), Is.True);
        var cache = new VerificationCache(directory.FullName, 100_000);
        Assert.That(await cache.TryWriteAsync(response, inputHash, manifest, CancellationToken.None), Is.True);
        var cached = await cache.TryReadAsync(inputHash, manifest, budgets, CancellationToken.None);
        Assert.That(cached, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cached!.CallableResults.Select(result => result.CallableId), Is.EqualTo(s_ordered));
            Assert.That(cached.ClaimResults.Select(result => result.ClaimId), Is.EqualTo(s_orderedClaims));
            Assert.That(cached.ClaimResults.All(result => result.ProofCore.SequenceEqual(s_ordered)), Is.True);
            Assert.That(cached.ClaimResults.All(result => result.Outcome == WorkerClaimOutcome.Proven), Is.True);
            Assert.That(VerificationCache.IsCacheable(cached, inputHash, manifest), Is.True);
        }
    }
}
