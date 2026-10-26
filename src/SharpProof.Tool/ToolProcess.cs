using System.Diagnostics;
using System.Text;

namespace SharpProof.Tool;

internal static class ToolProcess
{
    internal const int MaximumOutputCharacters = 16 * 1024 * 1024;

    internal static Task<(int ExitCode, string Output)> RunAsync(string command, string directory, CancellationToken cancellationToken, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(arguments);
        return RunAsync(command, directory, TimeSpan.FromSeconds(30), cancellationToken, arguments);
    }

    internal static async Task<(int ExitCode, string Output)> RunAsync(string command, string directory, TimeSpan deadline, CancellationToken cancellationToken, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(deadline, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(command)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        { start.ArgumentList.Add(argument); }
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deadline);
        // Direct execution starts a fresh process or throws; shell reuse is disabled.
        process.Start();
        var output = ReadOutputAsync(process.StandardOutput, timeout.Token);
        var errors = DrainAsync(process.StandardError, timeout.Token);
        var exit = process.WaitForExitAsync(timeout.Token);
        try
        {
            // Observe a failed reader immediately, even when the child is still running.
            var pending = new List<Task> { output, errors, exit };
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                pending.Remove(completed);
            }
            return (process.ExitCode, await output.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TerminateAndJoin(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException(FormattableString.Invariant($"{command} did not finish within {deadline.TotalSeconds} seconds."));
        }
        catch
        {
            TerminateAndJoin(process);
            throw;
        }
        finally
        {
            // Cancel and join all owned operations before disposing their streams.
            await timeout.CancelAsync().ConfigureAwait(false);
            try
            { await Task.WhenAll(output, errors, exit).ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or InvalidOperationException) { }
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

    private static async Task<string> ReadOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var output = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            { return output.ToString(); }
            if (output.Length > MaximumOutputCharacters - read)
            { throw new IOException("Tool output exceeded the 16777216-character limit."); }
            output.Append(buffer, 0, read);
        }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        // Diagnostics are not returned to callers; do not retain a second unbounded string.
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
        { }
    }
}
