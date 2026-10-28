namespace SharpProof.Tool;

internal sealed class ConsoleCancellationScope : IDisposable
{
    private readonly object _gate = new();
    private bool _disposed;
    private int _activeCancellations;
    private readonly CancellationTokenSource _source = new();
    private readonly ConsoleCancelEventHandler _handler;

    internal ConsoleCancellationScope()
    {
        _handler = (_, eventArgs) =>
        {
            ArgumentNullException.ThrowIfNull(eventArgs);
            eventArgs.Cancel = true;
            Cancel();
        };
        Console.CancelKeyPress += _handler;
    }

    internal CancellationToken Token => _source.Token;

    private void Cancel()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _activeCancellations++;
        }
        try
        {
            // Cancellation invokes registered callbacks; never hold our lifetime gate here.
            _source.Cancel();
        }
        finally
        {
            bool disposeSource;
            lock (_gate)
            {
                _activeCancellations--;
                disposeSource = _disposed && _activeCancellations == 0;
            }
            if (disposeSource)
            {
                _source.Dispose();
            }
        }
    }

    public void Dispose()
    {
        bool disposeSource;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            disposeSource = _activeCancellations == 0;
        }
        Console.CancelKeyPress -= _handler;
        if (disposeSource)
        {
            _source.Dispose();
        }
    }
}
