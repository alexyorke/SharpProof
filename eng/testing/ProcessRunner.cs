using System.Diagnostics;

internal static class ProcessRunner
{
    internal static ProcessStartInfo CreateStartInfo(
        string workingDirectory,
        string fileName,
        IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    internal static Task<ProcessRunnerResult> RunCapturedAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        return RunCapturedAsync(
            CreateStartInfo(workingDirectory, fileName, arguments),
            CancellationToken.None);
    }

    internal static async Task<ProcessRunnerResult> RunCapturedAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "The process did not start: " + startInfo.FileName);
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken)
                .ConfigureAwait(false);
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

    private static void KillTree(Process process)
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

internal readonly record struct ProcessRunnerResult(
    int ExitCode,
    string Output,
    string Error);
