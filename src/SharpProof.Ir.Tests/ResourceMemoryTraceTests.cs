using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceMemoryTraceTests
{
    private static ResourceMemoryEvent Allocate(int identity, int root, int lower, int upper)
    {
        return new(ResourceMemoryEventKind.Allocate, identity, root, 0, lower, upper);
    }

    private static ResourceMemoryEvent Root(int slot, int target)
    {
        return new(ResourceMemoryEventKind.BindRoot, 0, slot, target);
    }

    private static ResourceMemoryEvent Store(int identity, int slot, int target)
    {
        return new(ResourceMemoryEventKind.StoreReference, identity, slot, target);
    }

    private static ResourceMemoryCheck Check(int output, params ResourceMemoryEvent[] events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return ResourceMemoryTrace.Evaluate([.. events], output, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public void RootOverwriteSeparatesCumulativePeakAndRetainedStorage()
    {
        var result = Check(2, Allocate(1, 0, 24, 24), Allocate(2, 0, 32, 32));
        Assert.That(result.Verified, Is.True, result.UnknownReason);
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(56, 56, 24, 24, 32, 32)));
    }

    [Test]
    public void AliasesKeepStorageLiveWithoutDoubleCounting()
    {
        var result = Check(0, Allocate(1, 0, 24, 24), Root(1, 1), Root(0, 0),
            Allocate(2, 2, 32, 32), Root(1, 0), Root(2, 0));
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(56, 56, 56, 56, 0, 0)));
    }

    [Test]
    public void ReturnedGraphIncludesSharedChildrenAndCyclesOnce()
    {
        var result = Check(1, Allocate(1, 0, 24, 24), Allocate(2, 1, 32, 32),
            Store(1, 0, 2), Store(1, 1, 2), Store(2, 0, 1), Root(1, 0), Root(0, 0));
        Assert.That(result.Verified, Is.False);
        Assert.That(result.UnknownReason, Is.EqualTo("UnownedMemoryOutput"));
        result = Check(1, Allocate(1, 0, 24, 24), Allocate(2, 1, 32, 32),
            Store(1, 0, 2), Store(1, 1, 2), Store(2, 0, 1), Root(1, 0));
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(56, 56, 0, 0, 56, 56)));
    }

    [Test]
    public void RemovedEdgesDoNotRetainFormerChildren()
    {
        var result = Check(1, Allocate(1, 0, 24, 24), Allocate(2, 1, 32, 32),
            Store(1, 0, 2), Root(1, 0), Store(1, 0, 0));
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(56, 56, 32, 32, 24, 24)));
    }

    [Test]
    public void PackingIntervalsRemainIntervalsForEveryMemoryMetric()
    {
        var result = Check(2, Allocate(1, 0, 24, 32), Allocate(2, 1, 24, 40));
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(48, 72, 24, 32, 24, 40)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(10)]
    public void MalformedAndUnownedEventsReturnUnknownWithoutPartialNumbers(int kind)
    {
        ResourceMemoryEvent[] events = kind switch
        {
            0 => [Allocate(0, 0, 24, 24)],
            1 => [Allocate(1, 0, -1, 24)],
            2 => [Allocate(1, 0, 32, 24)],
            3 => [Allocate(1, 0, 24, 24), Allocate(1, 1, 24, 24)],
            4 => [Root(0, 1)],
            5 => [Allocate(1, 0, 24, 24), Root(0, 0), Root(1, 1)],
            6 => [Allocate(1, 0, 24, 24), Store(1, 0, 2)],
            7 => [new(ResourceMemoryEventKind.Escape, 0, 0, 0)],
            8 => [new((ResourceMemoryEventKind)99, 0, 0, 0)],
            9 => [Root(-1, 0)],
            _ => [new(ResourceMemoryEventKind.BindRoot, 0, 0, 0, 1, 1)]
        };
        var result = Check(0, events);
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void EmptyTraceAndZeroByteObjectsAreValid()
    {
        Assert.That(Check(0).Bounds, Is.EqualTo(new ResourceMemoryBounds(0, 0, 0, 0, 0, 0)));
        Assert.That(Check(1, Allocate(1, 0, 0, 0)).Bounds, Is.EqualTo(new ResourceMemoryBounds(0, 0, 0, 0, 0, 0)));
        Assert.That(Check(-1).UnknownReason, Is.EqualTo("MalformedMemoryOutput"));
    }

    [Test]
    public void RepeatedRootReplacementHasConstantPeakAndLinearCumulativeCost()
    {
        for (var count = 1; count <= 100; count++)
        {
            var events = Enumerable.Range(1, count).Select(static identity => Allocate(identity, 0, 24, 24)).ToImmutableArray();
            var result = ResourceMemoryTrace.Evaluate(events, 0, TestContext.CurrentContext.CancellationToken);
            Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(count * 24, count * 24, count == 1 ? 24 : 48, count == 1 ? 24 : 48, 0, 0)));
        }
    }

    [Test]
    public void EventBudgetAndCancellationDoNotYieldPartialResults()
    {
        var events = Enumerable.Repeat(Root(0, 0), ResourceMemoryTrace.MaximumEvents + 1).ToImmutableArray();
        Assert.That(ResourceMemoryTrace.Evaluate(events, 0, TestContext.CurrentContext.CancellationToken).UnknownReason,
            Is.EqualTo("MemoryTraceBudgetExceeded"));
        events = Enumerable.Range(1, ResourceMemoryTrace.MaximumEvents)
            .Select(static identity => Allocate(identity, identity, 24, 24)).ToImmutableArray();
        var exhausted = ResourceMemoryTrace.Evaluate(events, 0, TestContext.CurrentContext.CancellationToken);
        Assert.That(exhausted.UnknownReason, Is.EqualTo("MemoryTraceBudgetExceeded"));
        Assert.That(exhausted.Bounds, Is.Null);
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => ResourceMemoryTrace.Evaluate([], 0, source.Token)));
        Assert.Throws<ArgumentException>((Action)(() => ResourceMemoryTrace.Evaluate(default, 0, TestContext.CurrentContext.CancellationToken)));
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void MemoryVerdictsCannotBeMissingOrContradictory(int mutation)
    {
        ResourceMemoryBounds? bounds = mutation == 2 ? new ResourceMemoryBounds(0, 0, 0, 0, 0, 0) : null;
        var reason = mutation == 0 ? null : mutation == 1 ? " " : "contradiction";
        Assert.Throws<ArgumentException>((Action)(() => new ResourceMemoryCheck(bounds, reason)));
    }

    [Test]
    public void MixedStorageIntervalsKeepEveryPublishedBoundDistinct()
    {
        var check = Check(2, Allocate(1, 0, 24, 32), Allocate(2, 1, 32, 40));
        Assert.That(check.Verified, Is.True, check.UnknownReason);
        var bounds = check.Bounds!.Value;
        Assert.That(bounds.CumulativeLower, Is.EqualTo(new System.Numerics.BigInteger(56)));
        Assert.That(bounds.CumulativeUpper, Is.EqualTo(new System.Numerics.BigInteger(72)));
        Assert.That(bounds.PeakAuxiliaryLower, Is.EqualTo(new System.Numerics.BigInteger(24)));
        Assert.That(bounds.PeakAuxiliaryUpper, Is.EqualTo(new System.Numerics.BigInteger(32)));
        Assert.That(bounds.RetainedLower, Is.EqualTo(new System.Numerics.BigInteger(32)));
        Assert.That(bounds.RetainedUpper, Is.EqualTo(new System.Numerics.BigInteger(40)));
    }
}
