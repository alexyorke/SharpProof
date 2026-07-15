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
        startInfo.ArgumentList.Add("--no-incremental");
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

    internal static async Task<MaterializedSarifInputs> MaterializeAsync(IEnumerable<string> inputs)
    {
        var materialized = new List<MaterializedSarifInput>();
        var temporaryPaths = new List<string>();
        try
        {
            foreach (var input in inputs)
            {
                if (!IsBuildInput(input))
                {
                    materialized.Add(new MaterializedSarifInput(input, input));
                    continue;
                }

                var sarifPath = Path.Combine(Path.GetTempPath(),
                    "sharpproof-" + Guid.NewGuid().ToString("N") + ".sarif");
                temporaryPaths.Add(sarifPath);
                await RunAsync(input, sarifPath);
                materialized.Add(new MaterializedSarifInput(input, sarifPath));
            }

            return new MaterializedSarifInputs(materialized, temporaryPaths);
        }
        catch
        {
            DeleteAll(temporaryPaths);
            throw;
        }
    }

    private static bool IsBuildInput(string input)
    {
        var extension = Path.GetExtension(input);
        return string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase);
    }

    internal static void DeleteAll(IEnumerable<string> paths)
    {
        foreach (var path in paths)
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
}

internal sealed class MaterializedSarifInputs(IReadOnlyList<MaterializedSarifInput> inputs,
    IReadOnlyList<string> temporaryPaths) : IDisposable
{
    internal IReadOnlyList<MaterializedSarifInput> Inputs { get; } = inputs;

    public void Dispose() => DotnetSarifBuildRunner.DeleteAll(temporaryPaths);
}

internal readonly record struct MaterializedSarifInput(string InputName, string SarifPath);
