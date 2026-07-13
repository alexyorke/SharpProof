using System.Diagnostics;

namespace SharpProof.Tools.Shared;

internal static class DotnetSarifBuildRunner
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(5);

    internal static async Task RunAsync(string input, string sarifPath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(input);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("/p:ErrorLog=" + sarifPath);

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start dotnet build.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(BuildTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await Task.WhenAll(outputTask, errorTask);
            throw new TimeoutException("dotnet build did not complete within 5 minutes.");
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                "dotnet build failed with exit code " + process.ExitCode + "." + Environment.NewLine +
                output + Environment.NewLine + error);

        if (!File.Exists(sarifPath))
            throw new InvalidOperationException(
                "dotnet build did not produce a SARIF error log." + Environment.NewLine +
                output + Environment.NewLine + error);
    }

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
