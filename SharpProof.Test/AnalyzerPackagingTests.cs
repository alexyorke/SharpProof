using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class AnalyzerPackagingTests
{
    [OneTimeTearDown]
    public void CleanupPreparedConsumerTemplates()
    {
        foreach (var templateFactory in PreparedConsumerTemplates.Values)
        {
            if (!templateFactory.IsValueCreated ||
                !templateFactory.Value.IsCompletedSuccessfully)
                continue;

            var template = templateFactory.Value.GetAwaiter().GetResult();
            if (Directory.Exists(template.RootDirectory)) Directory.Delete(template.RootDirectory, true);
        }
    }

    private static readonly ConcurrentDictionary<string, Lazy<Task<PreparedConsumerTemplate>>>
        PreparedConsumerTemplates =
            new(StringComparer.Ordinal);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> SymbolicCliSources =
        new(LoadSymbolicCliSources);

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
        var referenced = typeof(SharpProofAnalyzer)
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
            "AnalyzerPackagingTest",
            new[] { syntaxTree },
            new[] { coreLib },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.That(compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), Is.False,
            "Test compilation should be valid with in-source attribute stubs.");

        var analyzerPath = typeof(SharpProofAnalyzer).Assembly.Location;
        Assert.That(File.Exists(analyzerPath), Is.True, $"Analyzer assembly not found at {analyzerPath}");

        var loader = new SimpleAnalyzerAssemblyLoader();
        var analyzerRef = new AnalyzerFileReference(analyzerPath, loader);
        var analyzers = analyzerRef.GetAnalyzers(LanguageNames.CSharp);
        Assert.That(analyzers.Count, Is.GreaterThan(0), "No analyzers were discovered in the analyzer assembly.");
        Assert.That(analyzers.OfType<SharpProofDiagnosticSuppressor>(), Has.Exactly(1).Items,
            "The analyzer assembly must export the exact-proof diagnostic suppressor.");

        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            null,
            true,
            false,
            false));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.That(diagnostics, Is.Not.Null);
    }

    [Test]
    public void PackageProject_ShouldUseReleaseReadyNuGetMetadata()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "SharpProof.Package", "SharpProof.Package.csproj");
        var document = XDocument.Load(projectPath);
        var sharedDocument = XDocument.Load(Path.Combine(repositoryRoot, "SharpProof.PackageMetadata.props"));
        var releaseDocument = XDocument.Load(Path.Combine(repositoryRoot, "SharpProof.Release.props"));
        var properties = sharedDocument
            .Descendants("PropertyGroup")
            .Elements()
            .GroupBy(element => element.Name.LocalName)
            .ToDictionary(group => group.Key, group => group.Last().Value);

        Assert.That(properties["PackageLicenseExpression"], Is.EqualTo("MIT"));
        Assert.That(properties["PackageProjectUrl"], Is.EqualTo("$(SharpProofProjectUrl)"));
        Assert.That(properties["RepositoryUrl"], Is.EqualTo("$(SharpProofProjectUrl)"));
        Assert.That(properties["RepositoryType"], Is.EqualTo("git"));
        Assert.That(properties["PackageReadmeFile"], Is.EqualTo("README.md"));
        Assert.That(releaseDocument.Descendants("SharpProofProjectUrl").Single().Value,
            Is.EqualTo("https://github.com/alexyorke/SharpProof"));
        Assert.That(
            document.Descendants("Import").Any(element =>
                element.Attribute("Project")?.Value.EndsWith(
                    "SharpProof.PackageMetadata.props",
                    StringComparison.Ordinal) == true),
            Is.True);
        Assert.That(document.Descendants().All(element => element.Name.LocalName != "DevelopmentDependency"), Is.True,
            "The public SharpProof analyzer package should not be marked as a development-only dependency.");
        Assert.That(
            properties.Values.Concat(releaseDocument.Descendants("PropertyGroup").Elements().Select(e => e.Value)),
            Has.None.Contains("HERE_OR_DELETE"));
    }

    [Test]
    public void PackageProject_ShouldPackAnalyzerCodeFixAndAttributesInExpectedLocations()
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), "SharpProof.Package", "SharpProof.Package.csproj");
        var document = XDocument.Load(projectPath);
        var defaultAnalyzerPackagePath = document
            .Descendants("ItemDefinitionGroup")
            .Descendants("TfmSpecificPackageFile")
            .Elements("PackagePath")
            .Single()
            .Value;
        var propertyResolver = new MsBuildPropertyTestResolver(document);
        var packageFiles = document
            .Descendants("ItemGroup")
            .Elements()
            .Where(element =>
                string.Equals(element.Name.LocalName, "TfmSpecificPackageFile", StringComparison.Ordinal) ||
                string.Equals(element.Name.LocalName, "None", StringComparison.Ordinal))
            .SelectMany(element =>
                (element.Attribute("Include")?.Value ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(include => new
                {
                    Include = propertyResolver.Expand(include.Trim()),
                    PackagePath = element.Attribute("PackagePath")?.Value ??
                                  (element.Name.LocalName == "TfmSpecificPackageFile"
                                      ? defaultAnalyzerPackagePath
                                      : string.Empty)
                }))
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
                file.Include.EndsWith("SharpProof.NativeSmtLocator.txt", StringComparison.Ordinal) &&
                file.PackagePath == "analyzers/dotnet/cs"), Is.True,
            "The analyzer package must expose its original native-SMT directory through a locator.");

        Assert.That(packageFiles.Any(file =>
                file.Include.Replace('\\', '/').EndsWith(
                    "buildTransitive/SharpProof.targets",
                    StringComparison.Ordinal) &&
                file.PackagePath.Replace('\\', '/') == "buildTransitive/SharpProof.targets"), Is.True,
            "The package should ship the native-analyzer filtering target.");
    }

    [Test]
    public void PackageBuildTransitiveTargets_ShouldFilterNativeAnalyzerAndExposeSmtLocator()
    {
        var targetsPath = Path.Combine(FindRepositoryRoot(), "SharpProof.Package", "buildTransitive",
            "SharpProof.targets");
        Assert.That(File.Exists(targetsPath), Is.True);
        var targets = File.ReadAllText(targetsPath);

        Assert.That(targets, Does.Contain("_SharpProofExcludeNativeAnalyzerAssets"));
        Assert.That(targets, Does.Contain("'%(Analyzer.Filename)' == 'libz3'"));
        Assert.That(targets, Does.Contain(".dll"));
        Assert.That(targets, Does.Contain(".dylib"));
        Assert.That(targets, Does.Contain(".so"));
        Assert.That(targets, Does.Contain("<Analyzer Remove="));
        Assert.That(targets, Does.Contain("SharpProof.NativeSmtLocator.txt"));
        Assert.That(targets, Does.Contain("<AdditionalFiles Include="));
        Assert.That(targets, Does.Not.Contain("SharpProof.EffectSummary"));
        Assert.That(targets, Does.Not.Contain("BuiltInEffectSummary"));
    }

    [Test]
    public void PackageProject_ShouldNotPackLegacyInstallScripts()
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), "SharpProof.Package", "SharpProof.Package.csproj");
        var document = XDocument.Load(projectPath);
        var packedTools = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "None", StringComparison.Ordinal))
            .Select(element => element.Attribute("PackagePath")?.Value ?? string.Empty)
            .Where(path => path.Replace('\\', '/').StartsWith("tools/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.That(packedTools, Is.Empty,
            "PackageReference analyzers must not ship packages.config install/uninstall scripts.");

        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml");
        var workflow = File.ReadAllText(workflowPath);
        Assert.That(workflow, Does.Contain("$legacyInstallEntries = @("));
        Assert.That(workflow, Does.Contain("unexpectedly contains legacy install entry"));
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
            .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityBuiltInSummaryDirectory",
                StringComparison.Ordinal))
            .Select(element => element.Value.Trim())
            .LastOrDefault();
        Assert.That(generatedSummaryDirectory,
            Is.EqualTo(@"$(BaseIntermediateOutputPath)$(Configuration)\$(TargetFramework)\GeneratedPurity"));

        var generatedSummaryArtifactSourceDirectory = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityArtifactSpecSourcePath",
                StringComparison.Ordinal))
            .Select(element => element.Value.Trim())
            .LastOrDefault();
        Assert.That(generatedSummaryArtifactSourceDirectory,
            Is.EqualTo(@"$(MSBuildThisFileDirectory)BuiltInEffectSummaryArtifactSpec.json"));

        var generatedSummaryArtifactSpecPath = document
            .Descendants()
            .Where(element =>
                string.Equals(element.Name.LocalName, "GeneratedPurityArtifactSpecPath", StringComparison.Ordinal))
            .Select(element => element.Value.Trim())
            .LastOrDefault();
        Assert.That(generatedSummaryArtifactSpecPath,
            Is.EqualTo(@"$(GeneratedPurityBuiltInSummaryDirectory)\BuiltInEffectSummaryArtifactSpec.json"));

        var generatedSummaryStampPath = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityGenerationStampPath",
                StringComparison.Ordinal))
            .Select(element => element.Value.Trim())
            .LastOrDefault();
        Assert.That(
            generatedSummaryStampPath,
            Is.EqualTo(@"$(GeneratedPurityBuiltInSummaryDirectory)\generation.$(NETCoreSdkVersion).stamp"));

        var generatedSummaryStagingDirectory = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityBuiltInSummaryStagingDirectory",
                StringComparison.Ordinal))
            .Select(element => element.Value.Trim())
            .LastOrDefault();
        Assert.That(generatedSummaryStagingDirectory, Is.EqualTo("$(GeneratedPurityBuiltInSummaryDirectory).staging"));

        var generatedSummaryStagingSpecPath = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityStagingArtifactSpecPath",
                StringComparison.Ordinal))
            .Select(element => element.Value.Trim())
            .LastOrDefault();
        Assert.That(
            generatedSummaryStagingSpecPath,
            Is.EqualTo(@"$(GeneratedPurityBuiltInSummaryStagingDirectory)\BuiltInEffectSummaryArtifactSpec.json"));

        var dependencyManifestDirectory = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityDependencyManifestDirectory",
                StringComparison.Ordinal))
            .Select(element => element.Value.Trim())
            .LastOrDefault();
        Assert.That(
            dependencyManifestDirectory,
            Is.EqualTo(@"$(BaseIntermediateOutputPath)$(Configuration)\$(TargetFramework)"));
        var inputManifestPath = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityInputManifestPath",
                StringComparison.Ordinal))
            .Select(element => element.Value.Trim())
            .LastOrDefault();
        Assert.That(
            inputManifestPath,
            Is.EqualTo(@"$(GeneratedPurityDependencyManifestDirectory)\GeneratedPurity.inputs.txt"));
        var outputManifestPath = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "GeneratedPurityOutputManifestPath",
                StringComparison.Ordinal))
            .Select(element => element.Value.Trim())
            .LastOrDefault();
        Assert.That(
            outputManifestPath,
            Is.EqualTo(@"$(GeneratedPurityDependencyManifestDirectory)\GeneratedPurity.outputs.txt"));

        var skipGeneratedEffectSummaries = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "SharpProofSkipGeneratedEffectSummaries",
                StringComparison.Ordinal))
            .Select(element => element.Value.Trim())
            .LastOrDefault();
        Assert.That(skipGeneratedEffectSummaries, Is.EqualTo("false"));

        var buildTarget = document
            .Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "Target", StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Name")?.Value, "BuildBuiltInEffectSummaryTool",
                    StringComparison.Ordinal));
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

        var dependencyTarget = document
            .Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "Target", StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Name")?.Value, "ResolveBuiltInEffectSummaryDependencies",
                    StringComparison.Ordinal));
        Assert.That(dependencyTarget.Attribute("DependsOnTargets")?.Value,
            Is.EqualTo("BuildBuiltInEffectSummaryTool"));
        Assert.That(
            dependencyTarget.Attribute("Condition")?.Value,
            Is.EqualTo("'$(SharpProofSkipGeneratedEffectSummaries)' != 'true'"));
        var dependencyExec = dependencyTarget
            .Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "Exec", StringComparison.Ordinal));
        Assert.That(
            dependencyExec.Attribute("Command")?.Value,
            Is.EqualTo(
                "dotnet \"$(GeneratedPurityToolDllPath)\" --artifact-spec-dependencies \"$(GeneratedPurityArtifactSpecSourcePath)\" --input-manifest \"$(GeneratedPurityInputManifestPath)\" --output-manifest \"$(GeneratedPurityOutputManifestPath)\" --dependency-output-root \"$(GeneratedPurityBuiltInSummaryDirectory)\""));
        Assert.That(dependencyExec.Attribute("IgnoreExitCode")?.Value, Is.EqualTo("true"));
        Assert.That(dependencyExec.Attribute("ConsoleToMSBuild")?.Value, Is.EqualTo("true"));
        var dependencyReads = dependencyTarget
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "ReadLinesFromFile", StringComparison.Ordinal))
            .ToArray();
        Assert.That(dependencyReads, Has.Length.EqualTo(2));
        Assert.That(dependencyReads[0].Attribute("File")?.Value,
            Is.EqualTo("$(GeneratedPurityInputManifestPath)"));
        Assert.That(dependencyReads[1].Attribute("File")?.Value,
            Is.EqualTo("$(GeneratedPurityOutputManifestPath)"));
        var dependencyErrors = dependencyTarget
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Error", StringComparison.Ordinal))
            .ToArray();
        Assert.That(dependencyErrors, Has.Length.EqualTo(2));
        Assert.That(dependencyErrors.Select(error => error.Attribute("Code")?.Value),
            Is.EqualTo(new[] { "SPB0001", "SPB0001" }));
        Assert.That(dependencyErrors[0].Attribute("Text")?.Value,
            Does.Contain("framework/runtime availability, package restore state, and artifact source paths"));

        var stageTarget = document
            .Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "Target", StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Name")?.Value, "GenerateBuiltInEffectSummaries",
                    StringComparison.Ordinal));
        Assert.That(stageTarget.Attribute("BeforeTargets")?.Value, Is.EqualTo("AssignTargetPaths"));
        Assert.That(stageTarget.Attribute("DependsOnTargets")?.Value,
            Is.EqualTo("ResolveBuiltInEffectSummaryDependencies"));
        Assert.That(
            stageTarget.Attribute("Condition")?.Value,
            Is.EqualTo("'$(SharpProofSkipGeneratedEffectSummaries)' != 'true'"));
        Assert.That(
            stageTarget.Attribute("Inputs")?.Value,
            Is.EqualTo(
                "$(GeneratedPurityArtifactSpecSourcePath);$(GeneratedPurityToolDllPath);$(GeneratedPurityInputManifestPath);$(GeneratedPurityOutputManifestPath);@(_GeneratedPuritySummaryInputs)"));
        Assert.That(
            stageTarget.Attribute("Outputs")?.Value,
            Is.EqualTo(
                "$(GeneratedPurityGenerationStampPath);$(GeneratedPurityArtifactSpecPath);@(_GeneratedPurityExpectedOutputs)"));

        var stageCopies = stageTarget
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Copy", StringComparison.Ordinal))
            .ToArray();
        Assert.That(stageCopies, Has.Length.EqualTo(3));
        Assert.That(stageCopies[0].Attribute("SourceFiles")?.Value,
            Is.EqualTo("$(GeneratedPurityArtifactSpecSourcePath)"));
        Assert.That(stageCopies[0].Attribute("DestinationFiles")?.Value,
            Is.EqualTo("$(GeneratedPurityStagingArtifactSpecPath)"));
        Assert.That(stageCopies[1].Attribute("SourceFiles")?.Value,
            Is.EqualTo("$(GeneratedPurityStagingArtifactSpecPath)"));
        Assert.That(stageCopies[1].Attribute("DestinationFiles")?.Value,
            Is.EqualTo("$(GeneratedPurityArtifactSpecPath)"));
        Assert.That(stageCopies[2].Attribute("SourceFiles")?.Value, Is.EqualTo("@(_SharpProofStagedSummaryFiles)"));
        Assert.That(stageCopies[2].Attribute("DestinationFolder")?.Value,
            Is.EqualTo("$(GeneratedPurityBuiltInSummaryDirectory)"));

        var artifactSpecTouch = stageTarget
            .Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "Touch", StringComparison.Ordinal));
        Assert.That(artifactSpecTouch.Attribute("Files")?.Value,
            Is.EqualTo("$(GeneratedPurityArtifactSpecPath)"));

        var stageRemovals = stageTarget
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "RemoveDir", StringComparison.Ordinal))
            .ToArray();
        Assert.That(stageRemovals, Has.Length.EqualTo(3));
        Assert.That(stageRemovals[0].Attribute("Directories")?.Value,
            Is.EqualTo("$(GeneratedPurityBuiltInSummaryStagingDirectory)"));
        Assert.That(stageRemovals[0].Attribute("Condition")?.Value,
            Is.EqualTo("Exists('$(GeneratedPurityBuiltInSummaryStagingDirectory)')"));
        Assert.That(stageRemovals[1].Attribute("Directories")?.Value,
            Is.EqualTo("$(GeneratedPurityBuiltInSummaryDirectory)"));
        Assert.That(stageRemovals[1].Attribute("Condition")?.Value,
            Is.EqualTo("Exists('$(GeneratedPurityBuiltInSummaryDirectory)')"));
        Assert.That(stageRemovals[2].Attribute("Directories")?.Value,
            Is.EqualTo("$(GeneratedPurityBuiltInSummaryStagingDirectory)"));
        Assert.That(stageRemovals[2].Attribute("Condition")?.Value,
            Is.EqualTo("Exists('$(GeneratedPurityBuiltInSummaryStagingDirectory)')"));

        var stageDirectories = stageTarget
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "MakeDir", StringComparison.Ordinal))
            .ToArray();
        Assert.That(stageDirectories, Has.Length.EqualTo(2));
        Assert.That(stageDirectories[0].Attribute("Directories")?.Value,
            Is.EqualTo("$(GeneratedPurityBuiltInSummaryStagingDirectory)"));
        Assert.That(stageDirectories[1].Attribute("Directories")?.Value,
            Is.EqualTo("$(GeneratedPurityBuiltInSummaryDirectory)"));

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
            Is.EqualTo(
                "dotnet \"$(GeneratedPurityToolDllPath)\" --artifact-spec \"$(GeneratedPurityStagingArtifactSpecPath)\""));
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
        Assert.That(stageErrors[0].Attribute("Condition")?.Value,
            Is.EqualTo("'$(_SharpProofEffectSummaryExitCode)' != '0'"));
        Assert.That(stageErrors[0].Attribute("Code")?.Value, Is.EqualTo("SPB0002"));
        Assert.That(stageErrors[0].Attribute("Text")?.Value,
            Does.Contain("SharpProof effect-summary generation failed"));
        Assert.That(stageErrors[0].Attribute("Text")?.Value,
            Does.Contain("previous generated summaries were left untouched"));
        Assert.That(stageErrors[1].Attribute("Condition")?.Value,
            Is.EqualTo("'@(_SharpProofStagedSummaryFiles)' == ''"));
        Assert.That(stageErrors[1].Attribute("Code")?.Value, Is.EqualTo("SPB0003"));
        Assert.That(stageErrors[1].Attribute("Text")?.Value, Does.Contain("produced no summary resources"));

        var stampWrite = stageTarget
            .Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "WriteLinesToFile", StringComparison.Ordinal));
        Assert.That(stampWrite.Attribute("File")?.Value, Is.EqualTo("$(GeneratedPurityGenerationStampPath)"));
        Assert.That(stampWrite.Attribute("Lines")?.Value, Is.EqualTo("$(NETCoreSdkVersion)"));
        Assert.That(stampWrite.Attribute("Overwrite")?.Value, Is.EqualTo("true"));
        Assert.That(stampWrite.Attribute("Condition")?.Value,
            Is.EqualTo("'$(_SharpProofEffectSummaryExitCode)' == '0'"));

        var includeTarget = document
            .Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "Target", StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Name")?.Value, "IncludeGeneratedPurityBuiltInSummaries",
                    StringComparison.Ordinal));
        Assert.That(includeTarget.Attribute("BeforeTargets")?.Value, Is.EqualTo("AssignTargetPaths"));
        Assert.That(includeTarget.Attribute("DependsOnTargets")?.Value, Is.EqualTo("GenerateBuiltInEffectSummaries"));
        Assert.That(
            includeTarget.Attribute("Condition")?.Value,
            Is.EqualTo("'$(SharpProofSkipGeneratedEffectSummaries)' != 'true'"));

        var generatedSummaryInclude = includeTarget
            .Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "_GeneratedPurityBuiltInSummary", StringComparison.Ordinal))
            .Attribute("Include")?.Value;
        Assert.That(generatedSummaryInclude,
            Is.EqualTo(@"$(GeneratedPurityBuiltInSummaryDirectory)\*.SharpProof.EffectSummary.json"));

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
        var builtInArtifactSpecPath =
            Path.Combine(repositoryRoot, "SharpProof.Analyzer", "BuiltInEffectSummaryArtifactSpec.json");
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

        var reviewedSpecPath = Path.Combine(repositoryRoot, "Tools", "SharpProof.EffectSummary",
            "ReviewedRuntimeArtifactSpec.json");
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
        var assembly = typeof(SharpProofAnalyzer).Assembly;
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
                System.Text.RegularExpressions.Regex.IsMatch(
                    file.Content,
                    @"(?<![A-Za-z0-9_.-])SharpProof\.EffectSummary\.json",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            .Select(file => file.Path.Substring(repositoryRoot.Length).TrimStart(Path.DirectorySeparatorChar))
            .ToArray();

        Assert.That(legacyAutomationReferences, Is.Empty,
            "Build and packaging entrypoints should not wire in legacy effect-summary artifact files or refresh scripts.");
    }

    [Test]
    public void AnalyzerPackage_ShouldInclude_SymbolicProofCoreAndZ3Dependencies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageProjectPath = Path.Combine(repositoryRoot, "SharpProof.Package", "SharpProof.Package.csproj");
        var project = XDocument.Load(packageProjectPath);
        var propertyResolver = new MsBuildPropertyTestResolver(project);
        var analyzerPackagePath = project
            .Descendants()
            .Where(element => string.Equals(
                element.Name.LocalName,
                "TfmSpecificPackageFile",
                StringComparison.Ordinal))
            .Where(element => element.Attribute("Include") == null)
            .SelectMany(element => element.Elements())
            .Single(element => string.Equals(element.Name.LocalName, "PackagePath", StringComparison.Ordinal))
            .Value;
        var analyzerPackageFiles = project
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "TfmSpecificPackageFile", StringComparison.Ordinal))
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => propertyResolver.Expand(value!).Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.That(analyzerPackagePath, Is.EqualTo("analyzers/dotnet/cs"));
        Assert.That(analyzerPackageFiles, Does.Contain("SharpProof.Symbolic.dll"));
        Assert.That(analyzerPackageFiles, Does.Contain("SharpProof.ProofCore.dll"));
        Assert.That(analyzerPackageFiles, Does.Contain("SharpProof.Attributes.dll"));
        Assert.That(analyzerPackageFiles, Does.Contain("Microsoft.Z3.dll"));
        Assert.That(analyzerPackageFiles, Does.Contain("libz3.dll"));
        Assert.That(analyzerPackageFiles, Does.Contain("libz3.dylib"));
        Assert.That(analyzerPackageFiles, Does.Contain("SharpProof.NativeSmtLocator.txt"));

        var z3Reference = project.Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "PackageReference", StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Include")?.Value, "Microsoft.Z3", StringComparison.Ordinal));
        var z3Version = ReadExpandedProperty(
            Path.Combine(repositoryRoot, "Directory.Build.props"),
            "MicrosoftZ3PackageVersion");
        Assert.That(z3Version, Is.EqualTo("4.12.2"));
        Assert.That(z3Reference.Attribute("Version")?.Value, Is.EqualTo("$(MicrosoftZ3PackageVersion)"));
        Assert.That(z3Reference.Attribute("GeneratePathProperty")?.Value, Is.EqualTo("true"));
        Assert.That(z3Reference.Attribute("PrivateAssets")?.Value, Is.EqualTo("all"));

        var packedFiles = project.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "None", StringComparison.Ordinal))
            .Where(element => string.Equals(element.Attribute("Pack")?.Value, "true", StringComparison.Ordinal))
            .Select(element => element.Attribute("PackagePath")?.Value)
            .ToArray();
        Assert.That(packedFiles, Does.Contain("buildTransitive/SharpProof.targets"));
        Assert.That(packedFiles, Does.Contain(@"\"));
    }

    [Test]
    public void MicrosoftZ3Package_ShouldMatchDeclaredNativePlatformMatrix()
    {
        var repositoryRoot = FindRepositoryRoot();
        var symbolicProject = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SharpProof.Symbolic.csproj"));
        var z3VersionReference = symbolicProject.Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "PackageReference", StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Include")?.Value, "Microsoft.Z3", StringComparison.Ordinal))
            .Attribute("Version")?.Value;
        Assert.That(z3VersionReference, Is.EqualTo("$(MicrosoftZ3PackageVersion)"));
        Assert.That(symbolicProject.Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "PackageReference", StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Include")?.Value, "Microsoft.Z3", StringComparison.Ordinal))
            .Attribute("GeneratePathProperty")?.Value, Is.EqualTo("true"));
        var z3Version = ReadExpandedProperty(
            Path.Combine(repositoryRoot, "Directory.Build.props"),
            "MicrosoftZ3PackageVersion");
        Assert.That(z3Version, Is.EqualTo("4.12.2"));

        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
            packageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        var z3Root = Path.Combine(packageRoot!, "microsoft.z3", z3Version!);
        Assert.That(Directory.Exists(z3Root), Is.True, "Restore Microsoft.Z3 before packaging validation.");

        var windowsNative = Path.Combine(z3Root, "runtimes", "win-x64", "native", "libz3.dll");
        var macNative = Path.Combine(z3Root, "runtimes", "osx-x64", "native", "libz3.dylib");
        var linuxNative = Path.Combine(z3Root, "runtimes", "linux-x64", "native", "libz3.so");
        Assert.That(File.Exists(windowsNative), Is.True);
        Assert.That(File.Exists(macNative), Is.True);
        Assert.That(new FileInfo(windowsNative).Length, Is.GreaterThan(0));
        Assert.That(new FileInfo(macNative).Length, Is.GreaterThan(0));
        Assert.That(File.Exists(linuxNative), Is.False,
            "Adding a Linux native changes the documented package-consumer fallback policy.");
    }

    [Test]
    public void BuiltAnalyzerPackage_ShouldShip_SymbolicProofCoreAndZ3Dependencies_WhenPackageExists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageVersion = ReadPackageVersion(repositoryRoot);
        var packagePath = Path.Combine(repositoryRoot, "SharpProof.Package", "bin", "Release",
            $"SharpProof.{packageVersion}.nupkg");
        if (!File.Exists(packagePath)) Assert.Inconclusive("Build the package before verifying package contents.");

        using var archive = ZipFile.OpenRead(packagePath);
        var entryNames = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToArray();

        Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/SharpProof.Symbolic.dll"));
        Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/SharpProof.ProofCore.dll"));
        Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/SharpProof.Attributes.dll"));
        Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/Microsoft.Z3.dll"));
        Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/libz3.dll"));
        Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/libz3.dylib"));
        Assert.That(entryNames, Does.Contain("analyzers/dotnet/cs/SharpProof.NativeSmtLocator.txt"));
        Assert.That(entryNames, Does.Contain("buildTransitive/SharpProof.targets"));
        Assert.That(entryNames, Does.Contain("THIRD-PARTY-NOTICES.txt"));
        Assert.That(entryNames, Does.Not.Contain("tools/install.ps1"));
        Assert.That(entryNames, Does.Not.Contain("tools/uninstall.ps1"));
        Assert.That(entryNames, Does.Not.Contain("analyzers/dotnet/cs/libz3.so"));

        var targetEntry = archive.GetEntry("buildTransitive/SharpProof.targets");
        Assert.That(targetEntry, Is.Not.Null);
        using (var reader = new StreamReader(targetEntry!.Open(), Encoding.UTF8))
        {
            var targets = reader.ReadToEnd();
            Assert.That(targets, Does.Contain("_SharpProofExcludeNativeAnalyzerAssets"));
            Assert.That(targets, Does.Contain("'%(Analyzer.Filename)' == 'libz3'"));
            Assert.That(targets, Does.Contain("'%(Analyzer.Extension)' == '.dll'"));
            Assert.That(targets, Does.Contain("'%(Analyzer.Extension)' == '.dylib'"));
            Assert.That(targets, Does.Contain("'%(Analyzer.Extension)' == '.so'"));
            Assert.That(targets, Does.Contain("<Analyzer Remove=\"@(_SharpProofNativeAnalyzer)\""));
            Assert.That(targets, Does.Contain("SharpProof.NativeSmtLocator.txt"));
            Assert.That(targets, Does.Contain("<AdditionalFiles Include=\"@(_SharpProofNativeSmtLocator)\""));
        }

        var noticeEntry = archive.GetEntry("THIRD-PARTY-NOTICES.txt");
        Assert.That(noticeEntry, Is.Not.Null);
        using (var reader = new StreamReader(noticeEntry!.Open(), Encoding.UTF8))
        {
            var notices = reader.ReadToEnd();
            Assert.That(notices, Does.Contain("Microsoft Z3 4.12.2"));
            Assert.That(notices, Does.Contain("win-x64 libz3.dll"));
            Assert.That(notices, Does.Contain("osx-x64"));
            Assert.That(notices, Does.Contain("MIT License"));
        }

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
    public void BuiltAnalyzerPackage_ShouldContainCurrentAnalyzerAndCodeFixAssemblyBytes_WhenPackageExists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageVersion = ReadPackageVersion(repositoryRoot);
        var configuration = Directory.GetParent(
            Path.GetDirectoryName(typeof(AnalyzerPackagingTests).Assembly.Location)!)?.Name ?? "Release";
        var packagePath = Path.Combine(
            repositoryRoot,
            "SharpProof.Package",
            "bin",
            configuration,
            $"SharpProof.{packageVersion}.nupkg");
        if (!File.Exists(packagePath)) Assert.Inconclusive("Build the package before verifying package contents.");

        var packageTimestamp = File.GetLastWriteTimeUtc(packagePath);
        var assemblyPaths = new[]
        {
            typeof(SharpProofAnalyzer).Assembly.Location,
            typeof(SharpProofCodeFixProvider).Assembly.Location
        };
        if (assemblyPaths.Any(path => File.GetLastWriteTimeUtc(path) > packageTimestamp))
            Assert.Inconclusive("Rebuild the package after the analyzer assemblies before verifying contents.");

        using var archive = ZipFile.OpenRead(packagePath);
        AssertPackageEntryMatchesAssembly(
            archive,
            "analyzers/dotnet/cs/SharpProof.Analyzer.dll",
            typeof(SharpProofAnalyzer).Assembly.Location);
        AssertPackageEntryMatchesAssembly(
            archive,
            "analyzers/dotnet/cs/SharpProof.CodeFixes.dll",
            typeof(SharpProofCodeFixProvider).Assembly.Location);

        static void AssertPackageEntryMatchesAssembly(
            ZipArchive archive,
            string entryPath,
            string assemblyPath)
        {
            var entry = archive.Entries.Single(candidate =>
                string.Equals(
                    candidate.FullName.Replace('\\', '/'),
                    entryPath,
                    StringComparison.Ordinal));
            using var stream = entry.Open();
            var packagedHash = SHA256.HashData(stream);
            var builtHash = SHA256.HashData(File.ReadAllBytes(assemblyPath));

            Assert.That(packagedHash, Is.EqualTo(builtHash), entryPath);
        }
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
            new[] { "SP0002" },
            false);
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
            new[] { "SP0004" },
            true);
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
            new[] { "SP0013" },
            true);
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
            new[] { "SP0015" },
            true);
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
            new[] { "SP0016" },
            true);
        yield return new ConsumerPackageScenario(
            "source-diagnostic-surface",
            """
            using System;
            using SharpProof.Attributes;

            [EnforcePure,
             Pure,
             ZeroAllocations,
             AllowedCapabilities(SharpProofCapability.None),
             Ensures("result > 0"),
             Requires("true"),
             DoesNotThrow,
             AllowedExceptions(typeof(Exception)),
             ExpectedComplexity(ComplexityKind.Constant)]
            public sealed class MisplacedContracts
            {
            }

            namespace SharpProof.Contracts
            {
                [AttributeUsage(AttributeTargets.All)]
                public sealed class AllowSynchronizationAttribute : Attribute
                {
                }
            }

            [SharpProof.Contracts.AllowSynchronization]
            public sealed class StubMisplacedContracts
            {
            }

            namespace FakeContracts
            {
                public sealed class EnforcePureAttribute : Attribute
                {
                }
            }

            public sealed class ContractSurface
            {
                [EnforcePure, Impure]
                public int Conflicting() => DateTime.Now.Second;

                [AllowSynchronization]
                public int SyncWithoutPurity() => 1;

                [EnforcePure, AllowSynchronization]
                public int RedundantSynchronization() => 1;

                [ZeroAllocations]
                public object Allocate() => new object();

                [AllowedCapabilities(SharpProofCapability.None)]
                public void WriteConsole() => Console.WriteLine("hello");

                [AllowedCapabilities(SharpProofCapability.None)]
                public string DynamicCall(dynamic value) => value.ToString();

                [Ensures("result > 0")]
                public int FailingEnsure() => 0;

                [Ensures("local > 0")]
                public int UnsupportedEnsure() => 0;

                [Requires("value > 0")]
                public int Callee(int value) => value;

                public int Caller() => Callee(0);

                [Requires("result > 0")]
                public int UnsupportedRequire(int value) => value;

                [ExpectedComplexity(ComplexityKind.Linear)]
                public int Quadratic(int n)
                {
                    var sum = 0;
                    for (var i = 0; i < n; i++)
                    {
                        for (var j = 0; j < n; j++)
                        {
                            sum += i + j;
                        }
                    }

                    return sum;
                }

                [ExpectedComplexity(ComplexityKind.Linear)]
                public int UnsupportedComplexity(int n)
                {
                    var i = 0;
                    while (i < n)
                    {
                        i = ComplexityStep(i);
                    }

                    return i;
                }

                private static int ComplexityStep(int value) => value + 1;

                [AllowedExceptions(typeof(string))]
                public void InvalidExceptionContract()
                {
                }

                [DoesNotThrow]
                public void ThrowsUnexpectedly() => throw new InvalidOperationException();

                [AllowedExceptions(typeof(ArgumentException))]
                public void ThrowsDisallowed() => throw new InvalidOperationException();

                [FakeContracts.EnforcePure]
                public int FakeAttributeIdentity() => 1;
            }
            """,
            new[]
            {
                "SP0002", "SP0003", "SP0004", "SP0005", "SP0006", "SP0007", "SP0008",
                "SP0013", "SP0014", "SP0015", "SP0016", "SP0017", "SP0018", "SP0019",
                "SP0020", "SP0021", "SP0022", "SP0023", "SP0024", "SP0026", "SP0027",
                "SP0028", "SP0029", "SP0030", "SP0031"
            },
            true,
            "build_property.sharpproof_attribute_stub_namespaces = SharpProof.Contracts");
        yield return new ConsumerPackageScenario(
            "sp0009-exception-reporting",
            """
            using System;
            using SharpProof.Attributes;

            public sealed class DiagnosticSurface
            {
                [EnforcePure]
                public int ExplainAndCall()
                {
                    Thrower();
                    return DateTime.Now.Second;
                }

                private static void Thrower() => throw new InvalidOperationException();
            }
            """,
            new[] { "SP0002", "SP0009", "SP0010", "SP0011" },
            true,
            """
            sharpproof_emit_explanations = true
            sharpproof_report_exceptions = true
            sharpproof_checked_exceptions = true
            """);
        yield return new ConsumerPackageScenario(
            "sp0012-bcl-fallback",
            """
            using SharpProof.Attributes;

            public sealed class BclFallbackSurface
            {
                [EnforcePure]
                public int Normalize(int value) => System.Experimental.NumericFacts.Normalize(value);
            }
            """,
            new[] { "SP0002", "SP0012" },
            true,
            "sharpproof_report_bcl_fallback_guesses = true",
            AdditionalReferenceAssemblyName: "System.FallbackSdk",
            AdditionalReferenceSource: """
                                       namespace System.Experimental
                                       {
                                           public static class NumericFacts
                                           {
                                               public static int Normalize(int value) => value;
                                           }
                                       }
                                       """);
        yield return new ConsumerPackageScenario(
            "sp0025-invalid-configuration",
            """
            public sealed class InvalidConfigurationSurface
            {
                public int Value => 1;
            }
            """,
            new[] { "SP0025" },
            true,
            "sharpproof_runtime_hazard_mode = invalid-mode");
        yield return new ConsumerPackageScenario(
            "sp0032-invalid-additional-file",
            """
            public sealed class InvalidAdditionalFileSurface
            {
                public int Value => 1;
            }
            """,
            new[] { "SP0032" },
            true,
            AdditionalFiles: new Dictionary<string, string>
            {
                ["SharpProof.Baseline.json"] = "{"
            });
        yield return new ConsumerPackageScenario(
            "sp0033-unknown-runtime-hazard",
            """
            namespace Probe;

            public sealed class UnknownHazardSurface
            {
                public int Divide(int divisor) => 10 / divisor;
            }
            """,
            new[] { "SP0033" },
            false,
            "sharpproof_runtime_hazard_mode = unknowns");
        yield return new ConsumerPackageScenario(
            "sp0034-sp0039-inferred-contract-suggestions",
            """
            using System;

            namespace Probe;

            public static class InferredContractSurface
            {
                public static int Identity(int value) => value;

                public static int Positive(int value)
                {
                    if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                    return value;
                }
            }
            """,
            new[] { "SP0034", "SP0035", "SP0036", "SP0037", "SP0038", "SP0039" },
            false,
            """
            sharpproof_suggest_missing_enforce_pure = false
            sharpproof_suggest_inferred_contracts = true
            sharpproof_suggest_inferred_contracts_scope = all
            sharpproof_suggest_inferred_contracts_kinds = zero-allocations, capabilities, complexity, exceptions, ensures, requires
            sharpproof_suggest_inferred_contracts_minimum_confidence = high
            """);
        yield return new ConsumerPackageScenario(
            "sp0040-trusted-boundary-review",
            """
            using SharpProof.Attributes;

            namespace Probe;

            public static class TrustedBoundary
            {
                public static int Value(int value) => value;
            }

            public sealed class TrustedBoundaryConsumer
            {
                [EnforcePure]
                public int Read() => TrustedBoundary.Value(1);
            }
            """,
            new[] { "SP0040" },
            true,
            """
            sharpproof_suggest_missing_enforce_pure = false
            sharpproof_trusted_boundary_review_mode = used
            sharpproof_known_pure_methods = spm1|UHJvYmUuVHJ1c3RlZEJvdW5kYXJ5|b3JkaW5hcnk=|VmFsdWU=|0|1|bm9uZQ==|bmFtZWQ6U3lzdGVtLkludDMy|bm9uZQ==|bmFtZWQ6U3lzdGVtLkludDMy
            """);
        yield return new ConsumerPackageScenario(
            "sp0041-sp0047-nullable-verification",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;

            namespace Probe;

            public sealed class NullableSurface
            {
                private string? _field;
                private int _reads;
                private string? Unstable => _reads++ == 0 ? "value" : null;

                public string BrokenReturn() => null;

                public bool BrokenOut([NotNullWhen(true)] out string? value)
                {
                    value = null;
                    return true;
                }

                [MemberNotNull(nameof(_field))]
                public void BrokenMember() { }

                [MemberNotNull(nameof(Unstable))]
                public void UnknownMember() { }

                public int Unsafe()
                {
                    string? value = null;
                    return value!.Length;
                }

                public int Unnecessary(string value) => value!.Length;

                public string? Suggested() => "value";
            }
            """,
            new[] { "SP0041", "SP0042", "SP0043", "SP0044", "SP0045", "SP0046", "SP0047" },
            false,
            """
            sharpproof_suggest_missing_enforce_pure = false
            sharpproof_suggest_inferred_contracts = true
            sharpproof_suggest_inferred_contracts_scope = all
            sharpproof_suggest_inferred_contracts_kinds = nullability
            sharpproof_suggest_inferred_contracts_minimum_confidence = high
            sharpproof_report_nullable_inconclusive = true
            """);
        var commonBugSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "readme-examples",
            "common-bug-diagnostics",
            "input.cs"));
        yield return new ConsumerPackageScenario(
            "sp0048-sp0076-common-bug-diagnostics",
            commonBugSource,
            Enumerable.Range(48, 29).Select(static number => $"SP{number:0000}").ToArray(),
            false);
    }

    [Test]
    public void BuiltAnalyzerPackageScenarios_CoverSupportedDiagnosticsAndCodeFixIds()
    {
        var coveredDiagnosticIds = BuiltAnalyzerPackageScenarios()
            .SelectMany(scenario => scenario.ExpectedDiagnosticIds)
            .ToHashSet(StringComparer.Ordinal);
        var supportedDiagnosticIds = new SharpProofAnalyzer()
            .SupportedDiagnostics
            .Select(descriptor => descriptor.Id)
            .ToHashSet(StringComparer.Ordinal);
        var fixableDiagnosticIds = new SharpProofCodeFixProvider()
            .FixableDiagnosticIds
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(
            coveredDiagnosticIds,
            Is.SupersetOf(supportedDiagnosticIds),
            "Every public analyzer diagnostic must have a package-consumer scenario.");
        Assert.That(
            coveredDiagnosticIds,
            Is.SupersetOf(fixableDiagnosticIds),
            "Every code-fix diagnostic must have a package-consumer scenario.");
    }

    [TestCaseSource(nameof(BuiltAnalyzerPackageScenarios))]
    public async Task
        BuiltAnalyzerPackage_WhenConsumedByDisposableProject_ReportsCurrentBetaDiagnostics_WhenPackageExists(
            ConsumerPackageScenario scenario)
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageVersion = ReadPackageVersion(repositoryRoot);
        var packagePath = ResolveExistingPackageArtifact(
            repositoryRoot,
            "SharpProof.Package",
            $"SharpProof.{packageVersion}.nupkg");
        if (packagePath == null)
            Assert.Inconclusive("Build the package before verifying external package consumption.");
        var packageSource = Path.GetDirectoryName(packagePath)!;
        var editorConfigText = CreateAnalyzerSeverityEditorConfig(
            scenario.ExpectedDiagnosticIds,
            scenario.AdditionalEditorConfigText);
        (string DirectoryPath, string AssemblyPath)? fixture = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(scenario.AdditionalReferenceSource))
                fixture = CreateFixtureAssembly(
                    scenario.AdditionalReferenceAssemblyName ?? scenario.Name + ".Fixture",
                    scenario.AdditionalReferenceSource);

            var buildResult = scenario.UsePreparedTemplate
                ? await BuildPreparedPackageConsumerAsync(
                    await GetPreparedConsumerTemplateAsync(
                        "SharpProof",
                        packageVersion,
                        packageSource).ConfigureAwait(false),
                    scenario.Source,
                    editorConfigText,
                    scenario.AdditionalEditorConfigText,
                    scenario.AdditionalFiles,
                    fixture?.AssemblyPath).ConfigureAwait(false)
                : await BuildDisposablePackageConsumerAsync(
                    "SharpProof",
                    packageVersion,
                    packageSource,
                    scenario.Source,
                    editorConfigText,
                    scenario.AdditionalEditorConfigText,
                    scenario.AdditionalFiles,
                    fixture?.AssemblyPath).ConfigureAwait(false);

            Assert.That(
                buildResult.ExitCode,
                Is.Not.EqualTo(0),
                $"The packaged analyzer scenario '{scenario.Name}' should fail when promoted diagnostics are emitted.{Environment.NewLine}{buildResult.Output}");

            foreach (var diagnosticId in scenario.ExpectedDiagnosticIds)
                Assert.That(
                    buildResult.Output,
                    Does.Contain(diagnosticId),
                    $"The packaged SharpProof analyzer scenario '{scenario.Name}' should emit {diagnosticId}.{Environment.NewLine}{buildResult.Output}");
        }
        finally
        {
            if (fixture is { } createdFixture && Directory.Exists(createdFixture.DirectoryPath))
                Directory.Delete(createdFixture.DirectoryPath, true);
        }
    }

    [Test]
    public async Task
        BuiltAnalyzerPackage_WhenConsumedByDisposableProject_AcceptsPropertyAndIndexerGetterAliases_WhenPackageExists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageVersion = ReadPackageVersion(repositoryRoot);
        var packagePath = ResolveExistingPackageArtifact(
            repositoryRoot,
            "SharpProof.Package",
            $"SharpProof.{packageVersion}.nupkg");
        if (packagePath == null)
            Assert.Inconclusive("Build the package before verifying external package consumption.");
        var packageSource = Path.GetDirectoryName(packagePath)!;

        var buildResult = await BuildDisposablePackageConsumerAsync(
            "SharpProof",
            packageVersion,
            packageSource,
            """
            using System;
            using SharpProof.Attributes;

            namespace Probe;

            public sealed class GetterAliases
            {
                [EnforcePure]
                [ZeroAllocations]
                [AllowedCapabilities(SharpProofCapability.None)]
                [Ensures("result == 42")]
                [DoesNotThrow]
                [ExpectedComplexity(ComplexityKind.Constant)]
                public int Answer => 42;

                [Pure]
                [AllowedExceptions(typeof(ArgumentException))]
                public int this[int index]
                {
                    [Requires("index >= 0")]
                    get => index;
                }
            }
            """).ConfigureAwait(false);

        Assert.That(buildResult.ExitCode, Is.EqualTo(0), buildResult.Output);
        var placementDiagnosticIds = new[]
        {
            "SP0003", "SP0014", "SP0017", "SP0020", "SP0023", "SP0029", "SP0031"
        };
        foreach (var diagnosticId in placementDiagnosticIds)
            Assert.That(buildResult.Output, Does.Not.Contain(diagnosticId), buildResult.Output);
    }

    [Test]
    public async Task
        BuiltAnalyzerPackage_WhenConsumedByDisposableProject_SuppressesOnlyExactlyProvenCompilerDiagnostics_WhenPackageExists()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            !(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
              RuntimeInformation.IsOSPlatform(OSPlatform.OSX)))
            Assert.Ignore("The analyzer package bundles native SMT only for Windows and macOS x64.");

        var repositoryRoot = FindRepositoryRoot();
        var packageVersion = ReadPackageVersion(repositoryRoot);
        var packagePath = ResolveExistingPackageArtifact(
            repositoryRoot,
            "SharpProof.Package",
            $"SharpProof.{packageVersion}.nupkg");
        if (packagePath == null)
            Assert.Inconclusive("Build the package before verifying external package consumption.");
        var packageSource = Path.GetDirectoryName(packagePath)!;
        const string editorConfig = """
                                    root = true

                                    [*.cs]
                                    sharpproof_suppress_proven_diagnostics = true
                                    sharpproof_suppression_diagnostic_ids = CS8602
                                    sharpproof_suggest_missing_enforce_pure = false
                                    """;

        var provenResult = await BuildDisposablePackageConsumerAsync(
            "SharpProof",
            packageVersion,
            packageSource,
            """
            #nullable enable
            using SharpProof.Attributes;

            namespace Probe;

            public sealed class ProvenSafe
            {
                [Requires("value != null")]
                public int Length(string? value) => value.Length;
            }
            """,
            editorConfig,
            includeWarningsInOutput: true).ConfigureAwait(false);

        Assert.That(provenResult.ExitCode, Is.EqualTo(0), provenResult.Output);
        Assert.That(provenResult.Output, Does.Not.Contain("CS8602"), provenResult.Output);
        Assert.That(provenResult.Output, Does.Not.Contain("AD0001"), provenResult.Output);
        Assert.That(provenResult.Output, Does.Not.Contain("CS8034"), provenResult.Output);

        var uncertainResult = await BuildDisposablePackageConsumerAsync(
            "SharpProof",
            packageVersion,
            packageSource,
            """
            #nullable enable

            namespace Probe;

            public sealed class Uncertain
            {
                public int Length(string? value) => value.Length;
            }
            """,
            editorConfig,
            includeWarningsInOutput: true).ConfigureAwait(false);

        Assert.That(uncertainResult.ExitCode, Is.EqualTo(0), uncertainResult.Output);
        Assert.That(uncertainResult.Output, Does.Contain("CS8602"), uncertainResult.Output);
        Assert.That(uncertainResult.Output, Does.Not.Contain("AD0001"), uncertainResult.Output);
        Assert.That(uncertainResult.Output, Does.Not.Contain("CS8034"), uncertainResult.Output);
    }

    [Test]
    public async Task
        BuiltAttributesPackage_WhenConsumedByDisposableProject_AllowsAttributeCompileWithoutAnalyzerAssets_WhenPackageExists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageVersion = ReadPackageVersion(repositoryRoot);
        var packagePath = ResolveExistingPackageArtifact(
            repositoryRoot,
            "SharpProof.Attributes",
            $"SharpProof.Attributes.{packageVersion}.nupkg");
        if (packagePath == null)
            Assert.Inconclusive("Build the attributes package before verifying external package consumption.");
        var packageSource = Path.GetDirectoryName(packagePath)!;

        var buildResult = await BuildDisposablePackageConsumerAsync(
            "SharpProof.Attributes",
            packageVersion,
            packageSource,
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
    public void SymbolicPackage_ShouldDeclareSupportedPublicContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "SharpProof.Symbolic", "SharpProof.Symbolic.csproj");
        var baselinePath = Path.Combine(repositoryRoot, "SharpProof.Symbolic", "PackageBaseline.json");
        var project = XDocument.Load(projectPath);
        var repositoryDefaults = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));
        var sharedMetadata = XDocument.Load(Path.Combine(repositoryRoot, "SharpProof.PackageMetadata.props"));
        var releaseMetadata = XDocument.Load(Path.Combine(repositoryRoot, "SharpProof.Release.props"));
        using var baseline = JsonDocument.Parse(File.ReadAllText(baselinePath));
        var baselineRoot = baseline.RootElement;
        var propertyResolver = new MsBuildPropertyTestResolver(
            releaseMetadata,
            repositoryDefaults,
            sharedMetadata,
            project);
        var properties = repositoryDefaults
            .Descendants()
            .Concat(sharedMetadata.Descendants())
            .Concat(project.Descendants())
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
            .Select(static element => element.Name.LocalName)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                static name => name,
                propertyResolver.Get,
                StringComparer.Ordinal);

        Assert.That(baselineRoot.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(properties["PackageId"], Is.EqualTo(baselineRoot.GetProperty("packageId").GetString()));
        Assert.That(properties["Version"], Is.EqualTo(baselineRoot.GetProperty("packageVersion").GetString()));
        Assert.That(properties["TargetFramework"],
            Is.EqualTo(baselineRoot.GetProperty("targetFramework").GetString()));
        Assert.That(properties["IsPackable"], Is.EqualTo("true"));
        Assert.That(properties["GeneratePackageOnBuild"], Is.EqualTo("true"));
        Assert.That(properties["GenerateDocumentationFile"], Is.EqualTo("true"));
        Assert.That(properties["Nullable"], Is.EqualTo("enable"));
        Assert.That(properties["DebugType"], Is.EqualTo("portable"));
        Assert.That(properties["PublishRepositoryUrl"], Is.EqualTo("true"));
        Assert.That(properties["EmbedUntrackedSources"], Is.EqualTo("true"));
        Assert.That(properties["PackageReadmeFile"], Is.EqualTo("README.md"));
        Assert.That(properties["PackageLicenseExpression"], Is.EqualTo("MIT"));
        Assert.That(properties["AllowedOutputExtensionsInPackageBuildOutputFolder"], Does.Contain(".pdb"));

        var packageReferences = project.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element,
                StringComparer.Ordinal);
        Assert.That(packageReferences, Does.ContainKey("Microsoft.CodeAnalysis.PublicApiAnalyzers"));
        Assert.That(packageReferences["Microsoft.CodeAnalysis.PublicApiAnalyzers"].Attribute("PrivateAssets")?.Value,
            Is.EqualTo("all"));
        Assert.That(packageReferences, Does.ContainKey("Microsoft.Z3"));

        var proofCoreReference = project.Descendants()
            .Single(element =>
                element.Name.LocalName == "ProjectReference" &&
                element.Attribute("Include")?.Value.EndsWith("SharpProof.ProofCore\\SharpProof.ProofCore.csproj",
                    StringComparison.Ordinal) == true);
        Assert.That(proofCoreReference.Attribute("PrivateAssets")?.Value, Is.EqualTo("all"));

        var additionalFiles = project.Descendants()
            .Where(element => element.Name.LocalName == "AdditionalFiles")
            .Select(element => element.Attribute("Include")?.Value)
            .ToArray();
        Assert.That(additionalFiles, Does.Contain("PublicAPI.Shipped.txt"));
        Assert.That(additionalFiles, Does.Contain("PublicAPI.Unshipped.txt"));

        var shippedApiPath = Path.Combine(repositoryRoot, "SharpProof.Symbolic", "PublicAPI.Shipped.txt");
        var unshippedApiPath = Path.Combine(repositoryRoot, "SharpProof.Symbolic", "PublicAPI.Unshipped.txt");
        var shippedApi = File.ReadAllLines(shippedApiPath);
        Assert.That(shippedApi.FirstOrDefault(), Is.EqualTo("#nullable enable"));
        Assert.That(shippedApi, Has.Length.GreaterThan(100));
        Assert.That(File.ReadLines(unshippedApiPath).FirstOrDefault(), Is.EqualTo("#nullable enable"));

        var nullableProperty = typeof(SymbolicSourceInput).GetProperty(nameof(SymbolicSourceInput.FilePath));
        Assert.That(nullableProperty, Is.Not.Null);
        Assert.That(new NullabilityInfoContext().Create(nullableProperty!).ReadState,
            Is.EqualTo(NullabilityState.Nullable));
    }

    [Test]
    public void BuiltSymbolicPackage_ShouldMatchPackageBaseline_WhenPackageExists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var baselinePath = Path.Combine(repositoryRoot, "SharpProof.Symbolic", "PackageBaseline.json");
        using var baseline = JsonDocument.Parse(File.ReadAllText(baselinePath));
        var baselineRoot = baseline.RootElement;
        var packageId = baselineRoot.GetProperty("packageId").GetString()!;
        var packageVersion = baselineRoot.GetProperty("packageVersion").GetString()!;
        var packagePath = ResolveExistingPackageArtifact(
            repositoryRoot,
            "SharpProof.Symbolic",
            $"{packageId}.{packageVersion}.nupkg");
        if (packagePath == null)
            Assert.Inconclusive("Build the symbolic package before verifying its compatibility baseline.");

        using var archive = ZipFile.OpenRead(packagePath!);
        var entryNames = archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .ToArray();
        var expectedEntries = baselineRoot.GetProperty("requiredEntries")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Cast<string>()
            .ToArray();
        Assert.That(entryNames, Is.SupersetOf(expectedEntries));

        var nuspecEntry = archive.Entries.Single(entry =>
            entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using var nuspecStream = nuspecEntry.Open();
        var nuspec = XDocument.Load(nuspecStream);
        string MetadataValue(string name) => nuspec.Descendants()
            .Single(element => element.Name.LocalName == name)
            .Value.Trim();

        Assert.That(MetadataValue("id"), Is.EqualTo(packageId));
        Assert.That(MetadataValue("version"), Is.EqualTo(packageVersion));

        var expectedDependencies = baselineRoot.GetProperty("dependencies")
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal);
        var actualDependencies = nuspec.Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .ToDictionary(
                element => element.Attribute("id")!.Value,
                element => element.Attribute("version")!.Value,
                StringComparer.Ordinal);
        Assert.That(actualDependencies, Has.Count.EqualTo(expectedDependencies.Count));
        foreach (var expectedDependency in expectedDependencies)
        {
            Assert.That(actualDependencies, Does.ContainKey(expectedDependency.Key));
            Assert.That(actualDependencies[expectedDependency.Key], Is.EqualTo(expectedDependency.Value));
        }
    }

    [Test]
    public void BuiltSymbolicPackage_ShouldContainPortableSourceLinkedSymbols_WhenPackageExists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageVersion = ReadPackageVersion(repositoryRoot);
        var packagePath = ResolveExistingPackageArtifact(
            repositoryRoot,
            "SharpProof.Symbolic",
            $"SharpProof.Symbolic.{packageVersion}.nupkg");
        if (packagePath == null)
            Assert.Inconclusive("Build the symbolic package before verifying Source Link metadata.");

        using var archive = ZipFile.OpenRead(packagePath!);
        var pdbEntry = archive.GetEntry("lib/netstandard2.0/SharpProof.Symbolic.pdb");
        Assert.That(pdbEntry, Is.Not.Null);
        using var pdb = new MemoryStream();
        using (var pdbEntryStream = pdbEntry!.Open()) pdbEntryStream.CopyTo(pdb);
        pdb.Position = 0;

        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen);
        var reader = provider.GetMetadataReader();
        var sourceLinkKind = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");
        var sourceLinkHandles = reader.CustomDebugInformation
            .Where(handle =>
            {
                var information = reader.GetCustomDebugInformation(handle);
                return reader.GetGuid(information.Kind) == sourceLinkKind;
            })
            .ToArray();
        Assert.That(sourceLinkHandles, Has.Length.EqualTo(1));

        var sourceLink = reader.GetCustomDebugInformation(sourceLinkHandles[0]);
        var sourceLinkJson = Encoding.UTF8.GetString(reader.GetBlobBytes(sourceLink.Value));
        using var sourceLinkDocument = JsonDocument.Parse(sourceLinkJson);
        var mappings = sourceLinkDocument.RootElement.GetProperty("documents")
            .EnumerateObject()
            .ToArray();
        Assert.That(mappings, Is.Not.Empty);
        Assert.That(mappings.Select(mapping => mapping.Value.GetString()),
            Has.Some.StartsWith("https://raw.githubusercontent.com/alexyorke/SharpProof/"));
    }

    [Test]
    public async Task BuiltSymbolicPackage_WhenConsumedByDisposableConsole_RunsPackagedSample_WhenPackageExists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageVersion = ReadPackageVersion(repositoryRoot);
        var packagePath = ResolveExistingPackageArtifact(
            repositoryRoot,
            "SharpProof.Symbolic",
            $"SharpProof.Symbolic.{packageVersion}.nupkg");
        if (packagePath == null)
            Assert.Inconclusive("Build the symbolic package before verifying external package consumption.");
        var attributesPackagePath = ResolveExistingPackageArtifact(
            repositoryRoot,
            "SharpProof.Attributes",
            $"SharpProof.Attributes.{packageVersion}.nupkg");
        if (attributesPackagePath == null)
            Assert.Inconclusive("Build the attributes package before verifying external package consumption.");

        string sampleSource;
        using (var archive = ZipFile.OpenRead(packagePath!))
        {
            var sampleEntry = archive.GetEntry("samples/SharpProof.Symbolic/Program.cs");
            Assert.That(sampleEntry, Is.Not.Null);
            using var reader = new StreamReader(sampleEntry!.Open(), Encoding.UTF8);
            sampleSource = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        var runResult = await RunDisposablePackageConsoleAsync(
            "SharpProof.Symbolic",
            packageVersion,
            new[]
            {
                Path.GetDirectoryName(packagePath!)!,
                Path.GetDirectoryName(attributesPackagePath!)!
            },
            sampleSource).ConfigureAwait(false);
        Assert.That(runResult.ExitCode, Is.EqualTo(0), runResult.Output);
        Assert.That(runResult.Output, Does.Contain("Program points:"));
        Assert.That(runResult.Output, Does.Contain("Invariant:"));
    }

    [Test]
    public void SymbolicCli_ShouldReferenceSymbolicLibrary_AndAnalyzerForBuildDiagnostics()
    {
        var repositoryRoot = FindRepositoryRoot();
        var cliProjectPath = Path.Combine(repositoryRoot, "Tools", "SharpProof.SymbolicCli",
            "SharpProof.SymbolicCli.csproj");
        var project = XDocument.Load(cliProjectPath);
        var projectReferences = project
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.That(projectReferences, Does.Contain(@"..\..\SharpProof.Symbolic\SharpProof.Symbolic.csproj"));
        Assert.That(projectReferences, Does.Contain(@"..\..\SharpProof.Analyzer\SharpProof.Analyzer.csproj"));
    }

    [Test]
    public void SymbolicCli_ShouldExposeLightweightSourceAndJsonRequestInputs()
    {
        AssertSymbolicCliSourceContains(
            ("SymbolicCliOptions.cs", "--stdin"),
            ("SymbolicCliOptions.cs", "--source-text <text>"),
            ("SymbolicCliOptions.cs", "--source-file-name <path>"),
            ("SymbolicCliOptions.cs", "--source-map-uri <uri>"),
            ("SymbolicCliOptions.cs", "--request-json <json>"),
            ("SymbolicCliOptions.cs", "--request-json-stdin"),
            ("SymbolicCliJsonRequest.cs", "SchemaVersion != 1"),
            ("SymbolicCliJsonRequest.cs", "JsonUnmappedMemberHandling.Disallow"),
            ("SymbolicCliJsonRequest.cs", "--smt-timeout-ms"),
            ("SymbolicCliJsonRequest.cs", "--analysis-limit"),
            ("SymbolicCliJsonRequest.cs", "--compact-json"));
    }

    [Test]
    public void SymbolicCli_ShouldExposeBoundedExplainReportFormats()
    {
        AssertSymbolicCliSourceContains(
            ("SymbolicCliOptions.cs", "--sarif"),
            ("SymbolicCliOptions.cs", "--markdown"),
            ("SymbolicCliOptions.cs", "--report-max-diagnostics <n>"),
            ("SymbolicCliOptions.cs", "--report-max-hazards <n>"),
            ("SymbolicCliOptions.cs", "--report-max-items <n>"),
            ("SymbolicCliJsonRequest.cs", "case \"sarif\":"),
            ("SymbolicCliJsonRequest.cs", "case \"markdown\":"),
            ("SymbolicCliJsonRequest.cs", "MaxDiagnostics"),
            ("SymbolicCliJsonRequest.cs", "MaxItems"),
            ("SymbolicCliExplainReport.cs", "public override string Kind => \"explain\""),
            ("SymbolicCompactDomainResults.cs", "public int SchemaVersion => 1"),
            ("SymbolicCliExplainReport.cs", "SymbolicCompactQueryResult Invariant"),
            ("SymbolicCliExplainReport.cs", "SymbolicCompactRuntimeHazardQueryResult RuntimeHazards"),
            ("SymbolicCliExplainReport.cs", "ToSarif()"),
            ("SymbolicCliExplainReport.cs", "ToMarkdown()"),
            ("SymbolicCliExplainReport.cs", "SPQ-REPORT-TRUNCATED"),
            ("SymbolicCliExplainReport.cs", "SymbolicCliExplainTruncation"));
    }

    [Test]
    public void SymbolicCli_ShouldExposeTypedCiExitGates()
    {
        AssertSymbolicCliSourceContains(
            ("SymbolicCliOptions.cs", "--fail-on-unproven-implies"),
            ("SymbolicCliOptions.cs", "--fail-on-capability-violation"),
            ("SymbolicCliOptions.cs", "--fail-on-capability-unknown"),
            ("SymbolicCliOptions.cs", "--fail-on-complexity-exceeded <bound>"),
            ("SymbolicCliOptions.cs", "--fail-on-complexity-unknown"),
            ("SymbolicCliOptions.cs", "--max-conservative-unknowns <n>"),
            ("SymbolicCliOptions.cs", "--fail-on-compact-truncation"),
            ("SymbolicCliOptions.cs", "--fail-on-compact-threshold <metric=max>"),
            ("SymbolicCliJsonRequest.cs", "SymbolicCliJsonGateOptions"),
            ("SymbolicCliExitGateEvaluator.cs", "SymbolicCliExitGateFailure"),
            ("SymbolicCliExitGateEvaluator.cs", "ComplexityComparison.Incomparable"));
    }

    [Test]
    public void SymbolicCliAndApi_ShouldExposeTypedErrorContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var errorSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicErrors.cs"));

        AssertSymbolicCliSourceContains(
            ("SymbolicCliOptions.cs", "--error-json"),
            ("Program.cs", "SymbolicCliErrorWriter.Write(ex, args)"),
            ("SymbolicCliErrorWriter.cs", "new SymbolicErrorEnvelope(error)"),
            ("SymbolicCliErrorWriter.cs", "error.RecommendedExitCode"));
        Assert.That(errorSource, Does.Contain("public const string InvalidRequest = \"SPQ1000\""));
        Assert.That(errorSource, Does.Contain("public const string NativeSolverUnavailable = \"SPQ2000\""));
        Assert.That(errorSource, Does.Contain("public const string Canceled = \"SPQ3000\""));
        Assert.That(errorSource, Does.Contain("public sealed class SymbolicOperationResult<T>"));
        Assert.That(errorSource, Does.Contain("public sealed class SymbolicErrorEnvelope"));
    }

    [Test]
    public void SymbolicCli_ShouldExposeSmtBudgetOptions()
    {
        var source = string.Join('\n', SymbolicCliSources.Value.Values);

        Assert.That(source, Does.Contain("--smt-mode <mode>"));
        Assert.That(source, Does.Contain("--smt-timeout-ms <n>"));
        Assert.That(source, Does.Contain("--smt-method-budget-ms <n>"));
        Assert.That(source, Does.Contain("--smt-max-path-conditions <n>"));
        Assert.That(source, Does.Contain("--smt-max-expression-nodes <n>"));
        Assert.That(source, Does.Contain("--project <path>"));
        Assert.That(source, Does.Contain("--solution <path>"));
        Assert.That(source, Does.Contain("--project-name <name>"));
        Assert.That(source, Does.Contain("--msbuild-property <name=value>"));
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
        Assert.That(source, Does.Contain("new SymbolicQueryContext("));
        Assert.That(source, Does.Contain("options.CreateRuntimeHazardOptions()"));
        Assert.That(source, Does.Contain("new SymbolicQueryContext("));
        Assert.That(source, Does.Contain("new SymbolicQueryContext("));
        Assert.That(source, Does.Contain("inputContext.SourceInput"));
        Assert.That(source, Does.Contain("options.CreateQueryTarget()"));
        Assert.That(source, Does.Contain("options.CreateRuntimeHazardTarget()"));
        Assert.That(source, Does.Contain("options.CreateComplexityTarget()"));
        Assert.That(source, Does.Contain("options.CreateCapabilityTarget()"));
        Assert.That(source, Does.Contain("options.CreateQueryOptions(smtAnalysis, true)"));
        Assert.That(source, Does.Contain("SymbolicComplexityResult"));
        Assert.That(source, Does.Contain("SymbolicCapabilityResult"));
        Assert.That(source, Does.Contain("SymbolicSourceQueryFilter"));
        Assert.That(source, Does.Contain("options.CreateSmtOptions()"));
        Assert.That(source, Does.Contain("Complexity:"));
        Assert.That(source, Does.Contain("Capabilities:"));
        Assert.That(source, Does.Contain("SharpProof explanation"));
        Assert.That(source, Does.Contain("Merged invariant"));
        Assert.That(source, Does.Contain("PrintScopedResult(result, \"Line\", options)"));
        Assert.That(source, Does.Contain("scopeLabel} merged invariant"));
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
        var catalogType = typeof(SharpProofAnalyzer).Assembly.GetType(
            "SharpProof.Analyzer.GeneratedPurityCatalog",
            true)!;
        var emptyCatalog = catalogType.GetField("Empty", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var useCurrent = catalogType.GetMethod("UseCurrent", BindingFlags.Public | BindingFlags.Static)!;
        var currentCatalog =
            catalogType.GetField("CurrentCatalog", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var valueProperty = currentCatalog.GetType().GetProperty("Value")!;

        using var scope = (IDisposable)useCurrent.Invoke(null, new[] { emptyCatalog })!;

        Assert.That(valueProperty.GetValue(currentCatalog), Is.Null,
            "An empty scoped catalog should defer to the built-in fallback instead of masking it.");
    }

    [Test]
    public void GeneratedPurityCatalog_CreateBuiltInCatalog_DoesNotLoadLooseAnalyzerDirectorySummary()
    {
        var analyzerAssemblyPath = typeof(SharpProofAnalyzer).Assembly.Location;
        var analyzerAssemblyDirectory = Path.GetDirectoryName(analyzerAssemblyPath);
        Assert.That(string.IsNullOrWhiteSpace(analyzerAssemblyDirectory), Is.False);

        var summaryPath = Path.Combine(
            analyzerAssemblyDirectory!,
            "AnalyzerPackagingTests." + Guid.NewGuid().ToString("N") + ".SharpProof.EffectSummary.json");
        var summaryJson = GeneratedPurityTestSupport.CreatePuritySummaryJson(
            typeof(Environment).Assembly.Location,
            "System.Environment.GetLogicalDrives()",
            "pure",
            "[]");

        try
        {
            File.WriteAllText(summaryPath, summaryJson);

            var catalogType = typeof(SharpProofAnalyzer).Assembly.GetType(
                "SharpProof.Analyzer.GeneratedPurityCatalog",
                true)!;
            var createBuiltInCatalog =
                catalogType.GetMethod("CreateBuiltInCatalog", BindingFlags.NonPublic | BindingFlags.Static)!;
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

            var (compilation, methodSymbol) = CompileAndResolveSingleInvocation(
                source,
                "GeneratedPurityBuiltInCatalogSmoke");

            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(builtInCatalog, args)!;
            var purityEntry = args[2];
            var classification = purityEntry == null
                ? null
                : (string?)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry);

            Assert.That(matched, Is.False,
                "Built-in generated purity should come only from embedded resources, not loose analyzer-directory summaries.");
            Assert.That(classification, Is.Null);
        }
        finally
        {
            if (File.Exists(summaryPath)) File.Delete(summaryPath);
        }
    }

    [Test]
    public void ExceptionSummaryCatalog_CreateBuiltInCatalog_DoesNotLoadLooseAnalyzerDirectorySummary()
    {
        var analyzerAssemblyPath = typeof(SharpProofAnalyzer).Assembly.Location;
        var analyzerAssemblyDirectory = Path.GetDirectoryName(analyzerAssemblyPath);
        Assert.That(string.IsNullOrWhiteSpace(analyzerAssemblyDirectory), Is.False);

        var summaryPath = Path.Combine(
            analyzerAssemblyDirectory!,
            "AnalyzerPackagingTests." + Guid.NewGuid().ToString("N") + ".SharpProof.EffectSummary.json");
        var summaryJson = GeneratedPurityTestSupport.CreatePuritySummaryJson(
                typeof(Environment).Assembly.Location,
                "System.Environment.GetEnvironmentVariable(string)",
                "pure",
                "[]")
            .Replace(@"""ThrownExceptionTypes"": [],", @"""ThrownExceptionTypes"": [ ""System.ArgumentException"" ],",
                StringComparison.Ordinal)
            .Replace(@"""TransitiveThrownExceptionTypes"": [],",
                @"""TransitiveThrownExceptionTypes"": [ ""System.ArgumentException"" ],", StringComparison.Ordinal);

        try
        {
            File.WriteAllText(summaryPath, summaryJson);

            var catalogType = typeof(SharpProofAnalyzer).Assembly.GetType(
                "SharpProof.Analyzer.ExceptionSummaryCatalog",
                true)!;
            var createBuiltInCatalog =
                catalogType.GetMethod("CreateBuiltInCatalog", BindingFlags.NonPublic | BindingFlags.Static)!;
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

            var (compilation, methodSymbol) = CompileAndResolveSingleInvocation(
                source,
                "ExceptionSummaryBuiltInCatalogSmoke");

            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetExceptions.Invoke(builtInCatalog, args)!;

            Assert.That(matched, Is.False,
                "Built-in generated exception summaries should come only from embedded resources, not loose analyzer-directory summaries.");
        }
        finally
        {
            if (File.Exists(summaryPath)) File.Delete(summaryPath);
        }
    }

    [Test]
    public void ExceptionSummaryCatalog_CreateBuiltInCatalog_IgnoresNonMatchingJsonFileNames()
    {
        var analyzerAssemblyPath = typeof(SharpProofAnalyzer).Assembly.Location;
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
            .Replace(@"""ThrownExceptionTypes"": [],", @"""ThrownExceptionTypes"": [ ""System.ArgumentException"" ],",
                StringComparison.Ordinal)
            .Replace(@"""TransitiveThrownExceptionTypes"": [],",
                @"""TransitiveThrownExceptionTypes"": [ ""System.ArgumentException"" ],", StringComparison.Ordinal);

        try
        {
            File.WriteAllText(summaryPath, summaryJson);

            var catalogType = typeof(SharpProofAnalyzer).Assembly.GetType(
                "SharpProof.Analyzer.ExceptionSummaryCatalog",
                true)!;
            var createBuiltInCatalog =
                catalogType.GetMethod("CreateBuiltInCatalog", BindingFlags.NonPublic | BindingFlags.Static)!;
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

            var (compilation, methodSymbol) = CompileAndResolveSingleInvocation(
                source,
                "ExceptionSummaryBuiltInCatalogIgnoredFileName",
                AnalyzerTestHost.GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(fixtureAssemblyPath)));

            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetExceptions.Invoke(builtInCatalog, args)!;

            Assert.That(matched, Is.False,
                "Only *.SharpProof.EffectSummary.json files should be consumed as built-in exception summaries.");
        }
        finally
        {
            if (File.Exists(summaryPath)) File.Delete(summaryPath);

            if (Directory.Exists(fixtureDirectory)) Directory.Delete(fixtureDirectory, true);
        }
    }

    [Test]
    public void AttributesPackage_ShouldUseReleaseReadyNuGetMetadata()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "SharpProof.Attributes", "SharpProof.Attributes.csproj");
        var document = XDocument.Load(projectPath);
        var properties = XDocument.Load(Path.Combine(repositoryRoot, "SharpProof.PackageMetadata.props"))
            .Descendants("PropertyGroup")
            .Elements()
            .GroupBy(element => element.Name.LocalName)
            .ToDictionary(group => group.Key, group => group.Last().Value);
        var description = document.Descendants("Description").Single().Value;

        Assert.That(properties["PackageLicenseExpression"], Is.EqualTo("MIT"));
        Assert.That(properties["PackageProjectUrl"], Is.EqualTo("$(SharpProofProjectUrl)"));
        Assert.That(properties["RepositoryUrl"], Is.EqualTo("$(SharpProofProjectUrl)"));
        Assert.That(properties["RepositoryType"], Is.EqualTo("git"));
        Assert.That(properties["PackageRequireLicenseAcceptance"], Is.EqualTo("false"));
        Assert.That(properties["PackageReadmeFile"], Is.EqualTo("README.md"));
        Assert.That(description, Does.Contain("PureExternal"));
        Assert.That(description, Does.Contain("Impure"));
    }

    [Test]
    public void CiWorkflow_ShouldRun_AllTestLanes_AndPackAllNuGetPackages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml");
        var source = File.ReadAllText(workflowPath);
        using var manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, "scripts", "package-projects.json")));
        var projects = manifest.RootElement.GetProperty("projects")
            .EnumerateArray()
            .Select(static project => project.GetString())
            .ToArray();

        Assert.That(source, Does.Contain("Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -TestLane All"));
        Assert.That(projects, Is.EqualTo(new[]
        {
            "SharpProof.Package/SharpProof.Package.csproj",
            "SharpProof.Attributes/SharpProof.Attributes.csproj",
            "SharpProof.Symbolic/SharpProof.Symbolic.csproj"
        }));
        Assert.That(source, Does.Contain("Get-Content scripts/package-projects.json -Raw | ConvertFrom-Json"));
        Assert.That(source, Does.Contain(
            "Invoke-SharpProofDotnet.ps1 pack $project --configuration Release --no-build --output nupkgs"));
        Assert.That(source, Does.Contain("SharpProof.Attributes package"));
        Assert.That(source, Does.Contain("SharpProof.Symbolic package"));
        Assert.That(source, Does.Contain("SharpProof.Symbolic/PackageBaseline.json"));
        Assert.That(source, Does.Contain("analyzers/dotnet/cs/libz3.dylib"));
        Assert.That(source, Does.Contain("buildTransitive/SharpProof.targets"));
        Assert.That(source, Does.Contain("THIRD-PARTY-NOTICES.txt"));
    }

    [Test]
    public void PackageConsumerWorkflow_ShouldCoverWindowsLinuxAndMacOsNativePolicy()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            repositoryRoot,
            ".github",
            "workflows",
            "package-consumers.yml"));
        var script = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Test-SharpProofPackageConsumers.ps1"));
        var symbolicProbe = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "package-consumers",
            "SymbolicConsumer.cs"));

        Assert.That(workflow, Does.Contain("windows-latest"));
        Assert.That(workflow, Does.Contain("ubuntu-latest"));
        Assert.That(workflow, Does.Contain("macos-15-intel"));
        Assert.That(workflow, Does.Contain("expected-smt: Required"));
        Assert.That(workflow, Does.Contain("expected-smt: Graceful"));
        Assert.That(workflow, Does.Contain("Test-SharpProofPackageConsumers.ps1"));
        Assert.That(script, Does.Contain("function Get-EvaluatedProjectProperty"));
        Assert.That(script, Does.Contain("\"-getProperty:$PropertyName\""));
        Assert.That(script, Does.Contain("'pack', $attributesProject"));
        Assert.That(script, Does.Not.Contain("SelectNodes("));
        Assert.That(script, Does.Contain("$loadFailureIds = @('AD0001', 'CS8032', 'CS8034', 'CS8785')"));
        Assert.That(script, Does.Contain("$loadFailureIds -contains $_.ruleId"));
        Assert.That(script, Does.Contain("SP0004"));
        Assert.That(script, Does.Contain("analyzer-diagnostics.sarif"));
        Assert.That(symbolicProbe, Does.Contain("SmtAnalysisHealthState.PermanentlyUnavailable"));
        Assert.That(symbolicProbe, Does.Contain("smt_native_library_missing"));
        Assert.That(symbolicProbe, Does.Contain("proofsHold"));
    }

    [Test]
    public void NuGetBuildScript_ShouldPackAllPublicPackages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "build-nuget.ps1"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "package-projects.json")));
        var projects = manifest.RootElement.GetProperty("projects")
            .EnumerateArray()
            .Select(static project => project.GetString())
            .ToArray();

        Assert.That(projects, Is.EqualTo(new[]
        {
            "SharpProof.Package/SharpProof.Package.csproj",
            "SharpProof.Attributes/SharpProof.Attributes.csproj",
            "SharpProof.Symbolic/SharpProof.Symbolic.csproj"
        }));
        Assert.That(source, Does.Contain("scripts\\package-projects.json"));
        Assert.That(File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml")),
            Does.Contain("scripts/package-projects.json"));
    }

    [Test]
    public void BuildScripts_ShouldBoundVsixMsBuildAndStageNuGetPublication()
    {
        var repositoryRoot = FindRepositoryRoot();
        var buildSource = File.ReadAllText(Path.Combine(repositoryRoot, "build.ps1"));
        var vsixSource = File.ReadAllText(Path.Combine(repositoryRoot, "build-vsix.ps1"));
        var nugetSource = File.ReadAllText(Path.Combine(repositoryRoot, "build-nuget.ps1"));

        Assert.That(buildSource, Does.Contain("$dotnetWrapper"));
        Assert.That(vsixSource, Does.Contain("$dotnetWrapper"));
        Assert.That(buildSource, Does.Not.Contain("Find-SharpProofMSBuild"));
        Assert.That(vsixSource, Does.Not.Contain("Find-SharpProofMSBuild"));
        Assert.That(nugetSource, Does.Contain("Packing NuGet packages to staging directory"));
        Assert.That(
            nugetSource.IndexOf("-o', $stagingDir", StringComparison.Ordinal),
            Is.LessThan(nugetSource.IndexOf("Remove-Item -Force", StringComparison.Ordinal)));
    }

    private static string ReadPackageVersion(string repositoryRoot)
    {
        return ReadExpandedProperty(
            Path.Combine(repositoryRoot, "SharpProof.Release.props"),
            "SharpProofPackageVersion");
    }

    private static string ReadExpandedProperty(string projectPath, string elementName)
    {
        var document = XDocument.Load(projectPath);
        var properties = document
            .Descendants("PropertyGroup")
            .Elements()
            .GroupBy(static element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last().Value.Trim(),
                StringComparer.Ordinal);
        Assert.That(properties.TryGetValue(elementName, out var value), Is.True,
            $"Expected {elementName} in project file '{projectPath}'.");

        for (var pass = 0; pass < properties.Count; pass++)
        {
            var expanded = value!;
            foreach (var property in properties)
                expanded = expanded.Replace(
                    "$(" + property.Key + ")",
                    property.Value,
                    StringComparison.Ordinal);

            if (string.Equals(expanded, value, StringComparison.Ordinal)) break;
            value = expanded;
        }

        Assert.That(value, Does.Not.Contain("$("),
            $"Expected {elementName} in '{projectPath}' to resolve from its shared properties.");
        return value!;
    }

    private static IReadOnlyDictionary<string, string> LoadSymbolicCliSources()
    {
        var cliDirectory = Path.Combine(FindRepositoryRoot(), "Tools", "SharpProof.SymbolicCli");
        return Directory.EnumerateFiles(cliDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .ToDictionary(
                static path => Path.GetFileName(path),
                File.ReadAllText,
                StringComparer.Ordinal);
    }

    private static void AssertSymbolicCliSourceContains(
        params (string FileName, string ExpectedText)[] expectations)
    {
        var sources = SymbolicCliSources.Value;
        foreach (var expectation in expectations)
            Assert.That(
                sources[expectation.FileName],
                Does.Contain(expectation.ExpectedText),
                expectation.FileName);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "SharpProof.Package"))) return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
    }

    private static string? ResolveExistingPackageArtifact(string repositoryRoot, string projectDirectoryName,
        string packageFileName)
    {
        var preferredPackagePath = Path.Combine(repositoryRoot, "nupkgs", packageFileName);
        var projectBinDirectory = Path.Combine(repositoryRoot, projectDirectoryName, "bin");
        var candidates = File.Exists(preferredPackagePath)
            ? new[] { preferredPackagePath }
            : Array.Empty<string>();
        if (Directory.Exists(projectBinDirectory))
            candidates = candidates
                .Concat(Directory.EnumerateFiles(projectBinDirectory, packageFileName, SearchOption.AllDirectories))
                .ToArray();

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
            CreateNoWindow = true
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

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
        string? editorConfigText = null,
        string? globalAnalyzerConfigText = null,
        IReadOnlyDictionary<string, string>? additionalFiles = null,
        string? additionalReferencePath = null,
        bool includeWarningsInOutput = false)
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
                File.WriteAllText(Path.Combine(projectDirectory, ".editorconfig"), editorConfigText);
            AddGlobalAnalyzerConfig(projectDirectory, globalAnalyzerConfigText);
            AddAdditionalFiles(projectDirectory, additionalFiles);
            AddAdditionalReference(projectDirectory, additionalReferencePath);

            return await RunDotnetAsync(
                projectDirectory,
                packageCache,
                "build",
                "--no-restore",
                "-p:UseSharedCompilation=false",
                "/warnaserror:SP0032",
                includeWarningsInOutput
                    ? "/clp:WarningsOnly;Summary"
                    : "/clp:ErrorsOnly;Summary").ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(probeRoot)) Directory.Delete(probeRoot, true);
        }
    }

    private static async Task<ProcessResult> RunDisposablePackageConsoleAsync(
        string packageId,
        string packageVersion,
        IReadOnlyList<string> packageSources,
        string source)
    {
        var probeRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "symbolic-package-consumer-" + Guid.NewGuid().ToString("N"));
        var packageCache = Path.Combine(probeRoot, ".nuget");

        Directory.CreateDirectory(probeRoot);
        try
        {
            var nugetConfig = new XDocument(
                new XElement("configuration",
                    new XElement("packageSources",
                        new XElement("clear"),
                        packageSources.Select((packageSource, index) =>
                            new XElement("add",
                                new XAttribute("key", "local-package-" + index),
                                new XAttribute("value", packageSource))),
                        new XElement("add",
                            new XAttribute("key", "nuget.org"),
                            new XAttribute("value", "https://api.nuget.org/v3/index.json")))));
            nugetConfig.Save(Path.Combine(probeRoot, "NuGet.Config"));

            var newResult = await RunDotnetAsync(
                probeRoot,
                packageCache,
                "new",
                "console",
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
                packageVersion).ConfigureAwait(false);
            Assert.That(addPackageResult.ExitCode, Is.EqualTo(0), addPackageResult.Output);

            File.WriteAllText(
                Path.Combine(projectDirectory, "Program.cs"),
                source,
                new UTF8Encoding(false));
            return await RunDotnetAsync(
                projectDirectory,
                packageCache,
                "run",
                "--no-restore").ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(probeRoot)) Directory.Delete(probeRoot, true);
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
            _ => new Lazy<Task<PreparedConsumerTemplate>>(() => CreatePreparedConsumerTemplateAsync(
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
        string? editorConfigText = null,
        string? globalAnalyzerConfigText = null,
        IReadOnlyDictionary<string, string>? additionalFiles = null,
        string? additionalReferencePath = null)
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
                if (File.Exists(editorConfigPath)) File.Delete(editorConfigPath);
            }
            else
            {
                File.WriteAllText(editorConfigPath, editorConfigText);
            }

            AddGlobalAnalyzerConfig(projectDirectory, globalAnalyzerConfigText);
            AddAdditionalFiles(projectDirectory, additionalFiles);
            AddAdditionalReference(projectDirectory, additionalReferencePath);

            return await RunDotnetAsync(
                projectDirectory,
                template.PackageCacheDirectory,
                "build",
                "--no-restore",
                "-p:UseSharedCompilation=false",
                "/warnaserror:SP0032",
                "/clp:ErrorsOnly;Summary").ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(probeRoot)) Directory.Delete(probeRoot, true);
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
            File.Copy(filePath, destinationPath, true);
        }
    }

    private static void AddAdditionalFiles(
        string projectDirectory,
        IReadOnlyDictionary<string, string>? additionalFiles)
    {
        if (additionalFiles == null || additionalFiles.Count == 0) return;

        var projectPath = Path.Combine(projectDirectory, "Probe.csproj");
        var project = XDocument.Load(projectPath);
        var itemGroup = new XElement(
            "ItemGroup",
            additionalFiles.Select(file =>
                new XElement(
                    "AdditionalFiles",
                    new XAttribute("Include", file.Key))));
        project.Root!.Add(itemGroup);
        project.Save(projectPath);

        foreach (var file in additionalFiles)
        {
            var filePath = Path.Combine(projectDirectory, file.Key);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(filePath, file.Value);
        }
    }

    private static void AddAdditionalReference(string projectDirectory, string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath)) return;

        var projectPath = Path.Combine(projectDirectory, "Probe.csproj");
        var project = XDocument.Load(projectPath);
        var itemGroup = new XElement(
            "ItemGroup",
            new XElement(
                "Reference",
                new XAttribute("Include", Path.GetFileNameWithoutExtension(assemblyPath)),
                new XElement("HintPath", assemblyPath),
                new XElement("Private", "false")));
        project.Root!.Add(itemGroup);
        project.Save(projectPath);
    }

    private static void AddGlobalAnalyzerConfig(
        string projectDirectory,
        string? editorConfigText)
    {
        var globalLines = string.IsNullOrWhiteSpace(editorConfigText)
            ? Array.Empty<string>()
            : editorConfigText
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.TrimEnd())
                .Where(IsGlobalAnalyzerConfigLine)
                .ToArray();
        if (globalLines.Length == 0) return;

        var projectPath = Path.Combine(projectDirectory, "Probe.csproj");
        var project = XDocument.Load(projectPath);
        project.Root!.Add(
            new XElement(
                "ItemGroup",
                new XElement(
                    "GlobalAnalyzerConfigFiles",
                    new XAttribute("Include", "SharpProof.globalconfig"))));
        project.Save(projectPath);

        File.WriteAllText(
            Path.Combine(projectDirectory, "SharpProof.globalconfig"),
            "is_global = true" + Environment.NewLine +
            string.Join(Environment.NewLine, globalLines) + Environment.NewLine);
    }

    private static string CreateAnalyzerSeverityEditorConfig(
        string[] diagnosticIds,
        string? additionalEditorConfigText = null)
    {
        var builder = new StringBuilder()
            .AppendLine("root = true");

        var additionalLines = string.IsNullOrWhiteSpace(additionalEditorConfigText)
            ? Array.Empty<string>()
            : additionalEditorConfigText
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.TrimEnd())
                .ToArray();
        builder.AppendLine()
            .AppendLine("[*.cs]");

        foreach (var diagnosticId in diagnosticIds.Distinct(StringComparer.Ordinal))
            builder.Append("dotnet_diagnostic.")
                .Append(diagnosticId)
                .AppendLine(".severity = error");

        var sectionAdditionalLines = additionalLines
            .Where(line => !IsGlobalAnalyzerConfigLine(line))
            .ToArray();
        if (sectionAdditionalLines.Length > 0)
            builder.AppendLine()
                .AppendLine(string.Join(Environment.NewLine, sectionAdditionalLines));

        return builder.ToString();
    }

    private static bool IsGlobalAnalyzerConfigLine(string line)
    {
        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0) return false;

        var key = line.Substring(0, separatorIndex).Trim();
        const string buildPropertyPrefix = "build_property.";
        if (key.StartsWith(buildPropertyPrefix, StringComparison.OrdinalIgnoreCase))
            key = key.Substring(buildPropertyPrefix.Length);

        return AnalyzerConfigurationOptionRegistry.All.Any(option =>
            option.Scope == AnalyzerConfigurationScope.GlobalOnly &&
            string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static (CSharpCompilation Compilation, IMethodSymbol MethodSymbol)
        CompileAndResolveSingleInvocation(
            string source,
            string assemblyName,
            IEnumerable<MetadataReference>? references = null)
    {
        var fixture = RoslynTestFixture.CreateSingleNode<InvocationExpressionSyntax>(
            source,
            assemblyName,
            references ?? AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpParseOptions(LanguageVersion.Preview));
        var methodSymbol = fixture.SemanticModel.GetSymbolInfo(fixture.Node).Symbol as IMethodSymbol;
        Assert.That(methodSymbol, Is.Not.Null, "The single invocation should resolve to a method.");
        return (fixture.Compilation, methodSymbol!);
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
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = File.Create(assemblyPath);
        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
            throw new AssertionException(string.Join(
                Environment.NewLine,
                emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        return (fixtureDirectory, assemblyPath);
    }

    private readonly record struct ProcessResult(int ExitCode, string Output);

    public readonly record struct ConsumerPackageScenario(
        string Name,
        string Source,
        string[] ExpectedDiagnosticIds,
        bool UsePreparedTemplate,
        string? AdditionalEditorConfigText = null,
        IReadOnlyDictionary<string, string>? AdditionalFiles = null,
        string? AdditionalReferenceAssemblyName = null,
        string? AdditionalReferenceSource = null)
    {
        public override string ToString()
        {
            return Name;
        }
    }

    private readonly record struct PreparedConsumerTemplate(
        string RootDirectory,
        string ProjectDirectory,
        string PackageCacheDirectory);
}
