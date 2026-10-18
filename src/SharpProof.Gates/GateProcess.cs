using System.Diagnostics;

namespace SharpProof.Gates;

internal static class GateProcess
{
    internal const int CleanupTimeoutSeconds = 5;

    internal static ProcessStartInfo CreateCaptured(
        string fileName,
        string workingDirectory)
    {
        System.ArgumentNullException.ThrowIfNull(fileName);
        System.ArgumentNullException.ThrowIfNull(workingDirectory);
        return new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
    }

    internal static async Task<GateProcessResult> RunCapturedAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The gate process did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        var waitCompleted = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            waitCompleted = true;
        }
        finally
        {
            if (!waitCompleted)
            {
                KillTree(process);
                // Reap the killed process even when the caller canceled.
                var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(GateProcess.CleanupTimeoutSeconds));
                try
                {
#pragma warning disable SPINT003
                    await process.WaitForExitAsync(cleanup.Token)
                        .ConfigureAwait(false);
#pragma warning restore SPINT003
                }
                finally
                {
                    cleanup.Dispose();
                }
            }
        }

        return new(
            process.ExitCode,
            await output.ConfigureAwait(false),
            await error.ConfigureAwait(false));
    }

    internal static void KillTree(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}

internal readonly record struct GateProcessResult(
    int ExitCode,
    string Output,
    string Error)
{
    public string Output { get; init; } = Output ?? throw new System.ArgumentNullException(nameof(Output));
    public string Error { get; init; } = Error ?? throw new System.ArgumentNullException(nameof(Error));
}
