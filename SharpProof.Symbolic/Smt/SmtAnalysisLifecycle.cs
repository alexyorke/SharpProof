using System.Text.Json.Serialization;

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

internal sealed record SmtAnalysisHealth(
    [property: JsonPropertyOrder(0)] SmtAnalysisHealthState State,
    [property: JsonPropertyOrder(3)] string LastFailureCode,
    [property: JsonPropertyOrder(4)] int ConsecutiveTransientFailureCount,
    [property: JsonPropertyOrder(5)] int TransientRetryCount,
    [property: JsonPropertyOrder(6)] int RecoveredTransientFailureCount,
    [property: JsonPropertyOrder(7)] int ContextRecycleCount,
    [property: JsonPropertyOrder(8)] long ContextGeneration)
{
    [JsonPropertyOrder(1)]
    public bool IsAvailable => State == SmtAnalysisHealthState.Ready;

    [JsonPropertyOrder(2)]
    public bool IsPermanentlyUnavailable => State == SmtAnalysisHealthState.PermanentlyUnavailable;
}

internal sealed record SmtSolverContextRecycleResult(
    SmtSolverContextRecycleScope Scope,
    bool DisposedCurrentThreadContext,
    long RequestedGeneration,
    int LocalCacheEntryCount,
    int SharedCacheEntryCount);

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
