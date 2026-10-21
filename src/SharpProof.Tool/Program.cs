using System.Collections.Immutable;

namespace SharpProof.Tool;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var cancellation = new ConsoleCancellationScope();
        return await RunMainAsync(args.ToImmutableArray(), cancellation.Token).ConfigureAwait(false);
    }

    internal static async Task<int> RunMainAsync(ImmutableArray<string> args, CancellationToken cancellationToken)
    {
        if (args.IsDefault)
        { throw new ArgumentNullException(nameof(args)); }
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
