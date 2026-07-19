namespace SharpProof.Symbolic.Smt;

internal sealed class SmtProofSearchSessionPool
{
    private static long s_globalGeneration;

    private readonly Func<IPurityProofSearchSession> _sessionFactory;
    private readonly ThreadLocal<SessionContext?> _sessions = new(trackAllValues: true);
    private bool _disposed;

    public SmtProofSearchSessionPool(Func<IPurityProofSearchSession> sessionFactory)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    public static long GlobalGeneration => Interlocked.Read(ref s_globalGeneration);

    public IPurityProofSearchSession GetOrCreate(out bool recycledStaleSession)
    {
        ThrowIfDisposed();

        var generation = GlobalGeneration;
        var context = _sessions.Value;
        recycledStaleSession = context != null && context.Generation != generation;
        if (recycledStaleSession)
        {
            DisposeSession(context!.Session);
            _sessions.Value = null;
            context = null;
        }

        if (context == null)
        {
            context = new SessionContext(_sessionFactory(), generation);
            _sessions.Value = context;
        }

        return context.Session;
    }

    public bool RecycleCurrentThread()
    {
        ThrowIfDisposed();
        if (!_sessions.IsValueCreated || _sessions.Value == null) return false;

        DisposeSession(_sessions.Value.Session);
        _sessions.Value = null;
        return true;
    }

    public long RequestGlobalRecycle(out bool recycledCurrentThread)
    {
        ThrowIfDisposed();

        var generation = Interlocked.Increment(ref s_globalGeneration);
        recycledCurrentThread = RecycleCurrentThread();
        return generation;
    }

    public int Dispose(bool disposeOwnedSessions)
    {
        if (_disposed) return 0;

        _disposed = true;
        var disposedCount = 0;
        if (disposeOwnedSessions)
        {
            foreach (var context in _sessions.Values
                         .Where(static context => context != null)
                         .Distinct())
            {
                DisposeSession(context!.Session);
                disposedCount++;
            }
        }

        _sessions.Dispose();
        return disposedCount;
    }

    private static void DisposeSession(IPurityProofSearchSession session)
    {
        try
        {
            session.Dispose();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Disposal is best effort for a failed or stale native context.
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SmtProofSearchSessionPool));
    }

    private sealed record SessionContext(
        IPurityProofSearchSession Session,
        long Generation);
}
