using System.Diagnostics;
using System.Globalization;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerResultAssemblerTests
{
    private const int ManifestSize = 4096;
    private static readonly string s_idPrefix = new('x', 1024);

    [Test]
    public void IncompleteAssemblyDoesNotRescanCallablesForEveryClaim()
    {
        _ = MeasureCreateIncomplete(CreateManifest(targetFirst: true, 1), 1);

        var targetFirst = MeasureCreateIncomplete(
            CreateManifest(targetFirst: true, ManifestSize),
            ManifestSize);
        var targetLast = MeasureCreateIncomplete(
            CreateManifest(targetFirst: false, ManifestSize),
            ManifestSize);
        var maximumTargetLast = targetFirst * 4 +
            TimeSpan.FromMilliseconds(250);

        Assert.That(
            targetLast,
            Is.LessThanOrEqualTo(maximumTargetLast),
            $"target-first={targetFirst.TotalMilliseconds:F0} ms, " +
            $"target-last={targetLast.TotalMilliseconds:F0} ms");
    }

    [Test]
    public void IncompleteAssemblyToleratesNullManifestEntries()
    {
        var manifest = new WorkerClaimManifest
        {
            Callables =
            [
                null!,
                new WorkerCallableManifestEntry
                {
                    CallableId = "callable",
                    Assumptions = null!
                }
            ],
            Claims =
            [
                null!,
                new WorkerClaimManifestEntry
                {
                    ClaimId = "claim",
                    CallableId = "callable",
                    Kind = WorkerClaimKind.Postcondition
                }
            ]
        };

        var response = WorkerResultAssembler.CreateIncomplete(
            WorkerProtocolVersions.EmptySha256,
            WorkerProtocolVersions.EmptySha256,
            manifest,
            new WorkerBudgets(),
            WorkerRunStatus.Failed,
            WorkerRunFailureReason.MalformedResult,
            WorkerCallableCoverageReason.InfrastructureFailure,
            WorkerClaimReason.InfrastructureFailure);

        Assert.That(response.CallableResults, Has.Length.EqualTo(1));
        Assert.That(response.CallableResults[0].CallableId, Is.EqualTo("callable"));
        Assert.That(response.CallableResults[0].Assumptions, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
        Assert.That(response.ClaimResults[0].ClaimId, Is.EqualTo("claim"));
        Assert.That(response.ClaimResults[0].Assumptions, Is.Empty);
        Assert.That(response.Summary.CallableCount, Is.EqualTo(1));
        Assert.That(response.Summary.ClaimCount, Is.EqualTo(1));
    }

    private static TimeSpan MeasureCreateIncomplete(
        WorkerClaimManifest manifest,
        int expectedSize)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = WorkerResultAssembler.CreateIncomplete(
            WorkerProtocolVersions.EmptySha256,
            WorkerProtocolVersions.EmptySha256,
            manifest,
            new WorkerBudgets(),
            WorkerRunStatus.Canceled,
            WorkerRunFailureReason.None,
            WorkerCallableCoverageReason.Canceled,
            WorkerClaimReason.Canceled);
        stopwatch.Stop();

        Assert.That(response.ClaimResults, Has.Length.EqualTo(expectedSize));
        Assert.That(
            response.ClaimResults.Select(static claim =>
                claim.Assumptions.Length),
            Is.All.EqualTo(1));
        return stopwatch.Elapsed;
    }

    private static WorkerClaimManifest CreateManifest(
        bool targetFirst,
        int size)
    {
        var targetId = CallableId(size - 1);
        var target = new WorkerCallableManifestEntry
        {
            CallableId = targetId,
            Assumptions =
            [
                new WorkerAssumptionEvidence
                {
                    Id = "assumption",
                    Kind = WorkerAssumptionKind.UserAssume
                }
            ]
        };
        var otherCallables = Enumerable.Range(0, size - 1)
            .Select(static index => new WorkerCallableManifestEntry
            {
                CallableId = CallableId(index)
            });
        var callables = targetFirst
            ? new[] { target }.Concat(otherCallables)
            : otherCallables.Append(target);

        return new WorkerClaimManifest
        {
            Callables = [.. callables],
            // Duplicate claim IDs keep response canonicalization constant-time
            // per row so this failure-path regression measures callable lookup.
            Claims = [.. Enumerable.Range(0, size).Select(_ =>
                new WorkerClaimManifestEntry
                {
                    ClaimId = "claim",
                    CallableId = targetId,
                    Kind = WorkerClaimKind.Postcondition
                })]
        };
    }

    private static string CallableId(int index)
    {
        return s_idPrefix + index.ToString("D8", CultureInfo.InvariantCulture);
    }
}
