using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class RepositoryArchitectureTests
{
    [Test]
    public void ProductionProjects_HaveValidDependencyDirectionAndModuleOwnership()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var script = Path.Combine(root, "scripts", "Get-SharpProofProductionMetrics.ps1");
        var startInfo = TestProcessSupport.CreatePowerShellStartInfo(root);
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-Json");
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.That(process.ExitCode, Is.EqualTo(0), standardError);
        using var document = JsonDocument.Parse(standardOutput);
        var report = document.RootElement;
        Assert.That(report.GetProperty("unassignedFiles").GetArrayLength(), Is.Zero);
        Assert.That(report.GetProperty("ambiguousFiles").GetArrayLength(), Is.Zero);
        Assert.That(report.GetProperty("dependencyViolations").GetArrayLength(), Is.Zero);
    }

    [Test]
    public void ProductionReductionBaseline_IsConsistentAndExcludesTests()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var script = Path.Combine(root, "scripts", "Get-SharpProofProductionReduction.ps1");
        var startInfo = TestProcessSupport.CreatePowerShellStartInfo(root);
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-Json");
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.That(process.ExitCode, Is.EqualTo(0), standardError);
        using var document = JsonDocument.Parse(standardOutput);
        var report = document.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(report.GetProperty("targetReductionLines").GetInt32(), Is.EqualTo(20_000));
            Assert.That(
                report.GetProperty("maximumMaintainedProductionLines").GetInt32(),
                Is.EqualTo(report.GetProperty("baselineLines").GetInt32() - 20_000));
            Assert.That(
                report.GetProperty("current").GetProperty("productionCSharp").GetProperty("files").GetInt32(),
                Is.GreaterThan(0));
            Assert.That(
                report.GetProperty("current").GetProperty("scripts").GetProperty("files").GetInt32(),
                Is.GreaterThan(0));
            Assert.That(
                report.GetProperty("current").GetProperty("specifications").GetProperty("files").GetInt32(),
                Is.GreaterThan(0));
            Assert.That(report.GetProperty("requiredReductionLines").GetInt32(), Is.Zero);
            Assert.That(report.GetProperty("meetsRequiredReduction").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void Projects_DoNotCompileSourceFromAnotherProject()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var violations = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path, root))
            .SelectMany(GetExternalCompileItems)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Production source must be owned by a project and shared through a ProjectReference.");
    }

    [Test]
    public void ProductionProjects_CommonImportsHaveOneProjectOwner()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var expectedProjects = new[]
        {
            "SharpProof.Analyzer",
            "SharpProof.Attributes",
            "SharpProof.Contracts",
            "SharpProof.ProofCore",
            "SharpProof.Symbolic",
            "Tools/SharpProof.CorpusReport.Core",
            "Tools/SharpProof.EffectSummary",
            "Tools/SharpProof.Fuzz.Core",
            "Tools/SharpProof.SymbolicCli"
        };
        var globalUsingsPaths = Directory.EnumerateFiles(root, "GlobalUsings.cs", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path, root))
            .ToArray();
        var actualProjects = globalUsingsPaths
            .Select(path => Path.GetRelativePath(root, Path.GetDirectoryName(path)!).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.That(actualProjects, Is.EqualTo(expectedProjects));

        foreach (var globalUsingsPath in globalUsingsPaths)
        {
            var projectRoot = Path.GetDirectoryName(globalUsingsPath)!;
            var globalDirectives = File.ReadLines(globalUsingsPath)
                .Where(static line => line.StartsWith("global using ", StringComparison.Ordinal))
                .Select(static line => line.Substring("global ".Length))
                .ToArray();
            Assert.That(globalDirectives, Is.Not.Empty, globalUsingsPath);
            foreach (var directive in globalDirectives)
            {
                var duplicate = Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
                    .Where(path => !string.Equals(path, globalUsingsPath, StringComparison.OrdinalIgnoreCase))
                    .Where(path => !IsIgnored(path, root))
                    .FirstOrDefault(path => File.ReadLines(path).Contains(directive));
                Assert.That(duplicate, Is.Null, $"{directive} is duplicated by {duplicate}");
            }
        }
    }

    [Test]
    public void ProductionProjects_DoNotOwnAdapterFiles()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var violations = Directory.EnumerateFiles(root, "*Adapter.cs", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path, root))
            .Where(path => !Path.GetRelativePath(root, path)
                .StartsWith("SharpProof.Test", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Canonical owners must expose their responsibility directly instead of preserving adapter layers.");
    }

    [Test]
    public void Repository_DoesNotTrackGeneratedOrTemporaryArtifacts()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("ls-files");
        using var process = Process.Start(startInfo)!;
        var paths = process.StandardOutput.ReadToEnd()
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.That(process.ExitCode, Is.EqualTo(0), standardError);

        var forbiddenExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".binlog", ".coverage", ".log", ".tmp", ".trx"
        };
        var violations = paths
            .Select(static path => path.Replace('\\', '/'))
            .Where(path =>
                path.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("nupkgs/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/TestResults/", StringComparison.OrdinalIgnoreCase) ||
                forbiddenExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Generated test, package, build-log, and temporary artifacts must remain untracked.");
    }

    [Test]
    public void SymbolicAssembly_DoesNotExportLegacySymbolicDtoTypes()
    {
        var exported = typeof(SharpProofAnalysisSession).Assembly
            .GetExportedTypes()
            .Where(static type => type.Name.StartsWith("Symbolic", StringComparison.Ordinal) ||
                                  type.Namespace == "SharpProof.Symbolic.Smt")
            .Select(static type => type.FullName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(exported, Is.Empty,
            "The supported .NET boundary is SharpProofAnalysisSession/query/result; legacy DTOs and raw SMT types stay internal.");
    }

    [Test]
    public void AnalyzerAssembly_DoesNotRetainDuplicateExceptionFactProjection()
    {
        var assembly = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly;
        var catalog = assembly.GetType("SharpProof.Analyzer.ExceptionSummaryCatalog", true)!;

        Assert.Multiple(() =>
        {
            Assert.That(
                assembly.GetType("SharpProof.Analyzer.ExceptionSummaryCatalog+SummaryExceptionFact"),
                Is.Null);
            Assert.That(catalog.GetMethod(
                "ParseExceptionFacts",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic), Is.Null);
        });
    }

    [Test]
    public void EffectSummary_UsesOneByRefLikeViewInferencePath()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var semanticWrappers = File.ReadAllText(Path.Combine(
            root, "Tools", "SharpProof.EffectSummary", "EffectSummarySemanticWrapperRules.cs"));
        var evidenceRules = File.ReadAllText(Path.Combine(
            root, "Tools", "SharpProof.EffectSummary", "EffectSummaryClassificationEvidenceRules.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(semanticWrappers, Does.Not.Contain("HasPureArrayBackedByRefLikeViewWrapperPattern"));
            Assert.That(semanticWrappers, Does.Not.Contain("HasPureSpanBackedByRefLikeViewWrapperPattern"));
            Assert.That(evidenceRules, Does.Contain("HasByRefLikeViewConstructionPattern"));
        });
    }

    [Test]
    public void SymbolicQueries_UseOneAggregateMetricsOwner()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var summaries = File.ReadAllText(Path.Combine(
            root, "SharpProof.Symbolic", "SymbolicQueryFactSummaries.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(summaries, Does.Contain("SymbolicQueryMetrics"));
            Assert.That(summaries, Does.Contain("SymbolicConditionProofProjection"));
            Assert.That(summaries, Does.Not.Contain("class SymbolicConditionProofSummary"));
            Assert.That(summaries, Does.Not.Contain("SymbolicProgramPointSummary.FromProgramPoints"));
            Assert.That(summaries, Does.Not.Contain("SymbolicReachabilitySummary.FromProgramPoints"));
            Assert.That(summaries, Does.Not.Contain("SymbolicProofOutcomeSummary.FromProofs"));
        });
    }

    [Test]
    public void SymbolicQueryResults_UseCanonicalContextAndPrimaryConstructorOwners()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var symbolicRoot = Path.Combine(root, "SharpProof.Symbolic");
        var programPoints = File.ReadAllText(Path.Combine(symbolicRoot, "SymbolicProgramPointResult.cs"));
        var queryResult = File.ReadAllText(Path.Combine(symbolicRoot, "SymbolicQueryResult.cs"));
        var runtimeHazards = File.ReadAllText(Path.Combine(
            symbolicRoot, "SymbolicRuntimeHazardQueryService.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(
                root, "SharpProof.Analyzer", "SharpProofProjectAnalysisContext.cs")), Is.False);
            Assert.That(programPoints, Does.Contain("class SymbolicProgramPointResult("));
            Assert.That(queryResult, Does.Contain("class SymbolicQueryResult("));
            Assert.That(runtimeHazards, Does.Contain("class SymbolicRuntimeHazardQueryResult("));
            Assert.That(runtimeHazards, Does.Contain("class SymbolicRuntimeHazard("));
            Assert.That(File.Exists(Path.Combine(
                root, "SharpProof.Symbolic", "SymbolicProgramPointAnalyzer.cs")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(
                    root, "SharpProof.Symbolic", "SymbolicInvariantService.cs")),
                Does.Contain("SymbolicProgramPointQueryContext Analyze("));
        });
    }

    [Test]
    public void AnalysisBudgets_UseOneCanonicalModelAndNamedRegistry()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var budgetApi = File.ReadAllText(Path.Combine(
            root, "SharpProof.Symbolic", "SharpProofAnalysisApi.cs"));
        var symbolicSources = string.Join("\n", Directory.EnumerateFiles(
            Path.Combine(root, "SharpProof.Symbolic"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        Assert.Multiple(() =>
        {
            Assert.That(budgetApi, Does.Contain("SharpProofAnalysisBudget FromNamedValues"));
            Assert.That(budgetApi, Does.Contain("bool IsNamedLimit"));
            Assert.That(symbolicSources, Does.Not.Contain("class SymbolicAnalysisLimits"));
            Assert.That(symbolicSources, Does.Not.Contain("new SymbolicAnalysisLimits"));
        });
    }

    [Test]
    public void MetadataImpurity_UsesInferenceInsteadOfBuiltInCatalogTables()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var constants = File.ReadAllText(Path.Combine(root, "SharpProof.Contracts", "Constants.cs"));
        var analyzerEngine = string.Join("\n", Directory.EnumerateFiles(
            Path.Combine(root, "SharpProof.Analyzer", "Engine"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        Assert.Multiple(() =>
        {
            Assert.That(constants, Does.Not.Contain("System.IO"));
            Assert.That(constants, Does.Not.Contain("System.Timers.Timer"));
            Assert.That(constants, Does.Not.Contain("JsonSerializer.Deserialize"));
            Assert.That(analyzerEngine, Does.Not.Contain("PurityCatalogSemantics"));
            Assert.That(analyzerEngine, Does.Contain("TryGetGeneratedMethodPurity"));
        });
    }

    [Test]
    public void CodeFixes_DispatchDirectlyFromTheExportedProvider()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var codeFixRoot = Path.Combine(root, "SharpProof.CodeFixes");
        var provider = File.ReadAllText(Path.Combine(codeFixRoot, "SharpProofCodeFixProvider.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(codeFixRoot, "CodeFixHandlers.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(codeFixRoot, "CodeFixHandlerRegistry.cs")), Is.False);
            Assert.That(provider, Does.Contain("TryGetSimpleRemoval"));
            Assert.That(provider, Does.Contain("RegisterSynchronizationCodeFix"));
            Assert.That(provider, Does.Contain("Formatter.FormatAsync"));
            Assert.That(provider, Does.Not.Contain("FormatMovedLeadingTrivia"));
            Assert.That(provider, Does.Not.Contain("FormatMovedTrailingTrivia"));
            Assert.That(provider, Does.Not.Contain("CreateExpressionBodiedGetter"));
        });
    }

    [Test]
    public void DiagnosticCatalog_DerivesDescriptorsAndSuppressionIdsFromTheirOwners()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var analyzerRoot = Path.Combine(root, "SharpProof.Analyzer");
        var catalog = File.ReadAllText(Path.Combine(analyzerRoot, "AnalyzerDiagnosticCatalog.cs"));
        var configuration = File.ReadAllText(Path.Combine(
            analyzerRoot, "Configuration", "AnalyzerConfiguration.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(catalog, Does.Contain("GetFields(BindingFlags.Public | BindingFlags.Static)"));
            Assert.That(catalog, Does.Not.Contain("AnalyzerDiagnosticDefinition"));
            Assert.That(configuration, Does.Not.Contain("AllSupportedDiagnosticIds"));
            Assert.That(configuration, Does.Contain("SharpProofDiagnosticSuppressor.SupportedDiagnosticIds"));
        });
    }

    [Test]
    public void UnknownReasons_UseOneTaxonomyInsteadOfParallelDomainRegistries()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var taxonomy = File.ReadAllText(Path.Combine(
            root, "SharpProof.Symbolic", "SymbolicUnknownReasonTaxonomy.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(taxonomy, Does.Not.Contain("CapabilityReasonDescriptors"));
            Assert.That(taxonomy, Does.Not.Contain("ComplexityReasonDescriptors"));
            Assert.That(taxonomy, Does.Contain("Describe(SymbolicCapabilityUnknownReason reason)"));
            Assert.That(taxonomy, Does.Contain("Describe(SymbolicComplexityUnknownReason reason)"));
        });
    }

    [Test]
    public void ProofCore_DelegatesGeneralStringShapeConsistencyToSmt()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var preprocessor = File.ReadAllText(Path.Combine(
            root, "SharpProof.ProofCore", "SmtConcreteFactPreprocessor.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(preprocessor, Does.Not.Contain("TryApplyStringShapeFacts"));
            Assert.That(preprocessor, Does.Not.Contain("StringShapeFact"));
            Assert.That(preprocessor, Does.Not.Contain("TryInferStringEqualitiesFromLengthConstrainedPredicates"));
            Assert.That(preprocessor, Does.Contain("InferConcreteStringsForRegexValidation"));
        });
    }

    [Test]
    public void BaselineTool_ReadsOnlyTheCurrentDocumentSchema()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "Tools", "SharpProof.Baseline.Core", "SharpProofBaseline.cs"));
        var analyzerReader = File.ReadAllText(Path.Combine(
            root, "SharpProof.Analyzer", "Configuration", "BaselineJsonReader.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Deserialize<BaselineDocument>"));
            Assert.That(source, Does.Not.Contain("LegacyEvidenceSchemaVersion"));
            Assert.That(source, Does.Not.Contain("AddBaselineEntries"));
            Assert.That(source, Does.Not.Contain("TryAddBaselineEntry"));
            Assert.That(analyzerReader, Does.Not.Contain("VisitJsonTree"));
            Assert.That(analyzerReader, Does.Not.Contain("diagnosticId"));
            Assert.That(analyzerReader, Does.Not.Contain("operation_kind"));
        });
    }

    [Test]
    public void AnalyzerFeatures_ConsumeOneTreeConfigurationSnapshot()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var configuration = File.ReadAllText(Path.Combine(
            root, "SharpProof.Analyzer", "Configuration", "AnalyzerConfiguration.cs"));
        var methodContext = File.ReadAllText(Path.Combine(
            root, "SharpProof.Analyzer", "MethodBodyAnalysisState.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(configuration, Does.Contain("AnalyzerTreeConfiguration GetTreeConfiguration"));
            Assert.That(methodContext, Does.Contain("AnalyzerTreeConfiguration Configuration"));
            Assert.That(methodContext, Does.Not.Contain("AnalyzerOptions Options"));
            Assert.That(configuration, Does.Not.Contain("GetEmitExplanations("));
            Assert.That(configuration, Does.Not.Contain("GetReportExceptions("));
            Assert.That(configuration, Does.Not.Contain("GetCheckedExceptions("));
        });
    }

    [Test]
    public void AnalysisCarriers_UsePrimaryOwnershipInsteadOfCopyConstructors()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var carriers = new[]
        {
            ("SharpProof.Analyzer/Engine/Rules/PurityAnalysisContext.cs", "record PurityAnalysisContext("),
            ("SharpProof.Analyzer/MethodAnalysisSnapshot.cs", "record MethodAnalysisSnapshot("),
            ("SharpProof.Analyzer/EffectSummaryEntryTrustMetadata.cs", "class EffectSummaryEntryTrustMetadata("),
            ("SharpProof.Symbolic/SymbolicSourceInput.cs", "record SymbolicSourceInput("),
            ("SharpProof.Symbolic/Smt/SmtAnalysisLifecycle.cs", "class SmtAnalysisHealth("),
            ("Tools/SharpProof.EffectSummary/EffectSummaryIlAnalysisContext.cs",
                "record EffectSummaryIlAnalysisContext(")
        };

        foreach (var (relativePath, declaration) in carriers)
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(source, Does.Contain(declaration), relativePath);
        }
    }

    [Test]
    public void CommandTools_ShareArgumentCursorAndDispatchOwnership()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var parser = File.ReadAllText(Path.Combine(
            root, "SharpProof.Tooling.Core", "ToolArgumentParser.cs"));
        var optionSources = new[]
        {
            "Tools/SharpProof.SymbolicCli/SymbolicCliOptions.cs",
            "Tools/SharpProof.EffectSummary/CliOptions.cs",
            "Tools/SharpProof.Fuzz.Core/FuzzOptions.cs",
            "Tools/SharpProof.Baseline/Program.cs",
            "Tools/SharpProof.CorpusReport/Program.cs"
        };

        Assert.That(parser, Does.Contain("class ToolArgumentReader"));
        foreach (var relativePath in optionSources)
        {
            var source = File.ReadAllText(Path.Combine(root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(source, Does.Contain("ToolOptionSet<"), relativePath);
            Assert.That(source, Does.Not.Contain("for (var i = 0; i < args.Length"), relativePath);
        }
    }

    [Test]
    public void CapabilityAnalysis_InfersIoFamiliesInsteadOfEnumeratingMembers()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "SharpProof.Symbolic", "SymbolicCapabilityService.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("memberName.StartsWith(\"Read\""));
            Assert.That(source, Does.Contain("IsFileLikeType"));
            Assert.That(source, Does.Not.Contain("FileReadMembers"));
            Assert.That(source, Does.Not.Contain("FileWriteMembers"));
            Assert.That(source, Does.Not.Contain("GenericIoMembers"));
        });
    }

    [Test]
    public void PurityCfg_OwnsImplicitSemanticsWithoutPostTraversalCompatibility()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var recursive = File.ReadAllText(Path.Combine(
            root, "SharpProof.Analyzer", "Engine", "PurityAnalysisEngine.Recursive.cs"));
        var cfgTransfer = File.ReadAllText(Path.Combine(
            root, "SharpProof.Analyzer", "Engine", "PurityAnalysisEngine.CfgTransfer.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(recursive, Does.Not.Contain("AnalyzePostCfgCompatibility"));
            Assert.That(recursive, Does.Not.Contain("TryGetPostCfgInvocationImpurity"));
            Assert.That(cfgTransfer, Does.Contain("CheckCfgImplicitSemantics"));
            Assert.That(cfgTransfer, Does.Contain("ITryOperation"));
            Assert.That(cfgTransfer, Does.Contain("OperationKind.Using"));
            Assert.That(cfgTransfer, Does.Contain("IForEachLoopOperation"));
            Assert.That(cfgTransfer, Does.Contain("ICompoundAssignmentOperation"));
        });
    }

    [Test]
    public void ExceptionFlow_CollectsExplicitCallsFromTheCanonicalOperationTree()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var analyzerRoot = Path.Combine(root, "SharpProof.Analyzer");
        var collector = File.ReadAllText(Path.Combine(analyzerRoot, "ExceptionFlowAnalyzer.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(collector, Does.Contain("ExecutionVisibility.VisibleDescendants(rootOperation)"));
            Assert.That(collector, Does.Contain("case IInvocationOperation"));
            Assert.That(collector, Does.Contain("case IObjectCreationOperation"));
            Assert.That(collector, Does.Contain("case IPropertyReferenceOperation"));
            Assert.That(collector, Does.Contain("case IInterpolatedStringHandlerCreationOperation"));
            Assert.That(collector, Does.Not.Contain("GetInvocationNodes"));
            Assert.That(collector, Does.Not.Contain("GetObjectCreationNodes"));
            Assert.That(collector, Does.Not.Contain("GetOperatorAndConversionNodes"));
            Assert.That(File.Exists(Path.Combine(
                analyzerRoot, "ExceptionFlowAnalyzer.PropertyFlow.cs")), Is.False);
        });
    }

    private static IEnumerable<string> GetExternalCompileItems(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var document = XDocument.Load(projectPath);
        foreach (var element in document.Descendants("Compile"))
        {
            var include = element.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include)) continue;

            var sourcePath = Path.GetFullPath(Path.Combine(projectDirectory, include));
            if (!sourcePath.StartsWith(projectDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                yield return $"{Path.GetFileName(projectPath)} -> {include}";
        }
    }

    private static bool IsIgnored(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith(".", StringComparison.Ordinal) ||
               relative.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase);
    }

}
