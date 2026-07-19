using SharpProof.ProofCore.Purity;

namespace SharpProof.Symbolic.Smt;

internal enum SmtAnalysisHealthState
{
    Disabled,
    Ready,
    Degraded,
    PermanentlyUnavailable,
    Disposed
}

internal enum SmtSolverContextRecycleScope
{
    CurrentThread,
    AllThreadsOnNextUse
}

internal sealed class SmtSolverLifecycleOptions(
    int maxTransientRetries = 1,
    bool recycleContextOnTransientFailure = true,
    bool disposeCurrentThreadContextOnServiceDispose = true)
{
    public static readonly SmtSolverLifecycleOptions Default = new();

    public int MaxTransientRetries { get; } = ValidateRetryCount(maxTransientRetries);
    public bool RecycleContextOnTransientFailure { get; } = recycleContextOnTransientFailure;
    public bool DisposeCurrentThreadContextOnServiceDispose { get; } = disposeCurrentThreadContextOnServiceDispose;

    private static int ValidateRetryCount(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxTransientRetries),
                "Transient retry count cannot be negative.");
        return value;
    }
}

internal sealed class SmtAnalysisHealth(
    SmtAnalysisHealthState state,
    string lastFailureCode,
    int consecutiveTransientFailureCount,
    int transientRetryCount,
    int recoveredTransientFailureCount,
    int contextRecycleCount,
    long contextGeneration)
{
    public SmtAnalysisHealthState State { get; } = state;

    public bool IsAvailable => State == SmtAnalysisHealthState.Ready;

    public bool IsPermanentlyUnavailable => State == SmtAnalysisHealthState.PermanentlyUnavailable;

    public string LastFailureCode { get; } = lastFailureCode ?? string.Empty;
    public int ConsecutiveTransientFailureCount { get; } = consecutiveTransientFailureCount;
    public int TransientRetryCount { get; } = transientRetryCount;
    public int RecoveredTransientFailureCount { get; } = recoveredTransientFailureCount;
    public int ContextRecycleCount { get; } = contextRecycleCount;
    public long ContextGeneration { get; } = contextGeneration;
}

internal sealed class SmtSolverContextRecycleResult(
    SmtSolverContextRecycleScope scope,
    bool disposedCurrentThreadContext,
    long requestedGeneration,
    int localCacheEntryCount,
    int sharedCacheEntryCount)
{
    public SmtSolverContextRecycleScope Scope { get; } = scope;
    public bool DisposedCurrentThreadContext { get; } = disposedCurrentThreadContext;
    public long RequestedGeneration { get; } = requestedGeneration;
    public int LocalCacheEntryCount { get; } = localCacheEntryCount;
    public int SharedCacheEntryCount { get; } = sharedCacheEntryCount;
}

internal interface ISmtProofSearchSession : IDisposable
{
    long ConsumedResourceCount { get; }

    PurityProofResult Classify(PurityProofQuery query, TimeSpan timeout);
}

internal sealed class ProofCoreProofSearchSession : ISmtProofSearchSession
{
    private readonly PurityProofSearch _search = new();

    public long ConsumedResourceCount => _search.ConsumedResourceCount;

    public PurityProofResult Classify(PurityProofQuery query, TimeSpan timeout)
    {
        return _search.Classify(query, timeout);
    }

    public void Dispose()
    {
        _search.Dispose();
    }
}
