using SearchLib.Purity;

namespace SharpProof.Symbolic.Smt;

public enum SmtAnalysisHealthState
{
    Disabled,
    Ready,
    Degraded,
    PermanentlyUnavailable,
    Disposed
}

public enum SmtSolverContextRecycleScope
{
    CurrentThread,
    AllThreadsOnNextUse
}

public sealed class SmtSolverLifecycleOptions
{
    public static readonly SmtSolverLifecycleOptions Default = new();

    public SmtSolverLifecycleOptions(
        int maxTransientRetries = 1,
        bool recycleContextOnTransientFailure = true,
        bool disposeCurrentThreadContextOnServiceDispose = false)
    {
        if (maxTransientRetries < 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxTransientRetries),
                "Transient retry count cannot be negative.");

        MaxTransientRetries = maxTransientRetries;
        RecycleContextOnTransientFailure = recycleContextOnTransientFailure;
        DisposeCurrentThreadContextOnServiceDispose = disposeCurrentThreadContextOnServiceDispose;
    }

    public int MaxTransientRetries { get; }

    public bool RecycleContextOnTransientFailure { get; }

    public bool DisposeCurrentThreadContextOnServiceDispose { get; }
}

public sealed class SmtAnalysisHealth
{
    internal SmtAnalysisHealth(
        SmtAnalysisHealthState state,
        string lastFailureCode,
        int consecutiveTransientFailureCount,
        int transientRetryCount,
        int recoveredTransientFailureCount,
        int contextRecycleCount,
        long contextGeneration)
    {
        State = state;
        LastFailureCode = lastFailureCode ?? string.Empty;
        ConsecutiveTransientFailureCount = consecutiveTransientFailureCount;
        TransientRetryCount = transientRetryCount;
        RecoveredTransientFailureCount = recoveredTransientFailureCount;
        ContextRecycleCount = contextRecycleCount;
        ContextGeneration = contextGeneration;
    }

    public SmtAnalysisHealthState State { get; }

    public bool IsAvailable => State == SmtAnalysisHealthState.Ready;

    public bool IsPermanentlyUnavailable => State == SmtAnalysisHealthState.PermanentlyUnavailable;

    public string LastFailureCode { get; }

    public int ConsecutiveTransientFailureCount { get; }

    public int TransientRetryCount { get; }

    public int RecoveredTransientFailureCount { get; }

    public int ContextRecycleCount { get; }

    public long ContextGeneration { get; }
}

public sealed class SmtSolverContextRecycleResult
{
    internal SmtSolverContextRecycleResult(
        SmtSolverContextRecycleScope scope,
        bool disposedCurrentThreadContext,
        long requestedGeneration,
        int localCacheEntryCount,
        int sharedCacheEntryCount)
    {
        Scope = scope;
        DisposedCurrentThreadContext = disposedCurrentThreadContext;
        RequestedGeneration = requestedGeneration;
        LocalCacheEntryCount = localCacheEntryCount;
        SharedCacheEntryCount = sharedCacheEntryCount;
    }

    public SmtSolverContextRecycleScope Scope { get; }

    public bool DisposedCurrentThreadContext { get; }

    public long RequestedGeneration { get; }

    public int LocalCacheEntryCount { get; }

    public int SharedCacheEntryCount { get; }
}

internal interface ISmtProofSearchSession : IDisposable
{
    long ConsumedResourceCount { get; }

    PurityProofResult Classify(PurityProofQuery query, TimeSpan timeout);
}

internal sealed class SearchLibProofSearchSession : ISmtProofSearchSession
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
