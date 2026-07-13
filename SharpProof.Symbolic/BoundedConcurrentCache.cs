namespace SharpProof.Symbolic;

internal sealed class BoundedConcurrentCache<TKey, TValue> where TKey : notnull
{
    private readonly SharpProof.ProofCore.Collections.BoundedConcurrentCache<TKey, TValue> _cache;

    internal BoundedConcurrentCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        _cache = new SharpProof.ProofCore.Collections.BoundedConcurrentCache<TKey, TValue>(capacity, comparer);
    }

    internal int Capacity => _cache.Capacity;
    internal int Count => _cache.Count;
    internal long HitCount => _cache.HitCount;
    internal long MissCount => _cache.MissCount;
    internal long EvictionCount => _cache.EvictionCount;

    internal bool TryGetValue(TKey key, out TValue value)
    {
        return _cache.TryGetValue(key, out value!);
    }

    internal TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        return _cache.GetOrAdd(key, valueFactory);
    }

    internal bool TryAdd(TKey key, TValue value)
    {
        return _cache.TryAdd(key, value);
    }
}
