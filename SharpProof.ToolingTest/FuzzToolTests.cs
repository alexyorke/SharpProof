using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Tools.Fuzz;

namespace SharpProof.Test
{
    [TestFixture]
    public class FuzzToolTests
    {
        private static readonly ImmutableDictionary<string, ShapeRegistryEntry> RegistryEntriesById =
            FuzzCaseGenerator.RegistryEntries.ToImmutableDictionary(entry => entry.Id, StringComparer.Ordinal);

        private static readonly ImmutableArray<FamilyExpectation> ExpandedCoverageExpectations = ImmutableArray.Create(
            new FamilyExpectation("ConservativeAddressOf", "AddressOf"),
            new FamilyExpectation("PureInlineArrayAccess", "InlineArrayAccess"),
            new FamilyExpectation("ConservativeInterpolatedStringHandler", "InterpolatedStringHandlerCreation", "InterpolatedStringAddition", "InterpolatedStringAppendLiteral", "InterpolatedStringAppendFormatted", "InterpolatedStringHandlerArgumentPlaceholder"),
            new FamilyExpectation("ConservativeDeclarationPattern", "DeclarationPattern"),
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
            new FamilyExpectation("ConservativeUsingAwaitDelegateFlow", "UsingDeclaration", "Await", "AnonymousFunction"),
            new FamilyExpectation("PureNestedOwnershipChain", "PropertyReference", "SimpleAssignment", "ObjectCreation"),
            new FamilyExpectation("ImpureOwnershipEscapeChain", "ObjectCreation", "PropertyReference", "Return"));

        private static readonly ImmutableArray<string> ExceptionCoverageFamilies = ImmutableArray.Create(
            "ExceptionDirectThrowInvalidOperation",
            "ExceptionGuardedThrowArgumentNull",
            "ExceptionThrowExpressionFormatException",
            "ExceptionDefiniteDivideByZero",
            "ExceptionDefiniteNullReference",
            "ExceptionUsingDisposeThrows",
            "ExceptionInvokedLocalFunctionThrow",
            "ExceptionInvokedLambdaThrow");

        private static readonly ImmutableArray<(string Family, bool ExpectSp0002, bool ExpectSp0010)> ExceptionNegativeExpectations = ImmutableArray.Create(
            (Family: "ExceptionCaughtInternalThrow", ExpectSp0002: true, ExpectSp0010: false),
            (Family: "ExceptionDeadBranchThrow", ExpectSp0002: false, ExpectSp0010: false),
            (Family: "ExceptionGuardedSafeDivideByZeroExcluded", ExpectSp0002: false, ExpectSp0010: false),
            (Family: "ExceptionGuardedNullDereferenceExcluded", ExpectSp0002: false, ExpectSp0010: false));

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
                Assert.That(summary.SchemaVersion, Is.EqualTo("1.2"));
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
                Assert.That(summary.ActionableUnobservedOperationKinds, Is.Empty);
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
        public async Task FuzzRunner_KnownImpureConsoleCase_ProducesSp0002()
        {
            var source = """
using System;
using SharpProof.Attributes;

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
            Assert.That(analysis.Diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
            Assert.That(analysis.Findings.Any(finding => finding.Category == "impure_missing_sp0002"), Is.False);
            Assert.That(analysis.OperationKinds.ContainsKey("Invocation"), Is.True);
        }

        [Test]
        public void FuzzOptions_Parse_RejectsInvalidDurationsAsArgumentErrors()
        {
            var nonFinite = Assert.Throws<ArgumentException>(
                static () => FuzzOptions.Parse(new[] { "--iterations", "0", "--seconds", "Infinity" }));
            Assert.That(nonFinite!.Message, Does.Contain("--seconds expects a finite non-negative number."));

            var outOfRange = Assert.Throws<ArgumentException>(
                static () => FuzzOptions.Parse(new[] { "--iterations", "0", "--hours", "1E100" }));
            Assert.That(outOfRange!.Message, Does.Contain("--hours expects a duration within TimeSpan range."));
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
            Assert.That(families, Does.Contain("PureNestedOwnershipChain"));
            Assert.That(families, Does.Contain("ImpureOwnershipEscapeChain"));
            Assert.That(families, Does.Contain("ConservativeSwitchExpression"));
            Assert.That(families, Does.Contain("ConservativeRangeSlice"));
            Assert.That(families, Does.Contain("ConservativeWithExpression"));
            Assert.That(families, Does.Contain("PureImplicitIndexerReference"));
            Assert.That(families, Does.Contain("ConservativeInterpolatedStringHandler"));
            Assert.That(families, Does.Contain("ConservativeAddressOf"));
            Assert.That(families, Does.Contain("PureInlineArrayAccess"));
            Assert.That(families, Does.Contain("ConservativeDeclarationPattern"));
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
            var analyses = await AnalyzeCachedRegistryFamiliesAsync();

            foreach (var expectation in ExpandedCoverageExpectations)
            {
                var analysis = analyses[expectation.Family];
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
        public async Task PureOwnershipCoverageFamily_RemainsDiagnosticFree()
        {
            var analysis = (await AnalyzeCachedRegistryFamiliesAsync())["PureNestedOwnershipChain"];

            Assert.That(analysis.CompilationErrors, Is.Empty);
            Assert.That(analysis.Findings, Is.Empty);
            Assert.That(
                analysis.Diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
                Is.False);
            Assert.That(
                analysis.Diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId),
                Is.False);
        }

        [Test]
        public async Task ImpureOwnershipCoverageFamily_EmitsSp0002()
        {
            var analysis = (await AnalyzeCachedRegistryFamiliesAsync())["ImpureOwnershipEscapeChain"];

            Assert.That(analysis.CompilationErrors, Is.Empty);
            Assert.That(analysis.Findings, Is.Empty);
            Assert.That(
                analysis.Diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
                Is.True);
        }

        [Test]
        public async Task ExceptionCoverageFamilies_EmitSp0002_And_Sp0010()
        {
            var analyses = await AnalyzeCachedRegistryFamiliesAsync();

            foreach (var family in ExceptionCoverageFamilies)
            {
                var registryEntry = RegistryEntriesById[family];
                var analysis = analyses[family];
                var purityDiagnostics = analysis.Diagnostics
                    .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
                    .ToArray();
                var exceptionDiagnostics = analysis.Diagnostics
                    .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId)
                    .ToArray();

                Assert.That(analysis.CompilationErrors, Is.Empty, family);
                Assert.That(
                    analysis.Findings,
                    Is.Empty,
                    family + Environment.NewLine + string.Join(Environment.NewLine, analysis.DiagnosticSignatures));
                Assert.That(
                    exceptionDiagnostics.Length,
                    Is.GreaterThan(0),
                    family + " missing SP0010");
                if (registryEntry.Expectation.Sp0002 == Sp0002ExpectationKind.MustEmit)
                {
                    Assert.That(
                        purityDiagnostics.Length,
                        Is.GreaterThan(0),
                        family + " missing SP0002");
                }

                if (purityDiagnostics.Length > 0)
                {
                    AssertRequiredProperties(
                        purityDiagnostics,
                        family,
                        SharpProofDiagnostics.ImpurityCategoryProperty,
                        SharpProofDiagnostics.ImpurityRuleProperty,
                        SharpProofDiagnostics.ImpurityOperationKindProperty);
                }

                AssertRequiredProperties(
                    exceptionDiagnostics,
                    family,
                    registryEntry.Expectation.RequiredSp0010Properties.ToArray());

                if (!registryEntry.Expectation.RequiredAnySp0010Properties.IsDefaultOrEmpty)
                {
                    Assert.That(
                        exceptionDiagnostics.Any(diagnostic => HasRequiredProperties(
                            diagnostic,
                            registryEntry.Expectation.RequiredAnySp0010Properties.ToArray())),
                        Is.True,
                        family + " missing additive SP0010 evidence");
                }
            }
        }

        [Test]
        public async Task ExceptionNegativeCoverageFamilies_RespectNoEscapeExpectations()
        {
            var analyses = await AnalyzeCachedRegistryFamiliesAsync();

            foreach (var expectation in ExceptionNegativeExpectations)
            {
                var analysis = analyses[expectation.Family];
                var purityDiagnostics = analysis.Diagnostics
                    .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
                    .ToArray();
                var exceptionDiagnostics = analysis.Diagnostics
                    .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId)
                    .ToArray();

                Assert.That(analysis.CompilationErrors, Is.Empty, expectation.Family);
                Assert.That(analysis.Findings, Is.Empty, expectation.Family);
                Assert.That(
                    purityDiagnostics.Length > 0,
                    Is.EqualTo(expectation.ExpectSp0002),
                    expectation.Family + " SP0002 expectation mismatch");
                Assert.That(
                    exceptionDiagnostics.Length > 0,
                    Is.EqualTo(expectation.ExpectSp0010),
                    expectation.Family + " SP0010 expectation mismatch");
            }
        }

        [Test]
        public async Task ExceptionCoverageFamily_DiagnosticSignatures_PreserveExceptionEdgesProperty_WhenPresent()
        {
            var analysis = (await AnalyzeCachedRegistryFamiliesAsync())["ExceptionInvokedLocalFunctionThrow"];
            var exceptionDiagnostics = analysis.Diagnostics
                .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId)
                .ToArray();
            var exceptionDiagnostic = exceptionDiagnostics
                .First(diagnostic =>
                    diagnostic.Properties.ContainsKey(SharpProofDiagnostics.ExceptionTypesProperty) &&
                    diagnostic.Properties.ContainsKey(SharpProofDiagnostics.ExceptionCategoriesProperty) &&
                    diagnostic.Properties.ContainsKey(SharpProofDiagnostics.ExceptionSourcesProperty));

            Assert.That(analysis.CompilationErrors, Is.Empty);
            AssertRequiredProperties(
                exceptionDiagnostics,
                "ExceptionInvokedLocalFunctionThrow",
                SharpProofDiagnostics.ExceptionTypesProperty,
                SharpProofDiagnostics.ExceptionCategoriesProperty,
                SharpProofDiagnostics.ExceptionSourcesProperty);

            if (exceptionDiagnostic.Properties.TryGetValue(SharpProofDiagnostics.ExceptionEdgesProperty, out var edges))
            {
                Assert.That(string.IsNullOrWhiteSpace(edges), Is.False);
                Assert.That(
                    analysis.DiagnosticSignatures.Any(signature => signature.Contains(
                        SharpProofDiagnostics.ExceptionEdgesProperty + "=" + edges,
                        StringComparison.Ordinal)),
                    Is.True,
                    "Diagnostic signature should preserve sharpproof.exceptions.edges when the analyzer emits it.");
            }
        }

        [Test]
        public async Task RunCasesAsync_SummaryReport_PreservesExceptionEdgesProperty_InUnexpectedSp0010Finding_WhenPresent()
        {
            var outputDirectory = CreateOutputDirectory();
            try
            {
                const string source = """
using System;
using SharpProof.Attributes;

public class FuzzExceptionEdgesReportCase
{
    [EnforcePure]
    public int TestMethod()
    {
        throw new InvalidOperationException("boom");
    }
}
""";

                var fuzzCase = new FuzzCase(
                    "FuzzExceptionEdgesReportCase",
                    "FuzzExceptionEdgesReportCase",
                    source,
                    AllowUnsafe: false,
                    new FuzzExpectation(
                        Sp0002ExpectationKind.MayEmitConservatively,
                        Sp0010ExpectationKind.MustNotEmit,
                        ImmutableArray.Create(
                            SharpProofDiagnostics.ImpurityCategoryProperty,
                            SharpProofDiagnostics.ImpurityRuleProperty,
                            SharpProofDiagnostics.ImpurityOperationKindProperty),
                        ImmutableArray.Create(
                            SharpProofDiagnostics.ExceptionTypesProperty,
                            SharpProofDiagnostics.ExceptionCategoriesProperty,
                            SharpProofDiagnostics.ExceptionSourcesProperty),
                        ImmutableArray<string>.Empty));

                var analysis = await FuzzRunner.AnalyzeCaseAsync(fuzzCase);
                var exceptionDiagnostic = analysis.Diagnostics.Single(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId);
                var summary = await FuzzRunner.RunCasesAsync(
                    ImmutableArray.Create(fuzzCase),
                    new FuzzOptions
                    {
                        OutputDirectory = outputDirectory,
                        MaxInterestingCases = 4,
                        MaxInterestingCasesPerFamily = 4,
                        CheckpointEvery = 1,
                        Parallelism = 4,
                        Quiet = true
                    });

                Assert.That(analysis.CompilationErrors, Is.Empty);
                AssertRequiredProperties(
                    new[] { exceptionDiagnostic },
                    fuzzCase.Family,
                    SharpProofDiagnostics.ExceptionTypesProperty,
                    SharpProofDiagnostics.ExceptionCategoriesProperty,
                    SharpProofDiagnostics.ExceptionSourcesProperty);
                Assert.That(summary.FindingCount, Is.GreaterThan(0));

                var summaryPath = Path.Combine(outputDirectory, "summary.json");
                Assert.That(File.Exists(summaryPath), Is.True);

                var summaryJson = await File.ReadAllTextAsync(summaryPath);
                var persistedSummary = JsonSerializer.Deserialize<FuzzRunSummary>(summaryJson);
                Assert.That(persistedSummary, Is.Not.Null);

                var unexpectedSp0010Finding = persistedSummary!.Findings.Single(finding => finding.Category == "unexpected_sp0010");
                var expectedSignature = analysis.DiagnosticSignatures.Single(signature => signature.Contains("SP0010", StringComparison.Ordinal));
                Assert.That(unexpectedSp0010Finding.Details, Does.Contain(expectedSignature));

                if (exceptionDiagnostic.Properties.TryGetValue(SharpProofDiagnostics.ExceptionEdgesProperty, out var edges))
                {
                    Assert.That(string.IsNullOrWhiteSpace(edges), Is.False);
                    Assert.That(
                        unexpectedSp0010Finding.Details.Any(detail => detail.Contains(
                            SharpProofDiagnostics.ExceptionEdgesProperty + "=" + edges,
                            StringComparison.Ordinal)),
                        Is.True,
                        "summary.json findings should preserve sharpproof.exceptions.edges when the analyzer emits it.");
                }
            }
            finally
            {
                DeleteOutputDirectory(outputDirectory);
            }
        }

        private static void AssertRequiredProperties(
            Diagnostic[] diagnostics,
            string family,
            params string[] propertyNames)
        {
            foreach (var diagnostic in diagnostics)
            {
                foreach (var propertyName in propertyNames)
                {
                    Assert.That(
                        diagnostic.Properties.TryGetValue(propertyName, out var value) &&
                        !string.IsNullOrWhiteSpace(value),
                        Is.True,
                        $"{family} missing property {propertyName}");
                }
            }
        }

        private static bool HasRequiredProperties(Diagnostic diagnostic, params string[] propertyNames)
        {
            return propertyNames.All(propertyName =>
                diagnostic.Properties.TryGetValue(propertyName, out var value) &&
                !string.IsNullOrWhiteSpace(value));
        }

        private static Task<ImmutableDictionary<string, FuzzCaseAnalysis>> AnalyzeCachedRegistryFamiliesAsync()
        {
            return ToolingFuzzAnalysisCache.GetRegistryEntryAnalysesAsync();
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
