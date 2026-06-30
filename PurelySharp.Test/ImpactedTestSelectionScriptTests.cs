using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class ImpactedTestSelectionScriptTests
    {
        [Test]
        public async Task ListOnlyJson_SelectsOwningFixtureForChangedTestFile()
        {
            using var recommendation = await RunImpactedSelectorJsonAsync(
                "PurelySharp.Test/SymbolicProgramPointFactTests.cs");
            var root = recommendation.RootElement;

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
            Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("SymbolicProgramPointFactTests"));
            Assert.That(
                root.GetProperty("testFilter").GetString(),
                Does.Contain("FullyQualifiedName~PurelySharp.Test.SymbolicProgramPointFactTests"));
        }

        [Test]
        public async Task ListOnlyJson_FallsBackForSharedTestInfrastructure()
        {
            using var recommendation = await RunImpactedSelectorJsonAsync(
                "PurelySharp.Test/PurelySharp.Test.csproj");
            var root = recommendation.RootElement;

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunFullSuite"));
            Assert.That(
                GetStringArray(root, "fullSuiteFallbackReasons").Single(),
                Does.Contain("changes shared test infrastructure"));
            Assert.That(root.GetProperty("testFilter").GetString(), Is.Empty);
        }

        [Test]
        public async Task ListOnlyJson_SelectsExpandedSymbolicSmtFixturesForConditionTranslator()
        {
            using var recommendation = await RunImpactedSelectorJsonAsync(
                "PurelySharp.Symbolic/Smt/CSharpConditionToFormula.cs");
            var root = recommendation.RootElement;
            var fixtures = GetStringArray(root, "selectedTestFixtures");

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("filterTooLong").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
            Assert.That(fixtures, Does.Contain("ExpressionSmtTranslationTests"));
            Assert.That(fixtures, Does.Contain("ExpressionAtomSmtTests"));
            Assert.That(fixtures, Does.Contain("StringLengthSmtTests"));
            Assert.That(fixtures, Does.Contain("ElementAccessSmtTests"));
            Assert.That(fixtures, Does.Contain("ReferenceReachabilitySmtTests"));
            Assert.That(fixtures, Does.Contain("SymbolicRuntimeHazardQueryTests"));
        }

        [Test]
        public async Task ListOnlyJson_SelectsAnalyzerSmtFixturesForExceptionPathFacts()
        {
            using var recommendation = await RunImpactedSelectorJsonAsync(
                "PurelySharp.Analyzer/ExceptionFlowAnalyzer.PathFacts.cs");
            var root = recommendation.RootElement;
            var fixtures = GetStringArray(root, "selectedTestFixtures");

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("filterTooLong").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
            Assert.That(fixtures, Does.Contain("ExceptionFlowPathFactStressTests"));
            Assert.That(fixtures, Does.Contain("PathFactExpressionReachabilityTests"));
            Assert.That(fixtures, Does.Contain("ReferenceReachabilitySmtTests"));
            Assert.That(fixtures, Does.Contain("SymbolicRuntimeHazardQueryTests"));
        }

        [Test]
        public async Task ListOnlyJson_FallsBackForUnmappedAnalyzerProductionFile()
        {
            using var recommendation = await RunImpactedSelectorJsonAsync(
                "PurelySharp.Analyzer/Engine/Analysis/WorklistPuritySolver.cs");
            var root = recommendation.RootElement;

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunFullSuite"));
            Assert.That(GetStringArray(root, "selectedTestFixtures"), Is.Empty);
            Assert.That(
                GetStringArray(root, "fullSuiteFallbackReasons").Single(),
                Does.Contain("has no impacted-test mapping"));
            Assert.That(root.GetProperty("testFilter").GetString(), Is.Empty);
        }

        [Test]
        public async Task ListOnlyJson_IgnoresDocumentationOnlyChanges()
        {
            using var recommendation = await RunImpactedSelectorJsonAsync(
                "docs/symbolic-invariants.md");
            var root = recommendation.RootElement;

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("Skip"));
            Assert.That(GetStringArray(root, "ignoredFiles"), Does.Contain("docs/symbolic-invariants.md"));
            Assert.That(root.GetProperty("testFilter").GetString(), Is.Empty);
        }

        private static async Task<JsonDocument> RunImpactedSelectorJsonAsync(params string[] changedFiles)
        {
            var repositoryRoot = FindRepositoryRoot();
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
            startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "scripts", "Invoke-PurelySharpImpactedTests.ps1"));
            startInfo.ArgumentList.Add("-ListOnly");
            startInfo.ArgumentList.Add("-Json");
            startInfo.ArgumentList.Add("-ChangedFile");
            foreach (var changedFile in changedFiles)
            {
                startInfo.ArgumentList.Add(changedFile);
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start impacted test selector.");
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
                    "Impacted test selector failed.",
                    "Exit code: " + process.ExitCode,
                    "stdout:",
                    output,
                    "stderr:",
                    error));
            }

            Assert.That(error, Is.Empty);
            return JsonDocument.Parse(output);
        }

        private static string[] GetStringArray(JsonElement root, string propertyName)
        {
            return root.GetProperty(propertyName)
                .EnumerateArray()
                .Select(static element => element.GetString() ?? string.Empty)
                .ToArray();
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
