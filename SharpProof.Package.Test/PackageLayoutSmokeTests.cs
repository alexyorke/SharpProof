using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;
using SharpProof.CompilerProbe.TestAsset;
using SharpProof.Worker;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class PackageLayoutSmokeTests {
    private static readonly Guid SourceLinkKind = new(
        "CC110556-A091-4D38-9FEC-25AB9A351A6A");

    private static readonly string[] ExpectedAnalyzerEntryFileNames = [
        "SharpProof.Analyzer.dll",
        "SharpProof.ContractForGenerator.dll"
    ];

    private static readonly string[] ExpectedAnalyzerDependencyFileNames = [
        "Microsoft.Bcl.AsyncInterfaces.dll",
        "SharpProof.CompilerArtifact.dll",
        "SharpProof.Contracts.dll",
        "SharpProof.Dataflow.dll",
        "SharpProof.Effects.dll",
        "SharpProof.Frontend.dll",
        "SharpProof.Ir.dll",
        "SharpProof.Specs.dll",
        "SharpProof.Worker.Protocol.dll",
        "System.Buffers.dll",
        "System.Collections.Immutable.dll",
        "System.IO.Pipelines.dll",
        "System.Memory.dll",
        "System.Numerics.Vectors.dll",
        "System.Reflection.Metadata.dll",
        "System.Runtime.CompilerServices.Unsafe.dll",
        "System.Text.Encoding.CodePages.dll",
        "System.Text.Encodings.Web.dll",
        "System.Text.Json.dll",
        "System.Threading.Tasks.Extensions.dll"
    ];

    private static readonly string[] ExpectedConditionalAnalyzerEntries = [
        "tools/analyzers/dotnet/cs/Microsoft.Bcl.AsyncInterfaces.dll",
        "tools/analyzers/dotnet/cs/SharpProof.Analyzer.dll",
        "tools/analyzers/dotnet/cs/SharpProof.CompilerArtifact.dll",
        "tools/analyzers/dotnet/cs/SharpProof.ContractForGenerator.dll",
        "tools/analyzers/dotnet/cs/SharpProof.Contracts.dll",
        "tools/analyzers/dotnet/cs/SharpProof.Dataflow.dll",
        "tools/analyzers/dotnet/cs/SharpProof.Effects.dll",
        "tools/analyzers/dotnet/cs/SharpProof.Frontend.dll",
        "tools/analyzers/dotnet/cs/SharpProof.Ir.dll",
        "tools/analyzers/dotnet/cs/SharpProof.Specs.dll",
        "tools/analyzers/dotnet/cs/SharpProof.Worker.Protocol.dll",
        "tools/analyzers/dotnet/cs/System.Buffers.dll",
        "tools/analyzers/dotnet/cs/System.Collections.Immutable.dll",
        "tools/analyzers/dotnet/cs/System.IO.Pipelines.dll",
        "tools/analyzers/dotnet/cs/System.Memory.dll",
        "tools/analyzers/dotnet/cs/System.Numerics.Vectors.dll",
        "tools/analyzers/dotnet/cs/System.Reflection.Metadata.dll",
        "tools/analyzers/dotnet/cs/System.Runtime.CompilerServices.Unsafe.dll",
        "tools/analyzers/dotnet/cs/System.Text.Encoding.CodePages.dll",
        "tools/analyzers/dotnet/cs/System.Text.Encodings.Web.dll",
        "tools/analyzers/dotnet/cs/System.Text.Json.dll",
        "tools/analyzers/dotnet/cs/System.Threading.Tasks.Extensions.dll"
    ];

    private static readonly string[] ExpectedToolEntries = [
        "tools/net9/Microsoft.Z3.dll",
        "tools/net9/SharpProof.CompilerArtifact.dll",
        "tools/net9/SharpProof.Dataflow.dll",
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
        "tools/net9/System.IO.Pipelines.dll",
        "tools/net9/System.Text.Encodings.Web.dll",
        "tools/net9/System.Text.Json.dll",
        "tools/net9/runtimes/win/lib/net9.0/System.Text.Encodings.Web.dll",
        "tools/net9/runtimes/win-x64/native/libz3.dll"
    ];

    private static readonly string[] ExpectedNativeZ3Entries = [
        "tools/net9/runtimes/win-x64/native/libz3.dll"
    ];
    private static readonly string[] ExpectedDependencyAttributes = [
        "id",
        "version"
    ];

    [Test]
    public async Task PackageGraphAndLayoutsAreExact() {
        var feed = await PackagedProductFeed.GetAsync();

        VerifyPackageGraph(feed);
        VerifyPackageLayouts(feed);
    }

    [Test]
    public async Task SymbolPackagesAreExactPortableAndSourceLinked() {
        var feed = await PackagedProductFeed.GetAsync();
        var repositoryRoot = FindRepositoryRoot();
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

        foreach (var package in feed.Packages) {
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
    public async Task ReleaseEvidenceIsDeterministicAndComplete() {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = ReleaseEvidenceWorkspace.Create();
        var script = Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "New-SharpProofReleaseEvidence.ps1");
        var arguments = new[] {
            "-NoLogo",
            "-NoProfile",
            "-File",
            script,
            "-PackageSource",
            feed.Source,
            "-SbomPath",
            workspace.SbomPath,
            "-OutputDirectory",
            workspace.OutputDirectory
        };
        var firstRun = await RunProcessAsync(
            FindRepositoryRoot(),
            "pwsh",
            arguments);
        Assert.That(firstRun.ExitCode, Is.Zero, firstRun.Output);
        var firstManifest = await File.ReadAllBytesAsync(
            workspace.ManifestPath);
        var firstSums = await File.ReadAllBytesAsync(
            workspace.SumsPath);
        var secondRun = await RunProcessAsync(
            FindRepositoryRoot(),
            "pwsh",
            arguments);
        Assert.That(secondRun.ExitCode, Is.Zero, secondRun.Output);
        Assert.That(
            await File.ReadAllBytesAsync(workspace.ManifestPath),
            Is.EqualTo(firstManifest));
        Assert.That(
            await File.ReadAllBytesAsync(workspace.SumsPath),
            Is.EqualTo(firstSums));
        Assert.That(
            firstManifest.Take(3),
            Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.That(Encoding.UTF8.GetString(firstManifest), Does.Not.Contain('\r'));
        Assert.That(Encoding.UTF8.GetString(firstSums), Does.Not.Contain('\r'));

        using var document = JsonDocument.Parse(firstManifest);
        var root = document.RootElement;
        Assert.That(
            root.GetProperty("schemaVersion").GetInt32(),
            Is.EqualTo(1));
        Assert.That(
            root.GetProperty("packageVersion").GetString(),
            Is.EqualTo(feed.Version));
        var artifacts = root.GetProperty("artifacts")
            .EnumerateArray()
            .ToArray();
        Assert.That(artifacts, Has.Length.EqualTo(7));
        Assert.That(
            artifacts.Select(static artifact =>
                artifact.GetProperty("kind").GetString()),
            Is.EquivalentTo([
                "package",
                "package",
                "package",
                "symbols",
                "symbols",
                "symbols",
                "sbom"
            ]));
        foreach (var artifact in artifacts) {
            var fileName = artifact.GetProperty("fileName").GetString() ??
                throw new InvalidDataException(
                    "Release artifact fileName is null.");
            var kind = artifact.GetProperty("kind").GetString();
            var path = kind == "sbom"
                ? workspace.SbomPath
                : Path.Combine(feed.Source, fileName);
            var hash = Convert.ToHexString(
                SHA256.HashData(
                    await File.ReadAllBytesAsync(path)));
            Assert.That(
                artifact.GetProperty("sha256").GetString(),
                Is.EqualTo(hash).IgnoreCase,
                fileName);
            Assert.That(
                artifact.GetProperty("bytes").GetInt64(),
                Is.EqualTo(new FileInfo(path).Length),
                fileName);
        }
        Assert.That(
            await File.ReadAllLinesAsync(workspace.SumsPath),
            Is.EqualTo(artifacts.Select(static artifact =>
                artifact.GetProperty("sha256").GetString() +
                "  " +
                artifact.GetProperty("fileName").GetString())));
    }

    [Test]
    public async Task PortablePackageRunsAdvisoryAndRequiresVerifier() {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WriteConsumer(feed.Version, PackagedProductFeed.PortablePackageId);
        var restore = await RestoreConsumerAsync(workspace, feed);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

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
            "/nodeReuse:false",
            "-p:UseSharedCompilation=false");
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
            "-p:UseSharedCompilation=false",
            "-p:SharpProofVerify=true");
        Assert.That(
            explicitVerification.ExitCode,
            Is.Not.Zero,
            explicitVerification.Output);
        Assert.That(
            explicitVerification.Output,
            Does.Contain(
                "requires the matching SharpProof.Verifier.Win-x64 package"));

        var strict = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "build",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false",
            "-p:UseSharedCompilation=false",
            "-p:SharpProofProfile=strict");
        Assert.That(strict.ExitCode, Is.Not.Zero, strict.Output);
        Assert.That(
            strict.Output,
            Does.Contain(
                "requires the matching SharpProof.Verifier.Win-x64 package"));
    }

    [Test]
    public async Task VerifierPackageTransitivelySuppliesPortableProduct() {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PackageWorkspace.Create();
        workspace.WriteConsumer(
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
                .Where(static item => item.Role == "Dependency")
                .Select(static item => item.FileName),
            Is.EquivalentTo(ExpectedAnalyzerDependencyFileNames));

        var advisory = await BuildAnalyzerConsumerAsync(workspace);
        Assert.That(advisory.ExitCode, Is.Zero, advisory.Output);
        Assert.That(advisory.Output, Does.Contain("SP0045"));

        var verification = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "build",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false",
            "-p:UseSharedCompilation=false",
            "-p:SharpProofVerify=true");
        if (OperatingSystem.IsWindows() &&
            RuntimeInformation.ProcessArchitecture == Architecture.X64 &&
            RuntimeInformation.OSArchitecture == Architecture.X64) {
            Assert.That(
                verification.ExitCode,
                Is.Zero,
                verification.Output);
            Assert.That(
                verification.Output,
                Does.Contain("SharpProof Proven"));
            Assert.That(File.Exists(workspace.ResultPath), Is.True);
        }
        else {
            Assert.That(
                verification.ExitCode,
                Is.Not.Zero,
                verification.Output);
            Assert.That(
                verification.Output,
                Does.Contain(
                    "supported only on Windows x64"));
        }
    }

    [Test]
    public async Task VerifierPackageRejectsANonX64BuildProcess() {
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
            "-p:UseSharedCompilation=false",
            "-p:SharpProofVerify=true",
            "-p:_SharpProofVerifierProcessArchitecture=X86");

        Assert.That(
            verification.ExitCode,
            Is.Not.Zero,
            verification.Output);
        Assert.That(
            verification.Output,
            Does.Contain("supported only on Windows x64"));
    }

    [Test]
    public async Task PackedAnalyzerReportsContractCorrectnessRegressions() {
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
    public async Task PackedConsumerProbeCapturesFinalCompilerInputs() {
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
        var firstChecksum = SnapshotChecksum(firstBytes);

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
            SnapshotChecksum(inputBytes),
            Is.Not.EqualTo(firstChecksum));

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
            SnapshotChecksum(configuredBytes),
            Is.Not.EqualTo(SnapshotChecksum(inputBytes)));
    }

    private static Task<ProcessResult> RestoreConsumerAsync(
        PackageWorkspace workspace,
        PackagedProductFeed feed) =>
        RunDotNetAsync(
            workspace.ConsumerDirectory,
            "restore",
            workspace.ConsumerProject,
            "--nologo",
            "/nodeReuse:false",
            "--source",
            feed.Source,
            "--packages",
            workspace.PackageCache);

    private static Task<ProcessResult> BuildAnalyzerConsumerAsync(
        PackageWorkspace workspace) =>
        RunDotNetAsync(
            workspace.ConsumerDirectory,
            "build",
            workspace.ConsumerProject,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false",
            "-p:UseSharedCompilation=false");

    private static Task<ProcessResult> RebuildCompilerProbeConsumerAsync(
        PackageWorkspace workspace,
        params (string Name, string Value)[] properties) {
        var arguments = new List<string> {
            "build",
            workspace.ConsumerProject,
            "-t:Rebuild",
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "/nodeReuse:false",
            "-p:UseSharedCompilation=false"
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
        string metadataValue) =>
        RebuildCompilerProbeConsumerAsync(
            workspace,
            ("SharpProofProfile", "advisory"),
            (
                CompilerProbeContract.OutputPathPropertyName,
                workspace.ProbeOutputPath),
            (
                CompilerProbeContract.GlobalValuePropertyName,
                globalValue),
            ("SharpProofProbeAdditionalMetadata", metadataValue));

    private static string SnapshotChecksum(byte[] snapshot) =>
        Convert.ToHexString(SHA256.HashData(snapshot));

    private static void VerifyProbeSnapshot(
        byte[] snapshot,
        string input,
        string globalValue,
        string metadataValue) {
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

    private static string TextChecksum(string value) =>
        Convert.ToHexString(
            SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)));

    private static int CountDiagnosticLines(string output, string id) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains(
                ": error " + id + ":",
                StringComparison.Ordinal));

    private static void VerifyPackageGraph(PackagedProductFeed feed) {
        var expectedDependencies =
            new Dictionary<string, (string Id, string Version)[]>(
                StringComparer.Ordinal) {
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
        foreach (var package in feed.Packages) {
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
                Assert.That(
                    dependency.Attributes()
                        .Select(static attribute =>
                            attribute.Name.LocalName),
                    Is.EquivalentTo(ExpectedDependencyAttributes),
                    package.Id +
                    " dependency metadata must not filter assets.");
        }
    }

    private static void VerifyPackageLayouts(PackagedProductFeed feed) {
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
                "buildTransitive/SharpProof.props",
                "buildTransitive/SharpProof.targets",
                "LICENSE",
                "package/services/metadata/core-properties/" +
                    "<generated>.psmdcp",
                "README.md",
                "SharpProof.nuspec",
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
                "buildTransitive/SharpProof.Verifier.Win-x64.props",
                "buildTransitive/SharpProof.Verifier.Win-x64.targets",
                "LICENSE",
                "package/services/metadata/core-properties/" +
                    "<generated>.psmdcp",
                "README.md",
                "SharpProof.Verifier.Win-x64.nuspec",
                "THIRD-PARTY-NOTICES.txt",
                .. ExpectedToolEntries
            ]);
        VerifyVerifierLayout(verifier.Path);

        var allEntries = feed.Packages.SelectMany(package => {
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
        string[] expectedEntries) {
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
        string commit) {
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
        foreach (var pdbEntry in expectedPdbEntries) {
            var entry = symbols.GetEntry(pdbEntry) ??
                throw new InvalidDataException(
                    "Symbol package entry was not found: " + pdbEntry);
            VerifyPortablePdbSourceLink(entry, commit);
        }
    }

    private static void VerifyPortablePdbSourceLink(
        ZipArchiveEntry entry,
        string commit) {
        using var image = new MemoryStream();
        using (var stream = entry.Open())
            stream.CopyTo(image);
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
        string commit) {
        using var archive = ZipFile.OpenRead(packagePath);
        var nuspec = archive.Entries.Single(entry =>
            entry.FullName.EndsWith(
                ".nuspec",
                StringComparison.OrdinalIgnoreCase));
        using var stream = nuspec.Open();
        var document = XDocument.Load(stream);
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

    private static string NormalizeGeneratedPackageEntry(string entry) {
        const string coreProperties =
            "package/services/metadata/core-properties/";
        if (entry.StartsWith(coreProperties, StringComparison.Ordinal) &&
            entry.EndsWith(".psmdcp", StringComparison.Ordinal))
            return coreProperties + "<generated>.psmdcp";
        return entry;
    }

    private static bool IsNativeZ3(string entry) {
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

    private static void VerifyPortableLayout(string packagePath) {
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
        Assert.That(analyzerEntries, Is.Empty);
        Assert.That(
            conditionalAnalyzerEntries,
            Is.EquivalentTo(ExpectedConditionalAnalyzerEntries));
        Assert.That(
            conditionalAnalyzerEntries,
            Has.None.Matches<string>(
                entry =>
                    entry.Contains("Microsoft.Z3", StringComparison.Ordinal) ||
                    entry.Contains("libz3", StringComparison.OrdinalIgnoreCase) ||
                    entry.Contains("NativeSmtLocator", StringComparison.Ordinal)));

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
                .And.Not.Contain(@"..\tools\analyzers"));
        Assert.That(
            ReadArchiveText(
                archive,
                "buildTransitive/SharpProof.targets"),
            Does.Not.Contain("*.dll")
                .And.Contain(
                    "<SharpProofAnalyzerRole>EntryPoint</SharpProofAnalyzerRole>")
                .And.Contain(
                    "<SharpProofAnalyzerRole>Dependency</SharpProofAnalyzerRole>"));
        Assert.That(
            entries,
            Has.None.StartsWith("lib/")
                .And.None.StartsWith("tools/net9/"));
    }

    private static void VerifyVerifierLayout(string packagePath) {
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
            Assert.That(
                ReadArchiveText(archive, dependencies),
                Does.Not.Contain("\"Microsoft.CodeAnalysis/\"")
                    .And.Not.Contain("\"Microsoft.CodeAnalysis.CSharp/\"")
                    .And.Not.Contain("SharpProof.Attributes")
                    .And.Not.Contain("SharpProof.Contracts")
                    .And.Not.Contain("SharpProof.Frontend"),
                dependencies);
        Assert.That(
            entries.Where(static entry =>
                IsNativeZ3(entry)),
            Is.EquivalentTo(ExpectedNativeZ3Entries));
    }

    private static string ReadArchiveText(
        ZipArchive archive,
        string entryPath) {
        var entry = archive.GetEntry(entryPath) ??
            throw new InvalidOperationException(
                "Package entry was not found: " + entryPath);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static async Task<ProcessResult> RunDotNetAsync(
        string workingDirectory,
        params string[] arguments) =>
        await RunProcessAsync(
            workingDirectory,
            "dotnet",
            arguments);

    private static async Task<ProcessResult> RunProcessAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments) {
        var startInfo = new ProcessStartInfo {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            (await standardOutput) + Environment.NewLine +
            (await standardError));
    }

    private static (string FileName, string Role)[]
        GetPackagedAnalyzerItems(string output) {
        using var document = JsonDocument.Parse(output);
        var result = new List<(string FileName, string Role)>();
        foreach (var item in document.RootElement
            .GetProperty("Items")
            .GetProperty("Analyzer")
            .EnumerateArray()) {
            var identity = item.GetProperty("Identity").GetString();
            if (identity == null ||
                !identity.Replace('\\', '/').Contains(
                    "/tools/analyzers/dotnet/cs/",
                    StringComparison.Ordinal))
                continue;
            result.Add((
                Path.GetFileName(identity),
                item.GetProperty("SharpProofAnalyzerRole").GetString() ?? ""));
        }
        return [.. result];
    }

    private static string FindRepositoryRoot() {
        var directory = new DirectoryInfo(
            typeof(SharpProofWorker).Assembly.Location);
        while (directory != null) {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "SharpProof.Release.props")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Repository root was not found.");
    }

    private sealed class PackageWorkspace : IDisposable {
        private readonly string _root;

        private PackageWorkspace(string root) {
            _root = root;
            PackageCache = Path.Combine(root, "package cache");
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

        internal string PackageCache { get; }
        internal string ConsumerDirectory { get; }
        internal string ConsumerProject { get; }
        internal string ResultPath { get; }
        internal string ProbeOutputPath { get; }
        internal string ProbeInputPath { get; }

        internal static PackageWorkspace Create() {
            var root = Path.Combine(
                Path.GetTempPath(),
                "SharpProof.Package.Layout.Test",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.Copy(
                Path.Combine(FindRepositoryRoot(), "global.json"),
                Path.Combine(root, "global.json"));
            return new PackageWorkspace(root);
        }

        internal void WriteConsumer(string version, string packageId) =>
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

        internal void WriteAnalyzerConsumer(
            string version,
            string packageId,
            string source,
            string features,
            params string[] enabledDiagnosticIds) {
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

        internal void WriteSource(string source) =>
            File.WriteAllText(
                Path.Combine(ConsumerDirectory, "Subject.cs"),
                source,
                new System.Text.UTF8Encoding(false));

        internal void WriteCompilerProbeConsumer(string version) {
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
                CompilerProbeContract.AssemblyPath);
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

        internal void WriteProbeInput(string value) =>
            File.WriteAllText(
                ProbeInputPath,
                value + "\n",
                new System.Text.UTF8Encoding(false));

        public void Dispose() {
            var resolved = Path.GetFullPath(_root);
            var expectedRoot = Path.GetFullPath(
                Path.Combine(
                    Path.GetTempPath(),
                    "SharpProof.Package.Layout.Test"));
            if (!resolved.StartsWith(
                    expectedRoot + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected test directory.");
            if (Directory.Exists(resolved))
                Directory.Delete(resolved, recursive: true);
        }
    }

    private sealed class ReleaseEvidenceWorkspace : IDisposable {
        private readonly string _root;

        private ReleaseEvidenceWorkspace(string root) {
            _root = root;
            OutputDirectory = Path.Combine(root, "output");
            SbomPath = Path.Combine(root, "SharpProof.spdx.json");
            ManifestPath = Path.Combine(
                OutputDirectory,
                "SharpProof.release.json");
            SumsPath = Path.Combine(OutputDirectory, "SHA256SUMS");
            File.WriteAllText(
                SbomPath,
                """
                {
                  "spdxVersion": "SPDX-2.3",
                  "dataLicense": "CC0-1.0",
                  "SPDXID": "SPDXRef-DOCUMENT",
                  "name": "SharpProof package test",
                  "documentNamespace": "https://github.com/alexyorke/SharpProof/test"
                }
                """,
                new UTF8Encoding(false));
        }

        internal string OutputDirectory { get; }
        internal string SbomPath { get; }
        internal string ManifestPath { get; }
        internal string SumsPath { get; }

        internal static ReleaseEvidenceWorkspace Create() {
            var root = Path.Combine(
                Path.GetTempPath(),
                "SharpProof.ReleaseEvidence.Test",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new ReleaseEvidenceWorkspace(root);
        }

        public void Dispose() {
            var resolved = Path.GetFullPath(_root);
            var expectedRoot = Path.GetFullPath(
                Path.Combine(
                    Path.GetTempPath(),
                    "SharpProof.ReleaseEvidence.Test"));
            if (!resolved.StartsWith(
                    expectedRoot + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected release-evidence " +
                    "test directory.");
            if (Directory.Exists(resolved))
                Directory.Delete(resolved, recursive: true);
        }
    }

    private readonly record struct ProcessResult(
        int ExitCode,
        string Output);
}
