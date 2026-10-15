using System.Collections.Concurrent;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class VerificationTargetSchedulerTests
{
    // The three pairs emitted by the worker after actual lane renewal failure.
    private static readonly (WorkerCallableCoverageReason Callable, WorkerClaimReason Claim)[] Retirements =
    [
        (WorkerCallableCoverageReason.MethodTimeout, WorkerClaimReason.MethodTimeout),
        (WorkerCallableCoverageReason.InfrastructureFailure, WorkerClaimReason.InfrastructureFailure),
        (WorkerCallableCoverageReason.InfrastructureFailure, WorkerClaimReason.BackendUnavailable)
    ];

    [Test]
    public void EmptyAndExhaustedQueuesNeverIssueAnOutOfRangeIndex()
    {
        var empty = new VerificationTargetScheduler(0);
        Assert.That(empty.TryClaim(out _), Is.False);
        Assert.That(empty.SnapshotRetirement(), Is.EqualTo(
            (WorkerCallableCoverageReason.InfrastructureFailure, WorkerClaimReason.InfrastructureFailure)));
        var scheduler = new VerificationTargetScheduler(2);
        Assert.That(scheduler.TryClaim(out var first), Is.True);
        Assert.That(scheduler.TryClaim(out var second), Is.True);
        Assert.That(first, Is.Zero);
        Assert.That(second, Is.EqualTo(1));
        Assert.That(scheduler.TryClaim(out _), Is.False);
        Assert.That(scheduler.TryClaim(out _), Is.False);
    }

    [Test]
    public void NegativeTargetCountIsRejected()
    {
        Action create = () => _ = new VerificationTargetScheduler(-1);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(create);
        Assert.That(exception!.ParamName, Is.EqualTo("targetCount"));
    }

    [Test]
    public async Task ConcurrentClaimersOwnEveryIndexExactlyOnce()
    {
        const int targetCount = 128;
        var scheduler = new VerificationTargetScheduler(targetCount);
        var claimed = new ConcurrentBag<int>();
        var workers = Enumerable.Range(0, 8).Select(laneIndex => Task.Run(() =>
        {
            while (scheduler.TryClaim(out var index))
            {
                claimed.Add(index);
            }
        })).ToArray();
        await Task.WhenAll(workers);
        Assert.That(claimed, Is.EquivalentTo(Enumerable.Range(0, targetCount)));
        Assert.That(scheduler.TryClaim(out _), Is.False);
    }

    [Test]
    public async Task InFlightClaimersCannotClaimAgainAfterRecordedRetirement()
    {
        const int laneCount = 4;
        var scheduler = new VerificationTargetScheduler(laneCount * 2);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claimed = new ConcurrentBag<int>();
        var entered = 0;
        var workers = Enumerable.Range(0, laneCount).Select(laneIndex => Task.Run(async () =>
        {
            Assert.That(scheduler.TryClaim(out var index), Is.True);
            claimed.Add(index);
            if (Interlocked.Increment(ref entered) == laneCount)
            {
                ready.TrySetResult();
            }
            await release.Task;
            Assert.That(scheduler.TryClaim(out _), Is.False);
        })).ToArray();
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var retirement = Retirements[1];
            scheduler.RecordRetirement(retirement.Callable, retirement.Claim);
        }
        finally
        {
            release.TrySetResult();
        }
        await Task.WhenAll(workers);
        Assert.That(claimed, Is.EquivalentTo(Enumerable.Range(0, laneCount)));
        Assert.That(scheduler.SnapshotRetirement(), Is.EqualTo(Retirements[1]));
    }

    [TestCase(0, 1, 2)]
    [TestCase(0, 2, 1)]
    [TestCase(1, 0, 2)]
    [TestCase(1, 2, 0)]
    [TestCase(2, 0, 1)]
    [TestCase(2, 1, 0)]
    public void StrongestRetirementIsStableInEveryOrder(int first, int second, int third)
    {
        var scheduler = new VerificationTargetScheduler(1);
        var strongest = -1;
        Record(first);
        Record(second);
        Record(third);
        Record(third);
        Assert.That(scheduler.TryClaim(out _), Is.False);

        void Record(int ordinal)
        {
            var retirement = Retirements[ordinal];
            scheduler.RecordRetirement(retirement.Callable, retirement.Claim);
            strongest = Math.Max(strongest, ordinal);
            Assert.That(scheduler.SnapshotRetirement(), Is.EqualTo(Retirements[strongest]));
        }
    }

    [Test]
    public async Task ConcurrentRetirementsRetainTheStrongestTypedFailure()
    {
        var scheduler = new VerificationTargetScheduler(1);
        await Task.WhenAll(Retirements.Select(retirement => Task.Run(() =>
            scheduler.RecordRetirement(retirement.Callable, retirement.Claim))));
        Assert.That(scheduler.SnapshotRetirement(), Is.EqualTo(Retirements[2]));
        Assert.That(scheduler.TryClaim(out _), Is.False);
    }
}
