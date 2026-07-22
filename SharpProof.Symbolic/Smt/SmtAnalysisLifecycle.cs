namespace SharpProof.Symbolic.Smt;
internal enum SmtAnalysisHealthState {
    Ready,
    Degraded,
    PermanentlyUnavailable,
    Disposed
}
internal sealed class SmtSolverLifecycleOptions(
    int maxTransientRetries = 1,
    bool recycleContextOnTransientFailure = true,
    bool disposeCurrentThreadContextOnServiceDispose = true) {
    public static readonly SmtSolverLifecycleOptions Default = new();
    public int MaxTransientRetries { get; } = maxTransientRetries >= 0
        ? maxTransientRetries
        : throw new ArgumentOutOfRangeException(nameof(maxTransientRetries), "Transient retry count cannot be negative.");
    public bool RecycleContextOnTransientFailure { get; } = recycleContextOnTransientFailure;
    public bool DisposeCurrentThreadContextOnServiceDispose { get; } = disposeCurrentThreadContextOnServiceDispose;
}
internal sealed record SmtAnalysisHealth(
    SmtAnalysisHealthState State,
    string LastFailureCode,
    int ConsecutiveTransientFailureCount,
    int TransientRetryCount,
    int RecoveredTransientFailureCount,
    int ContextRecycleCount);
