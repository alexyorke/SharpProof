namespace SharpProof.Tool;

internal sealed class ConsoleCancellationScope : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly ConsoleCancelEventHandler _handler;

    internal ConsoleCancellationScope()
    {
        _handler = (_, eventArgs) =>
        {
            ArgumentNullException.ThrowIfNull(eventArgs);
            eventArgs.Cancel = true;
            _source.Cancel();
        };
        Console.CancelKeyPress += _handler;
    }

    internal CancellationToken Token => _source.Token;

    public void Dispose()
    {
        Console.CancelKeyPress -= _handler;
        _source.Dispose();
    }
}
