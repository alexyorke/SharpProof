using System.Collections.Immutable;
using System.Diagnostics;

internal static class ProcessRunner
{
    internal static ProcessStartInfo CreateStartInfo(
        string workingDirectory,
        string fileName,
        IEnumerable<string> arguments)
    {
        System.ArgumentNullException.ThrowIfNull(arguments);
        System.ArgumentNullException.ThrowIfNull(fileName);
        System.ArgumentNullException.ThrowIfNull(workingDirectory);
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
        ImmutableArray<string> arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.IsDefault)
        { throw new ArgumentNullException(nameof(arguments)); }
        System.ArgumentNullException.ThrowIfNull(fileName);
        System.ArgumentNullException.ThrowIfNull(workingDirectory);
        return RunCapturedAsync(
            CreateStartInfo(workingDirectory, fileName, arguments),
            cancellationToken);
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
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cleanup.Token)
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
    string Error)
{
    internal string CombinedOutput => Output + Environment.NewLine + Error;
    public string Output { get; init; } = Output ?? throw new System.ArgumentNullException(nameof(Output));
    public string Error { get; init; } = Error ?? throw new System.ArgumentNullException(nameof(Error));
}
