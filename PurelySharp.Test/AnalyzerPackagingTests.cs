using System;
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
                .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityArtifactSourceDirectory", StringComparison.Ordinal))
                .Select(element => element.Value.Trim())
                .LastOrDefault();
            Assert.That(generatedSummaryArtifactSourceDirectory, Is.EqualTo(@"$(MSBuildThisFileDirectory)..\artifacts\effect-summary"));

            var stageTarget = document
                .Descendants()
                .Single(element =>
                    string.Equals(element.Name.LocalName, "Target", StringComparison.Ordinal) &&
                    string.Equals(element.Attribute("Name")?.Value, "StageGeneratedPurityBuiltInSummaries", StringComparison.Ordinal));
            Assert.That(stageTarget.Attribute("BeforeTargets")?.Value, Is.EqualTo("AssignTargetPaths"));

            var alreadyNamedInclude = stageTarget
                .Descendants()
                .Single(element => string.Equals(element.Name.LocalName, "_GeneratedPurityArtifactAlreadyNamed", StringComparison.Ordinal))
                .Attribute("Include")?.Value;
            Assert.That(alreadyNamedInclude, Is.EqualTo(@"$(GeneratedPurityArtifactSourceDirectory)\*.PurelySharp.EffectSummary.json"));

            var needsSuffixItem = stageTarget
                .Descendants()
                .Single(element => string.Equals(element.Name.LocalName, "_GeneratedPurityArtifactNeedsSuffix", StringComparison.Ordinal));
            Assert.That(needsSuffixItem.Attribute("Include")?.Value, Is.EqualTo(@"$(GeneratedPurityArtifactSourceDirectory)\*.json"));
            Assert.That(needsSuffixItem.Attribute("Exclude")?.Value, Is.EqualTo("@(_GeneratedPurityArtifactAlreadyNamed)"));

            var stageCopyWithSuffix = stageTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Copy", StringComparison.Ordinal))
                .Skip(1)
                .Single();
            Assert.That(
                stageCopyWithSuffix.Attribute("DestinationFiles")?.Value,
                Is.EqualTo(@"@(_GeneratedPurityArtifactNeedsSuffix->'$(GeneratedPurityBuiltInSummaryDirectory)\%(Filename).PurelySharp.EffectSummary.json')"));

            var includeTarget = document
                .Descendants()
                .Single(element =>
                    string.Equals(element.Name.LocalName, "Target", StringComparison.Ordinal) &&
                    string.Equals(element.Attribute("Name")?.Value, "IncludeGeneratedPurityBuiltInSummaries", StringComparison.Ordinal));
            Assert.That(includeTarget.Attribute("BeforeTargets")?.Value, Is.EqualTo("AssignTargetPaths"));
            Assert.That(includeTarget.Attribute("DependsOnTargets")?.Value, Is.EqualTo("StageGeneratedPurityBuiltInSummaries"));

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
                .Where(path => !string.Equals(
                    path,
                    Path.Combine(repositoryRoot, "PurelySharp.Analyzer", "PurelySharp.Analyzer.csproj"),
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
                    file.Content.Contains("Update-EffectSummaries.ps1", StringComparison.Ordinal) ||
                    file.Content.Contains("PurelySharp.EffectSummary.json", StringComparison.Ordinal))
                .Select(file => file.Path.Substring(repositoryRoot.Length).TrimStart(Path.DirectorySeparatorChar))
                .ToArray();

            Assert.That(legacyAutomationReferences, Is.Empty,
                "Build and packaging entrypoints should not wire in legacy effect-summary artifact files or refresh scripts.");
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
        public void GeneratedPurityCatalog_CreateBuiltInCatalog_LoadsGeneratedAnalyzerDirectorySummary()
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
    public static string? ReadEnvironmentValue()
    {
        return Environment.GetEnvironmentVariable("PATH");
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

                Assert.That(matched, Is.True, "Built-in generated purity should load summaries emitted beside the analyzer assembly.");
                Assert.That(classification, Is.EqualTo("pure"));
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
        public void ExceptionSummaryCatalog_CreateBuiltInCatalog_LoadsGeneratedAnalyzerDirectorySummary()
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
                    .Single(method => method.Name == "TryGetExceptions" &&
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
                var exceptionTypes = args[2] is ImmutableArray<string> values
                    ? values
                    : ImmutableArray<string>.Empty;

                Assert.That(matched, Is.True, "Built-in generated exception summaries should load summaries emitted beside the analyzer assembly.");
                Assert.That(exceptionTypes.ToArray(), Is.EqualTo(new[] { "System.ArgumentException" }));
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


