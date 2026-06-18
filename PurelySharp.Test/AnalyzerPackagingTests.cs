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
                .Descendants("TfmSpecificPackageFile")
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
                file.PackagePath == "analyzers/dotnet/cs"), Is.True,
                "The generated purity artifact must be packed next to the analyzer so runtime-helper proofs are available.");

            Assert.That(packageFiles.Any(file =>
                file.Include.EndsWith("*.PurelySharp.EffectSummary.json", StringComparison.Ordinal) &&
                file.PackagePath == "analyzers/dotnet/cs"), Is.True,
                "All domain-specific generated purity artifacts must be packed next to the analyzer, not just the root summary file.");
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


