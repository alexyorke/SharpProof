using System;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using System.Collections.Immutable;
using System.Xml.Linq;

namespace PurelySharp.Test
{
    [TestFixture]
    public class AnalyzerPackagingTests
    {
        private sealed class SimpleAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
        {
            public void AddDependencyLocation(string fullPath)
            {
            }

            public Assembly LoadFromPath(string fullPath)
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
            }
        }

        [Test]
        public void AnalyzerAssembly_ShouldNotReference_AttributesAssembly()
        {
            var referenced = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer)
                .Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToArray();

            Assert.That(referenced.Any(n => string.Equals(n, "PurelySharp.Attributes", StringComparison.Ordinal)), Is.False,
                "Analyzer assembly must not reference PurelySharp.Attributes to avoid runtime load failures in host environments.");
        }

        [Test]
        public async Task Analyzer_LoadedViaAnalyzerFileReference_RunsWithoutAttributesAssembly()
        {
            var source = @"
using System;
namespace PurelySharp.Attributes { public sealed class EnforcePureAttribute : Attribute {} public sealed class PureAttribute : Attribute {} public sealed class AllowSynchronizationAttribute : Attribute {} }
namespace TestNamespace {
    public class C {
        [PurelySharp.Attributes.EnforcePure]
        public void M() { }
    }
}
";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

            var coreLib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var compilation = CSharpCompilation.Create(
                assemblyName: "AnalyzerPackagingTest",
                syntaxTrees: new[] { syntaxTree },
                references: new[] { coreLib },
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            Assert.That(compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), Is.False,
                "Test compilation should be valid with in-source attribute stubs.");

            var analyzerPath = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly.Location;
            Assert.That(File.Exists(analyzerPath), Is.True, $"Analyzer assembly not found at {analyzerPath}");

            var loader = new SimpleAnalyzerAssemblyLoader();
            var analyzerRef = new AnalyzerFileReference(analyzerPath, loader);
            var analyzers = analyzerRef.GetAnalyzers(LanguageNames.CSharp);
            Assert.That(analyzers.Count, Is.GreaterThan(0), "No analyzers were discovered in the analyzer assembly.");

            var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, new CompilationWithAnalyzersOptions(
                new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false));

            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

            Assert.That(diagnostics, Is.Not.Null);
        }

        [Test]
        public void PackageProject_ShouldUseReleaseReadyNuGetMetadata()
        {
            var projectPath = Path.Combine(FindRepositoryRoot(), "PurelySharp.Package", "PurelySharp.Package.csproj");
            var document = XDocument.Load(projectPath);
            var properties = document
                .Descendants("PropertyGroup")
                .Elements()
                .GroupBy(element => element.Name.LocalName)
                .ToDictionary(group => group.Key, group => group.Last().Value);

            Assert.That(properties["PackageLicenseExpression"], Is.EqualTo("MIT"));
            Assert.That(properties["PackageProjectUrl"], Is.EqualTo("https://github.com/alexyorke/PurelySharp"));
            Assert.That(properties["RepositoryUrl"], Is.EqualTo("https://github.com/alexyorke/PurelySharp"));
            Assert.That(properties["RepositoryType"], Is.EqualTo("git"));
            Assert.That(properties["PackageReadmeFile"], Is.EqualTo("README.md"));
            Assert.That(properties.Values, Has.None.Contains("HERE_OR_DELETE"));
        }

        [Test]
        public void PackageProject_ShouldPackAnalyzerCodeFixAndAttributesInExpectedLocations()
        {
            var projectPath = Path.Combine(FindRepositoryRoot(), "PurelySharp.Package", "PurelySharp.Package.csproj");
            var document = XDocument.Load(projectPath);
            var packageFiles = document
                .Descendants()
                .Where(element =>
                    string.Equals(element.Name.LocalName, "TfmSpecificPackageFile", StringComparison.Ordinal) ||
                    string.Equals(element.Name.LocalName, "None", StringComparison.Ordinal))
                .Select(element => new
                {
                    Include = element.Attribute("Include")?.Value ?? string.Empty,
                    PackagePath = element.Attribute("PackagePath")?.Value ?? string.Empty
                })
                .ToArray();

            Assert.That(packageFiles.Any(file =>
                file.Include.EndsWith("PurelySharp.Analyzer.dll", StringComparison.Ordinal) &&
                file.PackagePath == "analyzers/dotnet/cs"), Is.True,
                "The analyzer assembly must be packed under analyzers/dotnet/cs.");

            Assert.That(packageFiles.Any(file =>
                file.Include.EndsWith("PurelySharp.CodeFixes.dll", StringComparison.Ordinal) &&
                file.PackagePath == "analyzers/dotnet/cs"), Is.True,
                "The code fix assembly must be packed next to the analyzer.");

            Assert.That(packageFiles.Any(file =>
                file.Include.EndsWith("PurelySharp.Attributes.dll", StringComparison.Ordinal) &&
                file.PackagePath == "lib/netstandard2.0"), Is.True,
                "The attributes assembly must be packed as a library reference.");

            Assert.That(packageFiles.Any(file =>
                file.Include.EndsWith("PurelySharp.EffectSummary.json", StringComparison.Ordinal) &&
                file.PackagePath == "analyzers/dotnet/cs"), Is.False,
                "The package should not ship built-in effect-summary JSON artifacts.");

            Assert.That(packageFiles.Any(file =>
                file.Include.EndsWith("*.PurelySharp.EffectSummary.json", StringComparison.Ordinal) &&
                file.PackagePath == "analyzers/dotnet/cs"), Is.False,
                "The package should not ship domain-specific effect-summary JSON artifacts.");

            Assert.That(packageFiles.Any(file =>
                file.Include.EndsWith("buildTransitive\\PurelySharp.targets", StringComparison.Ordinal) &&
                file.PackagePath == "buildTransitive\\PurelySharp.targets"), Is.False,
                "The package should not ship the old buildTransitive summary-target file.");
        }

        [Test]
        public void PackageBuildTransitiveTargets_ShouldNotExist()
        {
            var targetsPath = Path.Combine(FindRepositoryRoot(), "PurelySharp.Package", "buildTransitive", "PurelySharp.targets");
            Assert.That(File.Exists(targetsPath), Is.False,
                "The package should not keep the old buildTransitive summary-target file.");
        }

        [Test]
        public void PackageToolsDirectory_ShouldOnlyContain_InstallAndUninstallScripts()
        {
            var toolsDirectory = Path.Combine(FindRepositoryRoot(), "PurelySharp.Package", "tools");
            var toolScripts = Directory
                .EnumerateFiles(toolsDirectory, "*.ps1", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.That(toolScripts, Is.EqualTo(new[] { "install.ps1", "uninstall.ps1" }),
                "NuGet package tools should stay limited to analyzer install and uninstall helpers.");
        }

        [Test]
        public void AnalyzerProject_ShouldNotCopyOrEmbed_EffectSummaryJsonArtifacts()
        {
            var projectPath = Path.Combine(FindRepositoryRoot(), "PurelySharp.Analyzer", "PurelySharp.Analyzer.csproj");
            var document = XDocument.Load(projectPath);
            var effectSummaryItems = document
                .Descendants()
                .Where(element =>
                    string.Equals(element.Name.LocalName, "None", StringComparison.Ordinal) ||
                    string.Equals(element.Name.LocalName, "EmbeddedResource", StringComparison.Ordinal))
                .Select(element => new
                {
                    ItemName = element.Name.LocalName,
                    Include = element.Attribute("Include")?.Value ?? string.Empty
                })
                .Where(item => item.Include.EndsWith("PurelySharp.EffectSummary.json", StringComparison.Ordinal))
                .ToArray();

            Assert.That(effectSummaryItems, Is.Empty,
                "The analyzer project should not copy or embed built-in effect-summary JSON artifacts.");
        }

        [Test]
        public void AnalyzerProject_ShouldOnlyReference_GeneratedIntermediateEffectSummaryJsonArtifacts()
        {
            var projectPath = Path.Combine(FindRepositoryRoot(), "PurelySharp.Analyzer", "PurelySharp.Analyzer.csproj");
            var document = XDocument.Load(projectPath);

            var generatedSummaryDirectory = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityBuiltInSummaryDirectory", StringComparison.Ordinal))
                .Select(element => element.Value.Trim())
                .LastOrDefault();
            Assert.That(generatedSummaryDirectory, Is.EqualTo(@"$(BaseIntermediateOutputPath)$(Configuration)\$(TargetFramework)\GeneratedPurity"));

            var generatedSummaryArtifactSourceDirectory = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityArtifactSpecSourcePath", StringComparison.Ordinal))
                .Select(element => element.Value.Trim())
                .LastOrDefault();
            Assert.That(generatedSummaryArtifactSourceDirectory, Is.EqualTo(@"$(MSBuildThisFileDirectory)BuiltInEffectSummaryArtifactSpec.json"));

            var generatedSummaryArtifactSpecPath = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityArtifactSpecPath", StringComparison.Ordinal))
                .Select(element => element.Value.Trim())
                .LastOrDefault();
            Assert.That(generatedSummaryArtifactSpecPath, Is.EqualTo(@"$(GeneratedPurityBuiltInSummaryDirectory)\BuiltInEffectSummaryArtifactSpec.json"));

            var stageTarget = document
                .Descendants()
                .Single(element =>
                    string.Equals(element.Name.LocalName, "Target", StringComparison.Ordinal) &&
                    string.Equals(element.Attribute("Name")?.Value, "GenerateBuiltInEffectSummaries", StringComparison.Ordinal));
            Assert.That(stageTarget.Attribute("BeforeTargets")?.Value, Is.EqualTo("AssignTargetPaths"));

            var stageCopies = stageTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Copy", StringComparison.Ordinal))
                .ToArray();
            Assert.That(stageCopies, Has.Length.EqualTo(1));
            Assert.That(stageCopies[0].Attribute("SourceFiles")?.Value, Is.EqualTo("$(GeneratedPurityArtifactSpecSourcePath)"));
            Assert.That(stageCopies[0].Attribute("DestinationFiles")?.Value, Is.EqualTo("$(GeneratedPurityArtifactSpecPath)"));

            var stageRemovals = stageTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "RemoveDir", StringComparison.Ordinal))
                .ToArray();
            Assert.That(stageRemovals, Has.Length.EqualTo(1));
            Assert.That(stageRemovals[0].Attribute("Directories")?.Value, Is.EqualTo("$(GeneratedPurityBuiltInSummaryDirectory)"));
            Assert.That(stageRemovals[0].Attribute("Condition")?.Value, Is.EqualTo("Exists('$(GeneratedPurityBuiltInSummaryDirectory)')"));

            var stageDirectories = stageTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "MakeDir", StringComparison.Ordinal))
                .ToArray();
            Assert.That(stageDirectories, Has.Length.EqualTo(1));
            Assert.That(stageDirectories[0].Attribute("Directories")?.Value, Is.EqualTo("$(GeneratedPurityBuiltInSummaryDirectory)"));

            var stageBuilds = stageTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "MSBuild", StringComparison.Ordinal))
                .ToArray();
            Assert.That(stageBuilds, Has.Length.EqualTo(1));
            Assert.That(stageBuilds[0].Attribute("Projects")?.Value, Is.EqualTo("$(GeneratedPurityToolProjectPath)"));
            Assert.That(stageBuilds[0].Attribute("Targets")?.Value, Is.EqualTo("Build"));

            var stageExecs = stageTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Exec", StringComparison.Ordinal))
                .ToArray();
            Assert.That(stageExecs, Has.Length.EqualTo(1));
            Assert.That(
                stageExecs[0].Attribute("Command")?.Value,
                Is.EqualTo("dotnet \"$(GeneratedPurityToolDllPath)\" --artifact-spec \"$(GeneratedPurityArtifactSpecPath)\""));

            var includeTarget = document
                .Descendants()
                .Single(element =>
                    string.Equals(element.Name.LocalName, "Target", StringComparison.Ordinal) &&
                    string.Equals(element.Attribute("Name")?.Value, "IncludeGeneratedPurityBuiltInSummaries", StringComparison.Ordinal));
            Assert.That(includeTarget.Attribute("BeforeTargets")?.Value, Is.EqualTo("AssignTargetPaths"));
            Assert.That(includeTarget.Attribute("DependsOnTargets")?.Value, Is.EqualTo("GenerateBuiltInEffectSummaries"));

            var generatedSummaryInclude = includeTarget
                .Descendants()
                .Single(element => string.Equals(element.Name.LocalName, "_GeneratedPurityBuiltInSummary", StringComparison.Ordinal))
                .Attribute("Include")?.Value;
            Assert.That(generatedSummaryInclude, Is.EqualTo(@"$(GeneratedPurityBuiltInSummaryDirectory)\*.PurelySharp.EffectSummary.json"));

            var embeddedResourceInclude = includeTarget
                .Descendants()
                .Single(element => string.Equals(element.Name.LocalName, "EmbeddedResource", StringComparison.Ordinal))
                .Attribute("Include")?.Value;
            Assert.That(embeddedResourceInclude, Is.EqualTo("@(_GeneratedPurityBuiltInSummary)"));
            Assert.That(
                includeTarget.Descendants()
                    .Single(element => string.Equals(element.Name.LocalName, "LogicalName", StringComparison.Ordinal))
                    .Value.Trim(),
                Is.EqualTo("PurelySharp.Analyzer.GeneratedPurity.%(Filename)%(Extension)"));
        }

        [Test]
        public void Repository_ShouldNotKeep_CheckedInEffectSummaryJsonArtifacts()
        {
            var repositoryRoot = FindRepositoryRoot();
            var builtInArtifactSpecPath = Path.Combine(repositoryRoot, "PurelySharp.Analyzer", "BuiltInEffectSummaryArtifactSpec.json");
            var analyzerDirectory = Path.Combine(repositoryRoot, "PurelySharp.Analyzer");
            var checkedInSummaryFiles = Directory
                .EnumerateFiles(analyzerDirectory, "*.PurelySharp.EffectSummary.json", SearchOption.TopDirectoryOnly)
                .Concat(new[] { Path.Combine(analyzerDirectory, "PurelySharp.EffectSummary.json") })
                .Where(File.Exists)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.That(checkedInSummaryFiles, Is.Empty,
                "Checked-in effect-summary JSON artifacts should stay out of the repository.");
            Assert.That(File.Exists(builtInArtifactSpecPath), Is.True,
                "The analyzer should keep a checked-in build manifest for regenerating built-in effect summaries.");

            var reviewedSpecPath = Path.Combine(repositoryRoot, "Tools", "PurelySharp.EffectSummary", "ReviewedRuntimeArtifactSpec.json");
            Assert.That(File.Exists(reviewedSpecPath), Is.False,
                "The dormant reviewed effect-summary artifact spec should stay out of the repository.");
        }

        [Test]
        public void BuildAndPackagingEntrypoints_ShouldNotReference_LegacyEffectSummaryAutomation()
        {
            var repositoryRoot = FindRepositoryRoot();
            var entrypointFiles = Directory
                .EnumerateFiles(repositoryRoot, "*.sln", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(repositoryRoot, "*.ps1", SearchOption.TopDirectoryOnly))
                .Concat(Directory.EnumerateFiles(repositoryRoot, "*.props", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(repositoryRoot, "*.targets", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories))
                .Where(path => !string.Equals(
                    path,
                    Path.Combine(repositoryRoot, "PurelySharp.Analyzer", "PurelySharp.Analyzer.csproj"),
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.StartsWith(
                    Path.Combine(repositoryRoot, ".baseline-check") + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var legacyAutomationReferences = entrypointFiles
                .Select(path => new
                {
                    Path = path,
                    Content = File.ReadAllText(path)
                })
                .Where(file =>
                    file.Content.Contains("ReviewedRuntimeArtifactSpec.json", StringComparison.Ordinal) ||
                    file.Content.Contains("PurelySharp.EffectSummary.json", StringComparison.Ordinal))
                .Select(file => file.Path.Substring(repositoryRoot.Length).TrimStart(Path.DirectorySeparatorChar))
                .ToArray();

            Assert.That(legacyAutomationReferences, Is.Empty,
                "Build and packaging entrypoints should not wire in legacy effect-summary artifact files or refresh scripts.");
        }

        [Test]
        public void AnalyzerPackage_ShouldInclude_SymbolicSearchLibAndZ3Dependencies()
        {
            var repositoryRoot = FindRepositoryRoot();
            var packageProjectPath = Path.Combine(repositoryRoot, "PurelySharp.Package", "PurelySharp.Package.csproj");
            var project = XDocument.Load(packageProjectPath);
            var analyzerPackageFiles = project
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "TfmSpecificPackageFile", StringComparison.Ordinal))
                .Where(element => string.Equals(element.Attribute("PackagePath")?.Value, "analyzers/dotnet/cs", StringComparison.Ordinal))
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(Path.GetFileName)
                .ToArray();

            Assert.That(analyzerPackageFiles, Does.Contain("PurelySharp.Symbolic.dll"));
            Assert.That(analyzerPackageFiles, Does.Contain("SearchLib.dll"));
            Assert.That(analyzerPackageFiles, Does.Contain("Microsoft.Z3.dll"));
            Assert.That(analyzerPackageFiles, Does.Contain("libz3.dll"));
        }

        [Test]
        public void BuiltAnalyzerPackage_ShouldShip_SymbolicSearchLibAndZ3Dependencies_WhenPackageExists()
        {
            var repositoryRoot = FindRepositoryRoot();
            var packagePath = Path.Combine(repositoryRoot, "PurelySharp.Package", "bin", "Release", "PurelySharp.0.0.4.nupkg");
            if (!File.Exists(packagePath))
            {
                Assert.Inconclusive("Build the package before verifying package contents.");
            }

            using var archive = ZipFile.OpenRead(packagePath);
            var entryNames = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToArray();

            Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/PurelySharp.Symbolic.dll"));
            Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/SearchLib.dll"));
            Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/Microsoft.Z3.dll"));
            Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/libz3.dll"));
        }

        [Test]
        public void SymbolicCli_ShouldUseSymbolicLibrary_NotAnalyzerProject()
        {
            var repositoryRoot = FindRepositoryRoot();
            var cliProjectPath = Path.Combine(repositoryRoot, "Tools", "PurelySharp.SymbolicCli", "PurelySharp.SymbolicCli.csproj");
            var project = XDocument.Load(cliProjectPath);
            var projectReferences = project
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.That(projectReferences, Does.Contain(@"..\..\PurelySharp.Symbolic\PurelySharp.Symbolic.csproj"));
            Assert.That(projectReferences, Does.Not.Contain(@"..\..\PurelySharp.Analyzer\PurelySharp.Analyzer.csproj"));
        }

        [Test]
        public void SymbolicCli_ShouldExposeSmtBudgetOptions()
        {
            var repositoryRoot = FindRepositoryRoot();
            var cliProgramPath = Path.Combine(repositoryRoot, "Tools", "PurelySharp.SymbolicCli", "Program.cs");
            var source = File.ReadAllText(cliProgramPath);

            Assert.That(source, Does.Contain("--smt-mode <mode>"));
            Assert.That(source, Does.Contain("--smt-timeout-ms <n>"));
            Assert.That(source, Does.Contain("--smt-method-budget-ms <n>"));
            Assert.That(source, Does.Contain("--smt-max-path-conditions <n>"));
            Assert.That(source, Does.Contain("--smt-max-expression-nodes <n>"));
            Assert.That(source, Does.Contain("--position <n>"));
            Assert.That(source, Does.Contain("--all-lines"));
            Assert.That(source, Does.Contain("--node-kind <kind>"));
            Assert.That(source, Does.Contain("--with-facts"));
            Assert.That(source, Does.Contain("--reachability <r>"));
            Assert.That(source, Does.Contain("queryService.QueryFileAllLines"));
            Assert.That(source, Does.Contain("SymbolicFileQueryResult"));
            Assert.That(source, Does.Contain("QueryFileAtPosition"));
            Assert.That(source, Does.Contain("SymbolicSourceQueryFilter"));
            Assert.That(source, Does.Contain("options.CreateSmtOptions()"));
            Assert.That(source, Does.Contain("Merged invariant"));
            Assert.That(source, Does.Contain("Line merged invariant"));
            Assert.That(source, Does.Contain("Invariant merge"));
            Assert.That(source, Does.Contain("Path conditions"));
            Assert.That(source, Does.Contain("Program point summary"));
            Assert.That(source, Does.Contain("Proof outcomes"));
            Assert.That(source, Does.Contain("MaxPerPoint"));
            Assert.That(source, Does.Contain("Observed invariant merge"));
            Assert.That(source, Does.Contain("Observed distinct facts"));
            Assert.That(source, Does.Contain("Reachability summary"));
            Assert.That(source, Does.Contain("Lines with program points"));
            Assert.That(source, Does.Contain("Executed queries"));
            Assert.That(source, Does.Contain("Cache entries"));
            Assert.That(source, Does.Not.Contain("new SmtAnalysisService(SmtAnalysisOptions.Default)"));
        }

        [Test]
        public void GeneratedPurityCatalog_EmptyScope_DoesNotMaskBuiltInFallback()
        {
            var catalogType = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly.GetType(
                "PurelySharp.Analyzer.GeneratedPurityCatalog",
                throwOnError: true)!;
            var emptyCatalog = catalogType.GetField("Empty", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            var useCurrent = catalogType.GetMethod("UseCurrent", BindingFlags.Public | BindingFlags.Static)!;
            var currentCatalog = catalogType.GetField("CurrentCatalog", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var valueProperty = currentCatalog.GetType().GetProperty("Value")!;

            using var scope = (IDisposable)useCurrent.Invoke(null, new[] { emptyCatalog })!;

            Assert.That(valueProperty.GetValue(currentCatalog), Is.Null,
                "An empty scoped catalog should defer to the built-in fallback instead of masking it.");
        }

        [Test]
        public void GeneratedPurityCatalog_CreateBuiltInCatalog_DoesNotLoadLooseAnalyzerDirectorySummary()
        {
            var analyzerAssemblyPath = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly.Location;
            var analyzerAssemblyDirectory = Path.GetDirectoryName(analyzerAssemblyPath);
            Assert.That(string.IsNullOrWhiteSpace(analyzerAssemblyDirectory), Is.False);

            var summaryPath = Path.Combine(
                analyzerAssemblyDirectory!,
                "AnalyzerPackagingTests." + Guid.NewGuid().ToString("N") + ".PurelySharp.EffectSummary.json");
            var summaryJson = GeneratedPurityTestSupport.CreatePuritySummaryJson(
                typeof(System.Environment).Assembly.Location,
                "System.Environment.GetLogicalDrives()",
                "pure",
                "[]");

            try
            {
                File.WriteAllText(summaryPath, summaryJson);

                var catalogType = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly.GetType(
                    "PurelySharp.Analyzer.GeneratedPurityCatalog",
                    throwOnError: true)!;
                var createBuiltInCatalog = catalogType.GetMethod("CreateBuiltInCatalog", BindingFlags.NonPublic | BindingFlags.Static)!;
                var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
                var builtInCatalog = createBuiltInCatalog.Invoke(null, null)!;

                const string source = """
using System;

    public static class TestClass
    {
        public static string[] ReadLogicalDrives()
        {
        return Environment.GetLogicalDrives();
        }
    }
""";

                var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
                var compilation = CSharpCompilation.Create(
                    "GeneratedPurityBuiltInCatalogSmoke",
                    new[] { syntaxTree },
                    GetTrustedPlatformReferences(),
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var invocation = syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
                var methodSymbol = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!;

                var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
                var matched = (bool)tryGetPurity.Invoke(builtInCatalog, args)!;
                var purityEntry = args[2];
                var classification = purityEntry == null
                    ? null
                    : (string?)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry);

                Assert.That(matched, Is.False, "Built-in generated purity should come only from embedded resources, not loose analyzer-directory summaries.");
                Assert.That(classification, Is.Null);
            }
            finally
            {
                if (File.Exists(summaryPath))
                {
                    File.Delete(summaryPath);
                }
            }
        }

        [Test]
        public void ExceptionSummaryCatalog_CreateBuiltInCatalog_DoesNotLoadLooseAnalyzerDirectorySummary()
        {
            var analyzerAssemblyPath = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly.Location;
            var analyzerAssemblyDirectory = Path.GetDirectoryName(analyzerAssemblyPath);
            Assert.That(string.IsNullOrWhiteSpace(analyzerAssemblyDirectory), Is.False);

            var summaryPath = Path.Combine(
                analyzerAssemblyDirectory!,
                "AnalyzerPackagingTests." + Guid.NewGuid().ToString("N") + ".PurelySharp.EffectSummary.json");
            var summaryJson = GeneratedPurityTestSupport.CreatePuritySummaryJson(
                typeof(System.Environment).Assembly.Location,
                "System.Environment.GetEnvironmentVariable(string)",
                "pure",
                "[]")
                .Replace(@"""ThrownExceptionTypes"": [],", @"""ThrownExceptionTypes"": [ ""System.ArgumentException"" ],", StringComparison.Ordinal)
                .Replace(@"""TransitiveThrownExceptionTypes"": [],", @"""TransitiveThrownExceptionTypes"": [ ""System.ArgumentException"" ],", StringComparison.Ordinal);

            try
            {
                File.WriteAllText(summaryPath, summaryJson);

                var catalogType = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly.GetType(
                    "PurelySharp.Analyzer.ExceptionSummaryCatalog",
                    throwOnError: true)!;
                var createBuiltInCatalog = catalogType.GetMethod("CreateBuiltInCatalog", BindingFlags.NonPublic | BindingFlags.Static)!;
                var tryGetExceptions = catalogType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Single(method => method.Name == "TryGetExceptionInfos" &&
                        method.GetParameters().Length == 3 &&
                        method.GetParameters()[1].ParameterType == typeof(Compilation));
                var builtInCatalog = createBuiltInCatalog.Invoke(null, null)!;

                const string source = """
using System;

public static class TestClass
{
    public static string? ReadEnvironmentValue()
    {
        return Environment.GetEnvironmentVariable("PATH");
    }
}
""";

                var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
                var compilation = CSharpCompilation.Create(
                    "ExceptionSummaryBuiltInCatalogSmoke",
                    new[] { syntaxTree },
                    GetTrustedPlatformReferences(),
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var invocation = syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
                var methodSymbol = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!;

                var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
                var matched = (bool)tryGetExceptions.Invoke(builtInCatalog, args)!;

                Assert.That(matched, Is.False, "Built-in generated exception summaries should come only from embedded resources, not loose analyzer-directory summaries.");
            }
            finally
            {
                if (File.Exists(summaryPath))
                {
                    File.Delete(summaryPath);
                }
            }
        }

        [Test]
        public void ExceptionSummaryCatalog_CreateBuiltInCatalog_IgnoresNonMatchingJsonFileNames()
        {
            var analyzerAssemblyPath = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly.Location;
            var analyzerAssemblyDirectory = Path.GetDirectoryName(analyzerAssemblyPath);
            Assert.That(string.IsNullOrWhiteSpace(analyzerAssemblyDirectory), Is.False);

            var (fixtureDirectory, fixtureAssemblyPath) = CreateFixtureAssembly(
                "AnalyzerPackagingIgnoredSummaryFixture",
                """
namespace PackagingFixture;

public static class ThrowingBoundary
{
    public static void Invoke()
    {
    }
}
""");
            var summaryPath = Path.Combine(
                analyzerAssemblyDirectory!,
                "AnalyzerPackagingTests." + Guid.NewGuid().ToString("N") + ".json");
            var summaryJson = GeneratedPurityTestSupport.CreatePuritySummaryJson(
                fixtureAssemblyPath,
                "PackagingFixture.ThrowingBoundary.Invoke()",
                "pure",
                "[]")
                .Replace(@"""ThrownExceptionTypes"": [],", @"""ThrownExceptionTypes"": [ ""System.ArgumentException"" ],", StringComparison.Ordinal)
                .Replace(@"""TransitiveThrownExceptionTypes"": [],", @"""TransitiveThrownExceptionTypes"": [ ""System.ArgumentException"" ],", StringComparison.Ordinal);

            try
            {
                File.WriteAllText(summaryPath, summaryJson);

                var catalogType = typeof(PurelySharp.Analyzer.PurelySharpAnalyzer).Assembly.GetType(
                    "PurelySharp.Analyzer.ExceptionSummaryCatalog",
                    throwOnError: true)!;
                var createBuiltInCatalog = catalogType.GetMethod("CreateBuiltInCatalog", BindingFlags.NonPublic | BindingFlags.Static)!;
                var tryGetExceptions = catalogType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Single(method => method.Name == "TryGetExceptionInfos" &&
                        method.GetParameters().Length == 3 &&
                        method.GetParameters()[1].ParameterType == typeof(Compilation));
                var builtInCatalog = createBuiltInCatalog.Invoke(null, null)!;

                const string source = """
using PackagingFixture;

public static class TestClass
{
    public static void Call()
    {
        ThrowingBoundary.Invoke();
    }
}
""";

                var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
                var compilation = CSharpCompilation.Create(
                    "ExceptionSummaryBuiltInCatalogIgnoredFileName",
                    new[] { syntaxTree },
                    GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(fixtureAssemblyPath)),
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var invocation = syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
                var methodSymbol = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!;

                var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
                var matched = (bool)tryGetExceptions.Invoke(builtInCatalog, args)!;

                Assert.That(matched, Is.False, "Only *.PurelySharp.EffectSummary.json files should be consumed as built-in exception summaries.");
            }
            finally
            {
                if (File.Exists(summaryPath))
                {
                    File.Delete(summaryPath);
                }

                if (Directory.Exists(fixtureDirectory))
                {
                    Directory.Delete(fixtureDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void AttributesPackage_ShouldUseReleaseReadyNuGetMetadata()
        {
            var projectPath = Path.Combine(FindRepositoryRoot(), "PurelySharp.Attributes", "PurelySharp.Attributes.csproj");
            var document = XDocument.Load(projectPath);
            var properties = document
                .Descendants("PropertyGroup")
                .Elements()
                .GroupBy(element => element.Name.LocalName)
                .ToDictionary(group => group.Key, group => group.Last().Value);

            Assert.That(properties["PackageLicenseExpression"], Is.EqualTo("MIT"));
            Assert.That(properties["PackageProjectUrl"], Is.EqualTo("https://github.com/alexyorke/PurelySharp"));
            Assert.That(properties["RepositoryUrl"], Is.EqualTo("https://github.com/alexyorke/PurelySharp"));
            Assert.That(properties["RepositoryType"], Is.EqualTo("git"));
            Assert.That(properties["PackageRequireLicenseAcceptance"], Is.EqualTo("false"));
            Assert.That(properties["PackageReadmeFile"], Is.EqualTo("README.md"));
            Assert.That(properties["Description"], Does.Contain("PureExternal"));
            Assert.That(properties["Description"], Does.Contain("Impure"));
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "PurelySharp.Package")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
        }

        private static (string DirectoryPath, string AssemblyPath) CreateFixtureAssembly(string assemblyName, string source)
        {
            var fixtureDirectory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "analyzer-packaging-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtureDirectory);
            var assemblyPath = Path.Combine(fixtureDirectory, assemblyName + ".dll");

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = File.Create(assemblyPath);
            var emitResult = compilation.Emit(stream);
            if (!emitResult.Success)
            {
                throw new AssertionException(string.Join(
                    Environment.NewLine,
                    emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            }

            return (fixtureDirectory, assemblyPath);
        }

        private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToImmutableArray();
        }
    }
}


