namespace SharpProof.Smt;

// A query owns every Expr wrapper returned while constructing its assertions.
// Z3 keeps a native reference for each managed wrapper, so relying on the
// finalizer would make a long-lived backend's native footprint nondeterministic.
internal sealed class Z3ExpressionOwner : IDisposable
{
    private readonly List<IDisposable> _objects = [];
    private bool _disposed;

    internal int OwnedCount => _objects.Count;

    internal T Own<T>(T expression)
        where T : Expr
    {
        return OwnDisposable(expression);
    }

    internal T OwnSort<T>(T sort)
        where T : Sort
    {
        return OwnDisposable(sort);
    }

    private T OwnDisposable<T>(T disposable)
        where T : IDisposable
    {
        ArgumentNullGuard.NotNull(disposable, nameof(disposable));
        if (_disposed)
        {
            disposable.Dispose();
            throw new ObjectDisposedException(nameof(Z3ExpressionOwner));
        }

        _objects.Add(disposable);
        return disposable;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
            {
                _objects[index].Dispose();
            }
        }
        finally
        {
            _objects.Clear();
        }
    }
}
