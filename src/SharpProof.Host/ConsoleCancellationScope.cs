using System.Runtime.InteropServices;

namespace SharpProof.Host;

/// <summary>Links console interrupts to a token for an executable entry point.</summary>
internal sealed class ConsoleCancellationScope : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _source;
    private readonly ConsoleCancelEventHandler _handler;
    private readonly PosixSignalRegistration? _terminate;
    private bool _disposed;

#pragma warning disable SPINT005 // The executable entry point owns console cancellation; no caller token exists yet.
    public ConsoleCancellationScope() : this(handleTermination: false, CancellationToken.None)
    {
    }
#pragma warning restore SPINT005

    internal ConsoleCancellationScope(bool handleTermination, CancellationToken cancellationToken)
    {
        _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _handler = (_, eventArgs) =>
        {
            ArgumentNullException.ThrowIfNull(eventArgs);
            eventArgs.Cancel = true;
            Cancel();
        };
        Console.CancelKeyPress += _handler;
        if (handleTermination)
        {
            _terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                ArgumentNullException.ThrowIfNull(context);
                context.Cancel = true;
                Cancel();
            });
        }
    }

    public CancellationToken Token => _source.Token;

    private void Cancel()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _source.Cancel();
            }
        }
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= _handler;
        _terminate?.Dispose();
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
