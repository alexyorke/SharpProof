using System.Text.Json;
using SharpProof;
using SharpProof.Fuzz;

const string usage =
    "Usage: SharpProof.Fuzz [--cases N] [--seed N] [--max-parallelism 1..4]";

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

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var summary = await FuzzRunner.RunAsync(options, cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(
        summary,
        SharpProofJsonDefaults.Indented));
    return summary.Passed ? 0 : 1;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("SharpProof fuzz run cancelled.");
    return 130;
}
