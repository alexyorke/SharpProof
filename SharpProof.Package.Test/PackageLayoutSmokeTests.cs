using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerProbe.TestAsset;
using SharpProof.Worker;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class PackageLayoutSmokeTests {
    private static readonly string[] ExpectedAnalyzerEntryFileNames = [
        "SharpProof.Analyzer.dll",
        "SharpProof.ContractForGenerator.dll"
    ];

    private static readonly string[] ExpectedAnalyzerDependencyFileNames = [
        "Microsoft.Bcl.AsyncInterfaces.dll",
        "SharpProof.Attributes.dll",
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
        "tools/analyzers/dotnet/cs/SharpProof.Attributes.dll",
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

    [Test]
    public async Task PackedAnalyzerIsThinAndPackagedWorkerRuns() {
        using var workspace = PackageWorkspace.Create();
        var packagePath = await PackSharpProofAsync(workspace);
        VerifyPackageLayout(packagePath);

        workspace.WriteConsumer(GetPackageVersion(packagePath));
        var restore = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "restore",
            workspace.ConsumerProject,
            "--nologo",
            "/nodeReuse:false",
            "--source",
            workspace.PackageSource,
            "--packages",
            workspace.PackageCache);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var disabledItems = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "msbuild",
            workspace.ConsumerProject,
            "-getItem:Analyzer",
            "-p:SharpProofMode=OFF",
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

        if (!OperatingSystem.IsWindows() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            RuntimeInformation.OSArchitecture != Architecture.X64)
            return;

        var build = await RunDotNetAsync(
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
        Assert.That(build.ExitCode, Is.Zero, build.Output);
        Assert.That(build.Output, Does.Contain("SharpProof Proven"));
        Assert.That(File.Exists(workspace.ResultPath), Is.True);
    }

    [Test]
    public async Task PackedAnalyzerReportsContractCorrectnessRegressions() {
        using var workspace = PackageWorkspace.Create();
        var packagePath = await PackSharpProofAsync(workspace);
        workspace.WriteAnalyzerConsumer(
            GetPackageVersion(packagePath),
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
            "all-experimental",
            "SP0027");
        var restore = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "restore",
            workspace.ConsumerProject,
            "--nologo",
            "/nodeReuse:false",
            "--source",
            workspace.PackageSource,
            "--packages",
            workspace.PackageCache);
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
        using var workspace = PackageWorkspace.Create();
        var packagePath = await PackSharpProofAsync(workspace);
        workspace.WriteCompilerProbeConsumer(
            GetPackageVersion(packagePath));
        var restore = await RunDotNetAsync(
            workspace.ConsumerDirectory,
            "restore",
            workspace.ConsumerProject,
            "--nologo",
            "/nodeReuse:false",
            "--source",
            workspace.PackageSource,
            "--packages",
            workspace.PackageCache);
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

    private static async Task<string> PackSharpProofAsync(
        PackageWorkspace workspace) {
        var pack = await RunDotNetAsync(
            FindRepositoryRoot(),
            "pack",
            Path.Combine(
                FindRepositoryRoot(),
                "SharpProof.Package",
                "SharpProof.Package.csproj"),
            "-c",
            "Release",
            "--nologo",
            "/nodeReuse:false",
            "-p:UseSharedCompilation=false",
            "-p:GeneratePackageOnBuild=false",
            "--output",
            workspace.PackageSource);
        Assert.That(pack.ExitCode, Is.Zero, pack.Output);
        return Directory
            .EnumerateFiles(workspace.PackageSource, "SharpProof.*.nupkg")
            .Single();
    }

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

    private static void VerifyPackageLayout(string packagePath) {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Select(entry => entry.FullName)
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
                entry.EndsWith(
                    "/libz3.dll",
                    StringComparison.OrdinalIgnoreCase)),
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

    private static string GetPackageVersion(string packagePath) {
        const string prefix = "SharpProof.";
        const string suffix = ".nupkg";
        var name = Path.GetFileName(packagePath);
        return name.Substring(
            prefix.Length,
            name.Length - prefix.Length - suffix.Length);
    }

    private static async Task<ProcessResult> RunDotNetAsync(
        string workingDirectory,
        params string[] arguments) {
        var startInfo = new ProcessStartInfo {
            FileName = "dotnet",
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
            PackageSource = Path.Combine(root, "package source");
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
            Directory.CreateDirectory(PackageSource);
            Directory.CreateDirectory(ConsumerDirectory);
        }

        internal string PackageSource { get; }
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

        internal void WriteConsumer(string version) =>
            WriteAnalyzerConsumer(
                version,
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
                "all-experimental",
                "SP0045");

        internal void WriteAnalyzerConsumer(
            string version,
            string source,
            string mode,
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
            var escapedMode = SecurityElement.Escape(mode);
            File.WriteAllText(
                ConsumerProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <LangVersion>12.0</LangVersion>
                    <SharpProofMode>{escapedMode}</SharpProofMode>
                    <WarningsAsErrors>AD0001;CS8032;CS8034;CS8785</WarningsAsErrors>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="SharpProof"
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

    private readonly record struct ProcessResult(
        int ExitCode,
        string Output);
}
