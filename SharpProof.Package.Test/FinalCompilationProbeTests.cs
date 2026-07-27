using System.Diagnostics;
using System.Security;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerProbe.TestAsset;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class FinalCompilationProbeTests {
    private const string NetStandardTargetFramework = "netstandard2.0";
    private const string NetTargetFramework = "net8.0";

    [Test]
    public async Task MultiTargetBuildWritesOneIsolatedFinalCompilationPerTargetFramework() {
        using var workspace = ProbeWorkspace.Create();
        workspace.WriteConsumer(
            targetFrameworks:
                NetStandardTargetFramework + ";" + NetTargetFramework,
            enableProbe: true);

        var build = await workspace.BuildAsync();

        Assert.That(build.ExitCode, Is.Zero, build.Output);
        var artifactPaths = workspace.GetArtifactPaths();
        Assert.That(artifactPaths, Has.Length.EqualTo(2));
        var artifacts = new Dictionary<string, ProbeArtifact>(
            StringComparer.Ordinal);
        foreach (var artifactPath in artifactPaths) {
            var artifact = await ProbeArtifact.ReadAsync(artifactPath);
            artifacts.Add(artifact.TargetFramework, artifact);
        }

        Assert.That(
            artifacts.Keys,
            Is.EquivalentTo(new[] {
                NetStandardTargetFramework,
                NetTargetFramework
            }));
        var netStandard = artifacts[NetStandardTargetFramework];
        var net = artifacts[NetTargetFramework];
        using (Assert.EnterMultipleScope()) {
            Assert.That(netStandard.Options, Is.Not.Empty);
            Assert.That(net.Options, Is.Not.Empty);
            Assert.That(netStandard.Options, Is.Not.EqualTo(net.Options));
            Assert.That(netStandard.Options, Does.Not.Contain("NET8_0"));
            Assert.That(net.Options, Does.Not.Contain("NETSTANDARD2_0"));
            Assert.That(netStandard.PortableReferences, Is.Not.Empty);
            Assert.That(net.PortableReferences, Is.Not.Empty);
            Assert.That(
                netStandard.PortableReferences,
                Is.Not.EqualTo(net.PortableReferences));
            Assert.That(
                netStandard.FrameworkReferences,
                Has.None.Contains(NetTargetFramework));
            Assert.That(
                net.FrameworkReferences,
                Has.None.Contains(NetStandardTargetFramework));
            Assert.That(
                netStandard.SyntaxTrees,
                Has.Some.Contains(
                    CompilerProbeContract.GlobalUsingsHintName));
            Assert.That(
                net.SyntaxTrees,
                Has.Some.Contains(
                    CompilerProbeContract.GlobalUsingsHintName));
            Assert.That(
                netStandard.SyntaxTrees,
                Has.Some.Contains("Consumer.GlobalUsings.g.cs"));
            Assert.That(
                net.SyntaxTrees,
                Has.Some.Contains("Consumer.GlobalUsings.g.cs"));
            Assert.That(
                netStandard.SyntaxTrees,
                Has.None.Contains(NetTargetFramework));
            Assert.That(
                net.SyntaxTrees,
                Has.None.Contains(NetStandardTargetFramework));
            Assert.That(
                netStandard.AdditionalFiles,
                Has.Some.Contains(NetStandardTargetFramework));
            Assert.That(
                net.AdditionalFiles,
                Has.Some.Contains(NetTargetFramework));
            Assert.That(
                netStandard.AdditionalFiles,
                Has.None.Contains(NetTargetFramework));
            Assert.That(
                net.AdditionalFiles,
                Has.None.Contains(NetStandardTargetFramework));
        }
    }

    [Test]
    public async Task PackedCollectorSealsActualGeneratorOutput() {
        using var workspace = ProbeWorkspace.Create();
        var packagePath = await workspace.PackSharpProofAsync();
        workspace.WritePackedConsumer(GetPackageVersion(packagePath));
        var handwrittenSource =
            await File.ReadAllBytesAsync(workspace.SubjectPath);

        var restore = await workspace.RestoreAsync();
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var first = await workspace.RebuildAsync();
        Assert.That(first.ExitCode, Is.Zero, first.Output);
        var firstOracle = await ProbeArtifact.ReadAsync(
            workspace.PackedProbeArtifactPath);
        var firstSeal = await CompilationSeal.ReadAsync(
            workspace.CompilationSealPath);
        Assert.That(
            firstOracle.SyntaxTreePaths,
            Has.Some.EndsWith(CompilerProbeContract.GlobalUsingsHintName));
        Assert.That(
            firstOracle.SyntaxTreePaths,
            Has.Some.EndsWith(CompilerProbeContract.ContractHintName));
        Assert.That(
            firstSeal.SyntaxTreeCount,
            Is.EqualTo(firstOracle.SyntaxTreeCount));
        var firstGeneratedChecksum = firstOracle.GetTreeChecksum(
            CompilerProbeContract.ContractHintName);
        var firstHandwrittenChecksum =
            firstOracle.GetTreeChecksum("Subject.cs");

        var noOp = await workspace.RebuildAsync();
        Assert.That(noOp.ExitCode, Is.Zero, noOp.Output);
        Assert.That(
            await File.ReadAllBytesAsync(workspace.CompilationSealPath),
            Is.EqualTo(firstSeal.Bytes));
        var noOpOracle = await ProbeArtifact.ReadAsync(
            workspace.PackedProbeArtifactPath);
        Assert.That(
            noOpOracle.GetTreeChecksum(
                CompilerProbeContract.ContractHintName),
            Is.EqualTo(firstGeneratedChecksum));

        workspace.WriteProbeInput("changed-generator-input");
        var changed = await workspace.RebuildAsync();
        Assert.That(changed.ExitCode, Is.Zero, changed.Output);
        var changedOracle = await ProbeArtifact.ReadAsync(
            workspace.PackedProbeArtifactPath);
        var changedSeal = await CompilationSeal.ReadAsync(
            workspace.CompilationSealPath);
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                await File.ReadAllBytesAsync(workspace.SubjectPath),
                Is.EqualTo(handwrittenSource));
            Assert.That(
                changedOracle.GetTreeChecksum("Subject.cs"),
                Is.EqualTo(firstHandwrittenChecksum));
            Assert.That(
                changedOracle.GetTreeChecksum(
                    CompilerProbeContract.ContractHintName),
                Is.Not.EqualTo(firstGeneratedChecksum));
            Assert.That(
                changedSeal.SyntaxTreeCount,
                Is.EqualTo(changedOracle.SyntaxTreeCount));
            Assert.That(
                changedSeal.CompilationSha256,
                Is.Not.EqualTo(firstSeal.CompilationSha256));
        }
    }

    [TestCase(ProbeSuppression.DesignTimeBuild)]
    [TestCase(ProbeSuppression.ProfileOff)]
    [TestCase(ProbeSuppression.MissingControl)]
    public async Task SuppressedCompilationDoesNotWriteAnArtifact(
        ProbeSuppression suppression) {
        using var workspace = ProbeWorkspace.Create();
        workspace.WriteConsumer(
            targetFrameworks: NetTargetFramework,
            enableProbe: suppression != ProbeSuppression.MissingControl,
            profile: suppression == ProbeSuppression.ProfileOff
                ? "off"
                : "advisory",
            designTimeBuild:
                suppression == ProbeSuppression.DesignTimeBuild);

        var build = await workspace.BuildAsync();

        Assert.That(build.ExitCode, Is.Zero, build.Output);
        Assert.That(workspace.GetArtifactPaths(), Is.Empty);
    }

    public enum ProbeSuppression {
        DesignTimeBuild,
        ProfileOff,
        MissingControl
    }

    private sealed class ProbeArtifact {
        private ProbeArtifact(
            string targetFramework,
            string options,
            string[] syntaxTrees,
            string[] portableReferences,
            string[] additionalFiles) {
            TargetFramework = targetFramework;
            Options = options;
            SyntaxTrees = syntaxTrees;
            PortableReferences = portableReferences;
            AdditionalFiles = additionalFiles;
        }

        internal string TargetFramework { get; }
        internal string Options { get; }
        internal string[] SyntaxTrees { get; }
        internal string[] PortableReferences { get; }
        internal string[] AdditionalFiles { get; }
        internal int SyntaxTreeCount => SyntaxTrees.Length;
        internal string[] SyntaxTreePaths =>
            [.. SyntaxTrees.Select(static tree => {
                using var document = JsonDocument.Parse(tree);
                return document.RootElement.GetProperty("path").GetString() ??
                    string.Empty;
            })];
        internal string[] FrameworkReferences =>
            [.. PortableReferences.Where(static reference =>
                    !reference.Contains(
                        "SharpProof.Attributes.dll",
                        StringComparison.OrdinalIgnoreCase))];

        internal static async Task<ProbeArtifact> ReadAsync(string path) {
            var text = await File.ReadAllTextAsync(path);
            Assert.That(text, Does.Not.Contain('\r'), path);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            Assert.That(
                root.GetProperty("schema").GetString(),
                Is.EqualTo(CompilerProbeContract.SchemaName),
                path);
            Assert.That(
                root.GetProperty("schemaVersion").GetInt32(),
                Is.EqualTo(CompilerProbeContract.SchemaVersion),
                path);
            var canonical = JsonSerializer.Serialize(root);
            Assert.That(
                text,
                Is.EqualTo(canonical).Or.EqualTo(canonical + "\n"),
                path);

            var artifactDirectory = Path.GetDirectoryName(path) ??
                throw new InvalidDataException(
                    "The probe artifact has no parent directory.");
            var pathTargetFramework = Path.GetFileName(artifactDirectory);
            if (string.IsNullOrEmpty(pathTargetFramework))
                throw new InvalidDataException(
                    "The probe artifact has no target-framework directory.");
            _ = GetCanonicalRawRows(root, "consumedOptions", path);
            var targetFramework = root.GetProperty("consumedOptions")
                .EnumerateArray()
                .Single(option =>
                    option.GetProperty("key").GetString() ==
                    CompilerProbeContract.GlobalValueOptionKey)
                .GetProperty("value")
                .GetString();
            Assert.That(
                targetFramework,
                Is.EqualTo(pathTargetFramework).And.Not.Empty,
                path);
            return new ProbeArtifact(
                targetFramework!,
                root.GetProperty("options").GetRawText(),
                GetCanonicalSyntaxTrees(root, path),
                GetCanonicalRawRows(
                    root,
                    "portableReferences",
                    path),
                GetCanonicalRawRows(root, "additionalFiles", path));
        }

        internal string GetTreeChecksum(string pathSuffix) {
            var matches = SyntaxTrees
                .Where(tree => {
                    using var document = JsonDocument.Parse(tree);
                    return document.RootElement.GetProperty("path").GetString()?
                        .EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase) ==
                        true;
                })
                .ToArray();
            Assert.That(
                matches,
                Has.Length.EqualTo(1),
                "syntax tree suffix: " + pathSuffix);
            using var match = JsonDocument.Parse(matches[0]);
            return match.RootElement.GetProperty("textSha256").GetString() ??
                string.Empty;
        }

        private static string[] GetCanonicalSyntaxTrees(
            JsonElement root,
            string path) {
            var trees = root.GetProperty("syntaxTrees")
                .EnumerateArray()
                .Select(tree => new {
                    Path = tree.GetProperty("path").GetString() ?? "",
                    Ordinal = tree.GetProperty("ordinal").GetInt32(),
                    Raw = tree.GetRawText()
                })
                .ToArray();
            Assert.That(
                trees.Select(static tree => (tree.Path, tree.Ordinal)),
                Is.EqualTo(trees
                    .OrderBy(static tree => tree.Path, StringComparer.Ordinal)
                    .ThenBy(static tree => tree.Ordinal)
                    .Select(static tree => (tree.Path, tree.Ordinal))),
                path + ": syntaxTrees");
            Assert.That(
                trees.Select(static tree => tree.Raw)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                Is.EqualTo(trees.Length),
                path + ": syntaxTrees");
            return [.. trees.Select(static tree => tree.Raw)];
        }

        private static string[] GetCanonicalRawRows(
            JsonElement root,
            string propertyName,
            string path) {
            var rows = root.GetProperty(propertyName)
                .EnumerateArray()
                .Select(static row => row.GetRawText())
                .ToArray();
            Assert.That(
                rows,
                Is.Ordered.Using<string>(StringComparer.Ordinal),
                path + ": " + propertyName);
            Assert.That(
                rows.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(rows.Length),
                path + ": " + propertyName);
            return rows;
        }
    }

    private sealed record CompilationSeal(
        byte[] Bytes,
        int SyntaxTreeCount,
        string CompilationSha256) {
        internal static async Task<CompilationSeal> ReadAsync(string path) {
            Assert.That(File.Exists(path), Is.True, path);
            var bytes = await File.ReadAllBytesAsync(path);
            var text = Encoding.UTF8.GetString(bytes);
            Assert.That(text, Does.Not.Contain('\r'), path);
            var values = text.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
                .ToDictionary(
                    static row => row[
                        ..row.IndexOf('=', StringComparison.Ordinal)],
                    static row => row[
                        (row.IndexOf('=', StringComparison.Ordinal) + 1)..],
                    StringComparer.Ordinal);
            using (Assert.EnterMultipleScope()) {
                Assert.That(
                    values["schema"],
                    Is.EqualTo("SharpProof.CompilationSeal"),
                    path);
                Assert.That(values["schemaVersion"], Is.EqualTo("1"), path);
                Assert.That(
                    values["compilationSha256"],
                    Does.Match("^[0-9a-f]{64}$"),
                    path);
            }
            return new CompilationSeal(
                bytes,
                int.Parse(
                    values["syntaxTreeCount"],
                    System.Globalization.CultureInfo.InvariantCulture),
                values["compilationSha256"]);
        }
    }

    private sealed class ProbeWorkspace : IDisposable {
        private static readonly string s_workspaceParent = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.FinalProbe");
        private readonly string _root;

        private ProbeWorkspace(string root) {
            _root = root;
            ProjectPath = Path.Combine(root, "Consumer.csproj");
            ArtifactDirectory = Path.Combine(root, "probe");
            PackageSource = Path.Combine(root, "packages");
            PackageCache = Path.Combine(root, "package-cache");
            CompilationSealPath = Path.Combine(root, "seal", "compilation.seal");
            PackedProbeArtifactPath = Path.Combine(
                root,
                "probe",
                NetTargetFramework,
                "final-compilation.json");
            SubjectPath = Path.Combine(root, "Subject.cs");
        }

        internal string ProjectPath { get; }
        internal string ArtifactDirectory { get; }
        internal string PackageSource { get; }
        internal string PackageCache { get; }
        internal string CompilationSealPath { get; }
        internal string PackedProbeArtifactPath { get; }
        internal string SubjectPath { get; }

        internal static ProbeWorkspace Create() {
            var root = Path.Combine(
                s_workspaceParent,
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var workspace = new ProbeWorkspace(root);
            Directory.CreateDirectory(workspace.PackageSource);
            return workspace;
        }

        internal void WriteConsumer(
            string targetFrameworks,
            bool enableProbe,
            string profile = "advisory",
            bool designTimeBuild = false) {
            File.WriteAllText(
                SubjectPath,
                """
                namespace ProbeConsumer;
                public static class Subject {
                    public static int Identity(int value) => value;
                }
                """,
                new System.Text.UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(
                    _root,
                    CompilerProbeContract.AdditionalFileName),
                "probe-input\n",
                new System.Text.UTF8Encoding(false));
            File.WriteAllText(
                ProjectPath,
                CreateProjectXml(
                    targetFrameworks,
                    enableProbe,
                    profile,
                    designTimeBuild),
                new System.Text.UTF8Encoding(false));
        }

        internal void WritePackedConsumer(string packageVersion) {
            File.WriteAllText(
                SubjectPath,
                """
                namespace ProbeConsumer;
                public static class Subject {
                    public static int Identity(int value) => value;
                }
                """,
                new UTF8Encoding(false));
            WriteProbeInput("initial-generator-input");
            File.WriteAllText(
                ProjectPath,
                CreatePackedProjectXml(packageVersion),
                new UTF8Encoding(false));
        }

        internal void WriteProbeInput(string value) =>
            File.WriteAllText(
                Path.Combine(
                    _root,
                    CompilerProbeContract.AdditionalFileName),
                value + "\n",
                new UTF8Encoding(false));

        internal Task<ProcessResult> BuildAsync() =>
            RunDotNetAsync([
                "build",
                ProjectPath,
                "-c",
                "Release",
                "--nologo",
                "/nodeReuse:false",
                "-p:UseSharedCompilation=false"
            ]);

        internal Task<ProcessResult> RebuildAsync() =>
            RunDotNetAsync([
                "build",
                ProjectPath,
                "-t:Rebuild",
                "-c",
                "Release",
                "--no-restore",
                "--nologo",
                "/nodeReuse:false",
                "-p:UseSharedCompilation=false"
            ]);

        internal Task<ProcessResult> RestoreAsync() =>
            RunDotNetAsync([
                "restore",
                ProjectPath,
                "--nologo",
                "/nodeReuse:false",
                "--source",
                PackageSource,
                "--packages",
                PackageCache
            ]);

        internal async Task<string> PackSharpProofAsync() {
            var repositoryRoot = FindRepositoryRoot();
            var packageProject = Path.Combine(
                repositoryRoot,
                "SharpProof.Package",
                "SharpProof.Package.csproj");
            var pack = await RunDotNetAsync(
                [
                    "pack",
                    packageProject,
                    "-c",
                    "Release",
                    "--nologo",
                    "/nodeReuse:false",
                    "-p:UseSharedCompilation=false",
                    "-p:GeneratePackageOnBuild=false",
                    "--output",
                    PackageSource
                ],
                repositoryRoot);
            Assert.That(pack.ExitCode, Is.Zero, pack.Output);
            return Directory.EnumerateFiles(
                    PackageSource,
                    "SharpProof.*.nupkg")
                .Single(path => {
                    var name = Path.GetFileName(path);
                    return name.Length > "SharpProof.".Length &&
                        char.IsDigit(name["SharpProof.".Length]);
                });
        }

        private async Task<ProcessResult> RunDotNetAsync(
            string[] arguments,
            string? workingDirectory = null) {
            var startInfo = new ProcessStartInfo {
                FileName = "dotnet",
                WorkingDirectory = workingDirectory ?? _root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Failed to start dotnet.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ProcessResult(
                process.ExitCode,
                (await standardOutput) + Environment.NewLine +
                (await standardError));
        }

        internal string[] GetArtifactPaths() =>
            Directory.Exists(ArtifactDirectory)
                ? Directory.GetFiles(
                    ArtifactDirectory,
                    "*.json",
                    SearchOption.AllDirectories)
                : [];

        public void Dispose() {
            var resolved = Path.GetFullPath(_root);
            var expectedParent = Path.GetFullPath(s_workspaceParent);
            var relative = Path.GetRelativePath(expectedParent, resolved);
            if (Path.IsPathRooted(relative) ||
                relative == "." ||
                relative == ".." ||
                relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected test directory.");
            if (Directory.Exists(resolved))
                Directory.Delete(resolved, recursive: true);
        }

        private static string CreateProjectXml(
            string targetFrameworks,
            bool enableProbe,
            string profile,
            bool designTimeBuild) {
            var targetFrameworkProperty =
                targetFrameworks.Contains(';', StringComparison.Ordinal)
                    ? "TargetFrameworks"
                    : "TargetFramework";
            var control = enableProbe
                ? "<EmitSharpProofProbe>true</EmitSharpProofProbe>"
                : "";
            var analyzerPath = Escape(CompilerProbeContract.AssemblyPath);
            var attributesPath = Escape(typeof(Contract).Assembly.Location);
            var additionalFile = Escape(
                CompilerProbeContract.AdditionalFileName);
            var outputProperty = Escape(
                CompilerProbeContract.OutputPathPropertyName);
            var globalProperty = Escape(
                CompilerProbeContract.GlobalValuePropertyName);
            var metadataName = Escape(
                CompilerProbeContract.AdditionalFileMetadataName);
            return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <{targetFrameworkProperty}>{Escape(targetFrameworks)}</{targetFrameworkProperty}>
                    <AssemblyName>ProbeConsumer.$(TargetFramework)</AssemblyName>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <LangVersion>12.0</LangVersion>
                    <Nullable>enable</Nullable>
                    <SharpProofProfile>{Escape(profile)}</SharpProofProfile>
                    <DesignTimeBuild>{(designTimeBuild ? "true" : "false")}</DesignTimeBuild>
                    {control}
                    <{outputProperty} Condition="'$(TargetFramework)' != '' And '$(EmitSharpProofProbe)' == 'true' And '$(DesignTimeBuild)' != 'true' And '$(SharpProofProfile)' != 'off'">$(MSBuildProjectDirectory)/probe/$(TargetFramework)/final-compilation.json</{outputProperty}>
                    <{globalProperty}>$(TargetFramework)</{globalProperty}>
                    <CheckForOverflowUnderflow Condition="'$(TargetFramework)' == '{NetTargetFramework}'">true</CheckForOverflowUnderflow>
                    <WarningsAsErrors>CS8032;CS8785</WarningsAsErrors>
                  </PropertyGroup>
                  <ItemGroup>
                    <CompilerVisibleProperty Include="{outputProperty}" />
                    <CompilerVisibleProperty Include="{globalProperty}" />
                    <CompilerVisibleItemMetadata Include="AdditionalFiles" MetadataName="{metadataName}" />
                    <AdditionalFiles Include="{additionalFile}">
                      <{metadataName}>$(TargetFramework)</{metadataName}>
                    </AdditionalFiles>
                    <Analyzer Include="{analyzerPath}"
                              Condition="'$({outputProperty})' != '' And '$(SharpProofProfile)' != 'off' And '$(DesignTimeBuild)' != 'true'" />
                    <Reference Include="SharpProof.Attributes">
                      <HintPath>{attributesPath}</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """;
        }

        private string CreatePackedProjectXml(string packageVersion) {
            var analyzerPath = Escape(CompilerProbeContract.AssemblyPath);
            var additionalFile = Escape(
                CompilerProbeContract.AdditionalFileName);
            var outputProperty = Escape(
                CompilerProbeContract.OutputPathPropertyName);
            var globalProperty = Escape(
                CompilerProbeContract.GlobalValuePropertyName);
            var metadataName = Escape(
                CompilerProbeContract.AdditionalFileMetadataName);
            return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>{NetTargetFramework}</TargetFramework>
                    <AssemblyName>ProbeConsumer.$(TargetFramework)</AssemblyName>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <LangVersion>12.0</LangVersion>
                    <Nullable>enable</Nullable>
                    <SharpProofProfile>advisory</SharpProofProfile>
                    <SharpProofVerify>false</SharpProofVerify>
                    <_SharpProofCompilationSealPath>{Escape(CompilationSealPath)}</_SharpProofCompilationSealPath>
                    <_SharpProofCompilationTargetFramework>$(TargetFramework)</_SharpProofCompilationTargetFramework>
                    <{outputProperty}>{Escape(PackedProbeArtifactPath)}</{outputProperty}>
                    <{globalProperty}>$(TargetFramework)</{globalProperty}>
                    <WarningsAsErrors>AD0001;CS8032;CS8785</WarningsAsErrors>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="SharpProof"
                                      Version="{Escape(packageVersion)}" />
                    <Analyzer Include="{analyzerPath}" />
                    <AdditionalFiles Include="{additionalFile}">
                      <{metadataName}>packed-metadata</{metadataName}>
                    </AdditionalFiles>
                    <CompilerVisibleProperty Include="{outputProperty}" />
                    <CompilerVisibleProperty Include="{globalProperty}" />
                    <CompilerVisibleItemMetadata Include="AdditionalFiles"
                                                 MetadataName="{metadataName}" />
                  </ItemGroup>
                </Project>
                """;
        }

        private static string FindRepositoryRoot() {
            var directory = new DirectoryInfo(
                typeof(Contract).Assembly.Location);
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

        private static string Escape(string value) =>
            SecurityElement.Escape(value) ??
            throw new InvalidOperationException(
                "Failed to escape an MSBuild value.");
    }

    private static string GetPackageVersion(string packagePath) {
        const string prefix = "SharpProof.";
        const string suffix = ".nupkg";
        var name = Path.GetFileName(packagePath);
        return name.Substring(
            prefix.Length,
            name.Length - prefix.Length - suffix.Length);
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
