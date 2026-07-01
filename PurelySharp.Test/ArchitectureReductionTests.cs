using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class ArchitectureReductionTests
    {
        [Test]
        public void AnalyzerReachability_DoesNotOpenCodeBranchProofQueries()
        {
            var repositoryRoot = FindRepositoryRoot();
            var analyzerFiles = Directory.GetFiles(
                Path.Combine(repositoryRoot, "PurelySharp.Analyzer"),
                "*.cs",
                SearchOption.AllDirectories);

            var offenders = analyzerFiles
                .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(path => new
                {
                    Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                    Source = File.ReadAllText(path),
                })
                .Where(static file =>
                    file.Source.Contains("new PurityProofQuery", StringComparison.Ordinal) ||
                    file.Source.Contains("PurityHazardKind.BranchReachability", StringComparison.Ordinal) ||
                    file.Source.Contains(".ClassifyPathFeasibility(", StringComparison.Ordinal) ||
                    file.Source.Contains("CSharpConditionToFormula.TryCollectBranchAssumptions", StringComparison.Ordinal))
                .Select(static file => file.Path)
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void SymbolicReachabilityService_IsCanonicalProofFacade()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));

            Assert.That(source, Does.Contain("ClassifyPathFeasibility"));
            Assert.That(source, Does.Contain("PathConditionsImply"));
            Assert.That(source, Does.Contain("ClassifyBranchReachability"));
            Assert.That(source, Does.Contain("CollectPathConditionsAt"));
        }

        [Test]
        public void SymbolicPublicSurface_HidesImplementationTranslators()
        {
            Assert.That(typeof(CSharpConditionToFormula).IsPublic, Is.False);
            Assert.That(typeof(SymbolicQueryService).IsPublic, Is.True);
            Assert.That(typeof(SmtAnalysisService).IsPublic, Is.True);
            Assert.That(typeof(SmtAnalysisOptions).IsPublic, Is.True);
        }

        [Test]
        public async Task ProductionMetricsScript_ReportsProductionModulesAndExcludesTests()
        {
            var repositoryRoot = FindRepositoryRoot();
            using var document = await RunProductionMetricsJsonAsync(repositoryRoot);
            var root = document.RootElement;
            var moduleNames = root.GetProperty("modules")
                .EnumerateArray()
                .Select(static module => module.GetProperty("module").GetString() ?? string.Empty)
                .ToArray();
            var largestPaths = root.GetProperty("largestFiles")
                .EnumerateArray()
                .Select(static file => file.GetProperty("path").GetString() ?? string.Empty)
                .ToArray();

            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("totalFiles").GetInt32(), Is.GreaterThan(50));
            Assert.That(root.GetProperty("totalLines").GetInt32(), Is.GreaterThan(10000));
            Assert.That(moduleNames, Does.Contain("Analyzer"));
            Assert.That(moduleNames, Does.Contain("Symbolic"));
            Assert.That(moduleNames, Does.Contain("SearchLib"));
            Assert.That(largestPaths, Has.None.StartsWith("PurelySharp.Test/"));
        }

        private static async Task<JsonDocument> RunProductionMetricsJsonAsync(string repositoryRoot)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = FindPowerShellExecutable(),
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            if (OperatingSystem.IsWindows())
            {
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
            }

            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "scripts", "Get-PurelySharpProductionMetrics.ps1"));
            startInfo.ArgumentList.Add("-Json");

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start production metrics script.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new AssertionException(string.Join(
                    Environment.NewLine,
                    "Production metrics script failed.",
                    "Exit code: " + process.ExitCode,
                    "stdout:",
                    output,
                    "stderr:",
                    error));
            }

            Assert.That(error, Is.Empty);
            return JsonDocument.Parse(output);
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

        private static string FindPowerShellExecutable()
        {
            var candidates = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "pwsh", "powershell.exe", "powershell" }
                : new[] { "pwsh" };

            foreach (var candidate in candidates)
            {
                var path = FindExecutableOnPath(candidate);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }
            }

            return OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
        }

        private static string FindExecutableOnPath(string fileName)
        {
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }
    }
}
