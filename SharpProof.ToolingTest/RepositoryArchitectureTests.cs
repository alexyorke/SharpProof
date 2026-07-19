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
            "SharpProof.CodeFixes",
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
    public void SymbolicAssembly_UsesOneMethodLikeQueryTarget()
    {
        var assembly = typeof(SharpProofAnalysisSession).Assembly;

        Assert.Multiple(() =>
        {
            Assert.That(
                assembly.GetType("SharpProof.Symbolic.ResolvedMethodLikeTarget", true),
                Is.Not.Null);
            Assert.That(
                assembly.GetType("SharpProof.Symbolic.ResolvedComplexityTarget"),
                Is.Null);
            Assert.That(
                assembly.GetType("SharpProof.Symbolic.SymbolicCapabilityService+ResolvedCapabilityTarget"),
                Is.Null);
        });
    }

    [Test]
    public void AnalyzerAssembly_DoesNotRetainDuplicateExceptionFactProjection()
    {
        var assembly = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly;
        var catalog = assembly.GetType("SharpProof.Analyzer.EffectSummaryCatalog", true)!;

        Assert.Multiple(() =>
        {
            Assert.That(
                assembly.GetType("SharpProof.Analyzer.EffectSummaryCatalog+SummaryExceptionFact"),
                Is.Null);
            Assert.That(catalog.GetMethod(
                "ParseExceptionFacts",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic), Is.Null);
        });
    }

    [Test]
    public void AnalyzerAssembly_UsesCanonicalCoreOperationPurityPolicies()
    {
        var assembly = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly;
        var retiredRuleNames = new[]
        {
            "ArrayElementReferencePurityRule",
            "ArrayCreationPurityRule",
            "CollectionExpressionPurityRule",
            "CoalesceOperationPurityRule",
            "ConditionalAccessPurityRule",
            "EventAssignmentPurityRule",
            "EventReferencePurityRule",
            "InlineArrayAccessPurityRule",
            "IsNullPurityRule",
            "LockStatementPurityRule",
            "ObjectOrCollectionInitializerPurityRule",
            "RecursivePatternPurityRule",
            "SpreadOperationPurityRule",
            "SwitchExpressionPurityRule",
            "SwitchStatementPurityRule",
            "UnaryOperationPurityRule",
            "WithOperationPurityRule"
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                assembly.GetType("SharpProof.Analyzer.Engine.Rules.CoreOperationPurityRules", true),
                Is.Not.Null);
            Assert.That(
                assembly.GetType("SharpProof.Analyzer.Engine.Rules.PurityRuleBase`1"),
                Is.Null);
            foreach (var ruleName in retiredRuleNames)
                Assert.That(
                    assembly.GetType("SharpProof.Analyzer.Engine.Rules." + ruleName),
                    Is.Null,
                    ruleName + " must not regain an independent dispatch shell.");
        });
    }

    [Test]
    public void AnalyzerAssembly_UsesOneMemoizedRecursivePurityPipeline()
    {
        var assembly = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly;
        var service = assembly.GetType("SharpProof.Analyzer.Engine.CompilationPurityService", true)!;

        Assert.Multiple(() =>
        {
            Assert.That(assembly.GetType("SharpProof.Analyzer.Engine.Analysis.CallGraphBuilder"), Is.Null);
            Assert.That(assembly.GetType("SharpProof.Analyzer.Engine.Analysis.WorklistPuritySolver"), Is.Null);
            Assert.That(service.GetField("_callGraph",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic), Is.Null);
            Assert.That(service.GetField("_fixedPoint",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic), Is.Null);
            Assert.That(service.GetProperty("CachedPurityCount",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic), Is.Not.Null);
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
    public void AnalyzerEffectSummaryJson_UsesParserAndCatalogLayoutOwners()
    {
        var assembly = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly;

        Assert.Multiple(() =>
        {
            Assert.That(assembly.GetType("SharpProof.Analyzer.EffectSummaryJsonParser", true), Is.Not.Null);
            Assert.That(assembly.GetType("SharpProof.Analyzer.EffectSummaryJsonDocument"), Is.Null,
                "JSON lifetime and layout traversal must not regain a forwarding document facade.");
            Assert.That(assembly.GetType("SharpProof.Analyzer.EffectSummaryJsonAssembly"), Is.Null,
                "Assembly/method layout traversal belongs to EffectSummaryCatalog.");
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
    public void InternalAnalysisCarriers_UsePrimaryConstructorOwnership()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var expectedDeclarations = new Dictionary<string, string>
        {
            ["SharpProof.Analyzer/EffectSummaryCatalog.cs"] = "struct PurityEntry(",
            ["SharpProof.ProofCore/SmtIntegerInterval.cs"] = "struct SmtIntegerInterval(",
            ["SharpProof.Symbolic/Ir/SymbolicLoweringResult.cs"] = "class SymbolicLoweringResult<T>(",
            ["SharpProof.Symbolic/SymbolicProgramPointProjector.cs"] =
                "class SymbolicProgramPointQueryContext(",
            ["Tools/SharpProof.SymbolicCli/SymbolicCliInputContext.cs"] =
                "class SymbolicCliInputContext("
        };

        Assert.Multiple(() =>
        {
            foreach (var (path, declaration) in expectedDeclarations)
                Assert.That(File.ReadAllText(Path.Combine(root, path)), Does.Contain(declaration), path);
            Assert.That(
                File.ReadAllText(Path.Combine(root, "SharpProof.Analyzer", "EffectSummaryCatalog.cs")),
                Does.Contain("record SummaryExceptionInfo("));
            Assert.That(File.Exists(Path.Combine(
                root, "SharpProof.Analyzer", "ExceptionSummaryCatalog.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(
                root, "SharpProof.Analyzer", "MethodAnalysisRequest.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(
                root, "SharpProof.Symbolic", "SymbolicMethodAnalysisInput.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(
                root, "SharpProof.Symbolic", "SymbolicComplexityService.cs")), Is.False);
            var queryApi = File.ReadAllText(Path.Combine(
                root, "SharpProof.Symbolic", "SymbolicQueryApi.cs"));
            Assert.That(queryApi, Does.Not.Contain("TryQuery("));
            Assert.That(queryApi, Does.Not.Contain("TryProve("));
            Assert.That(queryApi, Does.Not.Contain("TryQueryComplexity"));
            Assert.That(queryApi, Does.Not.Contain("TryQueryCapabilities"));
            Assert.That(File.Exists(Path.Combine(
                root, "SharpProof.Analyzer", "GeneratedPurityCatalog.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(
                root, "SharpProof.Analyzer", "EffectSummaryCatalogEntryMap.cs")), Is.False);
        });
    }

    [Test]
    public void ToolHelpText_UsesEmbeddedByteStableResources()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var expectedHashes = new Dictionary<string, string>
        {
            ["Tools/SharpProof.SymbolicCli/SymbolicCliUsage.txt"] =
                "050F0A8EE439F27EA96C0FB09D2E2475C462331D6826C772127DB7A4B25B1E82",
            ["Tools/SharpProof.Fuzz.Core/FuzzUsage.txt"] =
                "522AD16BD2C36DB9C619D17573A37317673AE8C56ED148F96C899BD7284A60A6",
            ["Tools/SharpProof.EffectSummary/EffectSummaryUsage.txt"] =
                "2386BB3BA8A78C5D654E958E33BED58970777C0AAD2CE9542B9493F1664A5126"
        };

        Assert.Multiple(() =>
        {
            foreach (var (path, expectedHash) in expectedHashes)
            {
                var text = File.ReadAllText(Path.Combine(root, path)).TrimEnd('\r', '\n')
                    .Replace("\r\n", "\n", StringComparison.Ordinal);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(text)));
                Assert.That(hash, Is.EqualTo(expectedHash), path);
            }

            Assert.That(File.ReadAllText(Path.Combine(
                root, "Tools", "SharpProof.SymbolicCli", "SymbolicCliOptions.cs")),
                Does.Not.Contain("Usage = \"\"\""));
            Assert.That(File.ReadAllText(Path.Combine(
                root, "Tools", "SharpProof.Fuzz.Core", "FuzzOptions.cs")),
                Does.Not.Contain("Usage = \"\"\""));
            Assert.That(File.ReadAllText(Path.Combine(
                root, "Tools", "SharpProof.EffectSummary", "EffectSummaryCli.cs")),
                Does.Not.Contain("Console.Error.WriteLine(\"SharpProof.EffectSummary\")"));
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
    public void CodeFixes_DispatchFromOneExportedProviderIntoFamilyPartials()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var codeFixRoot = Path.Combine(root, "SharpProof.CodeFixes");
        var provider = File.ReadAllText(Path.Combine(codeFixRoot, "SharpProofCodeFixProvider.cs"));
        var attributeEdits = File.ReadAllText(Path.Combine(
            codeFixRoot, "SharpProofCodeFixProvider.AttributeEdits.cs"));
        var inferredContracts = File.ReadAllText(Path.Combine(
            codeFixRoot, "SharpProofCodeFixProvider.InferredContracts.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(codeFixRoot, "CodeFixHandlers.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(codeFixRoot, "CodeFixHandlerRegistry.cs")), Is.False);
            Assert.That(provider, Does.Contain("public sealed partial class SharpProofCodeFixProvider"));
            Assert.That(provider, Does.Contain("TryGetSimpleRemoval"));
            Assert.That(provider, Does.Contain("RegisterSynchronizationCodeFix"));
            Assert.That(attributeEdits, Does.Contain("RemoveAttributesMatchingAsync"));
            Assert.That(inferredContracts, Does.Contain("RegisterInferredContractCodeFix"));
            Assert.That(attributeEdits, Does.Contain("Formatter.FormatAsync"));
            Assert.That(attributeEdits + inferredContracts, Does.Not.Contain("FormatMovedLeadingTrivia"));
            Assert.That(attributeEdits + inferredContracts, Does.Not.Contain("FormatMovedTrailingTrivia"));
            Assert.That(attributeEdits + inferredContracts, Does.Not.Contain("CreateExpressionBodiedGetter"));
        });
    }

    [Test]
    public void DiagnosticCatalog_DerivesDescriptorsAndSuppressionIdsFromTheirOwners()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var analyzerRoot = Path.Combine(root, "SharpProof.Analyzer");
        var catalog = File.ReadAllText(Path.Combine(analyzerRoot, "AnalyzerDiagnosticCatalog.cs"));
        var catalogData = File.ReadAllText(Path.Combine(analyzerRoot, "AnalyzerDiagnosticCatalog.json"));
        var configuration = File.ReadAllText(Path.Combine(
            analyzerRoot, "Configuration", "AnalyzerConfiguration.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(catalog, Does.Contain("SharpProof.Analyzer.DiagnosticCatalog.json"));
            Assert.That(catalog, Does.Contain("DescriptorsByField"));
            Assert.That(catalog, Does.Not.Contain("GetFields("));
            Assert.That(catalogData.Split("\"FieldName\"", StringSplitOptions.None), Has.Length.EqualTo(76));
            Assert.That(configuration, Does.Not.Contain("AllSupportedDiagnosticIds"));
            Assert.That(configuration, Does.Contain("SharpProofDiagnosticSuppressor.SupportedDiagnosticIds"));
        });
    }

    [Test]
    public void ConfigurationCatalog_OwnsRuntimeAndDocumentationMetadata()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var analyzerRoot = Path.Combine(root, "SharpProof.Analyzer");
        var catalogPath = Path.Combine(analyzerRoot, "Configuration", "AnalyzerConfigurationOptions.json");
        var registry = File.ReadAllText(Path.Combine(
            analyzerRoot, "Configuration", "AnalyzerConfigurationOptionRegistry.cs"));
        var renderer = File.ReadAllText(Path.Combine(
            root, "Tools", "SharpProof.SymbolicCli", "ConfigurationReferenceCommand.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(catalogPath))),
                Is.EqualTo("725E718C118CFDBE51699D51B15BF1954F472D24A6B29E35E3DEAFA603664E9D"));
            Assert.That(registry, Does.Contain("SharpProof.Analyzer.Configuration.Options.json"));
            Assert.That(registry, Does.Not.Contain("GlobalOption(ConfigKeys."));
            Assert.That(renderer, Does.Not.Contain("GetRelatedDiagnostics("));
            Assert.That(renderer, Does.Not.Contain("GetSampleValue("));
            Assert.That(renderer, Does.Not.Contain("GetValueDescription("));
        });
    }

    [Test]
    public void RetiredProjectionAndPolicyHelpers_DoNotReturn()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var sources = string.Join("\n", new[]
        {
            Path.Combine(root, "SharpProof.Analyzer", "Configuration", "AnalyzerConfigurationOptionRegistry.cs"),
            Path.Combine(root, "SharpProof.Analyzer", "Engine", "PurityKnownBclSemantics.cs"),
            Path.Combine(root, "Tools", "SharpProof.EffectSummary",
                "EffectSummaryClassificationEvidenceRules.cs")
        }.Select(File.ReadAllText));

        Assert.Multiple(() =>
        {
            Assert.That(sources, Does.Not.Contain("ForSmtModes"));
            Assert.That(sources, Does.Not.Contain("IsArrayInterfaceGetEnumeratorInvocation"));
            Assert.That(sources, Does.Not.Contain("GetFreshArrayNote"));
            Assert.That(sources, Does.Not.Contain("AggregateEffectVisibilityClassification"));
        });
    }

    [Test]
    public void ReachabilityCache_DoesNotReintroduceASecondCfgExecutionTrace()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var trace = File.ReadAllText(Path.Combine(
            root, "SharpProof.Symbolic", "Ir", "SymbolicCfgStatementCompletion.cs"));
        var reachability = File.ReadAllText(Path.Combine(
            root, "SharpProof.Symbolic", "SymbolicReachabilityService.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(trace, Does.Not.Contain("CollectExecutionTrace"));
            Assert.That(trace, Does.Not.Contain("CollectStateFromExecutionTrace"));
            Assert.That(trace, Does.Not.Contain("TraceCacheEntry"));
            Assert.That(trace, Does.Not.Contain("RecordObservation"));
            Assert.That(reachability, Does.Not.Contain("TryCollectCachedExecutionTraceState"));
            Assert.That(reachability, Does.Contain("BoundedConcurrentCache<PathStateCacheKey, SymbolicState>"));
        });
    }

    [Test]
    public void ProofMetadata_HasOneResultAndProjectionOwner()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var symbolicRoot = Path.Combine(root, "SharpProof.Symbolic");
        var proofModel = File.ReadAllText(Path.Combine(symbolicRoot, "SymbolicPublicModels.cs"));
        var reachability = File.ReadAllText(Path.Combine(symbolicRoot, "SymbolicReachabilityService.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(symbolicRoot, "SymbolicIrProofResult.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(symbolicRoot, "SymbolicProofProjection.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(symbolicRoot, "SymbolicProofPipeline.cs")), Is.False);
            Assert.That(proofModel, Does.Contain("internal sealed record SymbolicProofInfo"));
            Assert.That(proofModel, Does.Contain("internal PurityProofResult? RawResult"));
            Assert.That(reachability, Does.Not.Contain("ClassifyStateFeasibility"));
            Assert.That(reachability, Does.Not.Contain("ClassifyStateConditionTruth"));
        });
    }

    [Test]
    public void ErrorMetadata_HasOneContractOwner()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var symbolicRoot = Path.Combine(root, "SharpProof.Symbolic");
        var api = File.ReadAllText(Path.Combine(symbolicRoot, "SharpProofAnalysisApi.cs"));
        var errors = File.ReadAllText(Path.Combine(symbolicRoot, "SymbolicErrors.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(api, Does.Contain("public sealed record SharpProofError("));
            Assert.That(api, Does.Contain("public enum SharpProofErrorCategory"));
            Assert.That(api, Does.Not.Contain("ToError("));
            Assert.That(errors, Does.Not.Contain("class SymbolicError("));
            Assert.That(errors, Does.Not.Contain("enum SymbolicErrorCategory"));
        });
    }

    [Test]
    public void FreshMutableOwnership_UsesCanonicalFactsInsteadOfAStatementWalker()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var classifier = File.ReadAllText(Path.Combine(
            root, "SharpProof.Analyzer", "Engine", "Rules", "OwnedFreshMutableObjectClassifier.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(classifier, Does.Contain("HasSymbolicFreshMutableObjectFactForSymbol"));
            Assert.That(classifier, Does.Not.Contain("IsAssignedFreshMutableObjectOnAllPaths"));
            Assert.That(classifier, Does.Not.Contain("AnalyzeFreshMutableAssignments"));
            Assert.That(classifier, Does.Not.Contain("AnalyzeFreshMutableAssignment"));
        });
    }

    [Test]
    public void FuzzShapeRegistry_HasOneDeclarativeOwner()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var fuzzRoot = Path.Combine(root, "Tools", "SharpProof.Fuzz.Core");
        var generator = File.ReadAllText(Path.Combine(fuzzRoot, "FuzzCaseGenerator.cs"));
        using var registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(fuzzRoot, "FuzzShapeRegistry.json")));
        var ids = registry.RootElement.EnumerateArray()
            .Select(entry => entry.GetProperty("Id").GetString())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(generator, Does.Contain("FuzzShapeRegistry.Load"));
            Assert.That(generator, Does.Not.Contain("MethodEntry("));
            Assert.That(generator, Does.Not.Contain("StaticEntry("));
            Assert.That(generator, Does.Not.Contain("ShapeRegistryEntry Entry("));
            Assert.That(ids, Does.Contain("PureArithmetic"));
            Assert.That(ids, Does.Contain("ImpureUsingAwaitDelegateFlow"));
        });
    }

    [Test]
    public void FuzzCoverageManifest_InfersRoslynFamiliesInsteadOfRepeatingTables()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var fuzzRoot = Path.Combine(root, "Tools", "SharpProof.Fuzz.Core");
        var manifest = File.ReadAllText(Path.Combine(fuzzRoot, "RoslynShapeManifest.cs"));
        var models = File.ReadAllText(Path.Combine(fuzzRoot, "FuzzModels.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(manifest, Does.Contain("RuleRegistry.GetDefaultRules"));
            Assert.That(manifest, Does.Contain("FuzzCaseGenerator.RegistryEntries"));
            Assert.That(manifest, Does.Contain("Enum.GetValues<OperationKind>()"));
            Assert.That(manifest, Does.Contain("Enum.GetValues<SyntaxKind>()"));
            Assert.That(manifest, Does.Not.Contain("ParentHandledOperationKinds"));
            Assert.That(manifest, Does.Not.Contain("SyntaxShadowKindNames"));
            Assert.That(manifest, Does.Not.Contain("AnalyzerActionSurfaceDecision"));
            Assert.That(models, Does.Contain("public sealed record FuzzRunSummary("));
        });
    }

    [Test]
    public void EffectSummaryGeneratedPurityRules_HaveOneDeclarativeOwner()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var toolRoot = Path.Combine(root, "Tools", "SharpProof.EffectSummary");
        var source = File.ReadAllText(Path.Combine(toolRoot, "EffectSummaryGeneratedPurityRules.cs"));
        using var registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            toolRoot, "EffectSummaryGeneratedPurityRules.json")));
        var impure = registry.RootElement.GetProperty("Impure");
        var pure = registry.RootElement.GetProperty("Pure");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("ToolEmbeddedText.Load"));
            Assert.That(source, Does.Not.Contain("System.Guid.NewGuid()"));
            Assert.That(source, Does.Not.Contain("System.Diagnostics.StackFrame.GetMethod()"));
            Assert.That(impure.GetArrayLength(), Is.EqualTo(19));
            Assert.That(pure.GetArrayLength(), Is.EqualTo(3));
            Assert.That(impure[0].GetProperty("ExactSymbols")[0].GetString(), Is.EqualTo("System.Guid.NewGuid()"));
            Assert.That(impure[17].GetProperty("Predicate").GetString(),
                Is.EqualTo("IsGeneratedArrayComparerSort"));
            Assert.That(pure[1].GetProperty("Predicate").GetString(),
                Is.EqualTo("IsImmutableHashSetEnumeratorMethod"));
        });
    }

    [Test]
    public void EffectSummaryDocuments_HaveOneTypedSerializationModel()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var toolRoot = Path.Combine(root, "Tools", "SharpProof.EffectSummary");
        var artifactSpec = File.ReadAllText(Path.Combine(toolRoot, "ArtifactSpec.cs"));
        var catalogReader = File.ReadAllText(Path.Combine(toolRoot, "GeneratedPurityCatalogReader.cs"));
        var progressStore = File.ReadAllText(Path.Combine(toolRoot, "EffectSummaryProgressStore.cs"));
        var models = File.ReadAllText(Path.Combine(toolRoot, "EffectSummaryMetadataModels.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(artifactSpec, Does.Contain("Deserialize<EffectSummaryDocument>"));
            Assert.That(catalogReader, Does.Contain("Deserialize<EffectSummaryDocument>"));
            Assert.That(progressStore, Does.Contain("Deserialize<TProgress>"));
            Assert.That(artifactSpec, Does.Not.Contain("JsonDocument.Parse"));
            Assert.That(catalogReader, Does.Not.Contain("JsonDocument.Parse"));
            Assert.That(progressStore, Does.Not.Contain("JsonDocument.Parse"));
            Assert.That(models, Does.Contain("JsonPropertyName(\"DisplayName\")"));
            Assert.That(models, Does.Contain("JsonPropertyName(\"Calls\")"));
        });
    }

    [Test]
    public void SymbolicPatternLowering_HasOneTypedDispatcher()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var symbolicIrRoot = Path.Combine(root, "SharpProof.Symbolic", "Ir");
        var dispatcher = File.ReadAllText(Path.Combine(symbolicIrRoot, "SymbolicIrLowerer.cs"));
        var patterns = File.ReadAllText(Path.Combine(symbolicIrRoot, "SymbolicPatternLowerer.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(dispatcher, Does.Contain("TryLowerPatternCondition("));
            Assert.That(dispatcher, Does.Not.Contain("TryLowerBinaryPatternCondition("));
            Assert.That(dispatcher, Does.Not.Contain("TryLowerNullPatternCondition("));
            Assert.That(dispatcher, Does.Not.Contain("TryLowerConstantPatternCondition("));
            Assert.That(dispatcher, Does.Not.Contain("TryLowerRelationalPatternCondition("));
            Assert.That(dispatcher, Does.Not.Contain("TryLowerEmptyRecursivePatternCondition("));
            Assert.That(dispatcher, Does.Not.Contain("TryLowerTypePatternCondition("));
            Assert.That(patterns, Does.Not.Contain("TryLowerBinaryPatternCondition("));
            Assert.That(patterns, Does.Not.Contain("TryLowerUnaryPatternCondition("));
            Assert.That(patterns.Split("IsPatternExpressionSyntax expression", StringSplitOptions.None),
                Has.Length.EqualTo(2));
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
    public void ProofCore_UsesConcreteFactPreprocessingInsteadOfAParallelProofClassifier()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var proofCore = Path.Combine(root, "SharpProof.ProofCore");
        var service = File.ReadAllText(Path.Combine(
            root, "SharpProof.Symbolic", "Smt", "SmtAnalysisService.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(proofCore, "SmtSyntacticClassifier.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(proofCore, "SmtConditionalFactSimplifier.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(proofCore, "SmtConcreteFactIndex.cs")), Is.True);
            Assert.That(service, Does.Contain("TryClassifyConcreteFacts"));
            Assert.That(service, Does.Not.Contain("TryClassifySyntactically"));
        });
    }

    [Test]
    public void BaselineTool_ReadsOnlyTheCurrentDocumentSchema()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "Tools", "SharpProof.Baseline.Core", "SharpProofBaseline.cs"));
        var analyzerReaderPath = Path.Combine(
            root, "SharpProof.Analyzer", "Configuration", "BaselineJsonReader.cs");
        var contract = File.ReadAllText(Path.Combine(
            root, "SharpProof.Contracts", "BaselineSchemaContract.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Deserialize<BaselineDocument>"));
            Assert.That(source, Does.Contain("BaselineSchemaContract.ValidateTreeOrThrow"));
            Assert.That(source, Does.Not.Contain("LegacyEvidenceSchemaVersion"));
            Assert.That(source, Does.Not.Contain("AddBaselineEntries"));
            Assert.That(source, Does.Not.Contain("TryAddBaselineEntry"));
            Assert.That(File.Exists(analyzerReaderPath), Is.False);
            Assert.That(contract, Does.Contain("TryValidateTree"));
            Assert.That(contract, Does.Contain("ReadEntryFields"));
            Assert.That(contract, Does.Contain("Deduplicate"));
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
            ("SharpProof.Symbolic/Smt/SmtAnalysisLifecycle.cs", "record SmtAnalysisHealth("),
            ("SharpProof.Symbolic/SymbolicAnalysisLimits.cs", "record SymbolicAnalysisTruncationInfo("),
            ("SharpProof.Symbolic/SymbolicCapabilityModels.cs", "record SymbolicCapabilityResult("),
            ("SharpProof.Symbolic/SymbolicComplexityModels.cs", "record SymbolicComplexityResult("),
            ("SharpProof.Symbolic/SymbolicInputWitness.cs", "record SymbolicInputWitness("),
            ("SharpProof.Symbolic/SymbolicInvariantService.cs", "record SymbolicProgramPointAnalysis("),
            ("SharpProof.Symbolic/SymbolicInvariantService.cs", "record SymbolicSmtDiagnostics("),
            ("SharpProof.Symbolic/SymbolicMethodResult.cs", "record SymbolicMethodResult("),
            ("SharpProof.Symbolic/SymbolicProgramPointResult.cs", "record SymbolicInvariantResult("),
            ("SharpProof.Symbolic/SymbolicPublicModels.cs", "record SymbolicInvariantInfo("),
            ("SharpProof.Symbolic/SymbolicQueryFactSummaries.cs", "record SymbolicMergedPathFacts("),
            ("SharpProof.Symbolic/SymbolicQueryTarget.cs", "record SymbolicQueryScope("),
            ("SharpProof.Symbolic/SymbolicUnknownReasonTaxonomy.cs", "record SymbolicUnknownReasonInfo("),
            ("Tools/SharpProof.EffectSummary/EffectSummaryIlAnalysisContext.cs",
                "record EffectSummaryIlAnalysisContext(")
        };

        foreach (var (relativePath, declaration) in carriers)
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(source, Does.Contain(declaration), relativePath);
        }

        var invariantService = File.ReadAllText(Path.Combine(
            root, "SharpProof.Symbolic", "SymbolicInvariantService.cs"));
        Assert.That(invariantService, Does.Not.Contain("record SymbolicSmtDiagnosticsSnapshot("));
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

    [Test]
    public void EffectSummarySchema_OwnsTypedIdentityAndExceptionParsing()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var contracts = File.ReadAllText(Path.Combine(
            root, "SharpProof.Contracts", "EffectSummarySchemaContract.cs"));
        var catalog = File.ReadAllText(Path.Combine(
            root, "SharpProof.Analyzer", "EffectSummaryCatalog.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(contracts, Does.Contain("EffectSummaryContractReader"));
            Assert.That(contracts, Does.Contain("EffectSummaryMethodContract"));
            Assert.That(catalog, Does.Contain("EffectSummaryContractReader.TryReadMethod"));
            Assert.That(catalog, Does.Not.Contain("ReadBooleanProperty"));
            Assert.That(catalog, Does.Not.Contain("EnumerateObjectArrayProperty"));
            Assert.That(File.Exists(Path.Combine(
                root, "SharpProof.Analyzer", "StructuralMethodIdentityJson.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(
                root, "SharpProof.Analyzer", "AnalyzerJsonElementReader.cs")), Is.False);
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
