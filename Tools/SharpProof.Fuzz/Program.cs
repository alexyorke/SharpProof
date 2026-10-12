using System.Text.Json;
using SharpProof;

namespace SharpProof.Fuzz;

internal static class Program
{
    private const string Usage =
        "Usage: SharpProof.Fuzz [--cases N] [--seed N] [--max-parallelism 1..4]";

    internal static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            return await RunAsync(args, Console.Out, Console.Error, cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        FuzzOptions options;
        try
        {
            options = FuzzOptions.Parse(args);
        }
        catch (FuzzUsageException exception)
        {
            if (!string.IsNullOrEmpty(exception.Message))
            {
                error.WriteLine(exception.Message);
            }

            error.WriteLine(Usage);
            return string.IsNullOrEmpty(exception.Message) ? 0 : 2;
        }

        try
        {
            var summary = await FuzzRunner.RunAsync(options, cancellationToken);
            output.WriteLine(JsonSerializer.Serialize(
                summary,
                SharpProofJsonDefaults.Indented));
            return summary.Passed ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error.WriteLine("SharpProof fuzz run cancelled.");
            return 130;
        }
    }
}
