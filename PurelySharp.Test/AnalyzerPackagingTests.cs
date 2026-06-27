using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        public void Repository_ShouldNotKeep_CheckedInEffectSummaryJsonArtifacts()
        {
            var repositoryRoot = FindRepositoryRoot();
            var analyzerDirectory = Path.Combine(repositoryRoot, "PurelySharp.Analyzer");
            var checkedInSummaryFiles = Directory
                .EnumerateFiles(analyzerDirectory, "*.PurelySharp.EffectSummary.json", SearchOption.TopDirectoryOnly)
                .Concat(new[] { Path.Combine(analyzerDirectory, "PurelySharp.EffectSummary.json") })
                .Where(File.Exists)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.That(checkedInSummaryFiles, Is.Empty,
                "Checked-in effect-summary JSON artifacts should stay out of the repository.");

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
                .Concat(Directory.EnumerateFiles(repositoryRoot, "*.props", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(repositoryRoot, "*.targets", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories))
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
                    file.Content.Contains("Update-EffectSummaries.ps1", StringComparison.Ordinal) ||
                    file.Content.Contains("PurelySharp.EffectSummary.json", StringComparison.Ordinal))
                .Select(file => file.Path.Substring(repositoryRoot.Length).TrimStart(Path.DirectorySeparatorChar))
                .ToArray();

            Assert.That(legacyAutomationReferences, Is.Empty,
                "Build and packaging entrypoints should not wire in legacy effect-summary artifact files or refresh scripts.");
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
    }
}


