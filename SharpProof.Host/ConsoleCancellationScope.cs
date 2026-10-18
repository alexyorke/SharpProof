namespace SharpProof.Host;

/// <summary>Links console interrupts to a token for an executable entry point.</summary>
public sealed class ConsoleCancellationScope : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly ConsoleCancelEventHandler _handler;

    public ConsoleCancellationScope()
    {
        _handler = (_, eventArgs) =>
        {
            System.ArgumentNullException.ThrowIfNull(eventArgs);
            eventArgs.Cancel = true;
            _source.Cancel();
        };
        Console.CancelKeyPress += _handler;
    }

    public CancellationToken Token => _source.Token;

    public void Dispose()
    {
        Console.CancelKeyPress -= _handler;
        _source.Dispose();
    }
}
