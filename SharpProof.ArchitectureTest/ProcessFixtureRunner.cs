using System.Diagnostics;

namespace SharpProof.ArchitectureTest;

internal readonly record struct ProcessFixtureResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

internal static class ProcessFixtureRunner
{
    internal static async Task<ProcessFixtureResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            timeout,
            TimeSpan.Zero,
            nameof(timeout));

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "The process fixture could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var timedOut = false;
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            timedOut = true;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited between the check and Kill.
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        return new(process.ExitCode, output, error, timedOut);
    }
}
