namespace SharpProof.Worker;

internal sealed class MethodResourceBudget(
    Func<long>? readConsumedResourceCount, uint queryRlimit, uint methodRlimit)
{
    private readonly Func<long>? _readConsumedResourceCount = readConsumedResourceCount;
    private readonly long _queryRlimit = ArgumentNullGuard.RequirePositive(
        queryRlimit,
        nameof(queryRlimit));
    private readonly long _methodRlimit = methodRlimit >= queryRlimit ? methodRlimit :
        throw new ArgumentOutOfRangeException(nameof(methodRlimit));
    private readonly long _startingResourceCount = RequireNonnegative(readConsumedResourceCount?.Invoke() ?? 0);
    private long _reservedResourceCount;
    private long _completedResourceCount;
    private readonly List<ResourceCounter> _resourceCounters = [];

    internal IDisposable TrackConsumption(Func<long> readConsumedResourceCount)
    {
        ArgumentNullException.ThrowIfNull(readConsumedResourceCount);
        var counter = new ResourceCounter(this, readConsumedResourceCount);
        _resourceCounters.Add(counter);
        return counter;
    }

    // Keep the actual reservation boundary visible to native resource witnesses.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal bool TryStartQuery(bool reserveResources = true)
    {
        var consumed = GetConsumedResourceCount();
        if (consumed > _methodRlimit || _methodRlimit - consumed < _queryRlimit)
        {
            return false;
        }

        if (reserveResources && _readConsumedResourceCount == null)
        {
            _reservedResourceCount += _queryRlimit;
        }

        return true;
    }

    internal bool IsExceeded => GetConsumedResourceCount() > _methodRlimit;

    private long GetConsumedResourceCount()
    {
        var additional = _completedResourceCount;
        foreach (var counter in _resourceCounters)
        { additional = checked(additional + counter.Consumed); }
        if (_readConsumedResourceCount == null)
        { return checked(_reservedResourceCount + additional); }

        var current = _readConsumedResourceCount();
        if (current < _startingResourceCount)
        {
            throw new InvalidOperationException("The backend resource counter must be monotonic.");
        }

        return checked(current - _startingResourceCount + additional);
    }

    private sealed class ResourceCounter : IDisposable
    {
        private readonly MethodResourceBudget _owner;
        private readonly Func<long> _read;
        private readonly long _starting;
        private bool _disposed;

        internal ResourceCounter(MethodResourceBudget owner, Func<long> read)
        {
            _owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
            _read = ArgumentNullGuard.NotNull(read, nameof(read));
            _starting = RequireNonnegative(_read());
        }

        internal long Consumed
        {
            get
            {
                var current = _read();
                return current >= _starting ? current - _starting :
                    throw new InvalidOperationException("The backend resource counter must be monotonic.");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            { return; }
            _owner._completedResourceCount = checked(_owner._completedResourceCount + Consumed);
            _owner._resourceCounters.Remove(this);
            _disposed = true;
        }
    }

    private static long RequireNonnegative(long count)
    {
        return count >= 0 ? count :
        throw new InvalidOperationException("The backend resource counter cannot be negative.");
    }
}
