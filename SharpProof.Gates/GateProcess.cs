using System.Diagnostics;

namespace SharpProof.Gates;

internal static class GateProcess
{
    internal static ProcessStartInfo CreateCaptured(
        string fileName,
        string workingDirectory)
    {
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
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            KillTree(process);
            await process.WaitForExitAsync(CancellationToken.None)
                .ConfigureAwait(false);
            throw;
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
    string Error);
