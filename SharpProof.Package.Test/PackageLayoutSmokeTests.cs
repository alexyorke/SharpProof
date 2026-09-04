using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerProbe.TestAsset;
using SharpProof.Worker;

namespace SharpProof.Package.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class PackageLayoutSmokeTests
{
    internal static void DisposeSharedPackageCache()
    {
        PackageWorkspace.DisposeSharedPackageCache();
    }

    private static readonly Guid SourceLinkKind = new(
        "CC110556-A091-4D38-9FEC-25AB9A351A6A");

    private static readonly string[] ExpectedAnalyzerEntryFileNames = [
        "SharpProof.Analyzer.dll"
    ];

    private static readonly string[] ExpectedGeneratorEntryFileNames = [
        "SharpProof.ContractForGenerator.dll"
    ];

    private static readonly string[] ExpectedAnalyzerDependencyFileNames = [
        "SharpProof.Analyzer.Core.dll",
        "SharpProof.Contracts.dll",
        "SharpProof.Dataflow.dll",
        "SharpProof.Effects.dll",
        "SharpProof.Frontend.dll",
        "SharpProof.Ir.dll",
        "SharpProof.Specs.dll",
        "System.Buffers.dll",
        "System.Collections.Immutable.dll",
        "System.Memory.dll",
        "System.Numerics.Vectors.dll",
        "System.Reflection.Metadata.dll",
        "System.Runtime.CompilerServices.Unsafe.dll",
        "System.Text.Encoding.CodePages.dll",
        "System.Threading.Tasks.Extensions.dll"
    ];

    private static readonly string[] ExpectedAllocationReplayEventKinds = [
        "ManagedArrayAllocation",
        "ManagedObjectAllocation"
    ];

    private static readonly ImmutableArray<string>
        ExpectedAllocationReplayWitnessKinds = AllocationWitnessKinds.Managed;

    private static readonly string[] ExpectedCollectorEntryFileNames = [
        "SharpProof.CompilerCollector.dll"
    ];

    private static readonly string[] ExpectedSourceAnalyzerProjectFileNames = [
        "SharpProof.Attributes.csproj",
        "SharpProof.Analyzer.csproj",
        "SharpProof.ContractForGenerator.csproj",
        "SharpProof.CompilerCollector.csproj"
    ];

    private static readonly string[] ExpectedCollectorDependencyFileNames = [
        "Microsoft.Bcl.AsyncInterfaces.dll",
        "SharpProof.CompilerArtifact.dll",
        "SharpProof.Summaries.dll",
        "SharpProof.Worker.Protocol.dll",
        "System.IO.Pipelines.dll",
        "System.Text.Encodings.Web.dll",
        "System.Text.Json.dll"
    ];

    private static readonly string[] ExpectedConditionalAnalyzerEntries = [
        "tools/analyzers/dotnet/cs/SharpProof.Analyzer.dll",
        "tools/analyzers/dotnet/cs/SharpProof.ContractForGenerator.dll",
        "tools/collector/SharpProof.CompilerCollector.dll",
        "tools/collector/RelationalSpecPackCatalog.json",
        "tools/shared/netstandard2.0/Microsoft.Bcl.AsyncInterfaces.dll",
        "tools/shared/netstandard2.0/SharpProof.Analyzer.Core.dll",
        "tools/shared/netstandard2.0/SharpProof.CompilerArtifact.dll",
        "tools/shared/netstandard2.0/SharpProof.Contracts.dll",
        "tools/shared/netstandard2.0/SharpProof.Dataflow.dll",
        "tools/shared/netstandard2.0/SharpProof.Effects.dll",
        "tools/shared/netstandard2.0/SharpProof.Frontend.dll",
        "tools/shared/netstandard2.0/SharpProof.Ir.dll",
        "tools/shared/netstandard2.0/SharpProof.Specs.dll",
        "tools/shared/netstandard2.0/SharpProof.Summaries.dll",
        "tools/shared/netstandard2.0/SharpProof.Worker.Protocol.dll",
        "tools/shared/netstandard2.0/System.Buffers.dll",
        "tools/shared/netstandard2.0/System.Collections.Immutable.dll",
        "tools/shared/netstandard2.0/System.IO.Pipelines.dll",
        "tools/shared/netstandard2.0/System.Memory.dll",
        "tools/shared/netstandard2.0/System.Numerics.Vectors.dll",
        "tools/shared/netstandard2.0/System.Reflection.Metadata.dll",
        "tools/shared/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll",
        "tools/shared/netstandard2.0/System.Text.Encoding.CodePages.dll",
        "tools/shared/netstandard2.0/System.Text.Encodings.Web.dll",
        "tools/shared/netstandard2.0/System.Text.Json.dll",
        "tools/shared/netstandard2.0/System.Threading.Tasks.Extensions.dll"
    ];

    private static readonly string[] ExpectedToolEntries = [
        "tools/net9/Microsoft.Z3.dll",
        "tools/net9/SharpProof.BuildTasks.deps.json",
        "tools/net9/SharpProof.BuildTasks.dll",
        "tools/net9/SharpProof.BuildTasks.runtimeconfig.json",
        "tools/net9/SharpProof.CompilerArtifact.dll",
        "tools/net9/SharpProof.Dataflow.dll",
        "tools/net9/SharpProof.Host.dll",
        "tools/net9/SharpProof.Ir.dll",
        "tools/net9/SharpProof.Smt.dll",
        "tools/net9/SharpProof.Specs.dll",
        "tools/net9/SharpProof.Verify.dll",
        "tools/net9/SharpProof.Worker.deps.json",
        "tools/net9/SharpProof.Worker.dll",
        "tools/net9/SharpProof.Worker.Launcher.deps.json",
        "tools/net9/SharpProof.Worker.Launcher.dll",
        "tools/net9/SharpProof.Worker.Launcher.runtimeconfig.json",
        "tools/net9/SharpProof.Worker.Protocol.dll",
        "tools/net9/SharpProof.Worker.runtimeconfig.json",
        "tools/net9/System.Collections.Immutable.dll",
        "tools/net9/System.IO.Pipelines.dll",
        "tools/net9/System.Text.Encodings.Web.dll",
        "tools/net9/System.Text.Json.dll"
    ];

    private static readonly string[] ExpectedNativeZ3Entries = [
        "tools/native/linux-x64/libz3.so"
    ];
    private static readonly string[] ExpectedDependencyAttributes = [
        "id",
        "version"
    ];

    [Test]
    public async Task PackageGraphAndLayoutsAreExact()
    {
        var feed = await PackagedProductFeed.GetAsync();

        VerifyPackageGraph(feed);
        VerifyPackageLayouts(feed);
    }

    [Test]
    public async Task VerifierNativeToolDoesNotBecomeApplicationRuntimeAsset()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WriteRuntimeAssetIsolationConsumer(feed.Version);
        var restore = await RestoreConsumerAsync(workspace, feed);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var assetsPath = Path.Combine(
            workspace.ConsumerDirectory,
            "obj",
            "project.assets.json");
        using (var assets = JsonDocument.Parse(
                   await File.ReadAllTextAsync(assetsPath)))
        {
            foreach (var target in assets.RootElement
                         .GetProperty("targets")
                         .EnumerateObject())
            {
                foreach (var library in target.Value.EnumerateObject())
                {
                    foreach (var assetKind in new[] {
                                 "native", "runtime", "runtimeTargets"
                             })
                    {
                        if (!library.Value.TryGetProperty(
                                assetKind,
                                out var assetsByPath))
                        {
                            continue;
                        }
                        Assert.That(
                            assetsByPath.EnumerateObject()
                                .Select(static asset => asset.Name),
                            Has.None.EndsWith("libz3.so"),
                            target.Name + "/" + library.Name + "/" + assetKind);
                    }
                }
            }
        }

        var runtimeItems = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            workspace.ConsumerProject,
            "-t:CaptureRuntimeAssets",
            "--nologo",
            "/nodeReuse:false");
        Assert.That(runtimeItems.ExitCode, Is.Zero, runtimeItems.Output);
        Assert.That(
            await File.ReadAllTextAsync(Path.Combine(
                workspace.ConsumerDirectory,
                "obj",
                "runtime-assets.txt")),
            Does.Not.Contain("libz3.so"));

        var build = await BuildAnalyzerConsumerAsync(workspace);
        Assert.That(build.ExitCode, Is.Zero, build.Output);
        var publish = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "publish",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false");
        Assert.That(publish.ExitCode, Is.Zero, publish.Output);
        Assert.That(
            Directory.EnumerateFiles(
                workspace.ConsumerDirectory,
                "libz3.so",
                SearchOption.AllDirectories),
            Is.Empty);
    }

    [Test]
    public async Task StrictAnalyzerSetDiscoversEachEntrypointOnce()
    {
        var feed = await PackagedProductFeed.GetAsync();
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-analyzer-discovery-");
        try
        {
            ZipFile.ExtractToDirectory(
                feed.GetPackagePath(PackagedProductFeed.PortablePackageId),
                directory.FullName);
            var analyzerDirectory = Path.Combine(
                directory.FullName,
                "tools",
                "analyzers",
                "dotnet",
                "cs");
            var collectorDirectory = Path.Combine(
                directory.FullName,
                "tools",
                "collector");
            var sharedDirectory = Path.Combine(
                directory.FullName,
                "tools",
                "shared",
                "netstandard2.0");
            using var loader = new PackageAnalyzerAssemblyLoader();
            foreach (var dependency in Directory.EnumerateFiles(
                         directory.FullName,
                         "*.dll",
                         SearchOption.AllDirectories))
            {
                loader.AddDependencyLocation(dependency);
            }

            var failures = new List<string>();
            var analyzers = new List<DiagnosticAnalyzer>();
            var generators = new List<ISourceGenerator>();
            foreach (var path in new[]
                     {
                         Path.Combine(
                             analyzerDirectory,
                             "SharpProof.Analyzer.dll"),
                         Path.Combine(
                             analyzerDirectory,
                             "SharpProof.ContractForGenerator.dll"),
                         Path.Combine(
                             collectorDirectory,
                             "SharpProof.CompilerCollector.dll"),
                         Path.Combine(
                             sharedDirectory,
                             "SharpProof.Analyzer.Core.dll")
                     })
            {
                var reference = new AnalyzerFileReference(path, loader);
                reference.AnalyzerLoadFailed += (_, args) => failures.Add(
                    args.ErrorCode + ": " + args.Message);
                analyzers.AddRange(reference.GetAnalyzers(
                    LanguageNames.CSharp));
                generators.AddRange(reference.GetGenerators(
                    LanguageNames.CSharp));
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failures, Is.Empty);
                Assert.That(
                    analyzers.Count(analyzer =>
                        string.Equals(
                            analyzer.GetType().FullName,
                            "SharpProof.Analyzer.SharpProofAnalyzer",
                            StringComparison.Ordinal)),
                    Is.EqualTo(1));
                Assert.That(
                    analyzers.Count(analyzer =>
                        string.Equals(
                            analyzer.GetType().FullName,
                            "SharpProof.CompilerCollector.FinalCompilationCollectorAnalyzer",
                            StringComparison.Ordinal)),
                    Is.EqualTo(1));
                Assert.That(
                    analyzers,
                    Has.Count.EqualTo(2));
                Assert.That(
                    generators,
                    Has.Count.EqualTo(1));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task SymbolPackagesAreExactPortableAndSourceLinked()
    {
        var feed = await PackagedProductFeed.GetAsync();
        var repositoryRoot = TestRepository.FindRoot();
        var revision = await RunProcessAsync(
            repositoryRoot,
            "git",
            "rev-parse",
            "HEAD");
        Assert.That(revision.ExitCode, Is.Zero, revision.Output);
        var commit = revision.Output.Trim();
        Assert.That(
            commit,
            Does.Match("^[0-9a-f]{40}$"));

        foreach (var package in feed.Packages)
        {
            var symbolPackagePath =
                feed.GetSymbolPackagePath(package.Id);
            VerifyRepositoryMetadata(package.Path, commit);
            VerifyRepositoryMetadata(symbolPackagePath, commit);
            VerifySymbolPackagePair(
                package.Path,
                symbolPackagePath,
                package.Id + ".nuspec",
                commit);
        }
    }

    [Test]
    public async Task ReleaseEvidenceIsDeterministicAndComplete()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = ReleaseEvidenceWorkspace.Create();
        var script = Path.Combine(
            TestRepository.FindRoot(),
            "scripts",
            "New-SharpProofReleaseEvidence.ps1");
        var arguments = new[] {
            "-NoLogo",
            "-NoProfile",
            "-File",
            script,
            "-PackageSource",
            feed.Source,
            "-OutputDirectory",
            workspace.OutputDirectory
        };
        var firstRun = await RunProcessAsync(
            TestRepository.FindRoot(),
            "pwsh",
            arguments);
        Assert.That(firstRun.ExitCode, Is.Zero, firstRun.Output);
        var firstManifest = await File.ReadAllBytesAsync(
            workspace.ManifestPath);
        var secondRun = await RunProcessAsync(
            TestRepository.FindRoot(),
            "pwsh",
            arguments);
        Assert.That(secondRun.ExitCode, Is.Zero, secondRun.Output);
        Assert.That(
            await File.ReadAllBytesAsync(workspace.ManifestPath),
            Is.EqualTo(firstManifest));
        Assert.That(
            firstManifest.Take(3),
            Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.That(Encoding.UTF8.GetString(firstManifest), Does.Not.Contain('\r'));

        using var document = JsonDocument.Parse(firstManifest);
        var root = document.RootElement;
        Assert.That(
            root.GetProperty("schemaVersion").GetInt32(),
            Is.EqualTo(2));
        Assert.That(
            root.GetProperty("packageVersion").GetString(),
            Is.EqualTo(feed.Version));
        var artifacts = root.GetProperty("artifacts")
            .EnumerateArray()
            .ToArray();
        Assert.That(artifacts, Has.Length.EqualTo(6));
        Assert.That(
            artifacts.Select(static artifact =>
                artifact.GetProperty("kind").GetString()),
            Is.EquivalentTo([
                "package",
                "package",
                "package",
                "symbols",
                "symbols",
                "symbols"
            ]));
        foreach (var artifact in artifacts)
        {
            var fileName = artifact.GetProperty("fileName").GetString() ??
                throw new InvalidDataException(
                    "Release artifact fileName is null.");
            var path = Path.Combine(feed.Source, fileName);
            Assert.That(
                artifact.GetProperty("bytes").GetInt64(),
                Is.EqualTo(new FileInfo(path).Length),
                fileName);
        }
        var thirdPartyComponents = root
            .GetProperty("thirdPartyComponents")
            .EnumerateArray()
            .ToArray();
        Assert.That(thirdPartyComponents, Has.Length.EqualTo(17));
        Assert.That(
            thirdPartyComponents.Select(static component =>
                component.GetProperty("license").GetString()),
            Is.All.EqualTo("MIT"));
        Assert.That(
            thirdPartyComponents.Select(static component =>
                component.GetProperty("packageId").GetString())
                .Distinct(StringComparer.Ordinal),
            Is.EquivalentTo([
                PackagedProductFeed.PortablePackageId,
                PackagedProductFeed.VerifierPackageId
            ]));
        var validationScript = Path.Combine(
            TestRepository.FindRoot(),
            "scripts",
            "Test-SharpProofReleaseArtifacts.ps1");
        var validation = await RunProcessAsync(
            TestRepository.FindRoot(),
            "pwsh",
            [
                "-NoLogo",
                "-NoProfile",
                "-File",
                validationScript,
                "-PackageSource",
                workspace.OutputDirectory,
                "-ExpectedTag",
                "v" + feed.Version
            ]);
        Assert.That(validation.ExitCode, Is.Zero, validation.Output);
    }

    [Test]
    public async Task PortablePackageRunsAdvisoryAndRequiresVerifier()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WriteConsumer(feed.Version, PackagedProductFeed.PortablePackageId);
        var restore = await RestoreConsumerAsync(workspace, feed);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var unsupportedCompiler = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            workspace.ConsumerProject,
            "-t:_SharpProofValidateConfiguration",
            "-p:NETCoreSdkVersion=9.0.200",
            "--nologo");
        Assert.That(
            unsupportedCompiler.ExitCode,
            Is.Not.Zero,
            unsupportedCompiler.Output);
        Assert.That(
            unsupportedCompiler.Output,
            Does.Contain("requires .NET SDK 9.0.300 or newer")
                .And.Contain("Roslyn 4.14 or newer"));
        var disabledOnUnsupportedCompiler = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            workspace.ConsumerProject,
            "-t:_SharpProofValidateConfiguration",
            "-p:SharpProofProfile=off",
            "-p:NETCoreSdkVersion=9.0.200",
            "--nologo");
        Assert.That(
            disabledOnUnsupportedCompiler.ExitCode,
            Is.Zero,
            disabledOnUnsupportedCompiler.Output);

        var runtimeContracts = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            workspace.ConsumerProject,
            "-t:_SharpProofValidateConfiguration",
            "-p:DefineConstants=SHARPPROOF_CONTRACTS",
            "--nologo");
        Assert.That(
            runtimeContracts.ExitCode,
            Is.Not.Zero,
            runtimeContracts.Output);
        Assert.That(
            runtimeContracts.Output,
            Does.Contain(
                "SHARPPROOF_CONTRACTS enables runtime evaluation of ghost contracts"));
        var disabledRuntimeContracts = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            workspace.ConsumerProject,
            "-t:_SharpProofValidateConfiguration",
            "-p:SharpProofProfile=off",
            "-p:DefineConstants=SHARPPROOF_CONTRACTS",
            "--nologo");
        Assert.That(
            disabledRuntimeContracts.ExitCode,
            Is.Zero,
            disabledRuntimeContracts.Output);

        var disabledItems = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            workspace.ConsumerProject,
            "-getItem:Analyzer",
            "-p:SharpProofProfile=off",
            "--nologo");
        Assert.That(disabledItems.ExitCode, Is.Zero, disabledItems.Output);
        Assert.That(
            GetPackagedAnalyzerItems(disabledItems.Output),
            Is.Empty);
        var enabledItems = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            workspace.ConsumerProject,
            "-getItem:Analyzer",
            "--nologo");
        Assert.That(enabledItems.ExitCode, Is.Zero, enabledItems.Output);
        Assert.That(
            enabledItems.Output,
            Does.Contain("SharpProof.Analyzer.dll")
                .And.Contain("SharpProof.ContractForGenerator.dll"));
        var packagedAnalyzerItems =
            GetPackagedAnalyzerItems(enabledItems.Output);
        Assert.That(
            packagedAnalyzerItems
                .Where(static item => item.Role == "EntryPoint")
                .Select(static item => item.FileName),
            Is.EquivalentTo(ExpectedAnalyzerEntryFileNames));
        Assert.That(
            packagedAnalyzerItems
                .Where(static item => item.Role == "Generator")
                .Select(static item => item.FileName),
            Is.EquivalentTo(ExpectedGeneratorEntryFileNames));
        Assert.That(
            packagedAnalyzerItems
                .Where(static item => item.Role == "Dependency")
                .Select(static item => item.FileName),
            Is.EquivalentTo(ExpectedAnalyzerDependencyFileNames));

        var analyzerBuild = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "build",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false");
        Assert.That(analyzerBuild.ExitCode, Is.Zero, analyzerBuild.Output);
        Assert.That(analyzerBuild.Output, Does.Contain("SP0045"));

        var explicitVerification = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "build",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false",
            "-p:SharpProofVerify=true");
        Assert.That(
            explicitVerification.ExitCode,
            Is.Not.Zero,
            explicitVerification.Output);
        Assert.That(
            explicitVerification.Output,
            Does.Contain(
                "requires the matching SharpProof.Verifier package"));

        var strict = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "build",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false",
            "-p:SharpProofProfile=strict");
        Assert.That(strict.ExitCode, Is.Not.Zero, strict.Output);
        Assert.That(
            strict.Output,
            Does.Contain(
                "requires the matching SharpProof.Verifier package"));
    }

    [Test]
    public async Task CollectorAnalyzerItemsFollowVerificationPolicy()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WritePassingVerifierConsumer(
            feed.Version,
            PackagedProductFeed.VerifierPackageId);
        var restore = await RestoreConsumerAsync(workspace, feed);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        AssertPackagedAnalyzerItems(
            await EvaluatePackagedAnalyzerItemsAsync(workspace),
            includePortable: true,
            includeCollector: false);
        AssertPackagedAnalyzerItems(
            await EvaluatePackagedAnalyzerItemsAsync(
                workspace,
                ("SharpProofVerify", "true")),
            includePortable: true,
            includeCollector: true);
        AssertPackagedAnalyzerItems(
            await EvaluatePackagedAnalyzerItemsAsync(
                workspace,
                ("SharpProofProfile", "strict")),
            includePortable: true,
            includeCollector: true);
        AssertPackagedAnalyzerItems(
            await EvaluatePackagedAnalyzerItemsAsync(
                workspace,
                ("SharpProofProfile", "off"),
                ("SharpProofVerify", "true")),
            includePortable: false,
            includeCollector: false);
        AssertPackagedAnalyzerItems(
            await EvaluatePackagedAnalyzerItemsAsync(
                workspace,
                ("SharpProofVerify", "true"),
                ("DesignTimeBuild", "true")),
            includePortable: true,
            includeCollector: false);
        AssertPackagedAnalyzerItems(
            await EvaluatePackagedAnalyzerItemsAsync(
                workspace,
                ("SharpProofVerify", "true"),
                ("_SharpProofVerifierHostSupported", "false")),
            includePortable: true,
            includeCollector: true);

        var unsupported = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "build",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false",
            "-p:SharpProofVerify=true",
            "-p:_SharpProofVerifierHostSupported=false");
        Assert.That(unsupported.ExitCode, Is.Not.Zero, unsupported.Output);
        Assert.That(
            unsupported.Output,
            Does.Contain(
                "supported only by Core MSBuild inside the canonical Linux amd64 container"));
    }

    [Test]
    public async Task SourceConsumerAnalyzerItemsFollowVerificationPolicy()
    {
        using var workspace = PackageWorkspace.Create();

        await AssertSourceConsumerAnalyzerItemsAsync(
            workspace,
            includePortable: true,
            includeCollector: false);
        await AssertSourceConsumerAnalyzerItemsAsync(
            workspace,
            includePortable: true,
            includeCollector: true,
            ("SharpProofVerify", "true"));
        await AssertSourceConsumerAnalyzerItemsAsync(
            workspace,
            includePortable: true,
            includeCollector: true,
            ("SharpProofProfile", "strict"));
        await AssertSourceConsumerAnalyzerItemsAsync(
            workspace,
            includePortable: false,
            includeCollector: false,
            ("SharpProofProfile", "off"),
            ("SharpProofVerify", "true"));
        await AssertSourceConsumerAnalyzerItemsAsync(
            workspace,
            includePortable: true,
            includeCollector: false,
            ("SharpProofVerify", "true"),
            ("DesignTimeBuild", "true"));
        await AssertSourceConsumerAnalyzerItemsAsync(
            workspace,
            includePortable: true,
            includeCollector: true,
            ("SharpProofVerify", "true"),
            ("_SharpProofVerifierHostSupported", "false"));
    }

    [Test]
    [Platform("Linux")]
    public async Task AnalyzerAndProjectIncludesPreserveSemicolonsInPaths()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Package.Semicolon.Test",
            Guid.NewGuid().ToString("N"));
        try
        {
            var repository = TestRepository.FindRoot();
            var packageRoot = Directory.CreateDirectory(
                Path.Combine(root, "package;layout"));
            var packageBuild = Directory.CreateDirectory(
                Path.Combine(packageRoot.FullName, "buildTransitive"));
            foreach (var fileName in new[] {
                         "SharpProof.ConsumerContract.props",
                         "SharpProof.props",
                         "SharpProof.targets"
                     })
            {
                File.Copy(
                    Path.Combine(
                        repository,
                        "SharpProof.Package",
                        "buildTransitive",
                        fileName),
                    Path.Combine(packageBuild.FullName, fileName));
            }

            var packageProject = Path.Combine(root, "PackageConsumer.csproj");
            var configuredPackageRoot = Path.Combine(
                root,
                "configured;package");
            await File.WriteAllTextAsync(
                packageProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <SharpProofVerify>true</SharpProofVerify>
                    <SharpProofAnalyzerDirectory>{SecurityElement.Escape(Path.Combine(configuredPackageRoot, "analyzers"))}</SharpProofAnalyzerDirectory>
                    <SharpProofCollectorDirectory>{SecurityElement.Escape(Path.Combine(configuredPackageRoot, "collector"))}</SharpProofCollectorDirectory>
                    <_SharpProofSharedDirectory>{SecurityElement.Escape(Path.Combine(configuredPackageRoot, "shared"))}</_SharpProofSharedDirectory>
                  </PropertyGroup>
                  <Import Project="{EscapeMsBuildImportPath(Path.Combine(packageBuild.FullName, "SharpProof.props"))}" />
                  <Import Project="{EscapeMsBuildImportPath(Path.Combine(packageBuild.FullName, "SharpProof.targets"))}" />
                </Project>
                """,
                new UTF8Encoding(false));
            var packageEvaluation = await RunDotNetAsync(
                root,
                "msbuild",
                packageProject,
                "-getItem:Analyzer",
                "--nologo");
            Assert.That(
                packageEvaluation.ExitCode,
                Is.Zero,
                packageEvaluation.Output);
            var packageAnalyzers = GetEvaluatedItemIdentities(
                packageEvaluation.Output,
                "Analyzer",
                "SharpProofAnalyzerRole");

            var sourceRoot = Directory.CreateDirectory(
                Path.Combine(root, "source;tree"));
            var sourceProps = Path.Combine(
                sourceRoot.FullName,
                "SharpProof.AnalyzerConsumer.props");
            File.Copy(
                Path.Combine(repository, "SharpProof.AnalyzerConsumer.props"),
                sourceProps);
            var sourcePackageBuild = Directory.CreateDirectory(Path.Combine(
                sourceRoot.FullName,
                "SharpProof.Package",
                "buildTransitive"));
            File.Copy(
                Path.Combine(
                    repository,
                    "SharpProof.Package",
                    "buildTransitive",
                    "SharpProof.ConsumerContract.props"),
                Path.Combine(
                    sourcePackageBuild.FullName,
                    "SharpProof.ConsumerContract.props"));
            var sourceProject = Path.Combine(root, "SourceConsumer.csproj");
            await File.WriteAllTextAsync(
                sourceProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <SharpProofVerify>true</SharpProofVerify>
                  </PropertyGroup>
                  <Import Project="{EscapeMsBuildImportPath(sourceProps)}" />
                </Project>
                """,
                new UTF8Encoding(false));
            var sourceEvaluation = await RunDotNetAsync(
                root,
                "msbuild",
                sourceProject,
                "-getItem:Analyzer;ProjectReference",
                "--nologo");
            Assert.That(
                sourceEvaluation.ExitCode,
                Is.Zero,
                sourceEvaluation.Output);
            var sourceAnalyzers = GetEvaluatedItemIdentities(
                sourceEvaluation.Output,
                "Analyzer")
                .Where(path =>
                    ExpectedAnalyzerDependencyFileNames.Contains(
                        Path.GetFileName(path),
                        StringComparer.Ordinal) ||
                    ExpectedCollectorDependencyFileNames.Contains(
                        Path.GetFileName(path),
                        StringComparer.Ordinal))
                .ToArray();
            var sourceProjects = GetEvaluatedItemIdentities(
                sourceEvaluation.Output,
                "ProjectReference");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    packageAnalyzers.Select(Path.GetFileName),
                    Is.EquivalentTo(
                        ExpectedAnalyzerEntryFileNames
                            .Concat(ExpectedGeneratorEntryFileNames)
                            .Concat(ExpectedAnalyzerDependencyFileNames)
                            .Concat(ExpectedCollectorEntryFileNames)
                            .Concat(ExpectedCollectorDependencyFileNames)));
                Assert.That(
                    packageAnalyzers,
                    Has.All.Contains("configured;package"));
                Assert.That(
                    sourceAnalyzers.Select(Path.GetFileName),
                    Is.EquivalentTo(
                        ExpectedAnalyzerDependencyFileNames.Concat(
                            ExpectedCollectorDependencyFileNames)));
                Assert.That(
                    sourceProjects.Select(Path.GetFileName),
                    Is.EquivalentTo(ExpectedSourceAnalyzerProjectFileNames));
                Assert.That(
                    sourceAnalyzers.Concat(sourceProjects),
                    Has.All.Contains("source;tree"));
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task SourceConsumerAnalyzerDependenciesUseMappedConfiguration()
    {
        using var workspace = PackageWorkspace.Create();
        var solution = workspace.WriteMappedSourceConsumerSolution();

        var evaluation = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            solution,
            "-target:Consumer:CaptureMappedAnalyzerItems",
            "-property:Configuration=Debug",
            "-property:Platform=Any CPU",
            "-property:DesignTimeBuild=true",
            "--nologo");

        Assert.That(evaluation.ExitCode, Is.Zero, evaluation.Output);
        Assert.That(
            await File.ReadAllLinesAsync(
                workspace.MappedProjectConfigurationsPath),
            Does.Contain("SharpProof.Analyzer|Release")
                .And.Contain("SharpProof.ContractForGenerator|Release"));

        var dependencyPaths = (await File.ReadAllLinesAsync(
                workspace.MappedAnalyzerItemsPath))
            .Where(path => ExpectedAnalyzerDependencyFileNames.Contains(
                Path.GetFileName(path),
                StringComparer.Ordinal))
            .ToArray();
        Assert.That(
            dependencyPaths.Select(Path.GetFileName),
            Is.EquivalentTo(ExpectedAnalyzerDependencyFileNames));
        Assert.That(
            dependencyPaths,
            Has.All.Matches<string>(path => path.Contains(
                Path.Combine("bin", "Release", "netstandard2.0"),
                StringComparison.Ordinal)));
    }

    [TestCase(
        "SharpProofMode",
        "contracts",
        "SharpProofMode was removed before preview.1")]
    [TestCase(
        "SharpProofPortableAnalyzerPath",
        "legacy.dll",
        "SharpProofPortableAnalyzerPath was removed before preview.1")]
    public async Task SourceConsumerRejectsRetiredConfiguration(
        string property,
        string value,
        string expectedMessage)
    {
        using var workspace = PackageWorkspace.Create();
        workspace.WriteSourceConsumerEvaluationProject((property, value));

        var validation = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            workspace.ConsumerProject,
            "-target:_SharpProofValidateConsumerConfiguration",
            "--nologo");

        Assert.That(validation.ExitCode, Is.Not.Zero, validation.Output);
        Assert.That(validation.Output, Does.Contain(expectedMessage));
    }

    [TestCase("netstandard2.0")]
    [TestCase("net472")]
    public async Task PortablePackageBuildsFrameworkConsumerFromIsolatedFeed(
        string targetFramework)
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WriteFrameworkConsumer(feed.Version, targetFramework);
        var restore = await RestoreConsumerAsync(
            workspace,
            feed,
            includeNetStandardFrameworkPackages:
                targetFramework == "netstandard2.0",
            includeNet472ReferenceAssemblies:
                targetFramework == "net472");
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var enabledItems = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            workspace.ConsumerProject,
            "-getItem:Analyzer",
            "--nologo");
        Assert.That(enabledItems.ExitCode, Is.Zero, enabledItems.Output);
        var packagedAnalyzerItems =
            GetPackagedAnalyzerItems(enabledItems.Output);
        Assert.That(
            packagedAnalyzerItems
                .Where(static item => item.Role == "EntryPoint")
                .Select(static item => item.FileName),
            Is.EquivalentTo(ExpectedAnalyzerEntryFileNames));
        Assert.That(
            packagedAnalyzerItems
                .Where(static item => item.Role == "Generator")
                .Select(static item => item.FileName),
            Is.EquivalentTo(ExpectedGeneratorEntryFileNames));

        // net472 qualification is intentionally build-only: the package
        // supplies compiler analyzers, but this test never executes the
        // resulting .NET Framework consumer assembly.
        var build = await BuildAnalyzerConsumerAsync(workspace);
        Assert.That(build.ExitCode, Is.Zero, build.Output);
    }

    [Test]
    public async Task VerifierPackageTransitivelySuppliesPortableProduct()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WritePassingVerifierConsumer(
            feed.Version,
            PackagedProductFeed.VerifierPackageId);
        var restore = await RestoreConsumerAsync(workspace, feed);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var enabledItems = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            workspace.ConsumerProject,
            "-getItem:Analyzer",
            "--nologo");
        Assert.That(enabledItems.ExitCode, Is.Zero, enabledItems.Output);
        var packagedAnalyzerItems =
            GetPackagedAnalyzerItems(enabledItems.Output);
        Assert.That(
            packagedAnalyzerItems
                .Where(static item => item.Role == "EntryPoint")
                .Select(static item => item.FileName),
            Is.EquivalentTo(ExpectedAnalyzerEntryFileNames));
        Assert.That(
            packagedAnalyzerItems
                .Where(static item => item.Role == "Generator")
                .Select(static item => item.FileName),
            Is.EquivalentTo(ExpectedGeneratorEntryFileNames));
        Assert.That(
            packagedAnalyzerItems
                .Where(static item => item.Role == "Dependency")
                .Select(static item => item.FileName),
            Is.EquivalentTo(ExpectedAnalyzerDependencyFileNames));

        var advisory = await BuildAnalyzerConsumerAsync(workspace);
        Assert.That(advisory.ExitCode, Is.Zero, advisory.Output);
        Assert.That(advisory.Output, Does.Not.Contain("SP0045"));

        var verification = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "build",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false",
            "-p:SharpProofVerify=true");
        Assert.That(
            verification.ExitCode,
            Is.Zero,
            verification.Output);
        Assert.That(
            verification.Output,
            Does.Contain("SharpProof Proven"));
        Assert.That(File.Exists(workspace.ResultPath), Is.True);
    }

    [Test]
    public async Task PackagedVerifierReplaysObjectAndArrayAllocationEffects()
    {
        TestRepository.RequireCanonicalContainer();
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WriteEffectReplayVerifierConsumer(feed.Version);
        var restore = await RestoreConsumerAsync(workspace, feed);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var verification = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "build",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false",
            "-p:SharpProofVerify=true",
            "-p:SharpProofVerifyCacheEnabled=true");
        Assert.That(
            verification.ExitCode,
            Is.Not.Zero,
            verification.Output);
        Assert.That(
            verification.Output,
            Does.Contain("SharpProof Refuted")
                .And.Contain("failed with exit code 5"));
        Assert.That(
            File.Exists(workspace.ResultPath),
            Is.True,
            verification.Output);
        Assert.That(
            File.Exists(workspace.CompilerManifestPath),
            Is.True,
            verification.Output);

        using (var manifest = JsonDocument.Parse(
                   await File.ReadAllTextAsync(
                       workspace.CompilerManifestPath)))
        {
            Assert.That(
                manifest.RootElement
                    .GetProperty("schemaVersion")
                    .GetInt32(),
                Is.EqualTo(CompilerManifestArtifactVersions.Current));
            var effectClaims = manifest.RootElement
                .GetProperty("callables")
                .EnumerateArray()
                .SelectMany(static callable => callable
                    .GetProperty("effectClaims")
                    .EnumerateArray()
                    .Select(claim => new
                    {
                        CallableId = callable
                            .GetProperty("callableId")
                            .GetString() ?? string.Empty,
                        Claim = claim
                    }))
                .ToArray();
            Assert.That(effectClaims, Has.Length.EqualTo(2));
            Assert.That(
                effectClaims.Select(static item => item.Claim
                    .GetProperty("claimId")
                    .GetString()),
                Is.Unique.And.All.Not.Empty);
            var expectedClaims = new[]
            {
                (Callable: "AllocateArray",
                    Event: "ManagedArrayAllocation"),
                (Callable: "AllocateObject",
                    Event: "ManagedObjectAllocation")
            };
            var eventKinds = expectedClaims
                .Select(expected =>
                {
                    var item = effectClaims.Single(candidate =>
                        candidate.CallableId.Contains(
                            expected.Callable,
                            StringComparison.Ordinal));
                    var claim = item.Claim;
                    var replay = claim.GetProperty("replay");
                    Assert.That(
                        replay.ValueKind,
                        Is.EqualTo(JsonValueKind.Object),
                        item.CallableId + ":" + claim.GetRawText());
                    var events = replay
                        .GetProperty("events")
                        .EnumerateArray()
                        .ToArray();
                    Assert.That(
                        events,
                        Has.Length.EqualTo(1),
                        item.CallableId);
                    var eventKind = events[0]
                        .GetProperty("kind")
                        .GetString();
                    Assert.That(
                        eventKind,
                        Is.EqualTo(expected.Event),
                        item.CallableId);
                    return eventKind;
                })
                .OrderBy(static kind => kind, StringComparer.Ordinal)
                .ToArray();
            Assert.That(
                eventKinds,
                Is.EqualTo(ExpectedAllocationReplayEventKinds));
        }

        using var result = JsonDocument.Parse(
            await File.ReadAllTextAsync(workspace.ResultPath));
        Assert.That(
            result.RootElement.GetProperty("runStatus").GetString(),
            Is.EqualTo("Complete"));
        Assert.That(
            result.RootElement
                .GetProperty("summary")
                .GetProperty("cacheStatus")
                .GetString(),
            Is.EqualTo("Miss"));
        var claims = result.RootElement
            .GetProperty("claimResults")
            .EnumerateArray()
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claims, Has.Length.EqualTo(2));
            Assert.That(
                claims.Select(static claim =>
                    claim.GetProperty("outcome").GetString()),
                Is.All.EqualTo("Refuted"));
            Assert.That(
                claims.Select(static claim =>
                    claim.GetProperty("effectCertainty").GetString()),
                Is.All.EqualTo("DefiniteViolation"));
            Assert.That(
                claims.Select(static claim => claim
                        .GetProperty("effectWitness")
                        .GetProperty("kind")
                        .GetString())
                    .OrderBy(static kind => kind, StringComparer.Ordinal),
                Is.EqualTo(ExpectedAllocationReplayWitnessKinds));
        }
    }

    [Test]
    public async Task PackagedVerifierPreservesLinkedAndMappedLocationsInSarif()
    {
        TestRepository.RequireCanonicalContainer();
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WriteLinkedMappedVerifierConsumer(feed.Version);
        var restore = await RestoreConsumerAsync(workspace, feed);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var verification = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "build",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false",
            "-p:SharpProofVerify=true",
            "-p:SharpProofVerifyCacheEnabled=false",
            "-p:SharpProofVerifySarifFile=" + workspace.SarifPath);
        Assert.That(verification.ExitCode, Is.Not.Zero);
        Assert.That(
            verification.Output,
            Does.Contain("SharpProof Refuted")
                .And.Contain("failed with exit code 5"));
        Assert.That(
            File.Exists(workspace.CompilerManifestPath),
            Is.True,
            verification.Output);
        Assert.That(
            File.Exists(workspace.SarifPath),
            Is.True,
            verification.Output);

        using (var manifest = JsonDocument.Parse(
                   await File.ReadAllTextAsync(
                       workspace.CompilerManifestPath)))
        {
            var syntaxTreePaths = manifest.RootElement
                .GetProperty("compilation")
                .GetProperty("syntaxTrees")
                .EnumerateArray()
                .Select(static tree =>
                    tree.GetProperty("path").GetString() ?? string.Empty)
                .ToArray();
            Assert.That(
                syntaxTreePaths.Any(static path =>
                    path.Replace('\\', '/').EndsWith(
                        "/shared source/LinkedSubject.cs",
                        StringComparison.OrdinalIgnoreCase)),
                Is.True,
                "The final compiler artifact must retain the linked " +
                "file's physical source identity.");

            var claims = manifest.RootElement
                .GetProperty("manifest")
                .GetProperty("claims")
                .EnumerateArray()
                .ToArray();
            Assert.That(claims, Has.Length.EqualTo(1));
            var location = claims[0].GetProperty("location");
            Assert.That(
                location.GetProperty("path").GetString(),
                Is.EqualTo("mapped/contracts/Identity.cs"));
            Assert.That(
                location.GetProperty("line").GetInt32(),
                Is.EqualTo(73));
        }

        using var sarif = JsonDocument.Parse(
            await File.ReadAllTextAsync(workspace.SarifPath));
        var refuted = sarif.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results")
            .EnumerateArray()
            .Single(static result =>
                result.GetProperty("ruleId").GetString() ==
                "SharpProof.Refuted");
        var physicalLocation = refuted
            .GetProperty("locations")[0]
            .GetProperty("physicalLocation");
        Assert.That(
            physicalLocation.GetProperty("artifactLocation")
                .GetProperty("uri").GetString(),
            Is.EqualTo("mapped/contracts/Identity.cs"));
        Assert.That(
            physicalLocation.GetProperty("region")
                .GetProperty("startLine").GetInt32(),
            Is.EqualTo(73));
    }

    [Test]
    public async Task VerifierPackageRejectsANonX64BuildProcess()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WriteConsumer(
            feed.Version,
            PackagedProductFeed.VerifierPackageId);
        var restore = await RestoreConsumerAsync(workspace, feed);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var verification = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "build",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false",
            "-p:SharpProofVerify=true",
            "-p:_SharpProofVerifierProcessArchitecture=X86");

        Assert.That(
            verification.ExitCode,
            Is.Not.Zero,
            verification.Output);
        Assert.That(
            verification.Output,
            Does.Contain(
                "supported only by Core MSBuild inside the canonical Linux amd64 container"));
    }

    [Test]
    public async Task PackedAnalyzerReportsContractCorrectnessRegressions()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WriteAnalyzerConsumer(
            feed.Version,
            PackagedProductFeed.PortablePackageId,
            """
            using SharpProof.Attributes;

            public static class Subject {
                public sealed class Positive {
                    public Positive(int value) {
                        Contract.Requires(value > 0);
                    }
                }

                public static Positive RefutedConstructor() =>
                    new Positive(-1);
            }
            """,
            "all",
            "SP0027");
        var restore = await RestoreConsumerAsync(workspace, feed);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var validBuild = await BuildAnalyzerConsumerAsync(workspace);
        Assert.That(validBuild.ExitCode, Is.Zero, validBuild.Output);
        Assert.That(validBuild.Output, Does.Contain("SP0027"));

        workspace.WriteSource(
            """
            using System;
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public interface Subject {
                [AllowedCapabilities((SharpProofCapability)(1 << 30))]
                [AllowedExceptions(typeof(string))]
                [AllowedExceptions(typeof(int))]
                [return: Positive]
                Task Unsupported(
                    [Positive] string text,
                    [NotNull] int count,
                    [InRange(5, 1)] int range);
            }

            public static class PlacementSubject {
                public static void InvalidPlacements(bool condition) {
                    if (condition) {
                        Contract.Requires(condition);
                    }
                    _ = condition;
                    Contract.Ensures(condition);
                    {
                        Contract.Assume(condition);
                    }
                }
            }
            """);
        var invalidBuild = await BuildAnalyzerConsumerAsync(workspace);
        Assert.That(invalidBuild.ExitCode, Is.Not.Zero, invalidBuild.Output);
        Assert.That(
            CountDiagnosticLines(invalidBuild.Output, "SP0024"),
            Is.GreaterThanOrEqualTo(10),
            invalidBuild.Output);
        Assert.That(
            invalidBuild.Output,
            Does.Contain("AllowedCapabilities")
                .And.Contain("AllowedExceptions")
                .And.Contain("[Positive]")
                .And.Contain("[NotNull]")
                .And.Contain("[InRange]")
                .And.Contain("invalid argument 'Task'")
                .And.Contain("expected an unconditional prologue statement")
                .And.Contain(
                    "expected the clause before every non-contract statement")
                .And.Contain("expected a direct prologue statement"));
    }

    [Test]
    public async Task PackedAnalyzerDoesNotHideIntrinsicLengthReads()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WriteAnalyzerConsumer(
            feed.Version,
            PackagedProductFeed.PortablePackageId,
            """
            using SharpProof.Attributes;

            public static class Subject {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static int ReadString([NotNull] string value) =>
                    value.Length;

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static int ReadArray([NotNull] int[] value) =>
                    value.Length;
            }
            """,
            "effects",
            "SP0047");
        var restore = await RestoreConsumerAsync(workspace, feed);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var build = await BuildAnalyzerConsumerAsync(workspace);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(build.ExitCode, Is.Zero, build.Output);
            Assert.That(build.Output, Does.Contain("SP0047"));
            Assert.That(
                build.Output,
                Does.Contain("'ReadString'")
                    .And.Contain("'ReadArray'")
                    .And.Contain(
                        "EffectContractDoesNotCoverBodySummary"));
        }
    }

    [Test]
    public async Task PackedConsumerProbeCapturesFinalCompilerInputs()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WriteCompilerProbeConsumer(
            feed.Version);
        var restore = await RestoreConsumerAsync(workspace, feed);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var withoutPath =
            await RebuildCompilerProbeConsumerAsync(workspace);
        Assert.That(withoutPath.ExitCode, Is.Zero, withoutPath.Output);
        Assert.That(File.Exists(workspace.ProbeOutputPath), Is.False);
        var profileOff = await RebuildCompilerProbeConsumerAsync(
            workspace,
            ("SharpProofProfile", "off"),
            (
                CompilerProbeContract.OutputPathPropertyName,
                workspace.ProbeOutputPath));
        Assert.That(profileOff.ExitCode, Is.Zero, profileOff.Output);
        Assert.That(File.Exists(workspace.ProbeOutputPath), Is.False);

        var first = await RebuildProbeAsync(
            workspace,
            "first-global",
            "first-metadata");
        Assert.That(first.ExitCode, Is.Zero, first.Output);
        Assert.That(
            File.Exists(workspace.ProbeOutputPath),
            Is.True,
            first.Output);
        var firstBytes =
            await File.ReadAllBytesAsync(workspace.ProbeOutputPath);
        VerifyProbeSnapshot(
            firstBytes,
            "first-input",
            "first-global",
            "first-metadata");
        var noOp = await RebuildProbeAsync(
            workspace,
            "first-global",
            "first-metadata");
        Assert.That(noOp.ExitCode, Is.Zero, noOp.Output);
        Assert.That(
            await File.ReadAllBytesAsync(workspace.ProbeOutputPath),
            Is.EqualTo(firstBytes));

        workspace.WriteProbeInput("second-input");
        var changedInput = await RebuildProbeAsync(
            workspace,
            "first-global",
            "first-metadata");
        Assert.That(changedInput.ExitCode, Is.Zero, changedInput.Output);
        var inputBytes =
            await File.ReadAllBytesAsync(workspace.ProbeOutputPath);
        VerifyProbeSnapshot(
            inputBytes,
            "second-input",
            "first-global",
            "first-metadata");
        Assert.That(
            inputBytes,
            Is.Not.EqualTo(firstBytes));

        var changedConfiguration = await RebuildProbeAsync(
            workspace,
            "second-global",
            "second-metadata");
        Assert.That(
            changedConfiguration.ExitCode,
            Is.Zero,
            changedConfiguration.Output);
        var configuredBytes =
            await File.ReadAllBytesAsync(workspace.ProbeOutputPath);
        VerifyProbeSnapshot(
            configuredBytes,
            "second-input",
            "second-global",
            "second-metadata");
        Assert.That(
            configuredBytes,
            Is.Not.EqualTo(inputBytes));
    }

    private static Task<ProcessResult> RestoreConsumerAsync(
        PackageWorkspace workspace,
        PackagedProductFeed feed,
        bool includeNetStandardFrameworkPackages = false,
        bool includeNet472ReferenceAssemblies = false)
    {
        var offlineFrameworkSource = includeNetStandardFrameworkPackages ||
            includeNet472ReferenceAssemblies
                ? workspace.PrepareFrameworkSource(
                    includeNetStandardFrameworkPackages,
                    includeNet472ReferenceAssemblies)
                : null;
        var nugetConfig = IsolatedPackageFeedConfiguration.Write(
            workspace.ConsumerDirectory,
            feed.Source,
            offlineFrameworkSource);
        var arguments = new List<string> {
            "restore",
            workspace.ConsumerProject,
            "--nologo",
            "/nodeReuse:false",
            "--configfile",
            nugetConfig
        };
        arguments.Add("--packages");
        arguments.Add(workspace.PackageCache);
        return RunDotNetAsync(
            workspace.ConsumerDirectory,
            [.. arguments]);
    }

    private static Task<ProcessResult> BuildAnalyzerConsumerAsync(
        PackageWorkspace workspace)
    {
        return RunDotNetAsync(
            workspace.ConsumerDirectory,
            "build",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false");
    }

    private static async Task<PackagedAnalyzerItem[]>
        EvaluatePackagedAnalyzerItemsAsync(
            PackageWorkspace workspace,
            params (string Name, string Value)[] properties)
    {
        var arguments = new List<string> {
            "msbuild",
            workspace.ConsumerProject,
            "-getItem:Analyzer",
            "--nologo"
        };
        arguments.AddRange(properties.Select(static property =>
            "-p:" + property.Name + "=" + property.Value));
        var evaluation = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            [.. arguments]);
        Assert.That(evaluation.ExitCode, Is.Zero, evaluation.Output);
        return GetPackagedAnalyzerItems(evaluation.Output);
    }

    private static async Task AssertSourceConsumerAnalyzerItemsAsync(
        PackageWorkspace workspace,
        bool includePortable,
        bool includeCollector,
        params (string Name, string Value)[] properties)
    {
        workspace.WriteSourceConsumerEvaluationProject(properties);
        var evaluation = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            workspace.ConsumerProject,
            "-getItem:Analyzer;ProjectReference",
            "--nologo");
        Assert.That(evaluation.ExitCode, Is.Zero, evaluation.Output);
        var items = GetSourceConsumerAnalyzerItems(evaluation.Output);

        Assert.That(
            items.EntryFileNames,
            Is.EquivalentTo(
                (includePortable
                    ? ExpectedAnalyzerEntryFileNames.Concat(
                        ExpectedGeneratorEntryFileNames)
                    : [])
                .Concat(
                    includeCollector
                        ? ExpectedCollectorEntryFileNames
                        : [])));
        Assert.That(
            items.DependencyFileNames,
            Is.EquivalentTo(
                (includePortable
                    ? ExpectedAnalyzerDependencyFileNames
                    : [])
                .Concat(
                    includeCollector
                        ? ExpectedCollectorDependencyFileNames
                        : [])));
        Assert.That(
            items.EntryFileNames.Concat(items.DependencyFileNames),
            Is.Unique);
    }

    private static Task<ProcessResult> RebuildCompilerProbeConsumerAsync(
        PackageWorkspace workspace,
        params (string Name, string Value)[] properties)
    {
        var arguments = new List<string> {
            "build",
            workspace.ConsumerProject,
            "-t:Rebuild",
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false"
        };
        arguments.AddRange(properties.Select(static property =>
            "-p:" + property.Name + "=" + property.Value));
        return RunDotNetAsync(
            workspace.ConsumerDirectory,
            [.. arguments]);
    }

    private static Task<ProcessResult> RebuildProbeAsync(
        PackageWorkspace workspace,
        string globalValue,
        string metadataValue)
    {
        return RebuildCompilerProbeConsumerAsync(
            workspace,
            ("SharpProofProfile", "advisory"),
            (
                CompilerProbeContract.OutputPathPropertyName,
                workspace.ProbeOutputPath),
            (
                CompilerProbeContract.GlobalValuePropertyName,
                globalValue),
            ("SharpProofProbeAdditionalMetadata", metadataValue));
    }

    private static void VerifyProbeSnapshot(
        byte[] snapshot,
        string input,
        string globalValue,
        string metadataValue)
    {
        using var document = JsonDocument.Parse(snapshot);
        var root = document.RootElement;
        Assert.That(
            root.EnumerateObject().Select(static property => property.Name),
            Is.EqualTo([
                "schema",
                "schemaVersion",
                "assembly",
                "options",
                "consumedOptions",
                "syntaxTrees",
                "portableReferences",
                "additionalFiles"
            ]));
        Assert.That(
            root.GetProperty("schema").GetString(),
            Is.EqualTo(CompilerProbeContract.SchemaName));
        Assert.That(
            root.GetProperty("schemaVersion").GetInt32(),
            Is.EqualTo(CompilerProbeContract.SchemaVersion));
        Assert.That(
            root.GetProperty("assembly").GetProperty("name").GetString(),
            Is.EqualTo("Consumer"));

        var syntaxTrees = root.GetProperty("syntaxTrees")
            .EnumerateArray()
            .ToArray();
        Assert.That(
            syntaxTrees,
            Has.Some.Matches<JsonElement>(tree =>
                tree.GetProperty("path").GetString()?
                    .EndsWith("/Subject.cs", StringComparison.Ordinal) ==
                true));
        Assert.That(
            syntaxTrees,
            Has.Some.Matches<JsonElement>(tree =>
                tree.GetProperty("path").GetString()?
                    .EndsWith(
                        "/" + CompilerProbeContract.GlobalUsingsHintName,
                        StringComparison.Ordinal) ==
                true));
        Assert.That(
            syntaxTrees,
            Has.Some.Matches<JsonElement>(tree =>
                tree.GetProperty("path").GetString()?
                    .EndsWith(
                        "/" + CompilerProbeContract.ContractHintName,
                        StringComparison.Ordinal) ==
                true));
        var subjectTree = syntaxTrees.Single(tree =>
            tree.GetProperty("path").GetString()?
                .EndsWith("/Subject.cs", StringComparison.Ordinal) ==
            true);
        Assert.That(
            subjectTree.GetProperty("declaredSymbols")
                .EnumerateArray()
                .Select(static symbol => symbol.GetString()),
            Does.Contain("HandwrittenProbe.AliasAssemblyName()")
                .And.Contain("HandwrittenProbe.GeneratedIdentity(int)"));
        var contractTree = syntaxTrees.Single(tree =>
            tree.GetProperty("path").GetString()?
                .EndsWith(
                    "/" + CompilerProbeContract.ContractHintName,
                    StringComparison.Ordinal) ==
            true);
        Assert.That(
            contractTree.GetProperty("declaredSymbols")
                .EnumerateArray()
                .Select(static symbol => symbol.GetString()),
            Does.Contain(
                    CompilerProbeContract.GeneratedTypeMetadataName)
                .And.Contain(
                    CompilerProbeContract.GeneratedTypeMetadataName +
                    "." + CompilerProbeContract.GeneratedMethodName +
                    "(int)"));
        var parseOptions = subjectTree.GetProperty("parseOptions");
        Assert.That(
            parseOptions.GetProperty("languageVersion").GetString(),
            Is.EqualTo("CSharp13"));
        Assert.That(
            parseOptions.GetProperty("specifiedLanguageVersion").GetString(),
            Is.EqualTo("CSharp13"));
        var options = root.GetProperty("options");
        Assert.That(
            options.GetProperty("nullableContextOptions").GetString(),
            Is.EqualTo("Annotations"));
        Assert.That(
            options.GetProperty("optimizationLevel").GetString(),
            Is.EqualTo("Debug"));
        Assert.That(
            options.GetProperty("platform").GetString(),
            Is.EqualTo("X64"));
        Assert.That(options.GetProperty("allowUnsafe").GetBoolean(), Is.True);
        Assert.That(options.GetProperty("checkOverflow").GetBoolean(), Is.True);
        Assert.That(options.GetProperty("deterministic").GetBoolean(), Is.True);
        Assert.That(
            options.GetProperty("languageVersions")
                .EnumerateArray()
                .Select(static value => value.GetString()),
            Does.Contain("CSharp13"));
        Assert.That(
            options.GetProperty("preprocessorSymbols")
                .EnumerateArray()
                .Select(static symbol => symbol.GetString()),
            Does.Contain("PROBE_SYMBOL")
                .And.Contain("SHARPPROOF_PROBE_GENERATED"));

        var consumedOptions = root.GetProperty("consumedOptions")
            .EnumerateArray()
            .ToArray();
        var globalOption = consumedOptions.Single(option =>
            option.GetProperty("key").GetString() ==
                CompilerProbeContract.GlobalValueOptionKey &&
            string.IsNullOrEmpty(
                option.GetProperty("path").GetString()));
        Assert.That(
            globalOption.GetProperty("value").GetString(),
            Is.EqualTo(globalValue));
        var outputOption = consumedOptions.Single(option =>
            option.GetProperty("key").GetString() ==
                CompilerProbeContract.OutputPathOptionKey);
        Assert.That(
            outputOption.GetProperty("value").GetString(),
            Is.Not.Null.And.Not.Empty);
        var metadataOption = consumedOptions.Single(option =>
            option.GetProperty("key").GetString() ==
                CompilerProbeContract.AdditionalFileMetadataOptionKey &&
            option.GetProperty("path").GetString()?
                .EndsWith(
                    "/" + CompilerProbeContract.AdditionalFileName,
                    StringComparison.Ordinal) ==
            true);
        Assert.That(
            metadataOption.GetProperty("value").GetString(),
            Is.EqualTo(metadataValue));

        var additionalFile = root.GetProperty("additionalFiles")
            .EnumerateArray()
            .Single(file =>
                file.GetProperty("path").GetString()?
                    .EndsWith(
                        "/" + CompilerProbeContract.AdditionalFileName,
                        StringComparison.Ordinal) ==
                true);
        Assert.That(
            additionalFile.GetProperty("metadataValue").GetString(),
            Is.EqualTo(metadataValue));
        Assert.That(
            additionalFile.GetProperty("textSha256").GetString(),
            Is.EqualTo(TextChecksum(input + "\n")).IgnoreCase);

        var aliasReference = root.GetProperty("portableReferences")
            .EnumerateArray()
            .Single(reference =>
                reference.GetProperty("aliases")
                    .EnumerateArray()
                    .Any(alias =>
                        alias.GetString() == "probealias"));
        Assert.That(
            aliasReference.GetProperty("assemblyOrModuleIdentity")
                .GetString(),
            Does.Contain("NUnit.Framework").IgnoreCase);
    }

    private static string TextChecksum(string value)
    {
        return Convert.ToHexString(
            SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)));
    }

    private static int CountDiagnosticLines(string output, string id)
    {
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains(
                ": error " + id + ":",
                StringComparison.Ordinal));
    }

    private static void VerifyPackageGraph(PackagedProductFeed feed)
    {
        var expectedDependencies =
            new Dictionary<string, (string Id, string Version)[]>(
                StringComparer.Ordinal)
            {
                [PackagedProductFeed.AttributesPackageId] = [],
                [PackagedProductFeed.PortablePackageId] = [
                    (
                        PackagedProductFeed.AttributesPackageId,
                        "[" + feed.Version + "]")
                ],
                [PackagedProductFeed.VerifierPackageId] = [
                    (
                        PackagedProductFeed.PortablePackageId,
                        "[" + feed.Version + "]")
                ]
            };
        foreach (var package in feed.Packages)
        {
            using var archive = ZipFile.OpenRead(package.Path);
            var nuspec = archive.Entries.Single(entry =>
                entry.FullName.EndsWith(
                    ".nuspec",
                    StringComparison.OrdinalIgnoreCase));
            using var stream = nuspec.Open();
            var document = XDocument.Load(stream);
            var metadata = document.Descendants().Single(element =>
                element.Name.LocalName == "metadata");
            Assert.That(
                metadata.Elements().Single(element =>
                    element.Name.LocalName == "id").Value,
                Is.EqualTo(package.Id));
            Assert.That(
                metadata.Elements().Single(element =>
                    element.Name.LocalName == "version").Value,
                Is.EqualTo(feed.Version));
            var dependencies = metadata.Descendants()
                .Where(element =>
                    element.Name.LocalName == "dependency")
                .ToArray();
            Assert.That(
                dependencies.Select(static dependency => (
                    Id: dependency.Attribute("id")?.Value,
                    Version: dependency.Attribute("version")?.Value)),
                Is.EqualTo(expectedDependencies[package.Id]),
                package.Id);
            foreach (var dependency in dependencies)
            {
                Assert.That(
                    dependency.Attributes()
                        .Select(static attribute =>
                            attribute.Name.LocalName),
                    Is.EquivalentTo(ExpectedDependencyAttributes),
                    package.Id +
                    " dependency metadata must not filter assets.");
            }
        }
    }

    private static void VerifyPackageLayouts(PackagedProductFeed feed)
    {
        var attributes = feed.GetPackage(
            PackagedProductFeed.AttributesPackageId);
        AssertArchiveLayout(
            attributes.Path,
            [
                "_rels/.rels",
                "[Content_Types].xml",
                "lib/netstandard2.0/SharpProof.Attributes.dll",
                "lib/netstandard2.0/SharpProof.Attributes.xml",
                "LICENSE",
                "package/services/metadata/core-properties/" +
                    "<generated>.psmdcp",
                "README.md",
                "SharpProof.Attributes.nuspec"
            ]);

        var portable = feed.GetPackage(
            PackagedProductFeed.PortablePackageId);
        AssertArchiveLayout(
            portable.Path,
            [
                "_rels/.rels",
                "[Content_Types].xml",
                "buildTransitive/SharpProof.ConsumerContract.props",
                "buildTransitive/SharpProof.props",
                "buildTransitive/SharpProof.targets",
                "LICENSE",
                "package/services/metadata/core-properties/" +
                    "<generated>.psmdcp",
                "README.md",
                "SharpProof.nuspec",
                "THIRD-PARTY-NOTICES.txt",
                .. ExpectedConditionalAnalyzerEntries
            ]);
        VerifyPortableLayout(portable.Path);

        var verifier = feed.GetPackage(
            PackagedProductFeed.VerifierPackageId);
        AssertArchiveLayout(
            verifier.Path,
            [
                "_rels/.rels",
                "[Content_Types].xml",
                "buildTransitive/SharpProof.Verifier.props",
                "buildTransitive/SharpProof.Verifier.targets",
                "LICENSE",
                "package/services/metadata/core-properties/" +
                    "<generated>.psmdcp",
                "README.md",
                "SharpProof.Verifier.nuspec",
                "THIRD-PARTY-NOTICES.txt",
                .. ExpectedToolEntries,
                .. ExpectedNativeZ3Entries
            ]);
        VerifyVerifierLayout(verifier.Path);

        var allEntries = feed.Packages.SelectMany(package =>
        {
            using var archive = ZipFile.OpenRead(package.Path);
            return archive.Entries
                .Select(entry => (package.Id, entry.FullName))
                .ToArray();
        }).ToArray();
        Assert.That(
            allEntries.Where(static entry =>
                entry.FullName.EndsWith(
                    "/SharpProof.Attributes.dll",
                    StringComparison.OrdinalIgnoreCase)),
            Is.EqualTo([
                (
                    PackagedProductFeed.AttributesPackageId,
                    "lib/netstandard2.0/SharpProof.Attributes.dll")
            ]));
        Assert.That(
            allEntries.Where(static entry =>
                IsNativeZ3(entry.FullName)),
            Is.EqualTo([
                (
                    PackagedProductFeed.VerifierPackageId,
                    ExpectedNativeZ3Entries[0])
            ]));
    }

    private static void AssertArchiveLayout(
        string packagePath,
        string[] expectedEntries)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Select(static entry =>
                NormalizeGeneratedPackageEntry(entry.FullName))
            .ToArray();
        Assert.That(
            entries,
            Is.EquivalentTo(expectedEntries),
            Path.GetFileName(packagePath));
    }

    private static void VerifySymbolPackagePair(
        string packagePath,
        string symbolPackagePath,
        string nuspecName,
        string commit)
    {
        using var package = ZipFile.OpenRead(packagePath);
        var packageEntries = package.Entries
            .Select(static entry => entry.FullName)
            .ToArray();
        Assert.That(
            packageEntries,
            Has.None.EndsWith(".pdb"),
            Path.GetFileName(packagePath));
        var expectedPdbEntries = packageEntries
            .Where(static entry =>
                entry.EndsWith(".dll", StringComparison.Ordinal) &&
                Path.GetFileName(entry).StartsWith(
                    "SharpProof.",
                    StringComparison.Ordinal))
            .Select(static entry => entry[..^".dll".Length] + ".pdb")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(
            expectedPdbEntries,
            Is.Not.Empty,
            Path.GetFileName(packagePath));
        AssertArchiveLayout(
            symbolPackagePath,
            [
                "_rels/.rels",
                "[Content_Types].xml",
                "package/services/metadata/core-properties/" +
                    "<generated>.psmdcp",
                nuspecName,
                .. expectedPdbEntries
            ]);

        using var symbols = ZipFile.OpenRead(symbolPackagePath);
        foreach (var pdbEntry in expectedPdbEntries)
        {
            var entry = symbols.GetEntry(pdbEntry) ??
                throw new InvalidDataException(
                    "Symbol package entry was not found: " + pdbEntry);
            VerifyPortablePdbSourceLink(entry, commit);
        }
    }

    private static void VerifyPortablePdbSourceLink(
        ZipArchiveEntry entry,
        string commit)
    {
        using var image = new MemoryStream();
        using (var stream = entry.Open())
        {
            stream.CopyTo(image);
        }

        image.Position = 0;
        using var provider =
            MetadataReaderProvider.FromPortablePdbStream(
                image,
                MetadataStreamOptions.LeaveOpen);
        var reader = provider.GetMetadataReader();
        var sourceLinks = reader.CustomDebugInformation
            .Select(reader.GetCustomDebugInformation)
            .Where(information =>
                reader.GetGuid(information.Kind) == SourceLinkKind)
            .ToArray();
        Assert.That(
            sourceLinks,
            Has.Length.EqualTo(1),
            entry.FullName);
        var json = Encoding.UTF8.GetString(
            reader.GetBlobBytes(sourceLinks[0].Value));
        using var document = JsonDocument.Parse(json);
        Assert.That(
            document.RootElement.ValueKind,
            Is.EqualTo(JsonValueKind.Object),
            entry.FullName);
        var documents = document.RootElement
            .GetProperty("documents");
        Assert.That(
            documents.ValueKind,
            Is.EqualTo(JsonValueKind.Object),
            entry.FullName);
        var mappings = documents
            .EnumerateObject()
            .ToArray();
        Assert.That(mappings, Is.Not.Empty, entry.FullName);
        var expectedUrl =
            "https://raw.githubusercontent.com/alexyorke/" +
            "SharpProof/" + commit + "/*";
        Assert.That(
            mappings.Select(static mapping =>
                mapping.Name.Replace('\\', '/')),
            Has.All.EndsWith("/*"),
            entry.FullName);
        Assert.That(
            mappings.Select(static mapping =>
                mapping.Value.GetString()),
            Is.All.EqualTo(expectedUrl),
            entry.FullName);
    }

    private static void VerifyRepositoryMetadata(
        string packagePath,
        string commit)
    {
        var document = PackageNuspecReader.Read(packagePath);
        var repository = document.Descendants().Single(element =>
            element.Name.LocalName == "repository");
        Assert.That(
            repository.Attribute("type")?.Value,
            Is.EqualTo("git"),
            Path.GetFileName(packagePath));
        Assert.That(
            repository.Attribute("url")?.Value,
            Is.EqualTo(
                "https://github.com/alexyorke/SharpProof"),
            Path.GetFileName(packagePath));
        Assert.That(
            repository.Attribute("commit")?.Value,
            Is.EqualTo(commit),
            Path.GetFileName(packagePath));
    }

    private static string NormalizeGeneratedPackageEntry(string entry)
    {
        const string coreProperties =
            "package/services/metadata/core-properties/";
        if (entry.StartsWith(coreProperties, StringComparison.Ordinal) &&
            entry.EndsWith(".psmdcp", StringComparison.Ordinal))
        {
            return coreProperties + "<generated>.psmdcp";
        }

        return entry;
    }

    private static bool IsNativeZ3(string entry)
    {
        var fileName = Path.GetFileName(entry);
        return string.Equals(
                fileName,
                "libz3.dll",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                fileName,
                "libz3.so",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                fileName,
                "libz3.dylib",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void VerifyPortableLayout(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Select(static entry => entry.FullName)
            .ToArray();
        var analyzerEntries = entries
            .Where(entry => entry.StartsWith(
                "analyzers/dotnet/cs/",
                StringComparison.Ordinal))
            .ToArray();
        var conditionalAnalyzerEntries = entries
            .Where(entry => entry.StartsWith(
                "tools/analyzers/dotnet/cs/",
                StringComparison.Ordinal))
            .ToArray();
        var collectorEntries = entries
            .Where(entry => entry.StartsWith(
                "tools/collector/",
                StringComparison.Ordinal))
            .ToArray();
        var sharedEntries = entries
            .Where(entry => entry.StartsWith(
                "tools/shared/netstandard2.0/",
                StringComparison.Ordinal))
            .ToArray();
        Assert.That(analyzerEntries, Is.Empty);
        Assert.That(
            conditionalAnalyzerEntries,
            Is.EquivalentTo(
                ExpectedConditionalAnalyzerEntries.Where(static entry =>
                    entry.StartsWith(
                        "tools/analyzers/dotnet/cs/",
                        StringComparison.Ordinal))));
        Assert.That(
            collectorEntries,
            Is.EquivalentTo(
                ExpectedConditionalAnalyzerEntries.Where(static entry =>
                    entry.StartsWith(
                        "tools/collector/",
                        StringComparison.Ordinal))));
        Assert.That(
            sharedEntries,
            Is.EquivalentTo(
                ExpectedConditionalAnalyzerEntries.Where(static entry =>
                    entry.StartsWith(
                        "tools/shared/netstandard2.0/",
                        StringComparison.Ordinal))));
        Assert.That(
            conditionalAnalyzerEntries
                .Concat(collectorEntries)
                .Concat(sharedEntries),
            Has.None.Matches<string>(
                entry =>
                    entry.Contains("Microsoft.Z3", StringComparison.Ordinal) ||
                    entry.Contains("libz3", StringComparison.OrdinalIgnoreCase) ||
                    entry.Contains("NativeSmtLocator", StringComparison.Ordinal)));

        Assert.That(
            entries,
            Does.Contain("buildTransitive/SharpProof.ConsumerContract.props"));
        Assert.That(
            entries,
            Does.Contain("buildTransitive/SharpProof.props"));
        Assert.That(
            entries,
            Does.Contain("buildTransitive/SharpProof.targets"));
        Assert.That(
            ReadArchiveText(
                archive,
                "buildTransitive/SharpProof.props"),
            Does.Contain(
                    "$(MSBuildThisFileDirectory)../tools/analyzers/dotnet/cs")
                .And.Contain(
                    "$(MSBuildThisFileDirectory)../tools/collector")
                .And.Contain(
                    "$(MSBuildThisFileDirectory)../tools/shared/netstandard2.0")
                .And.Not.Contain(@"..\tools\analyzers"));
        Assert.That(
            ReadArchiveText(
                archive,
                "buildTransitive/SharpProof.targets"),
            Does.Not.Contain("*.dll")
                .And.Contain(
                    "<SharpProofAnalyzerRole>EntryPoint</SharpProofAnalyzerRole>")
                .And.Contain(
                    "<SharpProofAnalyzerRole>Generator</SharpProofAnalyzerRole>")
                .And.Contain(
                    "<SharpProofAnalyzerRole>Dependency</SharpProofAnalyzerRole>")
                .And.Contain(
                    "<SharpProofAnalyzerRole>Collector</SharpProofAnalyzerRole>")
                .And.Contain(
                    "<SharpProofAnalyzerRole>CollectorDependency</SharpProofAnalyzerRole>"));
        Assert.That(
            entries,
            Has.None.StartsWith("lib/")
                .And.None.StartsWith("tools/net9/"));
    }

    private static void VerifyVerifierLayout(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Select(static entry => entry.FullName)
            .ToArray();
        var toolEntries = entries
            .Where(entry => entry.StartsWith(
                "tools/net9/",
                StringComparison.Ordinal))
            .ToArray();
        Assert.That(
            toolEntries,
            Is.EquivalentTo(ExpectedToolEntries));
        Assert.That(
            toolEntries,
            Does.Not.Contain(
                "tools/net9/Microsoft.CodeAnalysis.AnalyzerUtilities.dll"));
        Assert.That(
            toolEntries.Where(static entry =>
                entry.StartsWith(
                    "tools/net9/Microsoft.CodeAnalysis",
                    StringComparison.Ordinal) ||
                entry is
                    "tools/net9/SharpProof.Attributes.dll" or
                    "tools/net9/SharpProof.Contracts.dll" or
                    "tools/net9/SharpProof.Frontend.dll"),
            Is.Empty);
        foreach (var dependencies in new[] {
                     "tools/net9/SharpProof.Worker.deps.json",
                     "tools/net9/SharpProof.Worker.Launcher.deps.json"
                 })
        {
            Assert.That(
                ReadArchiveText(archive, dependencies),
                Does.Not.Contain("\"Microsoft.CodeAnalysis/\"")
                    .And.Not.Contain("\"Microsoft.CodeAnalysis.CSharp/\"")
                    .And.Not.Contain("SharpProof.Attributes")
                    .And.Not.Contain("SharpProof.Contracts")
                    .And.Not.Contain("SharpProof.Frontend"),
                dependencies);
        }

        Assert.That(
            entries.Where(static entry =>
                IsNativeZ3(entry)),
            Is.EquivalentTo(ExpectedNativeZ3Entries));
        VerifyPinnedZ3Payload(archive);
    }

    private static void VerifyPinnedZ3Payload(ZipArchive archive)
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(
                TestRepository.FindRoot(),
                "eng",
                "container",
                "toolchain.json")));
        var z3 = catalog.RootElement.GetProperty("z3");
        VerifyPackagePayload(
            archive,
            "tools/native/linux-x64/libz3.so",
            z3.GetProperty("libraryBytes").GetInt64());
        VerifyPackagePayload(
            archive,
            "tools/net9/Microsoft.Z3.dll",
            z3.GetProperty("managedAssemblyBytes").GetInt64());
    }

    private static void VerifyPackagePayload(
        ZipArchive archive,
        string path,
        long expectedBytes)
    {
        var entry = archive.GetEntry(path) ??
            throw new InvalidOperationException(
                "Package entry was not found: " + path);
        Assert.That(entry.Length, Is.EqualTo(expectedBytes), path);
    }

    private static string ReadArchiveText(
        ZipArchive archive,
        string entryPath)
    {
        var entry = archive.GetEntry(entryPath) ??
            throw new InvalidOperationException(
                "Package entry was not found: " + entryPath);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static async Task<ProcessResult> RunDotNetAsync(
        string workingDirectory,
        params string[] arguments)
    {
        return await RunProcessAsync(
            workingDirectory,
            "dotnet",
            arguments);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var startInfo = ProcessRunner.CreateStartInfo(
            workingDirectory,
            fileName,
            arguments);
        startInfo.Environment["SharedCompilationId"] =
            CreateSharedCompilationServerId();

        var result = await ProcessRunner.RunCapturedAsync(
            startInfo,
            CancellationToken.None);
        return new ProcessResult(
            result.ExitCode,
            result.Output + Environment.NewLine + result.Error);
    }

    private static string CreateSharedCompilationServerId()
    {
        return "sharpproof-package-layout-" +
            Guid.NewGuid().ToString("N");
    }

    private static PackagedAnalyzerItem[]
        GetPackagedAnalyzerItems(string output)
    {
        using var document = JsonDocument.Parse(output);
        var result = new List<PackagedAnalyzerItem>();
        foreach (var item in document.RootElement
            .GetProperty("Items")
            .GetProperty("Analyzer")
            .EnumerateArray())
        {
            var identity = item.GetProperty("Identity").GetString();
            if (!item.TryGetProperty(
                    "SharpProofAnalyzerRole", out var roleElement))
            {
                continue;
            }

            var role = roleElement.GetString() ?? "";
            var area = role is "EntryPoint" or "Generator" or "Dependency"
                ? "Analyzer"
                : role is "Collector" or "CollectorDependency"
                    ? "Collector"
                    : null;
            if (identity == null || area == null)
            {
                continue;
            }

            result.Add(new(
                Path.GetFileName(identity),
                role,
                area));
        }
        return [.. result];
    }

    private static SourceConsumerAnalyzerItems
        GetSourceConsumerAnalyzerItems(string output)
    {
        using var document = JsonDocument.Parse(output);
        var items = document.RootElement.GetProperty("Items");
        var dependencyNames = items.GetProperty("Analyzer")
            .EnumerateArray()
            .Select(static item =>
                Path.GetFileName(
                    item.GetProperty("Identity").GetString()) ??
                string.Empty)
            .Where(static fileName =>
                ExpectedAnalyzerDependencyFileNames.Contains(
                    fileName,
                    StringComparer.Ordinal) ||
                ExpectedCollectorDependencyFileNames.Contains(
                    fileName,
                    StringComparer.Ordinal))
            .ToArray();
        var entryNames = items.GetProperty("ProjectReference")
            .EnumerateArray()
            .Where(static item =>
                item.TryGetProperty("OutputItemType", out var outputType) &&
                outputType.GetString() == "Analyzer")
            .Select(static item =>
                Path.GetFileNameWithoutExtension(
                    item.GetProperty("Identity").GetString()) + ".dll")
            .ToArray();
        return new(entryNames, dependencyNames);
    }

    private static string[] GetEvaluatedItemIdentities(
        string output,
        string itemName,
        string? requiredMetadata = null)
    {
        using var document = JsonDocument.Parse(output);
        return [.. document.RootElement
            .GetProperty("Items")
            .GetProperty(itemName)
            .EnumerateArray()
            .Where(item => requiredMetadata == null ||
                item.TryGetProperty(requiredMetadata, out _))
            .Select(static item =>
                item.GetProperty("Identity").GetString() ?? string.Empty)];
    }

    private static string EscapeMsBuildImportPath(string path)
    {
        return SecurityElement.Escape(path)?.Replace(
            ";",
            "%3B",
            StringComparison.Ordinal) ?? string.Empty;
    }

    private static void AssertPackagedAnalyzerItems(
        PackagedAnalyzerItem[] items,
        bool includePortable,
        bool includeCollector)
    {
        Assert.That(
            items.Select(static item => item.FileName),
            Is.Unique);
        Assert.That(
            items.Where(static item => item.Role == "EntryPoint")
                .Select(static item => item.FileName),
            includePortable
                ? Is.EquivalentTo(ExpectedAnalyzerEntryFileNames)
                : Is.Empty);
        Assert.That(
            items.Where(static item => item.Role == "Generator")
                .Select(static item => item.FileName),
            includePortable
                ? Is.EquivalentTo(ExpectedGeneratorEntryFileNames)
                : Is.Empty);
        Assert.That(
            items.Where(static item => item.Role == "Dependency")
                .Select(static item => item.FileName),
            includePortable
                ? Is.EquivalentTo(ExpectedAnalyzerDependencyFileNames)
                : Is.Empty);
        Assert.That(
            items.Where(static item => item.Role == "Collector")
                .Select(static item => item.FileName),
            includeCollector
                ? Is.EquivalentTo(ExpectedCollectorEntryFileNames)
                : Is.Empty);
        Assert.That(
            items.Where(static item => item.Role == "CollectorDependency")
                .Select(static item => item.FileName),
            includeCollector
                ? Is.EquivalentTo(ExpectedCollectorDependencyFileNames)
                : Is.Empty);
        Assert.That(
            items.Where(static item =>
                    item.Role is "EntryPoint" or "Generator" or "Dependency")
                .Select(static item => item.Area),
            Has.All.EqualTo("Analyzer"));
        Assert.That(
            items.Where(static item =>
                    item.Role is "Collector" or "CollectorDependency")
                .Select(static item => item.Area),
            Has.All.EqualTo("Collector"));
        Assert.That(
            items.Select(static item => item.Role),
            Has.All.Matches<string>(role =>
                role is
                    "EntryPoint" or
                    "Generator" or
                    "Dependency" or
                    "Collector" or
                    "CollectorDependency"));
    }

    private sealed class PackageWorkspace : IDisposable
    {
        private static readonly string s_sharedPackageCache = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Package.Layout.Test",
            "package-cache-" + Guid.NewGuid().ToString("N"));
        private readonly string _root;

        private PackageWorkspace(string root)
        {
            _root = root;
            PackageCache = s_sharedPackageCache;
            ConsumerDirectory = Path.Combine(root, "consumer project");
            ConsumerProject = Path.Combine(
                ConsumerDirectory,
                "Consumer.csproj");
            ResultPath = Path.Combine(
                ConsumerDirectory,
                "obj",
                "Release",
                "net8.0",
                "SharpProof",
                "result.json");
            CompilerManifestPath = Path.Combine(
                ConsumerDirectory,
                "obj",
                "Release",
                "net8.0",
                "SharpProof",
                "compiler-manifest.json");
            SarifPath = Path.Combine(
                ConsumerDirectory,
                "obj",
                "Release",
                "net8.0",
                "SharpProof",
                "mapped-result.sarif");
            LinkedSourcePath = Path.Combine(
                root,
                "shared source",
                "LinkedSubject.cs");
            ProbeOutputPath = Path.Combine(
                ConsumerDirectory,
                "obj",
                "Release",
                "net8.0",
                "SharpProof",
                "compiler-probe.json");
            ProbeInputPath = Path.Combine(
                ConsumerDirectory,
                CompilerProbeContract.AdditionalFileName);
            Directory.CreateDirectory(ConsumerDirectory);
        }

        internal string PackageCache
        {
            get;
        }
        internal string ConsumerDirectory
        {
            get;
        }
        internal string ConsumerProject
        {
            get;
        }
        internal string ResultPath
        {
            get;
        }
        internal string CompilerManifestPath
        {
            get;
        }
        internal string SarifPath
        {
            get;
        }
        internal string LinkedSourcePath
        {
            get;
        }
        internal string ProbeOutputPath
        {
            get;
        }
        internal string ProbeInputPath
        {
            get;
        }
        internal string MappedAnalyzerItemsPath => Path.Combine(
            ConsumerDirectory,
            "mapped-analyzers.txt");
        internal string MappedProjectConfigurationsPath => Path.Combine(
            ConsumerDirectory,
            "mapped-project-configurations.txt");

        internal static PackageWorkspace Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "SharpProof.Package.Layout.Test",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.Copy(
                Path.Combine(TestRepository.FindRoot(), "global.json"),
                Path.Combine(root, "global.json"));
            return new PackageWorkspace(root);
        }

        internal static void DisposeSharedPackageCache()
        {
            TestRepository.DeleteOwnedTemporaryDirectory(
                s_sharedPackageCache,
                "SharpProof.Package.Layout.Test",
                "Refusing to remove an unexpected shared package cache.");
        }

        internal void WriteConsumer(string version, string packageId)
        {
            WriteAnalyzerConsumer(
                version,
                packageId,
                """
                using SharpProof.Attributes;
                public static class Subject {
                    [ZeroAllocations]
                    public static object Allocate() => new object();

                    public static long Identity(long value) {
                        Contract.Ensures(Contract.Result<long>() == value);
                        return value;
                    }
                }
                """,
                "all",
                "SP0045");
        }

        internal void WritePassingVerifierConsumer(
            string version,
            string packageId)
        {
            WriteAnalyzerConsumer(
                version,
                packageId,
                """
                using SharpProof.Attributes;
                public static class Subject {
                    [ZeroAllocations]
                    public static long Identity(long value) {
                        Contract.Ensures(
                            Contract.Result<long>() == value);
                        return value;
                    }
                }
                """,
                "all",
                "SP0045");
        }

        internal void WriteRuntimeAssetIsolationConsumer(string version)
        {
            WriteSource("public static class Subject { public static int Value => 1; }");
            var escapedVersion = SecurityElement.Escape(version);
            File.WriteAllText(
                ConsumerProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
                    <SelfContained>false</SelfContained>
                    <SharpProofProfile>off</SharpProofProfile>
                    <SharpProofVerify>false</SharpProofVerify>
                    <NuGetAudit>false</NuGetAudit>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="SharpProof.Verifier"
                                      Version="{escapedVersion}"
                                      PrivateAssets="all" />
                  </ItemGroup>
                  <Target Name="CaptureRuntimeAssets"
                          DependsOnTargets="ResolvePackageAssets">
                    <WriteLinesToFile
                        File="$(MSBuildProjectDirectory)/obj/runtime-assets.txt"
                        Lines="@(RuntimeCopyLocalItems);@(NativeCopyLocalItems)"
                        Overwrite="true" />
                  </Target>
                </Project>
                """,
                new System.Text.UTF8Encoding(false));
        }

        internal void WriteEffectReplayVerifierConsumer(string version)
        {
            WriteAnalyzerConsumer(
                version,
                PackagedProductFeed.VerifierPackageId,
                """
                using SharpProof.Attributes;
                public static class Subject {
                    [ZeroAllocations]
                    public static object AllocateObject() => new object();

                    [ZeroAllocations]
                    public static object[] AllocateArray() => new object[1];
                }
                """,
                "effects",
                "SP0016");
        }

        internal string PrepareFrameworkSource(
            bool includeNetStandard,
            bool includeNet472)
        {
            var frameworkSource = Path.Combine(
                _root,
                "framework package source");
            Directory.CreateDirectory(frameworkSource);
            if (includeNetStandard)
            {
                CopyGlobalPackageToSource(
                    frameworkSource,
                    "netstandard.library",
                    "2.0.3");
                CopyGlobalPackageToSource(
                    frameworkSource,
                    "microsoft.netcore.platforms",
                    "1.1.0");
            }
            if (includeNet472)
            {
                CopyGlobalPackageToSource(
                    frameworkSource,
                    "microsoft.netframework.referenceassemblies",
                    "1.0.3");
                CopyGlobalPackageToSource(
                    frameworkSource,
                    "microsoft.netframework.referenceassemblies.net472",
                    "1.0.3");
            }
            if (Directory.EnumerateFiles(
                    frameworkSource,
                    "SharpProof*.nupkg").Any())
            {
                throw new InvalidOperationException(
                    "The framework-only package source unexpectedly " +
                    "contains a SharpProof package.");
            }

            return frameworkSource;
        }

        private static void CopyGlobalPackageToSource(
            string destination,
            string packageId,
            string version)
        {
            var configuredPackages = Environment.GetEnvironmentVariable(
                "NUGET_PACKAGES");
            var globalPackages = string.IsNullOrWhiteSpace(configuredPackages)
                ? Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile),
                    ".nuget",
                    "packages")
                : Path.GetFullPath(configuredPackages);
            var fileName = packageId + "." + version + ".nupkg";
            var source = Path.Combine(
                globalPackages,
                packageId,
                version,
                fileName);
            if (!File.Exists(source))
            {
                throw new InvalidOperationException(
                    "The offline framework package is missing from the " +
                    "restored global package cache: " + source);
            }

            File.Copy(
                source,
                Path.Combine(destination, fileName),
                overwrite: true);
        }

        internal void WriteFrameworkConsumer(
            string version,
            string targetFramework)
        {
            WriteSource(
                """
                using SharpProof.Attributes;

                public static class Subject {
                    [ZeroAllocations]
                    public static int Identity(int value) => value;
                }
                """);
            var escapedVersion = SecurityElement.Escape(version);
            var escapedFramework =
                SecurityElement.Escape(targetFramework);
            var referenceAssemblies = targetFramework == "net472"
                ? """
                    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net472"
                                      Version="1.0.3"
                                      PrivateAssets="all" />
                  """
                : string.Empty;
            File.WriteAllText(
                ConsumerProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>{escapedFramework}</TargetFramework>
                    <LangVersion>12.0</LangVersion>
                    <SharpProofProfile>advisory</SharpProofProfile>
                    <SharpProofFeatures>all</SharpProofFeatures>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                    <WarningsAsErrors>AD0001;CS8032;CS8034;CS8785</WarningsAsErrors>
                    <NuGetAudit>false</NuGetAudit>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="SharpProof"
                                      Version="{escapedVersion}" />
                    {referenceAssemblies}
                  </ItemGroup>
                </Project>
                """,
                new System.Text.UTF8Encoding(false));
        }

        internal void WriteSourceConsumerEvaluationProject(
            params (string Name, string Value)[] properties)
        {
            var configuredProperties = string.Join(
                Environment.NewLine,
                properties.Select(static property =>
                    "    <" + property.Name + ">" +
                    SecurityElement.Escape(property.Value) +
                    "</" + property.Name + ">"));
            var consumerProps = SecurityElement.Escape(Path.Combine(
                TestRepository.FindRoot(),
                "SharpProof.AnalyzerConsumer.props"));
            File.WriteAllText(
                ConsumerProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                {configuredProperties}
                  </PropertyGroup>
                  <Import Project="{consumerProps}" />
                </Project>
                """,
                new System.Text.UTF8Encoding(false));
        }

        internal string WriteMappedSourceConsumerSolution()
        {
            var repository = TestRepository.FindRoot();
            var consumerProps = SecurityElement.Escape(Path.Combine(
                repository,
                "SharpProof.AnalyzerConsumer.props"));
            var analyzerItemsPath = SecurityElement.Escape(
                MappedAnalyzerItemsPath);
            var configurationsPath = SecurityElement.Escape(
                MappedProjectConfigurationsPath);
            File.WriteAllText(
                ConsumerProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <Import Project="{consumerProps}" />
                  <Target Name="CaptureMappedAnalyzerItems"
                          DependsOnTargets="AssignProjectConfiguration">
                    <WriteLinesToFile File="{analyzerItemsPath}"
                                      Lines="@(Analyzer)"
                                      Overwrite="true" />
                    <WriteLinesToFile File="{configurationsPath}"
                                      Lines="@(ProjectReferenceWithConfiguration->'%(Filename)|%(Configuration)')"
                                      Overwrite="true" />
                  </Target>
                </Project>
                """,
                new System.Text.UTF8Encoding(false));

            const string consumerGuid =
                "{2D442BC0-F301-4913-B82B-178DB3AE1012}";
            const string attributesGuid =
                "{7B5B2351-815A-4416-A221-7D14948A120B}";
            const string analyzerGuid =
                "{07A87750-C6BB-401D-B53D-1D9890F6FF3C}";
            const string generatorGuid =
                "{7F668C71-D5B2-48B7-8C57-FE9CDBED2FE5}";
            const string projectTypeGuid =
                "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
            var attributesProject = GetSolutionPath(
                repository,
                "SharpProof.Attributes",
                "SharpProof.Attributes.csproj");
            var analyzerProject = GetSolutionPath(
                repository,
                "SharpProof.Analyzer",
                "SharpProof.Analyzer.csproj");
            var generatorProject = GetSolutionPath(
                repository,
                "SharpProof.ContractForGenerator",
                "SharpProof.ContractForGenerator.csproj");
            var solution = Path.Combine(
                ConsumerDirectory,
                "MappedConsumer.sln");
            File.WriteAllText(
                solution,
                $"""
                Microsoft Visual Studio Solution File, Format Version 12.00
                # Visual Studio Version 17
                VisualStudioVersion = 17.0.31903.59
                MinimumVisualStudioVersion = 10.0.40219.1
                Project("{projectTypeGuid}") = "Consumer", "Consumer.csproj", "{consumerGuid}"
                EndProject
                Project("{projectTypeGuid}") = "SharpProof.Attributes", "{attributesProject}", "{attributesGuid}"
                EndProject
                Project("{projectTypeGuid}") = "SharpProof.Analyzer", "{analyzerProject}", "{analyzerGuid}"
                EndProject
                Project("{projectTypeGuid}") = "SharpProof.ContractForGenerator", "{generatorProject}", "{generatorGuid}"
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|Any CPU = Debug|Any CPU
                    EndGlobalSection
                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {consumerGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                        {consumerGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU
                        {attributesGuid}.Debug|Any CPU.ActiveCfg = Release|Any CPU
                        {attributesGuid}.Debug|Any CPU.Build.0 = Release|Any CPU
                        {analyzerGuid}.Debug|Any CPU.ActiveCfg = Release|Any CPU
                        {analyzerGuid}.Debug|Any CPU.Build.0 = Release|Any CPU
                        {generatorGuid}.Debug|Any CPU.ActiveCfg = Release|Any CPU
                        {generatorGuid}.Debug|Any CPU.Build.0 = Release|Any CPU
                    EndGlobalSection
                EndGlobal
                """,
                new System.Text.UTF8Encoding(false));
            return solution;

            string GetSolutionPath(
                string root,
                string projectDirectory,
                string projectFile)
            {
                return Path.GetRelativePath(
                        ConsumerDirectory,
                        Path.Combine(root, projectDirectory, projectFile))
                    .Replace('/', '\\');
            }
        }

        internal void WriteLinkedMappedVerifierConsumer(string version)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(LinkedSourcePath)!);
            File.WriteAllText(
                LinkedSourcePath,
                """
                using SharpProof.Attributes;

                public static class Subject {
                    public static long Identity(long value) {
                #line 73 "mapped/contracts/Identity.cs"
                        Contract.Ensures(
                            Contract.Result<long>() > value);
                #line default
                        return value;
                    }
                }
                """,
                new System.Text.UTF8Encoding(false));
            var escapedVersion = SecurityElement.Escape(version);
            var escapedSource =
                SecurityElement.Escape(LinkedSourcePath);
            File.WriteAllText(
                ConsumerProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <LangVersion>12.0</LangVersion>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                    <SharpProofProfile>advisory</SharpProofProfile>
                    <SharpProofFeatures>contracts</SharpProofFeatures>
                    <WarningsAsErrors>AD0001;CS8032;CS8034;CS8785</WarningsAsErrors>
                    <NuGetAudit>false</NuGetAudit>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="{escapedSource}"
                             Link="Linked/Subject.cs" />
                    <PackageReference Include="SharpProof.Verifier"
                                      Version="{escapedVersion}" />
                  </ItemGroup>
                </Project>
                """,
                new System.Text.UTF8Encoding(false));
        }

        internal void WriteAnalyzerConsumer(
            string version,
            string packageId,
            string source,
            string features,
            params string[] enabledDiagnosticIds)
        {
            WriteSource(source);
            File.WriteAllText(
                Path.Combine(ConsumerDirectory, ".globalconfig"),
                string.Join(
                    "\n",
                    enabledDiagnosticIds
                        .Select(static id =>
                            "dotnet_diagnostic." + id + ".severity = warning")
                        .Prepend("is_global = true")) + "\n",
                new System.Text.UTF8Encoding(false));
            var escapedVersion = SecurityElement.Escape(version);
            var escapedPackageId = SecurityElement.Escape(packageId);
            var escapedFeatures = SecurityElement.Escape(features);
            File.WriteAllText(
                ConsumerProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <LangVersion>12.0</LangVersion>
                    <SharpProofProfile>advisory</SharpProofProfile>
                    <SharpProofFeatures>{escapedFeatures}</SharpProofFeatures>
                    <WarningsAsErrors>AD0001;CS8032;CS8034;CS8785</WarningsAsErrors>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="{escapedPackageId}"
                                      Version="{escapedVersion}" />
                  </ItemGroup>
                </Project>
                """,
                new System.Text.UTF8Encoding(false));
        }

        internal void WriteSource(string source)
        {
            File.WriteAllText(
                Path.Combine(ConsumerDirectory, "Subject.cs"),
                source,
                new System.Text.UTF8Encoding(false));
        }

        internal void WriteCompilerProbeConsumer(string version)
        {
            WriteSource(
                """
                extern alias probealias;
                public static class HandwrittenProbe {
                    public static string AliasAssemblyName() =>
                        typeof(probealias::NUnit.Framework.Assert)
                            .Assembly.GetName().Name!;

                #if SHARPPROOF_PROBE_GENERATED
                    public static int GeneratedIdentity(int value) =>
                        ProbeGenerated.Verify(value);
                #endif
                }
                """);
            WriteProbeInput("first-input");
            var escapedVersion = SecurityElement.Escape(version);
            var escapedProbe = SecurityElement.Escape(
                ProductBuildOutputs.CompilerProbeAssemblyPath());
            var escapedAlias = SecurityElement.Escape(
                typeof(Assert).Assembly.Location);
            File.WriteAllText(
                ConsumerProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <LangVersion>13.0</LangVersion>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>annotations</Nullable>
                    <CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>
                    <Optimize>false</Optimize>
                    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                    <Deterministic>true</Deterministic>
                    <PlatformTarget>x64</PlatformTarget>
                    <DefineConstants>PROBE_SYMBOL</DefineConstants>
                    <DefineConstants Condition="'$({CompilerProbeContract.OutputPathPropertyName})' != '' AND '$(SharpProofProfile)' != 'off'">$(DefineConstants);SHARPPROOF_PROBE_GENERATED</DefineConstants>
                    <SharpProofProbeAdditionalMetadata>first-metadata</SharpProofProbeAdditionalMetadata>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="SharpProof"
                                      Version="{escapedVersion}" />
                    <Analyzer Include="{escapedProbe}"
                              Condition="'$({CompilerProbeContract.OutputPathPropertyName})' != '' AND '$(SharpProofProfile)' != 'off'" />
                    <AdditionalFiles Include="{CompilerProbeContract.AdditionalFileName}">
                      <{CompilerProbeContract.AdditionalFileMetadataName}>$(SharpProofProbeAdditionalMetadata)</{CompilerProbeContract.AdditionalFileMetadataName}>
                    </AdditionalFiles>
                    <CompilerVisibleProperty Include="{CompilerProbeContract.OutputPathPropertyName}" />
                    <CompilerVisibleProperty Include="{CompilerProbeContract.GlobalValuePropertyName}" />
                    <CompilerVisibleItemMetadata Include="AdditionalFiles"
                                                 MetadataName="{CompilerProbeContract.AdditionalFileMetadataName}" />
                    <Reference Include="NUnit.Framework">
                      <HintPath>{escapedAlias}</HintPath>
                      <Aliases>probealias</Aliases>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """,
                new System.Text.UTF8Encoding(false));
        }

        internal void WriteProbeInput(string value)
        {
            File.WriteAllText(
                ProbeInputPath,
                value + "\n",
                new System.Text.UTF8Encoding(false));
        }

        public void Dispose()
        {
            TestRepository.DeleteOwnedTemporaryDirectory(
                _root,
                "SharpProof.Package.Layout.Test");
        }
    }

    private sealed class ReleaseEvidenceWorkspace : IDisposable
    {
        private readonly string _root;

        private ReleaseEvidenceWorkspace(string root)
        {
            _root = root;
            OutputDirectory = Path.Combine(root, "output");
            ManifestPath = Path.Combine(
                OutputDirectory,
                "SharpProof.release.json");
        }

        internal string OutputDirectory
        {
            get;
        }
        internal string ManifestPath
        {
            get;
        }
        internal static ReleaseEvidenceWorkspace Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "SharpProof.ReleaseEvidence.Test",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new ReleaseEvidenceWorkspace(root);
        }

        public void Dispose()
        {
            TestRepository.DeleteOwnedTemporaryDirectory(
                _root,
                "SharpProof.ReleaseEvidence.Test",
                "Refusing to remove an unexpected release-evidence " +
                "test directory.");
        }
    }

    private readonly record struct PackagedAnalyzerItem(
        string FileName,
        string Role,
        string Area);

    private readonly record struct SourceConsumerAnalyzerItems(
        string[] EntryFileNames,
        string[] DependencyFileNames);

    private sealed class PackageAnalyzerAssemblyLoader :
        IAnalyzerAssemblyLoader,
        IDisposable
    {
        private readonly Dictionary<string, string> _dependencies =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly PackageAnalyzerLoadContext _context;

        internal PackageAnalyzerAssemblyLoader()
        {
            _context = new PackageAnalyzerLoadContext(_dependencies);
        }

        public void AddDependencyLocation(string fullPath)
        {
            var name = AssemblyName.GetAssemblyName(fullPath).Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                _dependencies[name] = Path.GetFullPath(fullPath);
            }
        }

        public Assembly LoadFromPath(string fullPath)
        {
            return _context.LoadPackageAssembly(Path.GetFullPath(fullPath));
        }

        public void Dispose()
        {
            _context.Unload();
        }

        private sealed class PackageAnalyzerLoadContext(
            IReadOnlyDictionary<string, string> dependencies) :
            AssemblyLoadContext(isCollectible: true)
        {
            internal Assembly LoadPackageAssembly(string path)
            {
                var identity = AssemblyName.GetAssemblyName(path);
                var loaded = Assemblies.FirstOrDefault(assembly =>
                    AssemblyName.ReferenceMatchesDefinition(
                        assembly.GetName(),
                        identity));
                return loaded ?? LoadWithoutLock(path);
            }

            protected override Assembly? Load(AssemblyName identity)
            {
                var compilerAssembly = Default.Assemblies.FirstOrDefault(
                    assembly => AssemblyName.ReferenceMatchesDefinition(
                        assembly.GetName(),
                        identity));
                if (compilerAssembly != null)
                {
                    return compilerAssembly;
                }
                return identity.Name != null &&
                    dependencies.TryGetValue(identity.Name, out var path)
                    ? LoadWithoutLock(path)
                    : null;
            }

            private Assembly LoadWithoutLock(string path)
            {
                using var assembly = File.OpenRead(path);
                var symbolsPath = Path.ChangeExtension(path, ".pdb");
                if (!File.Exists(symbolsPath))
                {
                    return LoadFromStream(assembly);
                }
                using var symbols = File.OpenRead(symbolsPath);
                return LoadFromStream(assembly, symbols);
            }
        }
    }

    private readonly record struct ProcessResult(
        int ExitCode,
        string Output);
}
