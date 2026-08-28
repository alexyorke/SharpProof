using System.Text.Json;
using SharpProof.Fuzz;

const string usage =
    "Usage: SharpProof.Fuzz [--cases N] [--seed N] [--replay-index N] [--max-parallelism 1..4]";

FuzzOptions options;
try
{
    options = FuzzOptions.Parse(args);
}
catch (FuzzUsageException exception)
{
    if (!string.IsNullOrEmpty(exception.Message))
    {
        Console.Error.WriteLine(exception.Message);
    }

    Console.Error.WriteLine(usage);
    return string.IsNullOrEmpty(exception.Message) ? 0 : 2;
}

using var cancellation = new CancellationGate();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    var summary = await FuzzRunner.RunAsync(options, cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(
        summary,
        FuzzJson.Indented));
    return summary.Passed ? 0 : 1;
}
#pragma warning disable SPMETA003 // The CLI translates cancellation into its documented process exit code.
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("SharpProof fuzz run cancelled.");
    return 130;
}
#pragma warning restore SPMETA003
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

file static class FuzzJson
{
    internal static JsonSerializerOptions Indented
    {
        get;
    } =
        new()
        {
            WriteIndented = true
        };
}

file sealed class CancellationGate : IDisposable
{
    private readonly object _synchronization = new();
    private readonly CancellationTokenSource _source = new();
    private bool _disposing;
    private int _callbacks;

    internal CancellationToken Token => _source.Token;

    internal bool IsCancellationRequested => _source.IsCancellationRequested;

    internal void Cancel()
    {
        lock (_synchronization)
        {
            if (_disposing)
            {
                return;
            }

            _callbacks++;
        }

        try
        {
            _source.Cancel();
        }
        finally
        {
            lock (_synchronization)
            {
                _callbacks--;
                if (_callbacks == 0)
                {
                    Monitor.PulseAll(_synchronization);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_synchronization)
        {
            _disposing = true;
            while (_callbacks != 0)
            {
                Monitor.Wait(_synchronization);
            }
        }

        _source.Dispose();
    }
}
