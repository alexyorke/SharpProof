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
                GetStringArray(GetEvidenceEntry(root, "PurelySharp.Test/SymbolicProgramPointFactTests.cs", "changed-test-file"), "selectedTestFixtures"),
                Does.Contain("SymbolicProgramPointFactTests"));
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
            var evidence = GetEvidenceEntry(root, "PurelySharp.Symbolic/Smt/CSharpConditionToFormula.cs", "path-map");

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("filterTooLong").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
            Assert.That(fixtures, Does.Contain("ExpressionSmtTranslationTests"));
            Assert.That(fixtures, Does.Contain("ExpressionAtomSmtTests"));
            Assert.That(fixtures, Does.Contain("StringLengthSmtTests"));
            Assert.That(fixtures, Does.Contain("ElementAccessSmtTests"));
            Assert.That(fixtures, Does.Contain("ReferenceReachabilitySmtTests"));
            Assert.That(fixtures, Does.Contain("RegexTests"));
            Assert.That(fixtures, Does.Contain("SymbolicRuntimeHazardQueryTests"));
            Assert.That(evidence.GetProperty("reason").GetString(), Is.EqualTo("Symbolic SMT string-length and regex translation change"));
        }

        [Test]
        public async Task ListOnlyJson_SelectsRegexFixtureForSearchLibStringRegexFormulaChange()
        {
            const string changedFile = "SearchLib/Z3FormulaEncoder.cs";
            using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
            var root = recommendation.RootElement;
            var fixtures = GetStringArray(root, "selectedTestFixtures");
            var evidence = GetEvidenceEntry(root, changedFile, "path-map");

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
            Assert.That(fixtures, Does.Contain("RegexTests"));
            Assert.That(fixtures, Does.Contain("SearchLibZ3SmokeTests"));
            Assert.That(fixtures, Does.Contain("SmtAnalysisServiceTests"));
            Assert.That(fixtures, Does.Contain("StringLengthSmtTests"));
            Assert.That(evidence.GetProperty("reason").GetString(), Is.EqualTo("SearchLib SMT string-length and regex formula change"));
            Assert.That(GetStringArray(evidence, "selectedTestFixtures"), Does.Contain("RegexTests"));
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
            Assert.That(fixtures, Does.Contain("DiagnosticEvidenceTests"));
            Assert.That(
                GetEvidenceReasons(root, "path-map"),
                Has.Some.Contains("Exception flow and runtime-hazard analyzer change"));
        }

        [Test]
        public async Task ListOnlyJson_SelectsRuntimeHazardFixturesForSymbolicQueryService()
        {
            const string changedFile = "PurelySharp.Symbolic/SymbolicRuntimeHazardQueryService.cs";
            using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
            var root = recommendation.RootElement;
            var fixtures = GetStringArray(root, "selectedTestFixtures");
            var evidence = GetEvidenceEntry(root, changedFile, "path-map");

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
            Assert.That(fixtures, Does.Contain("SymbolicRuntimeHazardQueryTests"));
            Assert.That(fixtures, Does.Contain("SymbolicSourceQueryLineTests"));
            Assert.That(fixtures, Does.Contain("DiagnosticEvidenceTests"));
            Assert.That(evidence.GetProperty("reason").GetString(), Is.EqualTo("Symbolic runtime-hazard query change"));
            Assert.That(GetStringArray(evidence, "selectedTestFixtures"), Does.Contain("DiagnosticEvidenceTests"));
        }

        [Test]
        public async Task ListOnlyJson_SelectsRuntimeHazardDiagnosticsForAnalyzerConfigKeys()
        {
            const string changedFile = "PurelySharp.Analyzer/Configuration/ConfigKeys.cs";
            using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
            var root = recommendation.RootElement;
            var fixtures = GetStringArray(root, "selectedTestFixtures");
            var evidence = GetEvidenceEntry(root, changedFile, "path-map");

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
            Assert.That(GetStringArray(root, "fullSuiteFallbackReasons"), Is.Empty);
            Assert.That(fixtures, Does.Contain("DiagnosticEvidenceTests"));
            Assert.That(fixtures, Does.Contain("SemanticOracleSmtTests"));
            Assert.That(evidence.GetProperty("reason").GetString(), Is.EqualTo("Analyzer runtime-hazard configuration change"));
        }

        [Test]
        public async Task ListOnlyJson_SelectsAnalyzerSmtFixturesForPathFactRule()
        {
            const string changedFile = "PurelySharp.Analyzer/Engine/Rules/BinaryOperationPurityRule.cs";
            using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
            var root = recommendation.RootElement;
            var fixtures = GetStringArray(root, "selectedTestFixtures");
            var evidence = GetEvidenceEntry(root, changedFile, "path-map");

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
            Assert.That(fixtures, Does.Contain("PathFactExpressionReachabilityTests"));
            Assert.That(fixtures, Does.Contain("ReferenceReachabilitySmtTests"));
            Assert.That(fixtures, Does.Contain("SymbolicRuntimeHazardQueryTests"));
            Assert.That(fixtures, Does.Contain("DiagnosticEvidenceTests"));
            Assert.That(evidence.GetProperty("reason").GetString(), Is.EqualTo("SMT path-fact analyzer rule change"));
            Assert.That(GetStringArray(evidence, "selectedTestFixtures"), Does.Contain("DiagnosticEvidenceTests"));
        }

        [Test]
        public async Task ListOnlyJson_SelectsSymbolicFactsForAnalyzerStateMerge()
        {
            const string changedFile = "PurelySharp.Analyzer/Engine/PurityAnalysisEngine.StateMerge.cs";
            using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
            var root = recommendation.RootElement;
            var fixtures = GetStringArray(root, "selectedTestFixtures");
            var evidence = GetEvidenceEntry(root, changedFile, "path-map");

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
            Assert.That(fixtures, Does.Contain("SymbolicProgramPointFactTests"));
            Assert.That(fixtures, Does.Contain("PathFactExpressionReachabilityTests"));
            Assert.That(fixtures, Does.Contain("StringLengthSmtTests"));
            Assert.That(fixtures, Does.Contain("DiagnosticEvidenceTests"));
            Assert.That(evidence.GetProperty("reason").GetString(), Is.EqualTo("Analyzer symbolic state-merge and path-fact change"));
        }

        [Test]
        public async Task ListOnlyJson_SelectsSpecificEvidenceForSymbolicProgramPointFacts()
        {
            const string changedFile = "PurelySharp.Symbolic/SymbolicProgramPointFacts.cs";
            using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
            var root = recommendation.RootElement;
            var fixtures = GetStringArray(root, "selectedTestFixtures");
            var evidence = GetEvidenceEntry(root, changedFile, "path-map");

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
            Assert.That(fixtures, Does.Contain("SymbolicProgramPointFactTests"));
            Assert.That(fixtures, Does.Contain("StringLengthSmtTests"));
            Assert.That(fixtures, Does.Contain("SymbolicRuntimeHazardQueryTests"));
            Assert.That(evidence.GetProperty("reason").GetString(), Is.EqualTo("Symbolic program-point fact extraction change"));
        }

        [Test]
        public async Task ListOnlyJson_PreservesFullSuiteFallbackForAnalyzerCore()
        {
            const string changedFile = "PurelySharp.Analyzer/Engine/PurityAnalysisEngine.cs";
            using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
            var root = recommendation.RootElement;
            var evidence = GetEvidenceEntry(root, changedFile, "full-suite-fallback");

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunFullSuite"));
            Assert.That(
                GetStringArray(root, "fullSuiteFallbackReasons"),
                Does.Contain(changedFile + " is high-fanout analyzer core"));
            Assert.That(
                GetStringArray(evidence, "fullSuiteFallbackReasons"),
                Does.Contain(changedFile + " is high-fanout analyzer core"));
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
        public async Task ListOnlyJson_PreservesTwentyWorkerCommandForSymbolicCliChange()
        {
            const string changedFile = "Tools/PurelySharp.SymbolicCli/Program.cs";
            using var recommendation = await RunImpactedSelectorJsonAsync(20, changedFile);
            var root = recommendation.RootElement;
            var fixtures = GetStringArray(root, "selectedTestFixtures");
            var command = root.GetProperty("suggestedCommand").GetString();

            Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
            Assert.That(fixtures, Does.Contain("SymbolicSourceQueryLineTests"));
            Assert.That(fixtures, Does.Contain("SymbolicRuntimeHazardQueryTests"));
            Assert.That(fixtures, Does.Contain("AnalyzerPackagingTests"));
            Assert.That(command, Does.Contain("-Workers 20"));
            Assert.That(command, Does.Contain("-Filter"));
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
            Assert.That(
                GetEvidenceEntry(root, "docs/symbolic-invariants.md", "ignored").GetProperty("reason").GetString(),
                Is.EqualTo("Documentation-only change"));
            Assert.That(root.GetProperty("testFilter").GetString(), Is.Empty);
        }

        private static Task<JsonDocument> RunImpactedSelectorJsonAsync(params string[] changedFiles)
        {
            return RunImpactedSelectorJsonAsync(0, changedFiles);
        }

        private static async Task<JsonDocument> RunImpactedSelectorJsonAsync(int workers, params string[] changedFiles)
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
            if (workers > 0)
            {
                startInfo.ArgumentList.Add("-Workers");
                startInfo.ArgumentList.Add(workers.ToString());
            }

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

        private static JsonElement GetEvidenceEntry(JsonElement root, string changedFile, string source)
        {
            return root.GetProperty("selectionEvidence")
                .EnumerateArray()
                .Single(entry =>
                    entry.GetProperty("changedFile").GetString() == changedFile &&
                    entry.GetProperty("source").GetString() == source);
        }

        private static string[] GetEvidenceReasons(JsonElement root, string source)
        {
            return root.GetProperty("selectionEvidence")
                .EnumerateArray()
                .Where(entry => entry.GetProperty("source").GetString() == source)
                .Select(entry => entry.GetProperty("reason").GetString() ?? string.Empty)
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
