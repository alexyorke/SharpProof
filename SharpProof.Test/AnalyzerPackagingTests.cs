using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using System.Collections.Immutable;
using System.Xml.Linq;

namespace SharpProof.Test
{
    [TestFixture]
    [Parallelizable(ParallelScope.Children)]
    public class AnalyzerPackagingTests
    {
        private static readonly ConcurrentDictionary<string, Lazy<Task<PreparedConsumerTemplate>>> PreparedConsumerTemplates =
            new(StringComparer.Ordinal);

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
            var referenced = typeof(SharpProof.Analyzer.SharpProofAnalyzer)
                .Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToArray();

            Assert.That(referenced.Any(n => string.Equals(n, "SharpProof.Attributes", StringComparison.Ordinal)), Is.False,
                "Analyzer assembly must not reference SharpProof.Attributes to avoid runtime load failures in host environments.");
        }

        [Test]
        public async Task Analyzer_LoadedViaAnalyzerFileReference_RunsWithoutAttributesAssembly()
        {
            var source = @"
using System;
namespace SharpProof.Attributes { public sealed class EnforcePureAttribute : Attribute {} public sealed class PureAttribute : Attribute {} public sealed class AllowSynchronizationAttribute : Attribute {} }
namespace TestNamespace {
    public class C {
        [SharpProof.Attributes.EnforcePure]
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

            var analyzerPath = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly.Location;
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
            var projectPath = Path.Combine(FindRepositoryRoot(), "SharpProof.Package", "SharpProof.Package.csproj");
            var document = XDocument.Load(projectPath);
            var properties = document
                .Descendants("PropertyGroup")
                .Elements()
                .GroupBy(element => element.Name.LocalName)
                .ToDictionary(group => group.Key, group => group.Last().Value);

            Assert.That(properties["PackageLicenseExpression"], Is.EqualTo("MIT"));
            Assert.That(properties["PackageProjectUrl"], Is.EqualTo("https://github.com/alexyorke/SharpProof"));
            Assert.That(properties["RepositoryUrl"], Is.EqualTo("https://github.com/alexyorke/SharpProof"));
            Assert.That(properties["RepositoryType"], Is.EqualTo("git"));
            Assert.That(properties["PackageReadmeFile"], Is.EqualTo("README.md"));
            Assert.That(properties.ContainsKey("DevelopmentDependency"), Is.False,
                "The public SharpProof analyzer package should not be marked as a development-only dependency.");
            Assert.That(properties.Values, Has.None.Contains("HERE_OR_DELETE"));
        }

        [Test]
        public void PackageProject_ShouldPackAnalyzerCodeFixAndAttributesInExpectedLocations()
        {
            var projectPath = Path.Combine(FindRepositoryRoot(), "SharpProof.Package", "SharpProof.Package.csproj");
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
                file.Include.EndsWith("SharpProof.Analyzer.dll", StringComparison.Ordinal) &&
                file.PackagePath == "analyzers/dotnet/cs"), Is.True,
                "The analyzer assembly must be packed under analyzers/dotnet/cs.");

            Assert.That(packageFiles.Any(file =>
                file.Include.EndsWith("SharpProof.CodeFixes.dll", StringComparison.Ordinal) &&
                file.PackagePath == "analyzers/dotnet/cs"), Is.True,
                "The code fix assembly must be packed next to the analyzer.");

            Assert.That(packageFiles.Any(file =>
                file.Include.EndsWith("SharpProof.Attributes.dll", StringComparison.Ordinal) &&
                file.PackagePath == "lib/netstandard2.0"), Is.True,
                "The attributes assembly must be packed as a library reference.");

            Assert.That(packageFiles.Any(file =>
                file.Include.EndsWith("SharpProof.EffectSummary.json", StringComparison.Ordinal) &&
                file.PackagePath == "analyzers/dotnet/cs"), Is.False,
                "The package should not ship built-in effect-summary JSON artifacts.");

            Assert.That(packageFiles.Any(file =>
                file.Include.EndsWith("*.SharpProof.EffectSummary.json", StringComparison.Ordinal) &&
                file.PackagePath == "analyzers/dotnet/cs"), Is.False,
                "The package should not ship domain-specific effect-summary JSON artifacts.");

            Assert.That(packageFiles.Any(file =>
                file.Include.EndsWith("buildTransitive\\SharpProof.targets", StringComparison.Ordinal) &&
                file.PackagePath == "buildTransitive\\SharpProof.targets"), Is.False,
                "The package should not ship the old buildTransitive summary-target file.");
        }

        [Test]
        public void PackageBuildTransitiveTargets_ShouldNotExist()
        {
            var targetsPath = Path.Combine(FindRepositoryRoot(), "SharpProof.Package", "buildTransitive", "SharpProof.targets");
            Assert.That(File.Exists(targetsPath), Is.False,
                "The package should not keep the old buildTransitive summary-target file.");
        }

        [Test]
        public void PackageToolsDirectory_ShouldOnlyContain_InstallAndUninstallScripts()
        {
            var toolsDirectory = Path.Combine(FindRepositoryRoot(), "SharpProof.Package", "tools");
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
            var projectPath = Path.Combine(FindRepositoryRoot(), "SharpProof.Analyzer", "SharpProof.Analyzer.csproj");
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
                .Where(item => item.Include.EndsWith("SharpProof.EffectSummary.json", StringComparison.Ordinal))
                .ToArray();

            Assert.That(effectSummaryItems, Is.Empty,
                "The analyzer project should not copy or embed built-in effect-summary JSON artifacts.");
        }

        [Test]
        public void AnalyzerProject_ShouldOnlyReference_GeneratedIntermediateEffectSummaryJsonArtifacts()
        {
            var projectPath = Path.Combine(FindRepositoryRoot(), "SharpProof.Analyzer", "SharpProof.Analyzer.csproj");
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

            var generatedSummaryStampPath = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityGenerationStampPath", StringComparison.Ordinal))
                .Select(element => element.Value.Trim())
                .LastOrDefault();
            Assert.That(
                generatedSummaryStampPath,
                Is.EqualTo(@"$(GeneratedPurityBuiltInSummaryDirectory)\generation.$(NETCoreSdkVersion).stamp"));

            var generatedSummaryStagingDirectory = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityBuiltInSummaryStagingDirectory", StringComparison.Ordinal))
                .Select(element => element.Value.Trim())
                .LastOrDefault();
            Assert.That(generatedSummaryStagingDirectory, Is.EqualTo("$(GeneratedPurityBuiltInSummaryDirectory).staging"));

            var generatedSummaryStagingSpecPath = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityStagingArtifactSpecPath", StringComparison.Ordinal))
                .Select(element => element.Value.Trim())
                .LastOrDefault();
            Assert.That(
                generatedSummaryStagingSpecPath,
                Is.EqualTo(@"$(GeneratedPurityBuiltInSummaryStagingDirectory)\BuiltInEffectSummaryArtifactSpec.json"));

            var skipGeneratedEffectSummaries = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "SharpProofSkipGeneratedEffectSummaries", StringComparison.Ordinal))
                .Select(element => element.Value.Trim())
                .LastOrDefault();
            Assert.That(skipGeneratedEffectSummaries, Is.EqualTo("false"));

            var buildTarget = document
                .Descendants()
                .Single(element =>
                    string.Equals(element.Name.LocalName, "Target", StringComparison.Ordinal) &&
                    string.Equals(element.Attribute("Name")?.Value, "BuildBuiltInEffectSummaryTool", StringComparison.Ordinal));
            var stageBuilds = buildTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "MSBuild", StringComparison.Ordinal))
                .ToArray();
            Assert.That(stageBuilds, Has.Length.EqualTo(1));
            Assert.That(stageBuilds[0].Attribute("Projects")?.Value, Is.EqualTo("$(GeneratedPurityToolProjectPath)"));
            Assert.That(stageBuilds[0].Attribute("Targets")?.Value, Is.EqualTo("Build"));

            Assert.That(
                buildTarget.Attribute("Condition")?.Value,
                Is.EqualTo("'$(SharpProofSkipGeneratedEffectSummaries)' != 'true'"));

            var stageTarget = document
                .Descendants()
                .Single(element =>
                    string.Equals(element.Name.LocalName, "Target", StringComparison.Ordinal) &&
                    string.Equals(element.Attribute("Name")?.Value, "GenerateBuiltInEffectSummaries", StringComparison.Ordinal));
            Assert.That(stageTarget.Attribute("BeforeTargets")?.Value, Is.EqualTo("AssignTargetPaths"));
            Assert.That(stageTarget.Attribute("DependsOnTargets")?.Value, Is.EqualTo("BuildBuiltInEffectSummaryTool"));
            Assert.That(
                stageTarget.Attribute("Condition")?.Value,
                Is.EqualTo("'$(SharpProofSkipGeneratedEffectSummaries)' != 'true'"));
            Assert.That(
                stageTarget.Attribute("Inputs")?.Value,
                Is.EqualTo("$(GeneratedPurityArtifactSpecSourcePath);$(GeneratedPurityToolDllPath)"));
            Assert.That(
                stageTarget.Attribute("Outputs")?.Value,
                Is.EqualTo("$(GeneratedPurityGenerationStampPath);$(GeneratedPurityArtifactSpecPath);@(_GeneratedPuritySummaryOutputs)"));

            var stageCopies = stageTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Copy", StringComparison.Ordinal))
                .ToArray();
            Assert.That(stageCopies, Has.Length.EqualTo(3));
            Assert.That(stageCopies[0].Attribute("SourceFiles")?.Value, Is.EqualTo("$(GeneratedPurityArtifactSpecSourcePath)"));
            Assert.That(stageCopies[0].Attribute("DestinationFiles")?.Value, Is.EqualTo("$(GeneratedPurityStagingArtifactSpecPath)"));
            Assert.That(stageCopies[1].Attribute("SourceFiles")?.Value, Is.EqualTo("$(GeneratedPurityStagingArtifactSpecPath)"));
            Assert.That(stageCopies[1].Attribute("DestinationFiles")?.Value, Is.EqualTo("$(GeneratedPurityArtifactSpecPath)"));
            Assert.That(stageCopies[2].Attribute("SourceFiles")?.Value, Is.EqualTo("@(_SharpProofStagedSummaryFiles)"));
            Assert.That(stageCopies[2].Attribute("DestinationFolder")?.Value, Is.EqualTo("$(GeneratedPurityBuiltInSummaryDirectory)"));

            var stageRemovals = stageTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "RemoveDir", StringComparison.Ordinal))
                .ToArray();
            Assert.That(stageRemovals, Has.Length.EqualTo(3));
            Assert.That(stageRemovals[0].Attribute("Directories")?.Value, Is.EqualTo("$(GeneratedPurityBuiltInSummaryStagingDirectory)"));
            Assert.That(stageRemovals[0].Attribute("Condition")?.Value, Is.EqualTo("Exists('$(GeneratedPurityBuiltInSummaryStagingDirectory)')"));
            Assert.That(stageRemovals[1].Attribute("Directories")?.Value, Is.EqualTo("$(GeneratedPurityBuiltInSummaryDirectory)"));
            Assert.That(stageRemovals[1].Attribute("Condition")?.Value, Is.EqualTo("Exists('$(GeneratedPurityBuiltInSummaryDirectory)')"));
            Assert.That(stageRemovals[2].Attribute("Directories")?.Value, Is.EqualTo("$(GeneratedPurityBuiltInSummaryStagingDirectory)"));
            Assert.That(stageRemovals[2].Attribute("Condition")?.Value, Is.EqualTo("Exists('$(GeneratedPurityBuiltInSummaryStagingDirectory)')"));

            var stageDirectories = stageTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "MakeDir", StringComparison.Ordinal))
                .ToArray();
            Assert.That(stageDirectories, Has.Length.EqualTo(2));
            Assert.That(stageDirectories[0].Attribute("Directories")?.Value, Is.EqualTo("$(GeneratedPurityBuiltInSummaryStagingDirectory)"));
            Assert.That(stageDirectories[1].Attribute("Directories")?.Value, Is.EqualTo("$(GeneratedPurityBuiltInSummaryDirectory)"));

            var unexpectedStageBuilds = stageTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "MSBuild", StringComparison.Ordinal))
                .ToArray();
            Assert.That(unexpectedStageBuilds, Is.Empty);

            var stageExecs = stageTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Exec", StringComparison.Ordinal))
                .ToArray();
            Assert.That(stageExecs, Has.Length.EqualTo(1));
            Assert.That(
                stageExecs[0].Attribute("Command")?.Value,
                Is.EqualTo("dotnet \"$(GeneratedPurityToolDllPath)\" --artifact-spec \"$(GeneratedPurityStagingArtifactSpecPath)\""));
            Assert.That(stageExecs[0].Attribute("IgnoreExitCode")?.Value, Is.EqualTo("true"));
            Assert.That(stageExecs[0].Attribute("ConsoleToMSBuild")?.Value, Is.EqualTo("true"));
            Assert.That(
                stageExecs[0]
                    .Descendants()
                    .Single(element => string.Equals(element.Name.LocalName, "Output", StringComparison.Ordinal))
                    .Attribute("TaskParameter")?.Value,
                Is.EqualTo("ExitCode"));

            var stageErrors = stageTarget
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Error", StringComparison.Ordinal))
                .ToArray();
            Assert.That(stageErrors, Has.Length.EqualTo(2));
            Assert.That(stageErrors[0].Attribute("Condition")?.Value, Is.EqualTo("'$(_SharpProofEffectSummaryExitCode)' != '0'"));
            Assert.That(stageErrors[0].Attribute("Text")?.Value, Does.Contain("SharpProof effect-summary generation failed"));
            Assert.That(stageErrors[0].Attribute("Text")?.Value, Does.Contain("previous generated summaries were left untouched"));
            Assert.That(stageErrors[1].Attribute("Condition")?.Value, Is.EqualTo("'@(_SharpProofStagedSummaryFiles)' == ''"));
            Assert.That(stageErrors[1].Attribute("Text")?.Value, Does.Contain("produced no summary resources"));

            var stampWrite = stageTarget
                .Descendants()
                .Single(element => string.Equals(element.Name.LocalName, "WriteLinesToFile", StringComparison.Ordinal));
            Assert.That(stampWrite.Attribute("File")?.Value, Is.EqualTo("$(GeneratedPurityGenerationStampPath)"));
            Assert.That(stampWrite.Attribute("Lines")?.Value, Is.EqualTo("$(NETCoreSdkVersion)"));
            Assert.That(stampWrite.Attribute("Overwrite")?.Value, Is.EqualTo("true"));
            Assert.That(stampWrite.Attribute("Condition")?.Value, Is.EqualTo("'$(_SharpProofEffectSummaryExitCode)' == '0'"));

            var includeTarget = document
                .Descendants()
                .Single(element =>
                    string.Equals(element.Name.LocalName, "Target", StringComparison.Ordinal) &&
                    string.Equals(element.Attribute("Name")?.Value, "IncludeGeneratedPurityBuiltInSummaries", StringComparison.Ordinal));
            Assert.That(includeTarget.Attribute("BeforeTargets")?.Value, Is.EqualTo("AssignTargetPaths"));
            Assert.That(includeTarget.Attribute("DependsOnTargets")?.Value, Is.EqualTo("GenerateBuiltInEffectSummaries"));
            Assert.That(
                includeTarget.Attribute("Condition")?.Value,
                Is.EqualTo("'$(SharpProofSkipGeneratedEffectSummaries)' != 'true'"));

            var generatedSummaryInclude = includeTarget
                .Descendants()
                .Single(element => string.Equals(element.Name.LocalName, "_GeneratedPurityBuiltInSummary", StringComparison.Ordinal))
                .Attribute("Include")?.Value;
            Assert.That(generatedSummaryInclude, Is.EqualTo(@"$(GeneratedPurityBuiltInSummaryDirectory)\*.SharpProof.EffectSummary.json"));

            var embeddedResourceInclude = includeTarget
                .Descendants()
                .Single(element => string.Equals(element.Name.LocalName, "EmbeddedResource", StringComparison.Ordinal))
                .Attribute("Include")?.Value;
            Assert.That(embeddedResourceInclude, Is.EqualTo("@(_GeneratedPurityBuiltInSummary)"));
            Assert.That(
                includeTarget.Descendants()
                    .Single(element => string.Equals(element.Name.LocalName, "LogicalName", StringComparison.Ordinal))
                    .Value.Trim(),
                Is.EqualTo("SharpProof.Analyzer.GeneratedPurity.%(Filename)%(Extension)"));
        }

        [Test]
        public void Repository_ShouldNotKeep_CheckedInEffectSummaryJsonArtifacts()
        {
            var repositoryRoot = FindRepositoryRoot();
            var builtInArtifactSpecPath = Path.Combine(repositoryRoot, "SharpProof.Analyzer", "BuiltInEffectSummaryArtifactSpec.json");
            var analyzerDirectory = Path.Combine(repositoryRoot, "SharpProof.Analyzer");
            var checkedInSummaryFiles = Directory
                .EnumerateFiles(analyzerDirectory, "*.SharpProof.EffectSummary.json", SearchOption.TopDirectoryOnly)
                .Concat(new[] { Path.Combine(analyzerDirectory, "SharpProof.EffectSummary.json") })
                .Where(File.Exists)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.That(checkedInSummaryFiles, Is.Empty,
                "Checked-in effect-summary JSON artifacts should stay out of the repository.");
            Assert.That(File.Exists(builtInArtifactSpecPath), Is.True,
                "The analyzer should keep a checked-in build manifest for regenerating built-in effect summaries.");

            var reviewedSpecPath = Path.Combine(repositoryRoot, "Tools", "SharpProof.EffectSummary", "ReviewedRuntimeArtifactSpec.json");
            Assert.That(File.Exists(reviewedSpecPath), Is.False,
                "The dormant reviewed effect-summary artifact spec should stay out of the repository.");
        }

        [Test]
        public void AnalyzerAssembly_ShouldEmbedEveryGeneratedEffectSummaryResource_WhenBuilt()
        {
            var repositoryRoot = FindRepositoryRoot();
            var specificationPath = Path.Combine(
                repositoryRoot,
                "SharpProof.Analyzer",
                "BuiltInEffectSummaryArtifactSpec.json");
            using var specification = JsonDocument.Parse(File.ReadAllText(specificationPath));
            var expectedArtifacts = specification.RootElement
                .GetProperty("Artifacts")
                .EnumerateArray()
                .Select(artifact => artifact.GetProperty("OutputPath").GetString())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            const string resourcePrefix = "SharpProof.Analyzer.GeneratedPurity.";
            var assembly = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly;
            var resources = assembly
                .GetManifestResourceNames()
                .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal) &&
                              name.EndsWith(".SharpProof.EffectSummary.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var actualArtifacts = resources
                .Select(name => name.Substring(resourcePrefix.Length))
                .ToHashSet(StringComparer.Ordinal);

            Assert.That(resources, Has.Length.EqualTo(expectedArtifacts.Count));
            Assert.That(actualArtifacts, Is.EquivalentTo(expectedArtifacts));

            foreach (var resourceName in resources)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                Assert.That(stream, Is.Not.Null, resourceName);
                Assert.That(stream!.Length, Is.GreaterThan(0), resourceName);

                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Object), resourceName);
                Assert.That(root.GetProperty("SchemaVersion").GetInt32(), Is.GreaterThanOrEqualTo(1), resourceName);
                Assert.That(root.GetProperty("Assemblies").GetArrayLength(), Is.GreaterThan(0), resourceName);
            }
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
                    Path.Combine(repositoryRoot, "SharpProof.Analyzer", "SharpProof.Analyzer.csproj"),
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
                    file.Content.Contains("SharpProof.EffectSummary.json", StringComparison.Ordinal))
                .Select(file => file.Path.Substring(repositoryRoot.Length).TrimStart(Path.DirectorySeparatorChar))
                .ToArray();

            Assert.That(legacyAutomationReferences, Is.Empty,
                "Build and packaging entrypoints should not wire in legacy effect-summary artifact files or refresh scripts.");
        }

        [Test]
        public void AnalyzerPackage_ShouldInclude_SymbolicSearchLibAndZ3Dependencies()
        {
            var repositoryRoot = FindRepositoryRoot();
            var packageProjectPath = Path.Combine(repositoryRoot, "SharpProof.Package", "SharpProof.Package.csproj");
            var project = XDocument.Load(packageProjectPath);
            var analyzerPackageFiles = project
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "TfmSpecificPackageFile", StringComparison.Ordinal))
                .Where(element => string.Equals(element.Attribute("PackagePath")?.Value, "analyzers/dotnet/cs", StringComparison.Ordinal))
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(Path.GetFileName)
                .ToArray();

            Assert.That(analyzerPackageFiles, Does.Contain("SharpProof.Symbolic.dll"));
            Assert.That(analyzerPackageFiles, Does.Contain("SearchLib.dll"));
            Assert.That(analyzerPackageFiles, Does.Contain("Microsoft.Z3.dll"));
            Assert.That(analyzerPackageFiles, Does.Contain("libz3.dll"));
        }

        [Test]
        public void BuiltAnalyzerPackage_ShouldShip_SymbolicSearchLibAndZ3Dependencies_WhenPackageExists()
        {
            var repositoryRoot = FindRepositoryRoot();
            var packageVersion = ReadPackageVersion(
                Path.Combine(repositoryRoot, "SharpProof.Package", "SharpProof.Package.csproj"),
                "PackageVersion");
            var packagePath = Path.Combine(repositoryRoot, "SharpProof.Package", "bin", "Release", $"SharpProof.{packageVersion}.nupkg");
            if (!File.Exists(packagePath))
            {
                Assert.Inconclusive("Build the package before verifying package contents.");
            }

            using var archive = ZipFile.OpenRead(packagePath);
            var entryNames = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToArray();

            Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/SharpProof.Symbolic.dll"));
            Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/SearchLib.dll"));
            Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/Microsoft.Z3.dll"));
            Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/libz3.dll"));

            var nuspecEntry = archive.Entries.Single(entry =>
                string.Equals(entry.FullName, "SharpProof.nuspec", StringComparison.Ordinal));
            using var nuspecStream = nuspecEntry.Open();
            var nuspecDocument = XDocument.Load(nuspecStream);
            var developmentDependencyValue = nuspecDocument
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "developmentDependency", StringComparison.Ordinal))
                .Select(element => element.Value.Trim())
                .SingleOrDefault();

            Assert.That(string.IsNullOrWhiteSpace(developmentDependencyValue), Is.True,
                "The public SharpProof analyzer package should not ship developmentDependency metadata.");
        }

        [Test]
        public void BuiltAnalyzerPackage_ShouldContainCurrentAnalyzerAssemblyBytes_WhenPackageExists()
        {
            var repositoryRoot = FindRepositoryRoot();
            var packageVersion = ReadPackageVersion(
                Path.Combine(repositoryRoot, "SharpProof.Package", "SharpProof.Package.csproj"),
                "PackageVersion");
            var packagePath = Path.Combine(
                repositoryRoot,
                "SharpProof.Package",
                "bin",
                "Release",
                $"SharpProof.{packageVersion}.nupkg");
            if (!File.Exists(packagePath))
            {
                Assert.Inconclusive("Build the package before verifying package contents.");
            }

            using var archive = ZipFile.OpenRead(packagePath);
            var analyzerEntry = archive.Entries.Single(entry =>
                string.Equals(
                    entry.FullName.Replace('\\', '/'),
                    "analyzers/dotnet/cs/SharpProof.Analyzer.dll",
                    StringComparison.Ordinal));
            using var analyzerStream = analyzerEntry.Open();
            var packagedHash = SHA256.HashData(analyzerStream);
            var builtHash = SHA256.HashData(File.ReadAllBytes(
                typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly.Location));

            Assert.That(packagedHash, Is.EqualTo(builtHash));
        }

        public static IEnumerable<ConsumerPackageScenario> BuiltAnalyzerPackageScenarios()
        {
            yield return new ConsumerPackageScenario(
                "sp0002-purity-not-verified",
                """
using System;
using SharpProof.Attributes;

namespace Probe;

public sealed class Demo
{
    [EnforcePure]
    public int ReadClock() => DateTime.Now.Second;
}
""",
                ExpectedDiagnosticIds: new[] { "SP0002" },
                UsePreparedTemplate: false);
            yield return new ConsumerPackageScenario(
                "sp0004-missing-enforce-pure",
                """
using SharpProof.Attributes;

namespace Probe;

public sealed class Demo
{
    public int AddOne(int value) => value + 1;
}
""",
                ExpectedDiagnosticIds: new[] { "SP0004" },
                UsePreparedTemplate: true);
            yield return new ConsumerPackageScenario(
                "sp0013-zero-allocations",
                """
using SharpProof.Attributes;

namespace Probe;

public sealed class Demo
{
    [Impure]
    [ZeroAllocations]
    public object Allocate() => new object();
}
""",
                ExpectedDiagnosticIds: new[] { "SP0013" },
                UsePreparedTemplate: true);
            yield return new ConsumerPackageScenario(
                "sp0015-capability-violation",
                """
using System;
using SharpProof.Attributes;

namespace Probe;

public sealed class Demo
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public void WriteConsole() => Console.WriteLine("hello");
}
""",
                ExpectedDiagnosticIds: new[] { "SP0015" },
                UsePreparedTemplate: true);
            yield return new ConsumerPackageScenario(
                "sp0016-capability-unknown",
                """
using SharpProof.Attributes;

namespace Probe;

public sealed class Demo
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public string Describe(dynamic value) => value.ToString();
}
""",
                ExpectedDiagnosticIds: new[] { "SP0016" },
                UsePreparedTemplate: true);
        }

        [TestCaseSource(nameof(BuiltAnalyzerPackageScenarios))]
        public async Task BuiltAnalyzerPackage_WhenConsumedByDisposableProject_ReportsCurrentBetaDiagnostics_WhenPackageExists(
            ConsumerPackageScenario scenario)
        {
            var repositoryRoot = FindRepositoryRoot();
            var packageVersion = ReadPackageVersion(
                Path.Combine(repositoryRoot, "SharpProof.Package", "SharpProof.Package.csproj"),
                "PackageVersion");
            var packagePath = ResolveExistingPackageArtifact(
                repositoryRoot,
                "SharpProof.Package",
                $"SharpProof.{packageVersion}.nupkg");
            if (packagePath == null)
            {
                Assert.Inconclusive("Build the package before verifying external package consumption.");
            }
            var packageSource = Path.GetDirectoryName(packagePath)!;
            var buildResult = scenario.UsePreparedTemplate
                ? await BuildPreparedPackageConsumerAsync(
                    await GetPreparedConsumerTemplateAsync(
                        packageId: "SharpProof",
                        packageVersion,
                        packageSource).ConfigureAwait(false),
                    source: scenario.Source,
                    editorConfigText: CreateAnalyzerSeverityEditorConfig(scenario.ExpectedDiagnosticIds)).ConfigureAwait(false)
                : await BuildDisposablePackageConsumerAsync(
                    packageId: "SharpProof",
                    packageVersion,
                    packageSource,
                    source: scenario.Source,
                    editorConfigText: CreateAnalyzerSeverityEditorConfig(scenario.ExpectedDiagnosticIds)).ConfigureAwait(false);

            Assert.That(
                buildResult.ExitCode,
                Is.Not.EqualTo(0),
                $"The packaged analyzer scenario '{scenario.Name}' should fail when promoted diagnostics are emitted.{Environment.NewLine}{buildResult.Output}");

            foreach (var diagnosticId in scenario.ExpectedDiagnosticIds)
            {
                Assert.That(
                    buildResult.Output,
                    Does.Contain(diagnosticId),
                    $"The packaged SharpProof analyzer scenario '{scenario.Name}' should emit {diagnosticId}.{Environment.NewLine}{buildResult.Output}");
            }
        }

        [Test]
        public async Task BuiltAttributesPackage_WhenConsumedByDisposableProject_AllowsAttributeCompileWithoutAnalyzerAssets_WhenPackageExists()
        {
            var repositoryRoot = FindRepositoryRoot();
            var packageVersion = ReadPackageVersion(
                Path.Combine(repositoryRoot, "SharpProof.Attributes", "SharpProof.Attributes.csproj"),
                "Version");
            var packagePath = ResolveExistingPackageArtifact(
                repositoryRoot,
                "SharpProof.Attributes",
                $"SharpProof.Attributes.{packageVersion}.nupkg");
            if (packagePath == null)
            {
                Assert.Inconclusive("Build the attributes package before verifying external package consumption.");
            }
            var packageSource = Path.GetDirectoryName(packagePath)!;

            var buildResult = await BuildDisposablePackageConsumerAsync(
                packageId: "SharpProof.Attributes",
                packageVersion,
                packageSource,
                source:
                """
using SharpProof.Attributes;

namespace Probe;

public sealed class Demo
{
    [EnforcePure]
    [ZeroAllocations]
    [AllowedCapabilities(SharpProofCapability.None)]
    public int Identity(int value) => value;
}
""").ConfigureAwait(false);

            Assert.That(buildResult.ExitCode, Is.EqualTo(0), buildResult.Output);
            Assert.That(buildResult.Output, Does.Not.Contain("SP000"),
                "Installing SharpProof.Attributes alone should not require analyzer assets or emit analyzer diagnostics.");
        }

        [Test]
        public void SymbolicCli_ShouldUseSymbolicLibrary_NotAnalyzerProject()
        {
            var repositoryRoot = FindRepositoryRoot();
            var cliProjectPath = Path.Combine(repositoryRoot, "Tools", "SharpProof.SymbolicCli", "SharpProof.SymbolicCli.csproj");
            var project = XDocument.Load(cliProjectPath);
            var projectReferences = project
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.That(projectReferences, Does.Contain(@"..\..\SharpProof.Symbolic\SharpProof.Symbolic.csproj"));
            Assert.That(projectReferences, Does.Not.Contain(@"..\..\SharpProof.Analyzer\SharpProof.Analyzer.csproj"));
        }

        [Test]
        public void SymbolicCli_ShouldExposeSmtBudgetOptions()
        {
            var repositoryRoot = FindRepositoryRoot();
            var cliProgramPath = Path.Combine(repositoryRoot, "Tools", "SharpProof.SymbolicCli", "Program.cs");
            var source = File.ReadAllText(cliProgramPath);

            Assert.That(source, Does.Contain("--smt-mode <mode>"));
            Assert.That(source, Does.Contain("--smt-timeout-ms <n>"));
            Assert.That(source, Does.Contain("--smt-method-budget-ms <n>"));
            Assert.That(source, Does.Contain("--smt-max-path-conditions <n>"));
            Assert.That(source, Does.Contain("--smt-max-expression-nodes <n>"));
            Assert.That(source, Does.Contain("--position <n>"));
            Assert.That(source, Does.Contain("--all-lines"));
            Assert.That(source, Does.Contain("--line-expressions"));
            Assert.That(source, Does.Contain("--node-kind <kind>"));
            Assert.That(source, Does.Contain("--program-point-kind <kind>"));
            Assert.That(source, Does.Contain("--filter-line <n>"));
            Assert.That(source, Does.Contain("--line-start <n>"));
            Assert.That(source, Does.Contain("--line-end <n>"));
            Assert.That(source, Does.Contain("--with-facts"));
            Assert.That(source, Does.Contain("--method-contains <text>"));
            Assert.That(source, Does.Contain("--reachability <r>"));
            Assert.That(source, Does.Contain("--with-proofs"));
            Assert.That(source, Does.Contain("--proof-outcome <v>"));
            Assert.That(source, Does.Contain("--proof-condition <expr>"));
            Assert.That(source, Does.Contain("--proof-condition-contains <text>"));
            Assert.That(source, Does.Contain("--complexity"));
            Assert.That(source, Does.Contain("--capabilities"));
            Assert.That(source, Does.Contain("explain"));
            Assert.That(source, Does.Contain("new SymbolicQueryService()"));
            Assert.That(source, Does.Contain("new SymbolicQueryRequest("));
            Assert.That(source, Does.Contain("new SymbolicRuntimeHazardRequest("));
            Assert.That(source, Does.Contain("new SymbolicComplexityRequest("));
            Assert.That(source, Does.Contain("new SymbolicCapabilityRequest("));
            Assert.That(source, Does.Contain("SymbolicSourceInput.FromFile(options.FilePath)"));
            Assert.That(source, Does.Contain("options.CreateQueryTarget()"));
            Assert.That(source, Does.Contain("options.CreateRuntimeHazardTarget()"));
            Assert.That(source, Does.Contain("options.CreateComplexityTarget()"));
            Assert.That(source, Does.Contain("options.CreateCapabilityTarget()"));
            Assert.That(source, Does.Contain("options.CreateQueryOptions(smtAnalysis, includeResultFilter: true)"));
            Assert.That(source, Does.Contain("SymbolicFileQueryResult"));
            Assert.That(source, Does.Contain("SymbolicComplexityResult"));
            Assert.That(source, Does.Contain("SymbolicCapabilityResult"));
            Assert.That(source, Does.Contain("SymbolicSourceQueryFilter"));
            Assert.That(source, Does.Contain("options.CreateSmtOptions()"));
            Assert.That(source, Does.Contain("Complexity:"));
            Assert.That(source, Does.Contain("Capabilities:"));
            Assert.That(source, Does.Contain("SharpProof explanation"));
            Assert.That(source, Does.Contain("Merged invariant"));
            Assert.That(source, Does.Contain("Line merged invariant"));
            Assert.That(source, Does.Contain("Invariant merge"));
            Assert.That(source, Does.Contain("Path conditions"));
            Assert.That(source, Does.Contain("Program point kind"));
            Assert.That(source, Does.Contain("Conservative unknown conditions"));
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
            Assert.That(source, Does.Not.Contain("queryService.QueryFileAllLines"));
            Assert.That(source, Does.Not.Contain("QueryFileAtPosition"));
            Assert.That(source, Does.Not.Contain("new SymbolicFileQuery("));
        }

        [Test]
        public void GeneratedPurityCatalog_EmptyScope_DoesNotMaskBuiltInFallback()
        {
            var catalogType = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly.GetType(
                "SharpProof.Analyzer.GeneratedPurityCatalog",
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
            var analyzerAssemblyPath = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly.Location;
            var analyzerAssemblyDirectory = Path.GetDirectoryName(analyzerAssemblyPath);
            Assert.That(string.IsNullOrWhiteSpace(analyzerAssemblyDirectory), Is.False);

            var summaryPath = Path.Combine(
                analyzerAssemblyDirectory!,
                "AnalyzerPackagingTests." + Guid.NewGuid().ToString("N") + ".SharpProof.EffectSummary.json");
            var summaryJson = GeneratedPurityTestSupport.CreatePuritySummaryJson(
                typeof(System.Environment).Assembly.Location,
                "System.Environment.GetLogicalDrives()",
                "pure",
                "[]");

            try
            {
                File.WriteAllText(summaryPath, summaryJson);

                var catalogType = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly.GetType(
                    "SharpProof.Analyzer.GeneratedPurityCatalog",
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
            var analyzerAssemblyPath = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly.Location;
            var analyzerAssemblyDirectory = Path.GetDirectoryName(analyzerAssemblyPath);
            Assert.That(string.IsNullOrWhiteSpace(analyzerAssemblyDirectory), Is.False);

            var summaryPath = Path.Combine(
                analyzerAssemblyDirectory!,
                "AnalyzerPackagingTests." + Guid.NewGuid().ToString("N") + ".SharpProof.EffectSummary.json");
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

                var catalogType = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly.GetType(
                    "SharpProof.Analyzer.ExceptionSummaryCatalog",
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
            var analyzerAssemblyPath = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly.Location;
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

                var catalogType = typeof(SharpProof.Analyzer.SharpProofAnalyzer).Assembly.GetType(
                    "SharpProof.Analyzer.ExceptionSummaryCatalog",
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

                Assert.That(matched, Is.False, "Only *.SharpProof.EffectSummary.json files should be consumed as built-in exception summaries.");
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
            var projectPath = Path.Combine(FindRepositoryRoot(), "SharpProof.Attributes", "SharpProof.Attributes.csproj");
            var document = XDocument.Load(projectPath);
            var properties = document
                .Descendants("PropertyGroup")
                .Elements()
                .GroupBy(element => element.Name.LocalName)
                .ToDictionary(group => group.Key, group => group.Last().Value);

            Assert.That(properties["PackageLicenseExpression"], Is.EqualTo("MIT"));
            Assert.That(properties["PackageProjectUrl"], Is.EqualTo("https://github.com/alexyorke/SharpProof"));
            Assert.That(properties["RepositoryUrl"], Is.EqualTo("https://github.com/alexyorke/SharpProof"));
            Assert.That(properties["RepositoryType"], Is.EqualTo("git"));
            Assert.That(properties["PackageRequireLicenseAcceptance"], Is.EqualTo("false"));
            Assert.That(properties["PackageReadmeFile"], Is.EqualTo("README.md"));
            Assert.That(properties["Description"], Does.Contain("PureExternal"));
            Assert.That(properties["Description"], Does.Contain("Impure"));
        }

        [Test]
        public void CiWorkflow_ShouldRun_AllTestLanes_AndPackBothNuGetPackages()
        {
            var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml");
            var source = File.ReadAllText(workflowPath);

            Assert.That(source, Does.Contain("Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -TestLane All"));
            Assert.That(source, Does.Contain("dotnet pack SharpProof.Package/SharpProof.Package.csproj --configuration Release --no-build --output nupkgs"));
            Assert.That(source, Does.Contain("dotnet pack SharpProof.Attributes/SharpProof.Attributes.csproj --configuration Release --no-build --output nupkgs"));
            Assert.That(source, Does.Contain("SharpProof.Attributes package"));
        }

        [OneTimeTearDown]
        public void CleanupPreparedConsumerTemplates()
        {
            foreach (var templateFactory in PreparedConsumerTemplates.Values)
            {
                if (!templateFactory.IsValueCreated ||
                    !templateFactory.Value.IsCompletedSuccessfully)
                {
                    continue;
                }

                var template = templateFactory.Value.GetAwaiter().GetResult();
                if (Directory.Exists(template.RootDirectory))
                {
                    Directory.Delete(template.RootDirectory, recursive: true);
                }
            }
        }

        private static string ReadPackageVersion(string projectPath, string elementName)
        {
            var document = XDocument.Load(projectPath);
            var value = document
                .Descendants(elementName)
                .Select(static element => element.Value.Trim())
                .LastOrDefault();
            Assert.That(string.IsNullOrWhiteSpace(value), Is.False,
                $"Expected {elementName} in project file '{projectPath}'.");
            return value!;
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "SharpProof.Package")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
        }

        private static string? ResolveExistingPackageArtifact(string repositoryRoot, string projectDirectoryName, string packageFileName)
        {
            var preferredPackagePath = Path.Combine(repositoryRoot, "nupkgs", packageFileName);
            if (File.Exists(preferredPackagePath))
            {
                return preferredPackagePath;
            }

            var projectBinDirectory = Path.Combine(repositoryRoot, projectDirectoryName, "bin");
            if (!Directory.Exists(projectBinDirectory))
            {
                return null;
            }

            return Directory.EnumerateFiles(projectBinDirectory, packageFileName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static async Task<ProcessResult> RunDotnetAsync(
            string workingDirectory,
            string packageCacheDirectory,
            params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.Environment["NUGET_PACKAGES"] = packageCacheDirectory;

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120)).ConfigureAwait(false);
            var output = new StringBuilder()
                .AppendLine(await standardOutput.ConfigureAwait(false))
                .AppendLine(await standardError.ConfigureAwait(false))
                .ToString();
            return new ProcessResult(process.ExitCode, output);
        }

        private static async Task<ProcessResult> BuildDisposablePackageConsumerAsync(
            string packageId,
            string packageVersion,
            string packageSource,
            string source,
            string? editorConfigText = null)
        {
            var probeRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "analyzer-package-consumer-" + Guid.NewGuid().ToString("N"));
            var packageCache = Path.Combine(probeRoot, ".nuget");

            Directory.CreateDirectory(probeRoot);
            try
            {
                var newResult = await RunDotnetAsync(
                    probeRoot,
                    packageCache,
                    "new",
                    "classlib",
                    "--framework",
                    "net8.0",
                    "--name",
                    "Probe").ConfigureAwait(false);
                Assert.That(newResult.ExitCode, Is.EqualTo(0), newResult.Output);

                var projectDirectory = Path.Combine(probeRoot, "Probe");
                var addPackageResult = await RunDotnetAsync(
                    projectDirectory,
                    packageCache,
                    "add",
                    "package",
                    packageId,
                    "--version",
                    packageVersion,
                    "--source",
                    packageSource).ConfigureAwait(false);
                Assert.That(addPackageResult.ExitCode, Is.EqualTo(0), addPackageResult.Output);

                File.WriteAllText(Path.Combine(projectDirectory, "Class1.cs"), source);
                if (!string.IsNullOrWhiteSpace(editorConfigText))
                {
                    File.WriteAllText(Path.Combine(projectDirectory, ".editorconfig"), editorConfigText);
                }

                return await RunDotnetAsync(
                    projectDirectory,
                    packageCache,
                    "build",
                    "--no-restore",
                    "/clp:ErrorsOnly;Summary").ConfigureAwait(false);
            }
            finally
            {
                if (Directory.Exists(probeRoot))
                {
                    Directory.Delete(probeRoot, recursive: true);
                }
            }
        }

        private static Task<PreparedConsumerTemplate> GetPreparedConsumerTemplateAsync(
            string packageId,
            string packageVersion,
            string packageSource)
        {
            var templateKey = string.Join("|", packageId, packageVersion, packageSource);
            var templateFactory = PreparedConsumerTemplates.GetOrAdd(
                templateKey,
                _ => new Lazy<Task<PreparedConsumerTemplate>>(
                    () => CreatePreparedConsumerTemplateAsync(
                        packageId,
                        packageVersion,
                        packageSource)));

            return templateFactory.Value;
        }

        private static async Task<PreparedConsumerTemplate> CreatePreparedConsumerTemplateAsync(
            string packageId,
            string packageVersion,
            string packageSource)
        {
            var probeRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "analyzer-package-template-" + packageId + "-" + Guid.NewGuid().ToString("N"));
            var packageCache = Path.Combine(probeRoot, ".nuget");

            Directory.CreateDirectory(probeRoot);

            var newResult = await RunDotnetAsync(
                probeRoot,
                packageCache,
                "new",
                "classlib",
                "--framework",
                "net8.0",
                "--name",
                "Probe").ConfigureAwait(false);
            Assert.That(newResult.ExitCode, Is.EqualTo(0), newResult.Output);

            var projectDirectory = Path.Combine(probeRoot, "Probe");
            var addPackageResult = await RunDotnetAsync(
                projectDirectory,
                packageCache,
                "add",
                "package",
                packageId,
                "--version",
                packageVersion,
                "--source",
                packageSource).ConfigureAwait(false);
            Assert.That(addPackageResult.ExitCode, Is.EqualTo(0), addPackageResult.Output);

            return new PreparedConsumerTemplate(probeRoot, projectDirectory, packageCache);
        }

        private static async Task<ProcessResult> BuildPreparedPackageConsumerAsync(
            PreparedConsumerTemplate template,
            string source,
            string? editorConfigText = null)
        {
            var probeRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "analyzer-package-scenario-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(probeRoot);
            try
            {
                var projectDirectory = Path.Combine(probeRoot, "Probe");
                CopyDirectory(template.ProjectDirectory, projectDirectory);

                File.WriteAllText(Path.Combine(projectDirectory, "Class1.cs"), source);
                var editorConfigPath = Path.Combine(projectDirectory, ".editorconfig");
                if (string.IsNullOrWhiteSpace(editorConfigText))
                {
                    if (File.Exists(editorConfigPath))
                    {
                        File.Delete(editorConfigPath);
                    }
                }
                else
                {
                    File.WriteAllText(editorConfigPath, editorConfigText);
                }

                return await RunDotnetAsync(
                    projectDirectory,
                    template.PackageCacheDirectory,
                    "build",
                    "--no-restore",
                    "/clp:ErrorsOnly;Summary").ConfigureAwait(false);
            }
            finally
            {
                if (Directory.Exists(probeRoot))
                {
                    Directory.Delete(probeRoot, recursive: true);
                }
            }
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, directory);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
            }

            foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
                var destinationPath = Path.Combine(destinationDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(filePath, destinationPath, overwrite: true);
            }
        }

        private static string CreateAnalyzerSeverityEditorConfig(string[] diagnosticIds)
        {
            var builder = new StringBuilder()
                .AppendLine("root = true")
                .AppendLine()
                .AppendLine("[*.cs]");

            foreach (var diagnosticId in diagnosticIds.Distinct(StringComparer.Ordinal))
            {
                builder.Append("dotnet_diagnostic.")
                    .Append(diagnosticId)
                    .AppendLine(".severity = error");
            }

            return builder.ToString();
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

        private readonly record struct ProcessResult(int ExitCode, string Output);
        public readonly record struct ConsumerPackageScenario(
            string Name,
            string Source,
            string[] ExpectedDiagnosticIds,
            bool UsePreparedTemplate)
        {
            public override string ToString()
            {
                return Name;
            }
        }
        private readonly record struct PreparedConsumerTemplate(string RootDirectory, string ProjectDirectory, string PackageCacheDirectory);
    }
}
