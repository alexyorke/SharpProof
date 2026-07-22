namespace SharpProof.Symbolic.Smt;
internal sealed class SmtProofSearchSessionPool(Func<IAnalysisProofSearchSession> sessionFactory) {
    private readonly Func<IAnalysisProofSearchSession> _sessionFactory =
        sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    private readonly ThreadLocal<IAnalysisProofSearchSession?> _sessions = new(trackAllValues: true);
    private bool _disposed;
    public IAnalysisProofSearchSession GetOrCreate() {
        ThrowIfDisposed();
        return _sessions.Value ??= _sessionFactory();
    }
    public bool RecycleCurrentThread() {
        ThrowIfDisposed();
        if (!_sessions.IsValueCreated || _sessions.Value == null) return false;
        DisposeSession(_sessions.Value);
        _sessions.Value = null;
        return true;
    }
    public int Dispose(bool disposeOwnedSessions) {
        if (_disposed) return 0;
        _disposed = true;
        var disposedCount = 0;
        if (disposeOwnedSessions) {
            foreach (var session in _sessions.Values
                         .Where(static session => session != null)
                         .Distinct()) {
                DisposeSession(session!);
                disposedCount++;
            }
        }
        _sessions.Dispose();
        return disposedCount;
    }
    private static void DisposeSession(IAnalysisProofSearchSession session) {
        try {
            session.Dispose();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            // Disposal is best effort for a failed or stale native context.
        }
    }
    private void ThrowIfDisposed() {
        if (_disposed) throw new ObjectDisposedException(nameof(SmtProofSearchSessionPool));
    }
}
