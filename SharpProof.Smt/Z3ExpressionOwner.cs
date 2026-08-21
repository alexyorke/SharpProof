namespace SharpProof.Smt;

// A query owns every Expr wrapper returned while constructing its assertions.
// Z3 keeps a native reference for each managed wrapper, so relying on the
// finalizer would make a long-lived backend's native footprint nondeterministic.
internal sealed class Z3ExpressionOwner : IDisposable
{
    private readonly List<Expr> _expressions = [];
    private bool _disposed;

    internal int OwnedCount => _expressions.Count;

    internal T Own<T>(T expression)
        where T : Expr
    {
        ArgumentNullGuard.NotNull(expression, nameof(expression));
        if (_disposed)
        {
            expression.Dispose();
            throw new ObjectDisposedException(nameof(Z3ExpressionOwner));
        }

        _expressions.Add(expression);
        return expression;
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
            for (var index = _expressions.Count - 1; index >= 0; index--)
            {
                _expressions[index].Dispose();
            }
        }
        finally
        {
            _expressions.Clear();
        }
    }
}
