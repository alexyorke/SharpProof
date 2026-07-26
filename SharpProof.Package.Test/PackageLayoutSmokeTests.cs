using System.Diagnostics;
using System.IO.Compression;
using System.Security;
using NUnit.Framework;
using SharpProof.Worker;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class PackageLayoutSmokeTests {
    [Test]
    public async Task MigrationCodeFixShipsAsASeparatePackage() {
        using var workspace = PackageWorkspace.Create();
        var pack = await RunDotNetAsync(
            FindRepositoryRoot(),
            "pack",
            Path.Combine(
                FindRepositoryRoot(),
                "SharpProof.Migration",
                "SharpProof.Migration.csproj"),
            "-c",
            "Release",
            "--nologo",
            "/nodeReuse:false",
            "-p:UseSharedCompilation=false",
            "-p:GeneratePackageOnBuild=false",
            "--output",
            workspace.PackageSource);
        Assert.That(pack.ExitCode, Is.Zero, pack.Output);

        var packagePath = Directory
            .EnumerateFiles(
                workspace.PackageSource,
                "SharpProof.Migration.*.nupkg")
            .Single();
        using var archive = ZipFile.OpenRead(packagePath);
        var analyzerEntries = archive.Entries
            .Select(static entry => entry.FullName)
            .Where(static entry => entry.StartsWith(
                "analyzers/dotnet/cs/",
                StringComparison.Ordinal))
            .ToArray();

        Assert.That(
            analyzerEntries,
            Is.EquivalentTo(new[] {
                "analyzers/dotnet/cs/SharpProof.Migration.dll",
                "analyzers/dotnet/cs/System.Composition.AttributedModel.dll",
                "analyzers/dotnet/cs/System.Composition.Convention.dll",
                "analyzers/dotnet/cs/System.Composition.Hosting.dll",
                "analyzers/dotnet/cs/System.Composition.Runtime.dll",
                "analyzers/dotnet/cs/System.Composition.TypedParts.dll"
            }));
        Assert.That(
            analyzerEntries,
            Has.None.Contains("SharpProof.Legacy.Attributes.dll"));
    }

    [Test]
    public async Task PackedAnalyzerIsThinAndPackagedWorkerRuns() {
        using var workspace = PackageWorkspace.Create();
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

        var packagePath = Directory
            .EnumerateFiles(workspace.PackageSource, "SharpProof.*.nupkg")
            .Single();
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
            disabledItems.Output,
            Does.Not.Contain("SharpProof.Analyzer.dll"));
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

        if (!OperatingSystem.IsWindows())
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
            Is.EquivalentTo(new[] {
                "tools/analyzers/dotnet/cs/Microsoft.CodeAnalysis.AnalyzerUtilities.dll",
                "tools/analyzers/dotnet/cs/SharpProof.Analyzer.dll",
                "tools/analyzers/dotnet/cs/SharpProof.Attributes.dll",
                "tools/analyzers/dotnet/cs/SharpProof.ContractForGenerator.dll",
                "tools/analyzers/dotnet/cs/SharpProof.Contracts.dll",
                "tools/analyzers/dotnet/cs/SharpProof.Dataflow.dll",
                "tools/analyzers/dotnet/cs/SharpProof.Effects.dll",
                "tools/analyzers/dotnet/cs/SharpProof.Frontend.dll",
                "tools/analyzers/dotnet/cs/SharpProof.Ir.dll",
                "tools/analyzers/dotnet/cs/SharpProof.Specs.dll",
                "tools/analyzers/dotnet/cs/System.Buffers.dll",
                "tools/analyzers/dotnet/cs/System.Collections.Immutable.dll",
                "tools/analyzers/dotnet/cs/System.Memory.dll",
                "tools/analyzers/dotnet/cs/System.Numerics.Vectors.dll",
                "tools/analyzers/dotnet/cs/System.Reflection.Metadata.dll",
                "tools/analyzers/dotnet/cs/System.Runtime.CompilerServices.Unsafe.dll",
                "tools/analyzers/dotnet/cs/System.Text.Encoding.CodePages.dll",
                "tools/analyzers/dotnet/cs/System.Threading.Tasks.Extensions.dll"
            }));
        Assert.That(
            conditionalAnalyzerEntries,
            Has.None.Matches<string>(
                entry =>
                    entry.Contains("SharpProof.Symbolic", StringComparison.Ordinal) ||
                    entry.Contains("SharpProof.ProofCore", StringComparison.Ordinal) ||
                    entry.Contains("Microsoft.Z3", StringComparison.Ordinal) ||
                    entry.Contains("libz3", StringComparison.OrdinalIgnoreCase) ||
                    entry.Contains("NativeSmtLocator", StringComparison.Ordinal)));

        Assert.That(
            entries,
            Does.Contain("buildTransitive/SharpProof.props"));
        Assert.That(
            entries,
            Does.Contain("buildTransitive/SharpProof.targets"));
        var toolEntries = entries
            .Where(entry => entry.StartsWith(
                "tools/net8/",
                StringComparison.Ordinal))
            .ToArray();
        Assert.That(
            toolEntries,
            Is.EquivalentTo(new[] {
                "tools/net8/libz3.dll",
                "tools/net8/Microsoft.CodeAnalysis.AnalyzerUtilities.dll",
                "tools/net8/Microsoft.CodeAnalysis.CSharp.dll",
                "tools/net8/Microsoft.CodeAnalysis.dll",
                "tools/net8/Microsoft.Z3.dll",
                "tools/net8/SharpProof.Attributes.dll",
                "tools/net8/SharpProof.Contracts.dll",
                "tools/net8/SharpProof.Dataflow.dll",
                "tools/net8/SharpProof.Frontend.dll",
                "tools/net8/SharpProof.Ir.dll",
                "tools/net8/SharpProof.Smt.dll",
                "tools/net8/SharpProof.Specs.dll",
                "tools/net8/SharpProof.Verify.dll",
                "tools/net8/SharpProof.Worker.deps.json",
                "tools/net8/SharpProof.Worker.dll",
                "tools/net8/SharpProof.Worker.Launcher.deps.json",
                "tools/net8/SharpProof.Worker.Launcher.dll",
                "tools/net8/SharpProof.Worker.Launcher.runtimeconfig.json",
                "tools/net8/SharpProof.Worker.Protocol.dll",
                "tools/net8/SharpProof.Worker.runtimeconfig.json",
                "tools/net8/System.Collections.Immutable.dll",
                "tools/net8/System.IO.Pipelines.dll",
                "tools/net8/System.Reflection.Metadata.dll",
                "tools/net8/System.Text.Encodings.Web.dll",
                "tools/net8/System.Text.Json.dll",
                "tools/net8/runtimes/win-x64/native/libz3.dll"
            }));
        Assert.That(
            entries,
            Has.None.Contains("SharpProof.SymbolicCli"));
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
                "result.v2.json");
            Directory.CreateDirectory(PackageSource);
            Directory.CreateDirectory(ConsumerDirectory);
        }

        internal string PackageSource { get; }
        internal string PackageCache { get; }
        internal string ConsumerDirectory { get; }
        internal string ConsumerProject { get; }
        internal string ResultPath { get; }

        internal static PackageWorkspace Create() {
            var root = Path.Combine(
                Path.GetTempPath(),
                "SharpProof.Package.Layout.Test",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new PackageWorkspace(root);
        }

        internal void WriteConsumer(string version) {
            var escapedVersion = SecurityElement.Escape(version);
            File.WriteAllText(
                Path.Combine(ConsumerDirectory, "Subject.cs"),
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
                new System.Text.UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(ConsumerDirectory, ".editorconfig"),
                """
                root = true

                [*.cs]
                dotnet_diagnostic.SP0045.severity = warning
                """,
                new System.Text.UTF8Encoding(false));
            File.WriteAllText(
                ConsumerProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <LangVersion>12.0</LangVersion>
                    <SharpProofMode>effects</SharpProofMode>
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
