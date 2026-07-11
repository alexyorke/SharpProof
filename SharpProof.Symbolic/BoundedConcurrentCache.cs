using System.Collections.Generic;

namespace SharpProof.Symbolic;

internal sealed class BoundedConcurrentCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, TValue> _entries;
    private readonly object _gate = new();
    private readonly Queue<TKey> _insertionOrder = new();
    private long _evictions;
    private long _hits;
    private long _misses;

    internal BoundedConcurrentCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _entries = new Dictionary<TKey, TValue>(comparer ?? EqualityComparer<TKey>.Default);
    }

    internal int Capacity => _capacity;

    internal int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    internal long HitCount
    {
        get
        {
            lock (_gate)
                return _hits;
        }
    }

    internal long MissCount
    {
        get
        {
            lock (_gate)
                return _misses;
        }
    }

    internal long EvictionCount
    {
        get
        {
            lock (_gate)
                return _evictions;
        }
    }

    internal bool TryGetValue(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out value!))
            {
                _hits++;
                return true;
            }

            _misses++;
            value = default!;
            return false;
        }
    }

    internal bool TryAdd(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_entries.ContainsKey(key)) return false;

            while (_entries.Count >= _capacity)
            {
                if (_insertionOrder.Count == 0)
                    throw new InvalidOperationException("Cache insertion order is inconsistent with its entries.");

                var oldest = _insertionOrder.Dequeue();
                if (_entries.Remove(oldest)) _evictions++;
            }

            _entries.Add(key, value);
            _insertionOrder.Enqueue(key);
            return true;
        }
    }
}
