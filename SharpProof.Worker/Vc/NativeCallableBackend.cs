namespace SharpProof.Worker;

// A verification lane calls this backend sequentially. Each new callable
// factory retires the preceding native session while retaining its resource
// count, so method meters can use one monotonic reader across the lane.
internal sealed class NativeCallableBackend : ISmtBackend, IDisposable
{
    private readonly IrSmtBackendOptions _options;
    private CallableSolverSession? _session;
    private IrFactory? _factory;
    private long _completedResourceCount;
    private bool _disposed;

    internal NativeCallableBackend(IrSmtBackendOptions options)
    {
        _options = ArgumentNullGuard.NotNull(options, nameof(options));
        // Match the existing worker's eager native-library availability check.
        using var probe = new Microsoft.Z3.Context();
    }

    internal long ConsumedResourceCount => _completedResourceCount + (_session?.ConsumedResourceCount ?? 0);

    public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(query, nameof(query));
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (query.Factory.Semantics != IrExecutionSemantics.Total)
        { return Task.FromResult(BackendCheckResult.Unknown(BackendFailureReason.UnsupportedEncoding)); }
        if (!ReferenceEquals(query.Factory, _factory))
        {
            RetireSession();
            _session = new CallableSolverSession(query.Factory, _options);
            _factory = query.Factory;
        }
        return _session!.CheckAsync(query, cancellationToken);
    }

    private void RetireSession()
    {
        var prior = _session;
        _session = null;
        _factory = null;
        if (prior != null)
        {
            _completedResourceCount += prior.ConsumedResourceCount;
            prior.Dispose();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            RetireSession();
        }
        GC.SuppressFinalize(this);
    }
}
