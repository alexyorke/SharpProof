using System.Text.Json.Serialization;

namespace SharpProof.Symbolic.Smt;

internal enum SmtAnalysisHealthState {
    Disabled,
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
        : throw new ArgumentOutOfRangeException(
            nameof(maxTransientRetries), "Transient retry count cannot be negative.");
    public bool RecycleContextOnTransientFailure { get; } = recycleContextOnTransientFailure;
    public bool DisposeCurrentThreadContextOnServiceDispose { get; } = disposeCurrentThreadContextOnServiceDispose;

}

internal sealed record SmtAnalysisHealth(
    [property: JsonPropertyOrder(0)] SmtAnalysisHealthState State,
    [property: JsonPropertyOrder(3)] string LastFailureCode,
    [property: JsonPropertyOrder(4)] int ConsecutiveTransientFailureCount,
    [property: JsonPropertyOrder(5)] int TransientRetryCount,
    [property: JsonPropertyOrder(6)] int RecoveredTransientFailureCount,
    [property: JsonPropertyOrder(7)] int ContextRecycleCount,
    [property: JsonPropertyOrder(8)] long ContextGeneration) {
    [JsonPropertyOrder(1)]
    public bool IsAvailable => State == SmtAnalysisHealthState.Ready;

    [JsonPropertyOrder(2)]
    public bool IsPermanentlyUnavailable => State == SmtAnalysisHealthState.PermanentlyUnavailable;
}
