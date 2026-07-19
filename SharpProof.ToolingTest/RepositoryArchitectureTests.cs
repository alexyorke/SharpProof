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
