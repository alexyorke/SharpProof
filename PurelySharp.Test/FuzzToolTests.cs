using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using PurelySharp.Tools.Fuzz;

namespace PurelySharp.Test
{
    [TestFixture]
    public class FuzzToolTests
    {
        [Test]
        public async Task FuzzRunner_SmokeRun_WritesSummaryAndCoverageArtifacts()
        {
            var outputDirectory = CreateOutputDirectory();
            var iterations = Math.Max(40, RoslynShapeManifest.GeneratorBackedShapeIds.Length + 2);
            try
            {
                var summary = await FuzzRunner.RunAsync(new FuzzOptions
                {
                    Iterations = iterations,
                    Seed = 20260614,
                    OutputDirectory = outputDirectory,
                    CheckpointEvery = 5,
                    Parallelism = 4,
                    Quiet = true
                });

                Assert.That(summary.CasesAnalyzed, Is.EqualTo(iterations));
                Assert.That(summary.SchemaVersion, Is.EqualTo("1.1"));
                Assert.That(summary.CompilationErrorCount, Is.EqualTo(0));
                Assert.That(summary.OperationKinds, Is.Not.Empty);
                Assert.That(summary.SyntaxKinds, Is.Not.Empty);
                Assert.That(summary.FamilyCounts, Is.Not.Empty);
                Assert.That(summary.PrimaryShapeCounts, Is.Not.Empty);
                Assert.That(summary.SamplerMode, Is.EqualTo("deterministic_shape_stratified"));
                Assert.That(summary.ManifestSurfaceCounts.ContainsKey("OperationKind"), Is.True);
                Assert.That(summary.ManifestSurfaceCounts.ContainsKey("SyntaxKind"), Is.True);
                Assert.That(summary.ManifestSurfaceCounts.ContainsKey("AnalyzerActionSurface"), Is.True);
                Assert.That(summary.GeneratorBackedShapeCount, Is.EqualTo(RoslynShapeManifest.GeneratorBackedShapeIds.Length));
                Assert.That(summary.GeneratorBackedShapesWithRegistryCount, Is.EqualTo(RoslynShapeManifest.GeneratorBackedShapeIds.Length));
                Assert.That(summary.UnobservedGeneratorBackedShapes, Is.Empty);
                Assert.That(summary.Parallelism, Is.EqualTo(4));
                Assert.That(File.Exists(Path.Combine(outputDirectory, "summary.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(outputDirectory, "coverage.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(outputDirectory, "summary.partial.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(outputDirectory, "coverage.partial.json")), Is.True);
            }
            finally
            {
                DeleteOutputDirectory(outputDirectory);
            }
        }

        [Test]
        public async Task FuzzRunner_KnownImpureConsoleCase_ProducesPs0002()
        {
            var source = """
using System;
using PurelySharp.Attributes;

public class KnownImpureConsoleCase
{
    [EnforcePure]
    public void TestMethod()
    {
        Console.WriteLine("impure");
    }
}
""";

            var analysis = await FuzzRunner.AnalyzeCaseAsync(new FuzzCase(
                "KnownImpureConsoleCase",
                "KnownImpureConsole",
                source,
                AllowUnsafe: false,
                FuzzExpectation.DefinitelyImpure()));

            Assert.That(analysis.CompilationErrors, Is.Empty);
            Assert.That(analysis.Diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.True);
            Assert.That(analysis.Findings.Any(finding => finding.Category == "impure_missing_ps0002"), Is.False);
            Assert.That(analysis.OperationKinds.ContainsKey("Invocation"), Is.True);
        }

        [Test]
        public async Task FuzzRunner_RunCasesAsync_DedupesRepeatedInterestingCases_AndCapsSavedCasesPerFamily()
        {
            var outputDirectory = CreateOutputDirectory();
            try
            {
                var repeatedSource = "public class C { public void M() { int value = ; } }";
                var distinctSource = "public class C { MissingType M() => null; }";
                var cases = ImmutableArray.Create(
                    new FuzzCase("CompileFailA", "CompileFail", repeatedSource, AllowUnsafe: false, FuzzExpectation.Conservative()),
                    new FuzzCase("CompileFailB", "CompileFail", repeatedSource, AllowUnsafe: false, FuzzExpectation.Conservative()),
                    new FuzzCase("CompileFailC", "CompileFail", distinctSource, AllowUnsafe: false, FuzzExpectation.Conservative()));

                var summary = await FuzzRunner.RunCasesAsync(
                    cases,
                    new FuzzOptions
                    {
                        OutputDirectory = outputDirectory,
                        MaxInterestingCases = 10,
                        MaxInterestingCasesPerFamily = 1,
                        CheckpointEvery = 2,
                        Parallelism = 4,
                        Quiet = true
                    });

                var interestingCasesDirectory = Path.Combine(outputDirectory, "interesting-cases");
                var savedFiles = Directory.GetFiles(interestingCasesDirectory);
                var occurrenceCounts = summary.Findings
                    .Select(finding => finding.OccurrenceCount)
                    .OrderBy(count => count)
                    .ToArray();

                Assert.That(summary.CasesAnalyzed, Is.EqualTo(3));
                Assert.That(summary.CompilationErrorCount, Is.EqualTo(3));
                Assert.That(summary.FindingCount, Is.EqualTo(3));
                Assert.That(summary.UniqueFindingCount, Is.EqualTo(2));
                Assert.That(summary.InterestingCasesSaved, Is.EqualTo(1));
                Assert.That(savedFiles.Length, Is.EqualTo(1));
                Assert.That(occurrenceCounts, Is.EqualTo(new[] { 1, 2 }));
            }
            finally
            {
                DeleteOutputDirectory(outputDirectory);
            }
        }

        [Test]
        public void FuzzCaseGenerator_DeterministicSample_IncludesExpandedFamilies()
        {
            var generator = new FuzzCaseGenerator(20260614);
            var generatedCases = Enumerable.Range(0, 1200)
                .Select(index => generator.Next(index))
                .ToArray();
            var families = generatedCases
                .Select(fuzzCase => fuzzCase.Family)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var primaryShapes = generatedCases
                .SelectMany(fuzzCase => fuzzCase.PrimaryShapeIds.IsDefaultOrEmpty ? Enumerable.Empty<string>() : fuzzCase.PrimaryShapeIds)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableHashSet(StringComparer.Ordinal);
            var missingShapes = RoslynShapeManifest.GeneratorBackedShapeIds
                .Where(shapeId => !primaryShapes.Contains(shapeId))
                .OrderBy(shapeId => shapeId, StringComparer.Ordinal)
                .ToArray();

            Assert.That(families, Does.Contain("ImpureAwaitTaskDelay"));
            Assert.That(families, Does.Contain("ImpureLockSection"));
            Assert.That(families, Does.Contain("ImpureUsingStandardOutput"));
            Assert.That(families, Does.Contain("PureInterpolatedString"));
            Assert.That(families, Does.Contain("PureUtf8String"));
            Assert.That(families, Does.Contain("PureArrayCreation"));
            Assert.That(families, Does.Contain("ConservativeSwitchExpression"));
            Assert.That(families, Does.Contain("ConservativeRangeSlice"));
            Assert.That(families, Does.Contain("ConservativeWithExpression"));
            Assert.That(families, Does.Contain("ConservativeImplicitIndexerReference"));
            Assert.That(families, Does.Contain("ConservativeInterpolatedStringHandler"));
            Assert.That(families, Does.Contain("ConservativeTryCatch"));
            Assert.That(families, Does.Contain("ConservativeConditionalAccessCoalesce"));
            Assert.That(families, Does.Contain("ConservativeTuple"));
            Assert.That(families, Does.Contain("ConservativeRecursivePattern"));
            Assert.That(families, Does.Contain("ConservativeSpreadCollectionExpression"));
            Assert.That(families, Does.Contain("ConservativeYieldReturn"));
            Assert.That(families, Does.Contain("ConservativeAnonymousFunction"));
            Assert.That(families, Does.Contain("ConservativeDelegateCreation"));
            Assert.That(families, Does.Contain("ConservativeNestedLambdaLocalFunction"));
            Assert.That(families, Does.Contain("ConservativeTuplePatternSwitch"));
            Assert.That(families, Does.Contain("ConservativeUsingAwaitDelegateFlow"));
            Assert.That(missingShapes, Is.Empty);
        }

        [Test]
        public async Task ExpandedCoverageFamilies_Compile_AndEmitExpectedOperationKinds()
        {
            var expectedOperationKinds = new[]
            {
                new FamilyExpectation("ConservativeTryCatch", "Try", "CatchClause"),
                new FamilyExpectation("ConservativeConditionalAccessCoalesce", "ConditionalAccess", "Coalesce"),
                new FamilyExpectation("ConservativeTuple", "Tuple"),
                new FamilyExpectation("ConservativeRecursivePattern", "RecursivePattern"),
                new FamilyExpectation("ConservativeSpreadCollectionExpression", "Spread"),
                new FamilyExpectation("ConservativeYieldReturn", "YieldReturn"),
                new FamilyExpectation("ConservativeAnonymousFunction", "AnonymousFunction"),
                new FamilyExpectation("ConservativeDelegateCreation", "DelegateCreation"),
                new FamilyExpectation("ConservativeNestedLambdaLocalFunction", "AnonymousFunction", "LocalFunction"),
                new FamilyExpectation("ConservativeTuplePatternSwitch", "Tuple", "SwitchExpression"),
                new FamilyExpectation("ConservativeUsingAwaitDelegateFlow", "UsingDeclaration", "Await", "AnonymousFunction")
            };

            var generator = new FuzzCaseGenerator(20260614);
            var generatedCasesByFamily = expectedOperationKinds.ToDictionary(
                expectation => expectation.Family,
                _ => (FuzzCase?)null,
                StringComparer.Ordinal);

            for (var index = 0; index < 8000 && generatedCasesByFamily.Values.Any(fuzzCase => fuzzCase is null); index++)
            {
                var fuzzCase = generator.Next(index);
                if (generatedCasesByFamily.ContainsKey(fuzzCase.Family) && generatedCasesByFamily[fuzzCase.Family] is null)
                {
                    generatedCasesByFamily[fuzzCase.Family] = fuzzCase;
                }
            }

            Assert.That(generatedCasesByFamily.Values.All(fuzzCase => fuzzCase is not null), Is.True);

            foreach (var expectation in expectedOperationKinds)
            {
                var fuzzCase = generatedCasesByFamily[expectation.Family];
                Assert.That(fuzzCase, Is.Not.Null, expectation.Family);

                var analysis = await FuzzRunner.AnalyzeCaseAsync(fuzzCase!);

                Assert.That(analysis.CompilationErrors, Is.Empty, expectation.Family);
                foreach (var operationKind in expectation.OperationKinds)
                {
                    Assert.That(
                        analysis.OperationKinds.ContainsKey(operationKind),
                        Is.True,
                        $"{expectation.Family} missing {operationKind}");
                }
            }
        }

        [Test]
        public async Task ExceptionCoverageFamilies_EmitPs0002_And_Ps0010()
        {
            var families = new[]
            {
                "ExceptionDirectThrowInvalidOperation",
                "ExceptionGuardedThrowArgumentNull",
                "ExceptionThrowExpressionFormatException"
            };

            var generator = new FuzzCaseGenerator(20260614);

            foreach (var family in families)
            {
                var registryEntry = FuzzCaseGenerator.RegistryEntries.Single(entry => entry.Id == family);
                var fuzzCase = generator.GenerateForRegistryEntry(registryEntry, 0);
                var analysis = await FuzzRunner.AnalyzeCaseAsync(fuzzCase);

                Assert.That(analysis.CompilationErrors, Is.Empty, family);
                Assert.That(analysis.Findings, Is.Empty, family);
                Assert.That(
                    analysis.Diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                    Is.True,
                    family + " missing PS0002");
                Assert.That(
                    analysis.Diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId),
                    Is.True,
                    family + " missing PS0010");
            }
        }

        private static string CreateOutputDirectory()
        {
            var outputDirectory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "fuzz-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputDirectory);
            return outputDirectory;
        }

        private static void DeleteOutputDirectory(string outputDirectory)
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }

        private sealed record FamilyExpectation(string Family, params string[] OperationKinds);
    }
}
