using NUnit.Framework;
using SharpProof.Symbolic;
namespace SharpProof.Test;
[TestFixture]
public sealed class EffectArchitectureTests {
    [Test]
    public void RemovedEffectInfrastructureCannotReturnToSupportedSurface() {
        var root = AnalyzerTestHost.GetRepositoryRoot();
        Assert.That(File.Exists(Path.Combine(root, "Tools", "SharpProof." + "Effect" + "Summary",
            "SharpProof." + "Effect" + "Summary.csproj")), Is.False);
        var removedFiles = new[] {
            "SharpProof.Contracts/BclPurityFallbackHeuristics.cs",
            "SharpProof.Contracts/InferredMethodSummary.cs",
            "SharpProof.Contracts/SharpProof.Contracts.csproj",
            "README.source.md",
            "docs/symbolic-query-api-migration.md",
            "SharpProof.Analyzer/Engine/Rules/CompilationSyntaxAccess.cs",
            "SharpProof.Analyzer/Engine/Rules/InvocationEvidence.cs",
            "SharpProof.Analyzer/LowerHexEncoding.cs",
            "SharpProof.Symbolic/SymbolicCapabilityService.cs",
            "SharpProof.Symbolic/SymbolicCapabilityModels.cs",
            "SharpProof.Symbolic/SymbolicQueryTarget.cs",
            "Tools/SharpProof.SymbolicCli.Core/SharpProof.SymbolicCli.Core.csproj",
            "SharpProof.CodeFixes/SharpProof.CodeFixes.csproj",
            "SharpProof.Vsix/SharpProof.Vsix.csproj",
            "Tools/VsixHarness/VsixHarness.csproj",
            "Tools/SharpProof.Baseline/SharpProof.Baseline.csproj",
            "Tools/SharpProof.Baseline.Core/SharpProof.Baseline.Core.csproj",
            "Tools/SharpProof.CorpusReport/SharpProof.CorpusReport.csproj",
            "Tools/SharpProof.CorpusReport.Core/SharpProof.CorpusReport.Core.csproj",
            "SharpProof.Demo/SharpProof.Demo.csproj",
            "scripts/Test-SharpProofTestPreservation.ps1",
            "scripts/Generate-ConfigurationReference.ps1",
            "docs/configuration-reference.md",
            "SharpProof.Analyzer/InferredContractSuggestionAnalyzer.cs",
            "SharpProof.Analyzer/SharpProofDiagnosticSuppressor.cs",
            "SharpProof.Analyzer/AttributePlacementAnalyzer.cs",
            "SharpProof.Analyzer/TrustedBoundaryReviewAnalyzer.cs",
            "SharpProof.Analyzer/Configuration/DiagnosticBaseline.cs",
            "SharpProof.Analyzer/AnalyzerDiagnosticCatalog.json",
            "SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptions.json",
            "SharpProof.Analyzer/AnalyzerFeatures.cs",
            "SharpProof.Analyzer/RequiresEntryStateBuilder.cs",
            "SharpProof.Symbolic/SharpProofCapabilityFacts.cs",
            "SharpProof.Symbolic/SymbolicInvariantTargetFilter.cs",
            "SharpProof.Symbolic/SymbolicSourceCompilationProfile.cs",
            "SharpProof.Symbolic/SymbolicProgramPointProjector.cs",
            "SharpProof.Testing/SymbolicSourceQueryServiceTestExtensions.cs",
            "SharpProof.Test/SemanticOracleTestSources.cs",
            "SharpProof.Test/DisposableTestSources.cs",
            "SharpProof.Test/MutableObjectTestSources.cs",
            "SharpProof.Test/EqualityTestSources.cs",
            "SharpProof.Test/ConfiguredMemberKeyTestFactory.cs",
            "SharpProof.ToolingTest/SourceMarker.cs",
            "SharpProof.Tooling.Core/SharpProof.Tooling.Core.csproj"
        };
        foreach (var relativePath in removedFiles)
            Assert.That(File.Exists(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))), Is.False,
                $"Removed compatibility file returned: {relativePath}");
        var roots = new[] {
            "SharpProof.Analyzer", "SharpProof.Attributes",
            "SharpProof.Package", "SharpProof.Symbolic", "Tools", "config", "docs"
        };
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".cs", ".csproj", ".json", ".props", ".targets", ".md"
        };
        var forbidden = new[] {
            "SharpProof." + "Effect" + "Summary",
            ".SharpProof." + "Effect" + "Summary.json",
            "Generated" + "Purity",
            "Known" + "Pure",
            "Known" + "Impure",
            "Purity" + "Profile",
            "Purity" + "ProofQuery",
            "Purity" + "ProofSearch",
            "Inferred" + "MethodSummary",
            "Bcl" + "PurityFallbackHeuristics",
            "Purity" + "PolicyImpact",
            "Missing" + "PuritySuggestionScope",
            "Collect" + "AncestorReachabilityState",
            "SHARPPROOF_" + "TRACE_CFG_FALLBACK",
            "Symbolic" + "CapabilityResult",
            "Symbolic" + "QueryScope",
            "Symbolic" + "QueryMetrics",
            "Symbolic" + "ConditionProofSummary",
            "Symbolic" + "MergedPathFacts",
            "Symbolic" + "InvariantInfo",
            "Method" + "PurityAnalyzer",
            "Sp0002" + "Expectation",
            "sharpproof." + "impurity.",
            "Exception" + "FlowQuery",
            "config" + "-reference",
            "generate" + "-configuration-reference",
            "With" + "EffectSummaries",
            "SharpProofSkipGenerated" + "EffectSummaries",
            "Allow" + "Synchronization",
            "Pure" + "External",
            "sharpproof_suggest_inferred_contracts",
            "sharpproof_runtime_hazard_mode",
            "sharpproof_report_exceptions",
            "Diagnostic" + "Baseline",
            "InferredContract" + "SuggestionAnalyzer",
            "TrustedBoundary" + "ReviewAnalyzer",
            "Symbolic" + "SourceInputKind",
            "Symbolic" + "FactInfo",
            "Symbolic" + "ProofBackend",
            "Symbolic" + "ProofStage",
            "Symbolic" + "ProofSupport",
            "Symbolic" + "ComplexityResultProjector",
            "Symbolic" + "ProgramPointProjector",
            "Format" + "MergedInvariant(",
            "Include" + "ExpressionProgramPoints",
            "Implied" + "Conditions"
        };
        var legacyDiagnostic = new System.Text.RegularExpressions.Regex(
            @"SP(?:000[3-9]|001[0-247]|002[0369]|003[1-9]|0040|00(?:4[89]|5[0-9]|6[0-9]|7[0-6]))",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        foreach (var relativeRoot in roots) {
            var path = Path.Combine(root, relativeRoot);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) {
                if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                    file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    !extensions.Contains(Path.GetExtension(file))) continue;
                var text = File.ReadAllText(file);
                foreach (var value in forbidden)
                    Assert.That(text, Does.Not.Contain(value), $"Forbidden legacy surface in {file}: {value}");
                Assert.That(legacyDiagnostic.IsMatch(text), Is.False, $"Disabled diagnostic returned in {file}");
            }
        }
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "SharpProof.Symbolic"), "*.cs", SearchOption.AllDirectories)) {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            var text = File.ReadAllText(file);
            Assert.That(text, Does.Not.Contain("System.Text.Json.Serialization"),
                $"Symbolic JSON presentation annotation returned in {file}");
            Assert.That(text, Does.Not.Contain("JsonPropertyOrder"),
                $"Symbolic JSON property ordering returned in {file}");
            Assert.That(text, Does.Not.Contain("JsonIgnore"),
                $"Symbolic JSON property filtering returned in {file}");
        }
    }
    [Test]
    public void AnalyzerCannotDisableOrBypassZ3ProofService() {
        var root = AnalyzerTestHost.GetRepositoryRoot();
        Assert.That(File.Exists(Path.Combine(root, "SharpProof.ProofCore", "SharpProof.ProofCore.csproj")), Is.True);
        Assert.That(File.Exists(Path.Combine(root, "SharpProof.ProofCore", "Z3FormulaEncoder.cs")), Is.True);
        var configuration = File.ReadAllText(Path.Combine(
            root, "SharpProof.Analyzer", "Configuration", "AnalyzerConfigurationOptionRegistry.cs"));
        Assert.That(configuration, Does.Contain("sharpproof_smt_mode"));
        Assert.That(configuration, Does.Not.Contain("disabled"));
        var session = File.ReadAllText(Path.Combine(root, "SharpProof.Analyzer", "AnalyzerSession.cs"));
        Assert.That(session, Does.Contain("ProofService.SmtAnalysis"));
        Assert.That(session, Does.Contain("MethodEffectAnalysisSession"));
        var api = File.ReadAllText(Path.Combine(root, "SharpProof.Symbolic", "SharpProofAnalysisApi.cs"));
        Assert.That(api, Does.Contain("new SmtAnalysisService"));
        Assert.That(api, Does.Contain("CompileSource"));
        Assert.That(api, Does.Contain("SymbolicSourceInput.FromSyntaxTree"));
        Assert.That(api, Does.Not.Contain("EnableSmt"));
        Assert.That(api, Does.Not.Contain("SharpProofEvidence"));
        Assert.That(api, Does.Not.Contain("SharpProofBudgetMetadata"));
        Assert.That(api, Does.Not.Contain("SharpProofVerdict Purity"));
        Assert.That(api, Does.Not.Contain("SharpProofTargetKind.Node"));
        Assert.That(api, Does.Not.Contain("SharpProofTargetKind.LineSpan"));
        Assert.That(
            Enum.GetNames(typeof(SharpProofTargetKind)),
            Is.EqualTo(["Point", "Position", "Line", "Span", "AllLines"]));
    }
}
