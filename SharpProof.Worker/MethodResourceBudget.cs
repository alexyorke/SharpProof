namespace SharpProof.Worker;

internal sealed class MethodResourceBudget {
    private readonly Func<long>? _readConsumedResourceCount;
    private readonly long _queryRlimit;
    private readonly long _methodRlimit;
    private readonly long _startingResourceCount;
    private long _reservedResourceCount;

    internal MethodResourceBudget(
        Func<long>? readConsumedResourceCount,
        uint queryRlimit,
        uint methodRlimit) {
        _readConsumedResourceCount = readConsumedResourceCount;
        if (queryRlimit == 0)
            throw new ArgumentOutOfRangeException(nameof(queryRlimit));
        if (methodRlimit < queryRlimit)
            throw new ArgumentOutOfRangeException(nameof(methodRlimit));
        _queryRlimit = queryRlimit;
        _methodRlimit = methodRlimit;
        _startingResourceCount =
            _readConsumedResourceCount?.Invoke() ?? 0;
        if (_startingResourceCount < 0)
            throw new InvalidOperationException(
                "The backend resource counter cannot be negative.");
    }

    internal bool TryStartQuery() {
        var consumed = GetConsumedResourceCount();
        if (consumed > _methodRlimit ||
            _methodRlimit - consumed < _queryRlimit)
            return false;
        if (_readConsumedResourceCount == null)
            _reservedResourceCount += _queryRlimit;
        return true;
    }

    internal bool IsExceeded =>
        _readConsumedResourceCount != null &&
        GetConsumedResourceCount() > _methodRlimit;

    private long GetConsumedResourceCount() {
        if (_readConsumedResourceCount == null)
            return _reservedResourceCount;
        var current = _readConsumedResourceCount();
        if (current < _startingResourceCount)
            throw new InvalidOperationException(
                "The backend resource counter must be monotonic.");
        return current - _startingResourceCount;
    }
}
