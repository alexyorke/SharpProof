namespace SharpProof.Worker;

internal sealed class MethodResourceBudget(
    Func<long>? readConsumedResourceCount, uint queryRlimit, uint methodRlimit)
{
    private readonly Func<long>? _readConsumedResourceCount = readConsumedResourceCount;
    private readonly long _queryRlimit = queryRlimit > 0 ? queryRlimit :
        throw new ArgumentOutOfRangeException(nameof(queryRlimit));
    private readonly long _methodRlimit = methodRlimit >= queryRlimit ? methodRlimit :
        throw new ArgumentOutOfRangeException(nameof(methodRlimit));
    private readonly long _startingResourceCount = RequireNonnegative(readConsumedResourceCount?.Invoke() ?? 0);
    private long _reservedResourceCount;

    internal bool TryStartQuery()
    {
        var consumed = GetConsumedResourceCount();
        if (consumed > _methodRlimit || _methodRlimit - consumed < _queryRlimit)
        {
            return false;
        }

        if (_readConsumedResourceCount == null)
        {
            _reservedResourceCount += _queryRlimit;
        }

        return true;
    }

    internal bool IsExceeded => _readConsumedResourceCount != null && GetConsumedResourceCount() > _methodRlimit;

    private long GetConsumedResourceCount()
    {
        if (_readConsumedResourceCount == null)
        {
            return _reservedResourceCount;
        }

        var current = _readConsumedResourceCount();
        if (current < _startingResourceCount)
        {
            throw new InvalidOperationException("The backend resource counter must be monotonic.");
        }

        return current - _startingResourceCount;
    }

    private static long RequireNonnegative(long count)
    {
        return count >= 0 ? count :
        throw new InvalidOperationException("The backend resource counter cannot be negative.");
    }
}
