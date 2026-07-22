using System.Diagnostics;
using NUnit.Framework;
namespace SharpProof.Test;
internal static class SymbolicCliTestHost {
    private static readonly SemaphoreSlim BuildGate = new(1, 1);
    private static readonly Lazy<string> RepositoryRoot = new(AnalyzerTestHost.GetRepositoryRoot);
    private static readonly Lazy<string> BuildConfiguration = new(FindBuildConfiguration);
    private static readonly Lazy<Task<string>> CliAssemblyPath =
        new(() => EnsureCliAssemblyPathAsync(RepositoryRoot.Value));
    public static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(params string[] arguments) {
        var repositoryRoot = RepositoryRoot.Value;
        var cliAssemblyPath = await CliAssemblyPath.Value.ConfigureAwait(false);
        var startInfo = new ProcessStartInfo {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(cliAssemblyPath);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return await RunProcessAsync(startInfo, TimeSpan.FromSeconds(90), "Failed to start symbolic CLI.")
            .ConfigureAwait(false);
    }
    private static async Task<string> EnsureCliAssemblyPathAsync(string repositoryRoot) {
        var existingPath = FindExistingCliAssemblyPath(repositoryRoot);
        if (existingPath != null) return existingPath;
        await BuildGate.WaitAsync().ConfigureAwait(false);
        try {
            existingPath = FindExistingCliAssemblyPath(repositoryRoot);
            if (existingPath != null) return existingPath;
            var buildConfiguration = FindBuildConfiguration();
            var startInfo = new ProcessStartInfo {
                FileName = "dotnet",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("build");
            startInfo.ArgumentList.Add(Path.Combine("Tools", "SharpProof.SymbolicCli", "SharpProof.SymbolicCli.csproj"));
            startInfo.ArgumentList.Add("--configuration");
            startInfo.ArgumentList.Add(buildConfiguration);
            startInfo.ArgumentList.Add("--verbosity");
            startInfo.ArgumentList.Add("minimal");
            startInfo.ArgumentList.Add("/m:1");
            startInfo.ArgumentList.Add("/nodeReuse:false");
            startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
            var buildResult = await RunProcessAsync(startInfo, TimeSpan.FromSeconds(420),
                "Failed to start symbolic CLI build.").ConfigureAwait(false);
            if (buildResult.ExitCode != 0)
                throw new InvalidOperationException(
                    "Building SharpProof.SymbolicCli failed." + Environment.NewLine +
                    buildResult.StandardOutput + Environment.NewLine +
                    buildResult.StandardError);
            existingPath = FindExistingCliAssemblyPath(repositoryRoot);
            if (existingPath != null) return existingPath;
        }
        finally {
            BuildGate.Release();
        }
        throw new FileNotFoundException(
            "Could not find built SharpProof.SymbolicCli.dll after building it on demand.",
            Path.Combine(repositoryRoot, "Tools", "SharpProof.SymbolicCli"));
    }
    private static string? FindExistingCliAssemblyPath(string repositoryRoot) {
        var targetFramework = Path.GetFileName(TestContext.CurrentContext.TestDirectory);
        var configurations = new[] {
                BuildConfiguration.Value,
                "Release",
                "Debug"
            }
            .Where(static configuration => !string.IsNullOrWhiteSpace(configuration))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var configuration in configurations) {
            var candidate = Path.Combine(
                repositoryRoot,
                "Tools",
                "SharpProof.SymbolicCli",
                "bin",
                configuration,
                targetFramework,
                "SharpProof.SymbolicCli.dll");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        string startFailureMessage) {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException(startFailureMessage);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try {
            await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            process.Kill(true);
            throw;
        }
        return (process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }
    private static string FindBuildConfiguration() {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null) {
            if (string.Equals(directory.Name, "Release", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(directory.Name, "Debug", StringComparison.OrdinalIgnoreCase))
                return directory.Name;
            directory = directory.Parent;
        }
        return "Debug";
    }
}
