using System.Diagnostics;
using System.Reflection;
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
        public void ExecutionVisibility_UsesSymbolicReachabilityForConditionProofs()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "ExecutionVisibility.cs"));

            Assert.That(source, Does.Not.Contain("CSharpConditionToFormula."));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.IsForInitialEntryConditionAlwaysFalse"));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.EvaluateKnownConditionTruth"));
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
        public void SymbolicIr_KeepsSmtConstructionBehindEncoderBoundary()
        {
            var repositoryRoot = FindRepositoryRoot();
            var irDirectory = Path.Combine(repositoryRoot, "PurelySharp.Symbolic", "Ir");
            var offenders = Directory.GetFiles(irDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(static path => !path.EndsWith("SymbolicIrFormulaEncoder.cs", StringComparison.Ordinal))
                .Select(path => new
                {
                    Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                    Source = File.ReadAllText(path),
                })
                .Where(static file =>
                    file.Source.Contains("new Smt", StringComparison.Ordinal) ||
                    file.Source.Contains(": SmtFormula", StringComparison.Ordinal))
                .Select(static file => file.Path)
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void SearchLib_RemainsSolverBackendWithoutRoslynOrSymbolicSemantics()
        {
            var repositoryRoot = FindRepositoryRoot();
            var searchLibDirectory = Path.Combine(repositoryRoot, "SearchLib");
            var offenders = Directory.GetFiles(searchLibDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => new
                {
                    Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                    Source = File.ReadAllText(path),
                })
                .Where(static file =>
                    file.Source.Contains("Microsoft.CodeAnalysis", StringComparison.Ordinal) ||
                    file.Source.Contains("PurelySharp.Symbolic", StringComparison.Ordinal) ||
                    file.Source.Contains("PurelySharp.Analyzer", StringComparison.Ordinal))
                .Select(static file => file.Path)
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void SymbolicIrLowerer_KeepsStringAndRegexLoweringsInDedicatedPartial()
        {
            var repositoryRoot = FindRepositoryRoot();
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var stringSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Strings.cs"));

            Assert.That(coreSource, Does.Contain("internal static partial class SymbolicIrLowerer"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerRegexIsMatchInvocation"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerStringPredicateInvocation"));
            Assert.That(stringSource, Does.Contain("private static bool TryLowerRegexIsMatchInvocation"));
            Assert.That(stringSource, Does.Contain("private static bool TryLowerStringPredicateInvocation"));
        }

        [Test]
        public void RuntimeHazardDivideByZero_UsesIrExceptionPreconditionTriggerBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateIrExceptionPreconditionTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.DivideByZero"));
            Assert.That(source, Does.Contain("return TryTranslateZeroCondition(divisor"));
            Assert.That(source, Does.Not.Contain("TryTranslateZeroCondition(binaryExpression.Right"));
            Assert.That(source, Does.Not.Contain("TryTranslateZeroCondition(assignment.Right"));
        }

        [Test]
        public void RuntimeHazardSimpleIndexing_UsesIrBoundsPreconditionBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateIrElementAccessOutOfRangeTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.IndexOutOfRange"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.ArgumentOutOfRange"));
            Assert.That(source, Does.Contain("new SymbolicBoundsAtom"));
            Assert.That(source, Does.Contain("CSharpConditionToFormula.TryTranslateBuiltInElementAccessInRange"));
        }

        [Test]
        public void RuntimeHazardArrayGetValue1D_UsesIrBoundsPreconditionBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.cs"));
            var irTriggerSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));

            Assert.That(source, Does.Contain("TryCreateIrArrayGetValueIndexOutOfRangeTrigger"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.array-get-value.bounds.in-range"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.IndexOutOfRange"));
            Assert.That(source, Does.Contain("new SymbolicBoundsAtom"));
            Assert.That(source, Does.Contain("TryTranslateArrayGetValueDimensionLength"));
            Assert.That(coreSource, Does.Contain("TryCreateIrArrayGetValueIndexOutOfRangeTrigger("));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateIrArrayGetValueIndexOutOfRangeTrigger"));
        }

        [Test]
        public void RuntimeHazardNegativeLengths_UseIrExceptionPreconditionsBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateNegativeLengthTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.NegativeLength"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.NegativeStackAllocLength"));
            Assert.That(source, Does.Contain("SymbolicRelationOperator.LessThan"));
            Assert.That(source, Does.Contain("return TryTranslateNegativeCondition(lengthExpression"));
            Assert.That(source, Does.Not.Contain("if (!TryTranslateNegativeCondition(lengthExpression"));
        }

        [Test]
        public void RuntimeHazardCheckedIntegralOutOfRange_UsesIrExceptionPreconditionsBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateCheckedIntegralOutOfRangeTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.CheckedOverflow"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.binary-overflow"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-conversion.overflow"));
            Assert.That(source, Does.Contain("IsSignedDivisionOverflowOperator"));
            Assert.That(source, Does.Contain("CSharpConditionToFormula.TryTranslateValue"));
        }

        [Test]
        public void RuntimeHazardStableNullDereferences_UseIrExceptionPreconditionsBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateNullDereferenceTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.NullDereference"));
            Assert.That(source, Does.Contain("IsStableIrReferenceSubject"));
            Assert.That(source, Does.Contain("return TryTranslateNullCondition(receiver"));
            Assert.That(source, Does.Contain("!TryCreateNullDereferenceTrigger(receiver"));
        }

        [Test]
        public void RuntimeHazardUnboxNull_UsesIrExceptionPreconditionBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateUnboxNullTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.UnboxNull"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.unbox-null"));
            Assert.That(source, Does.Contain("return TryTranslateNullCondition(expression"));
            Assert.That(source, Does.Contain("TryCreateUnboxNullTrigger("));
        }

        [Test]
        public void RuntimeHazardStableArgumentNull_UsesIrExceptionPreconditionsBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateArgumentNullTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.ArgumentNull"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.argument-null"));
            Assert.That(source, Does.Contain("return TryTranslateNullCondition(expression"));
            Assert.That(source, Does.Contain("!TryCreateArgumentNullTrigger(expression"));
        }

        [Test]
        public void RuntimeHazardNullableValue_UsesIrExceptionPreconditionBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateNullableValueWithoutValueTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.NullableValueWithoutValue"));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryLowerNullableHasValueTerm"));
            Assert.That(source, Does.Contain("CSharpConditionToFormula.TryTranslateNullableHasValue"));
            Assert.That(source, Does.Contain("!TryCreateNullableValueWithoutValueTrigger("));
        }

        [Test]
        public void RuntimeHazardInvalidReferenceCast_UsesIrTypeTestPreconditionBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.cs"));
            var irTriggerSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));

            Assert.That(source, Does.Contain("TryCreateIrRuntimeReferenceCastMismatchTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.InvalidCast"));
            Assert.That(source, Does.Contain("new SymbolicTypeTestAtom"));
            Assert.That(source, Does.Contain("SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey"));
            Assert.That(source, Does.Contain("CSharpConditionToFormula.TryCreateRuntimeTypeTestFormula"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateRuntimeReferenceCastMismatchTrigger"));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateRuntimeReferenceCastMismatchTrigger"));
        }

        [Test]
        public void RuntimeHazardDirectThrow_UsesIrExceptionPreconditionTrigger()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.cs"));
            var irTriggerSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));

            Assert.That(source, Does.Contain("TryCreateDirectThrowTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.DirectThrow"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.direct-throw"));
            Assert.That(coreSource, Does.Contain("var trigger = !isRethrow"));
            Assert.That(coreSource, Does.Contain("TryCreateDirectThrowTrigger(throwNode"));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateDirectThrowTrigger"));
        }

        [Test]
        public void RuntimeHazardIrTriggerBridge_LivesInDedicatedPartial()
        {
            var repositoryRoot = FindRepositoryRoot();
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.cs"));
            var irTriggerSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));

            Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateIrRelationalExceptionPreconditionTrigger"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateIrElementAccessOutOfRangeTrigger"));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateIrRelationalExceptionPreconditionTrigger"));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateIrElementAccessOutOfRangeTrigger"));
        }

        [Test]
        public void RuntimeExceptionEvidenceFacts_AcceptsAllSharedCategories()
        {
            var rejectedCategories = typeof(SymbolicRuntimeExceptionFacts.ExceptionCategories)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(static field => field.IsLiteral &&
                    !field.IsInitOnly &&
                    field.FieldType == typeof(string))
                .Select(static field => (string)field.GetRawConstantValue()!)
                .Where(static category => !SymbolicRuntimeExceptionFacts.IsKnownEvidenceCategory(category))
                .ToArray();

            Assert.That(rejectedCategories, Is.Empty);
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

        private static string ReadRuntimeHazardCandidateSources(string repositoryRoot)
        {
            return string.Concat(
                File.ReadAllText(Path.Combine(
                    repositoryRoot,
                    "PurelySharp.Symbolic",
                    "SymbolicRuntimeHazardCandidateFactory.cs")),
                Environment.NewLine,
                File.ReadAllText(Path.Combine(
                    repositoryRoot,
                    "PurelySharp.Symbolic",
                    "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs")));
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
