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
            "SharpProof.ToolingTest/EffectSummaryToolTests.cs");
        var root = recommendation.RootElement;

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Tooling"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("EffectSummaryToolTests"));
        Assert.That(
            GetStringArray(GetEvidenceEntry(root, "SharpProof.ToolingTest/EffectSummaryToolTests.cs", "changed-test-file"),
                "selectedTestFixtures"),
            Does.Contain("EffectSummaryToolTests"));
    }

    [Test]
    public async Task ListOnlyJson_UsesToolingLaneForLinkedPackagingFixture()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.ToolingTest/AnalyzerPackagingTests.cs");
        var root = recommendation.RootElement;

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Tooling"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("AnalyzerPackagingTests"));
        Assert.That(
            GetStringArray(GetEvidenceEntry(root, "SharpProof.ToolingTest/AnalyzerPackagingTests.cs", "changed-test-file"),
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
        const string changedFile = "SharpProof.Symbolic/Smt/CSharpConditionToFormula.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;

        AssertUsesInferredModuleClosure(root, changedFile, "Symbolic");
    }

    [Test]
    public async Task ListOnlyJson_SelectsRegexFixtureForProofCoreStringRegexFormulaChange()
    {
        const string changedFile = "SharpProof.ProofCore/Z3FormulaEncoder.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;

        AssertUsesInferredModuleClosure(root, changedFile, "ProofCore");
    }

    [Test]
    public async Task ListOnlyJson_SelectsRuntimeHazardFixturesForProofCoreSolverChange()
    {
        const string changedFile = "SharpProof.ProofCore/SmtSolver.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;

        AssertUsesInferredModuleClosure(root, changedFile, "ProofCore");
    }

    [Test]
    public async Task ListOnlyJson_SelectsAnalyzerSmtFixturesForExceptionPathFacts()
    {
        const string changedFile = "SharpProof.Analyzer/ExceptionFlowAnalyzer.PathFacts.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;

        AssertUsesInferredModuleClosure(root, changedFile, "Analyzer");
    }

    [Test]
    public async Task ListOnlyJson_SelectsRuntimeHazardFixturesForSymbolicQueryService()
    {
        const string changedFile = "SharpProof.Symbolic/SymbolicRuntimeHazardQueryService.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;

        AssertUsesInferredModuleClosure(root, changedFile, "Symbolic");
    }

    [Test]
    public async Task ListOnlyJson_SelectsRuntimeHazardDiagnosticsForAnalyzerConfigKeys()
    {
        const string changedFile = "SharpProof.Analyzer/Configuration/ConfigKeys.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;

        AssertUsesInferredModuleClosure(root, changedFile, "Analyzer");
    }

    [Test]
    public async Task ListOnlyJson_SelectsRuntimeHazardDiagnosticsForAnalyzerOptionRegistry()
    {
        const string changedFile = "SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptionRegistry.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;

        AssertUsesInferredModuleClosure(root, changedFile, "Analyzer");
    }

    [Test]
    public async Task ListOnlyJson_SelectsAnalyzerSmtFixturesForPathFactRule()
    {
        const string changedFile = "SharpProof.Analyzer/Engine/Rules/BinaryOperationPurityRule.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;

        AssertUsesInferredModuleClosure(root, changedFile, "Analyzer");
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

        AssertUsesInferredModuleClosure(root, changedFile, "Analyzer");
    }

    [Test]
    public async Task ListOnlyJson_SelectsComparerDispatchFixturesForFieldOrPropertyInitializerHelper()
    {
        const string changedFile = "SharpProof.Analyzer/Engine/Rules/FieldOrPropertyInitializerOperationHelper.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;

        AssertUsesInferredModuleClosure(root, changedFile, "Analyzer");
    }

    [Test]
    public async Task ListOnlyJson_DoesNotFallbackWhenMappedAnalyzerFilesShareFixtures()
    {
        const string exceptionSitesFile = "SharpProof.Analyzer/ExceptionFlowAnalyzer.ExceptionSites.cs";
        const string exceptionQueryFile = "SharpProof.Analyzer/ExceptionFlowQuery.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(exceptionSitesFile + "," + exceptionQueryFile);
        var root = recommendation.RootElement;

        AssertUsesInferredModuleClosure(root, exceptionSitesFile, "Analyzer");
        AssertUsesInferredModuleClosure(root, exceptionQueryFile, "Analyzer");
    }

    [Test]
    public async Task ListOnlyJson_SelectsSymbolicFactsForAnalyzerStateMerge()
    {
        const string changedFile = "SharpProof.Analyzer/Engine/PurityAnalysisStateMerger.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;

        AssertUsesInferredModuleClosure(root, changedFile, "Analyzer");
    }

    [Test]
    public async Task ListOnlyJson_SelectsSpecificEvidenceForSymbolicProgramPointFacts()
    {
        const string changedFile = "SharpProof.Symbolic/SymbolicProgramPointFacts.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;

        AssertUsesInferredModuleClosure(root, changedFile, "Symbolic");
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
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Is.Not.Empty);
        Assert.That(
            GetStringArray(root, "fullSuiteFallbackReasons").Single(),
            Does.Contain("uses inferred module-closure selection"));
        Assert.That(
            GetEvidenceEntry(root, "SharpProof.Analyzer/Engine/Analysis/FutureUnmappedAnalyzerFile.cs",
                "inventory-module-closure").GetProperty("module").GetString(),
            Is.EqualTo("Analyzer"));
    }

    [Test]
    public async Task ListOnlyJson_PreservesTwentyWorkerCommandForSymbolicCliChange()
    {
        const string changedFile = "Tools/SharpProof.SymbolicCli/Program.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(20, changedFile);
        var root = recommendation.RootElement;
        var command = root.GetProperty("suggestedCommand").GetString();

        AssertUsesInferredModuleClosure(root, changedFile, "SymbolicCli");
        Assert.That(command, Does.Contain("-Workers 20"));
        Assert.That(command, Does.Contain("-TestLane All"));
    }

    [Test]
    public async Task ListOnlyJson_UsesToolingLaneForFuzzCoreChange()
    {
        const string changedFile = "Tools/SharpProof.Fuzz.Core/Program.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var command = root.GetProperty("suggestedCommand").GetString();

        AssertUsesInferredModuleClosure(root, changedFile, "FuzzCore");
        Assert.That(command, Does.Contain("-TestLane All"));
    }

    [Test]
    public async Task ListOnlyJson_UsesGeneratedInventoryForSymbolReferences()
    {
        const string changedFile = "SharpProof.CodeFixes/SharpProofCodeFixProvider.cs";
        using var recommendation = await RunImpactedSelectorJsonAsync(changedFile);
        var root = recommendation.RootElement;
        var evidence = GetEvidenceEntry(root, changedFile, "inventory-symbol-reference");

        AssertUsesInferredModuleClosure(root, changedFile, "CodeFixes");
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("All"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("SharpProofCodeFixTests"));
        Assert.That(evidence.GetProperty("module").GetString(), Is.EqualTo("CodeFixes"));
        Assert.That(GetStringArray(evidence, "selectedTestFixtures"), Does.Contain("SharpProofCodeFixTests"));
        Assert.That(GetStringArray(evidence, "tokens"), Does.Contain("SharpProofCodeFixProvider"));
    }

    [Test]
    public async Task ListOnlyJson_UsesToolingLaneForLinkedCodeFixFixture()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.ToolingTest/SharpProofCodeFixTests.cs");
        var root = recommendation.RootElement;

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Tooling"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("SharpProofCodeFixTests"));
        Assert.That(
            GetStringArray(GetEvidenceEntry(root, "SharpProof.ToolingTest/SharpProofCodeFixTests.cs", "changed-test-file"),
                "selectedTestFixtures"),
            Does.Contain("SharpProofCodeFixTests"));
    }

    [Test]
    public async Task ListOnlyJson_UsesToolingLaneForLinkedImpactedSelectionFixture()
    {
        using var recommendation = await RunImpactedSelectorJsonAsync(
            "SharpProof.ToolingTest/ImpactedTestSelectionScriptTests.cs");
        var root = recommendation.RootElement;

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunPartial"));
        Assert.That(root.GetProperty("testLane").GetString(), Is.EqualTo("Tooling"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Does.Contain("ImpactedTestSelectionScriptTests"));
        Assert.That(
            GetStringArray(
                GetEvidenceEntry(root, "SharpProof.ToolingTest/ImpactedTestSelectionScriptTests.cs", "changed-test-file"),
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
        Assert.That(GetStringArray(inventory, "modules"), Does.Contain("Contracts"));
        Assert.That(GetStringArray(inventory, "modules"), Does.Contain("ToolingCore"));
        Assert.That(GetStringArray(inventory, "modules"), Does.Not.Contain("Shared"));
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
        Assert.That(moduleNames, Does.Contain("Contracts"));
        Assert.That(moduleNames, Does.Contain("ToolingCore"));
        Assert.That(moduleNames, Does.Contain("SymbolicCliCore"));
        Assert.That(moduleNames, Does.Not.Contain("Shared"));
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
        var startInfo = TestProcessSupport.CreatePowerShellStartInfo(repositoryRoot);

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

    private static void AssertUsesInferredModuleClosure(
        JsonElement root,
        string changedFile,
        string expectedModule)
    {
        var evidence = GetEvidenceEntry(root, changedFile, "inventory-module-closure");

        Assert.That(root.GetProperty("requiresFullSuite").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("suggestedAction").GetString(), Is.EqualTo("RunFullSuite"));
        Assert.That(GetStringArray(root, "selectedTestFixtures"), Is.Not.Empty);
        Assert.That(evidence.GetProperty("module").GetString(), Is.EqualTo(expectedModule));
        Assert.That(evidence.GetProperty("reason").GetString(),
            Does.StartWith("Generated module dependency closure impacts modules:"));
        Assert.That(GetStringArray(root, "fullSuiteFallbackReasons"),
            Has.Some.Contains("uses inferred module-closure selection"));
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
