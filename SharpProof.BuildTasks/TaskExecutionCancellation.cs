namespace SharpProof.BuildTasks;

internal sealed class TaskExecutionCancellation : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly object _synchronization = new();
    private bool _disposed;

    public CancellationToken Token => _source.Token;

    public void Cancel()
    {
        lock (_synchronization)
        {
            if (_disposed)
            {
                return;
            }
        }

        try
        {
            _source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A cancellation request racing task completion is harmless.
        }
    }

    public void Dispose()
    {
        lock (_synchronization)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }
        _source.Dispose();
    }
}
