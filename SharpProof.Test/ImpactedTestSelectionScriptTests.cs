using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class ImpactedTestSelectionScriptTests
{
    [OneTimeTearDown]
    public async Task TearDownJsonSessionAsync()
    {
        if (JsonSession.IsValueCreated) await JsonSession.Value.DisposeAsync();
    }

    private static readonly Lazy<ImpactedTestSelectionJsonSession> JsonSession =
        new(() => ImpactedTestSelectionJsonSession.Start(FindRepositoryRoot()));

    [Test]
    public async Task ListOnlyJson_SelectsOwningFixtureForChangedTestFile()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.Test/SymbolicProgramPointFactTests.cs");
        var root = recommendation.RootElement;

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("SymbolicProgramPointFactTests"));
        Assert.That(
            GetStringArray(
                GetEvidenceEntry(root, "SharpProof.Test/SymbolicProgramPointFactTests.cs", "changed-test-file"),
                "selectedTestFixtures"),
            Does.Contain("SymbolicProgramPointFactTests"));
        Assert.That(
            root.GetProperty("testFilter").GetString(),
            Does.Contain("FullyQualifiedName~SymbolicProgramPointFactTests."));
    }

    [Test]
    public async Task ListOnlyJson_SelectsOwningFixtureForChangedToolingTestFile()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.ToolingTest/FuzzToolTests.cs");
        var root = recommendation.RootElement;

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Tooling"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("FuzzToolTests"));
        Assert.That(
            GetStringArray(GetEvidenceEntry(root, "SharpProof.ToolingTest/FuzzToolTests.cs", "changed-test-file"),
                "selectedTestFixtures"),
            Does.Contain("FuzzToolTests"));
    }

    [Test]
    public async Task ListOnlyJson_FilterDoesNotAssumeTheFixtureNamespace()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.ToolingTest/BaselineWorkflowTests.cs");
        var root = recommendation.RootElement;

        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Tooling"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("BaselineWorkflowTests"));
        Assert.That(
            root.GetProperty("testFilter").GetString(),
            Does.Contain("FullyQualifiedName~BaselineWorkflowTests."));
        Assert.That(
            root.GetProperty("testFilter").GetString(),
            Does.Not.Contain("SharpProof.Test.BaselineWorkflowTests"));
    }

    [Test]
    public async Task ListOnlyJson_UsesToolingLaneForLinkedEffectSummaryFixture()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.Test/EffectSummaryToolTests.cs");
        var root = recommendation.RootElement;

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Tooling"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("EffectSummaryToolTests"));
        Assert.That(
            GetStringArray(GetEvidenceEntry(root, "SharpProof.Test/EffectSummaryToolTests.cs", "changed-test-file"),
                "selectedTestFixtures"),
            Does.Contain("EffectSummaryToolTests"));
    }

    [Test]
    public async Task ListOnlyJson_UsesToolingLaneForLinkedPackagingFixture()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.Test/AnalyzerPackagingTests.cs");
        var root = recommendation.RootElement;

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Tooling"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("AnalyzerPackagingTests"));
        Assert.That(
            GetStringArray(GetEvidenceEntry(root, "SharpProof.Test/AnalyzerPackagingTests.cs", "changed-test-file"),
                "selectedTestFixtures"),
            Does.Contain("AnalyzerPackagingTests"));
    }

    [Test]
    public async Task ListOnlyJson_FallsBackForSharedTestInfrastructure()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.Test/SharpProof.Test.csproj");
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
            "SharpProof.Symbolic/Smt/CSharpConditionToFormula.cs");
        var root = recommendation.RootElement;
        var fixtures = GetStringArray(root, "selectedTestFixtures");
        var evidence = GetEvidenceEntry(root, "SharpProof.Symbolic/Smt/CSharpConditionToFormula.cs", "path-map");

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
        Assert.That(evidence.GetProperty("reason").GetString(),
            Is.EqualTo("Symbolic SMT string-length and regex translation change"));
    }

    [Test]
    public async Task ListOnlyJson_SelectsRegexFixtureForProofCoreStringRegexFormulaChange()
    {
        const string changedFile = "SharpProof.ProofCore/Z3FormulaEncoder.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var fixtures = GetStringArray(root, "selectedTestFixtures");
        var evidence = GetEvidenceEntry(root, changedFile, "path-map");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(fixtures, Does.Contain("RegexTests"));
        Assert.That(fixtures, Does.Contain("ProofCoreZ3SmokeTests"));
        Assert.That(fixtures, Does.Contain("SmtAnalysisServiceTests"));
        Assert.That(fixtures, Does.Contain("StringLengthSmtTests"));
        Assert.That(evidence.GetProperty("reason").GetString(), Is.EqualTo("ProofCore SMT formula or encoder change"));
        Assert.That(GetStringArray(evidence, "selectedTestFixtures"), Does.Contain("RegexTests"));
    }

    [Test]
    public async Task ListOnlyJson_SelectsRuntimeHazardFixturesForProofCoreSolverChange()
    {
        const string changedFile = "SharpProof.ProofCore/SmtSolver.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var fixtures = GetStringArray(root, "selectedTestFixtures");
        var evidence = GetEvidenceEntry(root, changedFile, "path-map");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(fixtures, Does.Contain("RegexTests"));
        Assert.That(fixtures, Does.Contain("SemanticOracleSmtTests"));
        Assert.That(fixtures, Does.Contain("SymbolicRuntimeHazardQueryTests"));
        Assert.That(fixtures, Does.Contain("DiagnosticEvidenceTests"));
        Assert.That(evidence.GetProperty("reason").GetString(), Is.EqualTo("ProofCore SMT solver change"));
    }

    [Test]
    public async Task ListOnlyJson_SelectsAnalyzerSmtFixturesForExceptionPathFacts()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.Analyzer/ExceptionFlowAnalyzer.PathFacts.cs");
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
        const string changedFile = "SharpProof.Symbolic/SymbolicRuntimeHazardQueryService.cs";
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
        const string changedFile = "SharpProof.Analyzer/Configuration/ConfigKeys.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var fixtures = GetStringArray(root, "selectedTestFixtures");
        var evidence = GetEvidenceEntry(root, changedFile, "path-map");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(GetStringArray(root, "fullSuiteFallbackReasons"), Is.Empty);
        Assert.That(fixtures, Does.Contain("DiagnosticEvidenceTests"));
        Assert.That(fixtures, Does.Contain("SemanticOracleSmtTests"));
        Assert.That(evidence.GetProperty("reason").GetString(),
            Is.EqualTo("Analyzer runtime-hazard configuration change"));
    }

    [Test]
    public async Task ListOnlyJson_SelectsRuntimeHazardDiagnosticsForAnalyzerOptionRegistry()
    {
        const string changedFile = "SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptionRegistry.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var fixtures = GetStringArray(root, "selectedTestFixtures");
        var evidence = GetEvidenceEntry(root, changedFile, "path-map");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(fixtures, Does.Contain("DiagnosticEvidenceTests"));
        Assert.That(fixtures, Does.Contain("SemanticOracleSmtTests"));
        Assert.That(evidence.GetProperty("reason").GetString(),
            Is.EqualTo("Analyzer runtime-hazard configuration change"));
    }

    [Test]
    public async Task ListOnlyJson_SelectsAnalyzerSmtFixturesForPathFactRule()
    {
        const string changedFile = "SharpProof.Analyzer/Engine/Rules/BinaryOperationPurityRule.cs";
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
    public async Task ListOnlyJson_SelectsArchitectureGuardForProductionMetricsScript()
    {
        const string changedFile = "scripts/Get-SharpProofProductionMetrics.ps1";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var evidence = GetEvidenceEntry(root, changedFile, "path-map");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(GetStringArray(root, "fullSuiteFallbackReasons"), Is.Empty);
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("ArchitectureReductionTests"));
        Assert.That(evidence.GetProperty("reason").GetString(), Is.EqualTo("Production metrics script change"));
    }

    [Test]
    public async Task ListOnlyJson_SelectsProfileConformanceForConfigurationProfile()
    {
        const string changedFile = "config/profiles/sharpproof-ci.globalconfig";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var evidence = GetEvidenceEntry(root, changedFile, "path-map");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Main"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"),
            Is.EquivalentTo(new[] { "ConfigurationProfileTests" }));
        Assert.That(evidence.GetProperty("reason").GetString(), Is.EqualTo("SharpProof adoption profile change"));
    }

    [TestCase("scripts/test-impact-inventory.json")]
    [TestCase("scripts/test-impact-modules.json")]
    public async Task ListOnlyJson_SelectsSelectorTestsForImpactMetadata(string changedFile)
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var evidence = GetEvidenceEntry(root, changedFile, "path-map");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(GetStringArray(root, "fullSuiteFallbackReasons"), Is.Empty);
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("ImpactedTestSelectionScriptTests"));
        Assert.That(evidence.GetProperty("reason").GetString(), Is.EqualTo("Impacted-test metadata change"));
    }

    [Test]
    public async Task ListOnlyJson_SelectsRuntimeTypeTestFixturesForMethodInvocationRule()
    {
        const string changedFile = "SharpProof.Analyzer/Engine/Rules/MethodInvocationPurityRule.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var fixtures = GetStringArray(root, "selectedTestFixtures");
        var evidence = GetEvidenceEntry(root, changedFile, "path-map");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(fixtures, Does.Contain("PatternSmtInvariantTests"));
        Assert.That(fixtures, Does.Contain("ReferenceReachabilitySmtTests"));
        Assert.That(fixtures, Does.Contain("SymbolicRuntimeHazardQueryTests"));
        Assert.That(fixtures, Does.Contain("DiagnosticEvidenceTests"));
        Assert.That(evidence.GetProperty("reason").GetString(),
            Is.EqualTo("Analyzer as-conversion and runtime type-test SMT change"));
    }

    [Test]
    public async Task ListOnlyJson_SelectsComparerDispatchFixturesForFieldOrPropertyInitializerHelper()
    {
        const string changedFile = "SharpProof.Analyzer/Engine/Rules/FieldOrPropertyInitializerOperationHelper.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var fixtures = GetStringArray(root, "selectedTestFixtures");
        var evidence = GetEvidenceEntry(root, changedFile, "path-map");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(GetStringArray(root, "fullSuiteFallbackReasons"), Is.Empty);
        Assert.That(fixtures, Does.Contain("ObjectEqualsDispatchTests"));
        Assert.That(fixtures, Does.Contain("ComparisonDispatchTests"));
        Assert.That(evidence.GetProperty("reason").GetString(),
            Is.EqualTo("Field/property initializer receiver analysis change"));
    }

    [Test]
    public async Task ListOnlyJson_DoesNotFallbackWhenMappedAnalyzerFilesShareFixtures()
    {
        const string exceptionSitesFile = "SharpProof.Analyzer/ExceptionFlowAnalyzer.ExceptionSites.cs";
        const string exceptionQueryFile = "SharpProof.Analyzer/ExceptionFlowQuery.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(exceptionSitesFile + "," + exceptionQueryFile);
        var root = recommendation.RootElement;
        var fixtures = GetStringArray(root, "selectedTestFixtures");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(GetStringArray(root, "fullSuiteFallbackReasons"), Is.Empty);
        Assert.That(fixtures, Does.Contain("ExceptionReachabilitySmtTests"));
        Assert.That(fixtures, Does.Contain("AuthoringRuntimeHazardDiagnosticTests"));
        Assert.That(
            GetEvidenceEntry(root, exceptionSitesFile, "path-map").GetProperty("reason").GetString(),
            Is.EqualTo("Exception site reachability and runtime-hazard analyzer change"));
        Assert.That(
            GetEvidenceEntry(root, exceptionQueryFile, "path-map").GetProperty("reason").GetString(),
            Is.EqualTo("Exception flow query reachability and runtime-hazard change"));
    }

    [Test]
    public async Task ListOnlyJson_SelectsSymbolicFactsForAnalyzerStateMerge()
    {
        const string changedFile = "SharpProof.Analyzer/Engine/PurityAnalysisEngine.StateMerge.cs";
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
        Assert.That(evidence.GetProperty("reason").GetString(),
            Is.EqualTo("Analyzer symbolic state-merge and path-fact change"));
    }

    [Test]
    public async Task ListOnlyJson_SelectsSpecificEvidenceForSymbolicProgramPointFacts()
    {
        const string changedFile = "SharpProof.Symbolic/SymbolicProgramPointFacts.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var fixtures = GetStringArray(root, "selectedTestFixtures");
        var evidence = GetEvidenceEntry(root, changedFile, "path-map");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(fixtures, Does.Contain("SymbolicProgramPointFactTests"));
        Assert.That(fixtures, Does.Contain("StringLengthSmtTests"));
        Assert.That(fixtures, Does.Contain("SymbolicRuntimeHazardQueryTests"));
        Assert.That(evidence.GetProperty("reason").GetString(),
            Is.EqualTo("Symbolic program-point fact extraction change"));
    }

    [Test]
    public async Task ListOnlyJson_PreservesFullSuiteFallbackForAnalyzerCore()
    {
        const string changedFile = "SharpProof.Analyzer/Engine/PurityAnalysisEngine.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var fixtures = GetStringArray(root, "selectedTestFixtures");
        var manifestEvidence = GetEvidenceEntry(root, changedFile, "module-manifest");
        var command = root.GetProperty("suggestedCommand").GetString();

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunFullSuite"));
        Assert.That(
            GetStringArray(root, "fullSuiteFallbackReasons"),
            Has.Some.Contains("PurityCore"));
        Assert.That(manifestEvidence.GetProperty("module").GetString(), Is.EqualTo("PurityCore"));
        Assert.That(fixtures, Does.Contain("DiagnosticEvidenceTests"));
        Assert.That(fixtures, Does.Contain("EnsuresContractTests"));
        Assert.That(fixtures, Does.Contain("AnalyzerFeatureCompositionTests"));
        Assert.That(command, Does.Contain("-TestLane All"));
    }

    [Test]
    public async Task ListOnlyJson_UsesExplicitEnsuresModuleAndReverseClosure()
    {
        const string changedFile = "SharpProof.Analyzer/MethodEnsuresAnalyzer.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var fixtures = GetStringArray(root, "selectedTestFixtures");
        var evidence = GetEvidenceEntry(root, changedFile, "module-manifest");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(fixtures, Is.EquivalentTo(new[]
        {
            "AnalyzerFeatureCompositionTests",
            "DiagnosticExplainPropertyTests",
            "EnsuresContractTests",
            "RequiresContractTests"
        }));
        Assert.That(evidence.GetProperty("module").GetString(), Is.EqualTo("Ensures"));
        Assert.That(
            root.GetProperty("selectionEvidence").EnumerateArray().Any(entry =>
                entry.GetProperty("changedFile").GetString() == changedFile &&
                (entry.GetProperty("source").GetString() == "token-reference" ||
                 entry.GetProperty("source").GetString() == "inventory-symbol-reference")),
            Is.False);
    }

    [Test]
    public async Task ListOnlyJson_PreservesFullSuiteFallbackForAnalyzerComposition()
    {
        const string changedFile = "SharpProof.Analyzer/SharpProofAnalyzer.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var evidence = GetEvidenceEntry(root, changedFile, "module-manifest");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunFullSuite"));
        Assert.That(
            GetStringArray(root, "selectedTestFixtures"),
            Is.EquivalentTo(new[] { "AnalyzerFeatureCompositionTests" }));
        Assert.That(evidence.GetProperty("module").GetString(), Is.EqualTo("AnalyzerComposition"));
        Assert.That(GetStringArray(root, "fullSuiteFallbackReasons"), Has.Some.Contains("AnalyzerComposition"));
    }

    [Test]
    public async Task ListOnlyJson_MissingModuleManifestFallsBackForAnalyzerChange()
    {
        const string changedFile = "SharpProof.Analyzer/MethodEnsuresAnalyzer.cs";
        var missingManifestPath = Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "missing-test-impact-modules.json");
        Assert.That(File.Exists(missingManifestPath), Is.False);

        using var recommendation = await RunImpactedSelectorJsonWithManifestAsync(
            missingManifestPath,
            changedFile);
        var root = recommendation.RootElement;
        var moduleManifest = root.GetProperty("moduleManifest");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunFullSuite"));
        Assert.That(moduleManifest.GetProperty("loaded").GetBoolean(), Is.False);
        Assert.That(moduleManifest.GetProperty("valid").GetBoolean(), Is.False);
        Assert.That(GetStringArray(root, "fullSuiteFallbackReasons"),
            Has.Some.Contains("invalid module impact manifest"));
    }

    [Test]
    public async Task ListOnlyJson_FallsBackForUnmappedAnalyzerProductionFile()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.Analyzer/Engine/Analysis/FutureUnmappedAnalyzerFile.cs");
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
        const string changedFile = "Tools/SharpProof.SymbolicCli/Program.cs";
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
    public async Task ListOnlyJson_UsesToolingLaneForFuzzCoreChange()
    {
        const string changedFile = "Tools/SharpProof.Fuzz.Core/Program.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var fixtures = GetStringArray(root, "selectedTestFixtures");
        var command = root.GetProperty("suggestedCommand").GetString();

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Tooling"));
        Assert.That(fixtures, Does.Contain("FuzzToolTests"));
        Assert.That(fixtures, Does.Contain("RoslynShapeManifestCoverageTests"));
        Assert.That(command, Does.Contain("-TestLane Tooling"));
    }

    [Test]
    public async Task ListOnlyJson_UsesGeneratedInventoryForSymbolReferences()
    {
        const string changedFile = "SharpProof.CodeFixes/SharpProofCodeFixProvider.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var evidence = GetEvidenceEntry(root, changedFile, "inventory-symbol-reference");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Tooling"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("SharpProofCodeFixTests"));
        Assert.That(evidence.GetProperty("module").GetString(), Is.EqualTo("CodeFixes"));
        Assert.That(GetStringArray(evidence, "selectedTestFixtures"), Does.Contain("SharpProofCodeFixTests"));
        Assert.That(GetStringArray(evidence, "tokens"), Does.Contain("SharpProofCodeFixProvider"));
    }

    [Test]
    public async Task ListOnlyJson_UsesToolingLaneForLinkedCodeFixFixture()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.Test/SharpProofCodeFixTests.cs");
        var root = recommendation.RootElement;

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Tooling"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("SharpProofCodeFixTests"));
        Assert.That(
            GetStringArray(GetEvidenceEntry(root, "SharpProof.Test/SharpProofCodeFixTests.cs", "changed-test-file"),
                "selectedTestFixtures"),
            Does.Contain("SharpProofCodeFixTests"));
    }

    [Test]
    public async Task ListOnlyJson_UsesToolingLaneForLinkedImpactedSelectionFixture()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.Test/ImpactedTestSelectionScriptTests.cs");
        var root = recommendation.RootElement;

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Tooling"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("ImpactedTestSelectionScriptTests"));
        Assert.That(
            GetStringArray(
                GetEvidenceEntry(root, "SharpProof.Test/ImpactedTestSelectionScriptTests.cs", "changed-test-file"),
                "selectedTestFixtures"),
            Does.Contain("ImpactedTestSelectionScriptTests"));
    }

    [Test]
    public async Task ListOnlyJson_InventoryBroadDependencyTriggersFullSuiteFallback()
    {
        const string changedFile = "SharpProof.Analyzer/SharpProofDiagnostics.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunFullSuite"));
        Assert.That(
            GetStringArray(root, "fullSuiteFallbackReasons"),
            Does.Contain(changedFile + " is broad generated fixture dependency"));
    }

    [Test]
    public async Task ListOnlyJson_IncludesImpactMetadataSummaries()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.CodeFixes/SharpProofCodeFixProvider.cs");
        var inventory = recommendation.RootElement.GetProperty("inventory");
        var moduleManifest = recommendation.RootElement.GetProperty("moduleManifest");

        Assert.That(inventory.GetProperty("loaded").GetBoolean(), Is.True);
        Assert.That(inventory.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(GetStringArray(inventory, "modules"), Does.Contain("Analyzer"));
        Assert.That(GetStringArray(inventory, "modules"), Does.Contain("Symbolic"));
        Assert.That(GetStringArray(inventory, "modules"), Does.Contain("Shared"));
        Assert.That(moduleManifest.GetProperty("loaded").GetBoolean(), Is.True);
        Assert.That(moduleManifest.GetProperty("valid").GetBoolean(), Is.True);
        Assert.That(moduleManifest.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(GetStringArray(moduleManifest, "modules"), Does.Contain("PurityCore"));
        Assert.That(GetStringArray(moduleManifest, "modules"), Does.Contain("Ensures"));
        Assert.That(GetStringArray(moduleManifest, "modules"), Does.Contain("AnalyzerComposition"));
    }

    [Test]
    public async Task ListOnlyExplain_PrintsInventoryEvidence()
    {
        var output = await RunImpactedSelectorTextAsync(
            true,
            "SharpProof.CodeFixes/SharpProofCodeFixProvider.cs");

        Assert.That(output, Does.Contain("Impact-selection evidence:"));
        Assert.That(output, Does.Contain("Inventory loaded: True"));
        Assert.That(output, Does.Contain("Module manifest loaded: True; valid: True"));
        Assert.That(output, Does.Contain("inventory-symbol-reference"));
        Assert.That(output, Does.Contain("module=CodeFixes"));
        Assert.That(output, Does.Contain("tokens=SharpProofCodeFixProvider"));
    }

    [Test]
    public void TestImpactInventory_DefinesModulesFixturesAndDependencies()
    {
        using var inventory = ReadImpactInventory();
        var root = inventory.RootElement;
        var moduleNames = GetStringArray(root, "modules", "name");
        var fixtureNames = GetStringArray(root, "testFixtures", "name");
        var codeFixDependency = GetInventoryEntry(root, "fixtureDependencies",
            "SharpProof.CodeFixes/SharpProofCodeFixProvider.cs");

        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(moduleNames, Does.Contain("Analyzer"));
        Assert.That(moduleNames, Does.Contain("Symbolic"));
        Assert.That(moduleNames, Does.Contain("ProofCore"));
        Assert.That(moduleNames, Does.Contain("Shared"));
        Assert.That(moduleNames, Does.Contain("TestInfrastructure"));
        Assert.That(fixtureNames, Does.Contain("ImpactedTestSelectionScriptTests"));
        Assert.That(fixtureNames, Does.Contain("SymbolicSourceQueryLineTests"));
        Assert.That(codeFixDependency.GetProperty("module").GetString(), Is.EqualTo("CodeFixes"));
        Assert.That(GetStringArray(codeFixDependency, "selectedTestFixtures"), Does.Contain("SharpProofCodeFixTests"));
    }

    [Test]
    public void TestImpactInventory_SourceFilesStayWithinKnownModules()
    {
        using var inventory = ReadImpactInventory();
        var sourceFiles = inventory.RootElement.GetProperty("sourceFiles").EnumerateArray().ToArray();
        var unknownSources = sourceFiles
            .Where(static entry =>
                string.Equals(entry.GetProperty("module").GetString(), "Unknown", StringComparison.Ordinal))
            .Select(static entry => entry.GetProperty("path").GetString())
            .ToArray();

        Assert.That(sourceFiles.Length, Is.GreaterThan(50));
        Assert.That(unknownSources, Is.Empty);
        Assert.That(
            GetInventoryEntry(inventory.RootElement, "highFanoutFiles",
                    "SharpProof.Analyzer/Engine/PurityAnalysisEngine.cs")
                .GetProperty("reason")
                .GetString(),
            Is.EqualTo("high-fanout analyzer core"));
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

    private static JsonDocument ReadImpactInventory()
    {
        var repositoryRoot = FindRepositoryRoot();
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "test-impact-inventory.json")));
    }

    private static Task<JsonDocument> RunImpactedSelectorJsonAsync(params string[] changedFiles)
    {
        return RunImpactedSelectorJsonAsync(0, changedFiles);
    }

    private static async Task<JsonDocument> RunImpactedSelectorJsonAsync(int workers, params string[] changedFiles)
    {
        return await JsonSession.Value.InvokeJsonAsync(workers, changedFiles);
    }

    private static async Task<JsonDocument> RunImpactedSelectorJsonWithManifestAsync(
        string moduleImpactManifestPath,
        params string[] changedFiles)
    {
        var output = await RunImpactedSelectorProcessAsync(
            false,
            true,
            moduleImpactManifestPath,
            changedFiles);
        return JsonDocument.Parse(output);
    }

    private static async Task<string> RunImpactedSelectorTextAsync(bool explain, params string[] changedFiles)
    {
        return await RunImpactedSelectorProcessAsync(
            explain,
            false,
            string.Empty,
            changedFiles);
    }

    private static async Task<string> RunImpactedSelectorProcessAsync(
        bool explain,
        bool json,
        string moduleImpactManifestPath,
        params string[] changedFiles)
    {
        var repositoryRoot = FindRepositoryRoot();
        var startInfo = CreatePowerShellStartInfo(repositoryRoot);

        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "scripts", "Invoke-SharpProofImpactedTests.ps1"));
        startInfo.ArgumentList.Add("-ListOnly");
        if (json) startInfo.ArgumentList.Add("-Json");

        if (explain) startInfo.ArgumentList.Add("-Explain");

        if (!string.IsNullOrWhiteSpace(moduleImpactManifestPath))
        {
            startInfo.ArgumentList.Add("-ModuleImpactManifestPath");
            startInfo.ArgumentList.Add(moduleImpactManifestPath);
        }

        startInfo.ArgumentList.Add("-ChangedFile");
        foreach (var changedFile in changedFiles) startInfo.ArgumentList.Add(changedFile);

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start impacted test selector.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            process.Kill(true);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new AssertionException(string.Join(
                Environment.NewLine,
                "Impacted test selector failed.",
                "Exit code: " + process.ExitCode,
                "stdout:",
                output,
                "stderr:",
                error));

        Assert.That(error, Is.Empty);
        return output;
    }

    private static ProcessStartInfo CreatePowerShellStartInfo(string repositoryRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = TestProcessSupport.FindPowerShellExecutable(),
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }

        return startInfo;
    }

    private static string[] GetStringArray(JsonElement root, string propertyName)
    {
        return root.GetProperty(propertyName)
            .EnumerateArray()
            .Select(static element => element.GetString() ?? string.Empty)
            .ToArray();
    }

    private static string[] GetStringArray(JsonElement root, string arrayPropertyName, string elementPropertyName)
    {
        return root.GetProperty(arrayPropertyName)
            .EnumerateArray()
            .Select(element => element.GetProperty(elementPropertyName).GetString() ?? string.Empty)
            .ToArray();
    }

    private static JsonElement GetInventoryEntry(JsonElement root, string propertyName, string path)
    {
        return root.GetProperty(propertyName)
            .EnumerateArray()
            .Single(entry => entry.GetProperty("path").GetString() == path);
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
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln"))) return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

}
