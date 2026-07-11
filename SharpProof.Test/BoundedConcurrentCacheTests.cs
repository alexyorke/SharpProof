using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public class BoundedConcurrentCacheTests
{
    [Test]
    public void ConcurrentUniqueAdds_StayBoundedAndExposeTelemetry()
    {
        const int capacity = 32;
        const int valueCount = 4096;
        var cache = new BoundedConcurrentCache<int, int>(capacity);

        Parallel.For(0, valueCount, value =>
        {
            cache.TryAdd(value, value * 2);
            if (cache.TryGetValue(value, out var cached))
                Assert.That(cached, Is.EqualTo(value * 2));
        });

        cache.TryAdd(-1, -2);
        Assert.That(cache.TryGetValue(-1, out var sentinel), Is.True);
        Assert.That(sentinel, Is.EqualTo(-2));
        Assert.That(cache.TryGetValue(int.MinValue, out _), Is.False);
        Assert.That(cache.Count, Is.EqualTo(capacity));
        Assert.That(cache.HitCount, Is.GreaterThan(0));
        Assert.That(cache.MissCount, Is.GreaterThan(0));
        Assert.That(cache.EvictionCount, Is.EqualTo(valueCount + 1 - capacity));
    }

    [Test]
    public void Eviction_PreservesResultsComparedWithNoCache()
    {
        var cache = new BoundedConcurrentCache<int, int>(4);
        var inputs = Enumerable.Range(0, 64).Concat(Enumerable.Range(0, 64)).ToArray();

        var cachedResults = inputs.Select(input => GetOrCompute(cache, input)).ToArray();
        var uncachedResults = inputs.Select(static input => checked(input * input)).ToArray();

        Assert.That(cachedResults, Is.EqualTo(uncachedResults));
        Assert.That(cache.Count, Is.EqualTo(4));
        Assert.That(cache.EvictionCount, Is.GreaterThan(0));
    }

    private static int GetOrCompute(BoundedConcurrentCache<int, int> cache, int input)
    {
        if (cache.TryGetValue(input, out var value)) return value;

        value = checked(input * input);
        cache.TryAdd(input, value);
        return value;
    }
}
