using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;

namespace PurelySharp.Test
{
    internal static class SymbolicCliTestHost
    {
        private static readonly SemaphoreSlim BuildGate = new(1, 1);
        private static readonly SemaphoreSlim InvocationGate = new(1, 1);
        private static readonly Lazy<string> RepositoryRoot = new(FindRepositoryRoot);
        private static readonly Lazy<string> BuildConfiguration = new(FindBuildConfiguration);
        private static readonly Lazy<Task<string>> CliAssemblyPath = new(() => EnsureCliAssemblyPathAsync(RepositoryRoot.Value));
        private static readonly Lazy<Task<MethodInfo>> CliEntryPoint = new(LoadCliEntryPointAsync);

        public static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(params string[] arguments)
        {
            var entryPoint = await CliEntryPoint.Value.ConfigureAwait(false);
            await InvocationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var standardOutput = new StringWriter();
                var standardError = new StringWriter();
                var originalOut = Console.Out;
                var originalError = Console.Error;
                var originalDirectory = Environment.CurrentDirectory;
                Console.SetOut(standardOutput);
                Console.SetError(standardError);
                Environment.CurrentDirectory = RepositoryRoot.Value;
                try
                {
                    var exitCode = await InvokeEntryPointAsync(entryPoint, arguments).ConfigureAwait(false);
                    return (exitCode, standardOutput.ToString(), standardError.ToString());
                }
                finally
                {
                    Environment.CurrentDirectory = originalDirectory;
                    Console.SetOut(originalOut);
                    Console.SetError(originalError);
                }
            }
            finally
            {
                InvocationGate.Release();
            }
        }

        public static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunOutOfProcessAsync(params string[] arguments)
        {
            var repositoryRoot = RepositoryRoot.Value;
            var cliAssemblyPath = await CliAssemblyPath.Value.ConfigureAwait(false);
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(cliAssemblyPath);
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return await RunProcessAsync(startInfo, TimeSpan.FromSeconds(90), "Failed to start symbolic CLI.").ConfigureAwait(false);
        }

        private static async Task<MethodInfo> LoadCliEntryPointAsync()
        {
            var assemblyPath = await CliAssemblyPath.Value.ConfigureAwait(false);
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.Location, assemblyPath, StringComparison.OrdinalIgnoreCase))
                ?? Assembly.LoadFrom(assemblyPath);
            return assembly.EntryPoint ?? throw new InvalidOperationException("Could not find symbolic CLI entry point.");
        }

        private static async Task<int> InvokeEntryPointAsync(MethodInfo entryPoint, string[] arguments)
        {
            object? invocationResult;
            try
            {
                invocationResult = entryPoint.GetParameters().Length == 0
                    ? entryPoint.Invoke(null, null)
                    : entryPoint.Invoke(null, new object[] { arguments });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }

            if (invocationResult is Task task)
            {
                await task.ConfigureAwait(false);
                var resultProperty = task.GetType().GetProperty("Result");
                if (resultProperty != null)
                {
                    return Convert.ToInt32(resultProperty.GetValue(task), System.Globalization.CultureInfo.InvariantCulture);
                }

                return 0;
            }

            if (invocationResult == null)
            {
                return 0;
            }

            return Convert.ToInt32(invocationResult, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static async Task<string> EnsureCliAssemblyPathAsync(string repositoryRoot)
        {
            var existingPath = FindExistingCliAssemblyPath(repositoryRoot);
            if (existingPath != null)
            {
                return existingPath;
            }

            await BuildGate.WaitAsync().ConfigureAwait(false);
            try
            {
                existingPath = FindExistingCliAssemblyPath(repositoryRoot);
                if (existingPath != null)
                {
                    return existingPath;
                }

                var buildConfiguration = FindBuildConfiguration();
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = repositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add("build");
                startInfo.ArgumentList.Add(Path.Combine("Tools", "PurelySharp.SymbolicCli", "PurelySharp.SymbolicCli.csproj"));
                startInfo.ArgumentList.Add("--configuration");
                startInfo.ArgumentList.Add(buildConfiguration);
                startInfo.ArgumentList.Add("--verbosity");
                startInfo.ArgumentList.Add("minimal");
                startInfo.ArgumentList.Add("/m:1");
                startInfo.ArgumentList.Add("/nodeReuse:false");
                startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");

                var buildResult = await RunProcessAsync(
                    startInfo,
                    TimeSpan.FromSeconds(420),
                    "Failed to start symbolic CLI build.").ConfigureAwait(false);
                if (buildResult.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        "Building PurelySharp.SymbolicCli failed." + Environment.NewLine +
                        buildResult.StandardOutput + Environment.NewLine +
                        buildResult.StandardError);
                }

                existingPath = FindExistingCliAssemblyPath(repositoryRoot);
                if (existingPath != null)
                {
                    return existingPath;
                }
            }
            finally
            {
                BuildGate.Release();
            }

            throw new FileNotFoundException(
                "Could not find built PurelySharp.SymbolicCli.dll after building it on demand.",
                Path.Combine(repositoryRoot, "Tools", "PurelySharp.SymbolicCli"));
        }

        private static string? FindExistingCliAssemblyPath(string repositoryRoot)
        {
            var targetFramework = Path.GetFileName(TestContext.CurrentContext.TestDirectory);
            var configurations = new[]
            {
                BuildConfiguration.Value,
                "Release",
                "Debug",
            }
            .Where(static configuration => !string.IsNullOrWhiteSpace(configuration))
            .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var configuration in configurations)
            {
                var candidate = Path.Combine(
                    repositoryRoot,
                    "Tools",
                    "PurelySharp.SymbolicCli",
                    "bin",
                    configuration,
                    targetFramework,
                    "PurelySharp.SymbolicCli.dll");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            string startFailureMessage)
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException(startFailureMessage);
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            return (process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
        }

        private static string FindBuildConfiguration()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (string.Equals(directory.Name, "Release", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(directory.Name, "Debug", StringComparison.OrdinalIgnoreCase))
                {
                    return directory.Name;
                }

                directory = directory.Parent;
            }

            return "Debug";
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PurelySharp.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find repository root.");
        }
    }
}
