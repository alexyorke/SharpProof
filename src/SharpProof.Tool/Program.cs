namespace SharpProof.Tool;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var cancellation = new ConsoleCancellationScope();
        return await RunMainAsync(args, cancellation.Token).ConfigureAwait(false);
    }

    internal static async Task<int> RunMainAsync(string[] args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        try
        {
            return await ToolApplication.RunAsync(args, Console.Out, Console.Error, SdkProbe.ReadAsync, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
    }
}
