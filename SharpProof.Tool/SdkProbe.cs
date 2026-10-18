using System.Diagnostics;
using System.Text;

namespace SharpProof.Tool;

internal static class SdkProbe
{
    internal static async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        // Discovery follows the caller's normal CLI selection, including global.json.
        // The result is diagnostic information and never authorizes a verifier host.
        return await RunAsync("dotnet", ["--version"], TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<string> RunAsync(
        string executable, string[] arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Discovery must have a finite positive supported deadline.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token);
        var stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            var result = await stdout.ConfigureAwait(false);
            _ = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(result))
            {
                throw new InvalidOperationException("SDK discovery failed; check dotnet --version and global.json.");
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            TerminateAndJoin(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("SDK discovery exceeded its 15-second deadline.");
        }
        finally
        {
            // Join pipe reads before their owning process and streams are disposed.
            try
            {
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A discovery timeout cancels both owned reads.
            }
        }
    }

    internal static void TerminateAndJoin(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!process.HasExited)
        {
            // .NET's tree-kill implementation tolerates a concurrent process exit.
            process.Kill(entireProcessTree: true);
        }
        // Synchronous resource teardown intentionally outlives caller cancellation.
        // Pipe reads are separately canceled and joined by the owning async finally.
        _ = process.WaitForExit(5000);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var text = new StringBuilder();
        var buffer = new char[512];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return text.ToString();
            }
            if (text.Length + read > 4096)
            {
                throw new InvalidOperationException("SDK discovery output exceeded its 4096-character limit.");
            }
            text.Append(buffer, 0, read);
        }
    }
}
