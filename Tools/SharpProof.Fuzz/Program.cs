using SharpProof.Fuzz;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await FuzzRunner.RunCliAsync(
    args,
    Console.Out,
    Console.Error,
    cancellation.Token);
