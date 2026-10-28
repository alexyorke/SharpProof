namespace SharpProof.Tool;

internal sealed class ConsoleCancellationScope : IDisposable
{
    private readonly object _gate = new();
    private bool _disposed;
    private readonly CancellationTokenSource _source = new();
    private readonly ConsoleCancelEventHandler _handler;

    internal ConsoleCancellationScope()
    {
        _handler = (_, eventArgs) =>
        {
            ArgumentNullException.ThrowIfNull(eventArgs);
            eventArgs.Cancel = true;
            lock (_gate)
            {
                if (!_disposed)
                {
                    _source.Cancel();
                }
            }
        };
        Console.CancelKeyPress += _handler;
    }

    internal CancellationToken Token => _source.Token;

    public void Dispose()
    {
        Console.CancelKeyPress -= _handler;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _source.Dispose();
        }
    }
}
