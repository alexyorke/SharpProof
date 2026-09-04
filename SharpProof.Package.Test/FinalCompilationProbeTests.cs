using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerProbe.TestAsset;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class FinalCompilationProbeTests
{
    private const string NetStandardTargetFramework = "netstandard2.0";
    private const string NetTargetFramework = "net8.0";
    private static bool IsSupportedWorkerHost =>
        OperatingSystem.IsLinux() &&
        RuntimeInformation.ProcessArchitecture == Architecture.X64 &&
        RuntimeInformation.OSArchitecture == Architecture.X64 &&
        string.Equals(
            Environment.GetEnvironmentVariable("SHARPPROOF_CONTAINER"),
            "1",
            StringComparison.Ordinal);

    internal static void DisposeSharedPackageCache()
    {
        ProbeWorkspace.DisposeSharedPackageCache();
    }

    [Test]
    public async Task MultiTargetBuildWritesOneIsolatedFinalCompilationPerTargetFramework()
    {
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
        foreach (var artifactPath in artifactPaths)
        {
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
        using (Assert.EnterMultipleScope())
        {
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
    public async Task PackedCollectorAttestsAndVerifiesGeneratorOutput()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = ProbeWorkspace.Create();
        workspace.WritePackedConsumer(feed.Version);
        var handwrittenSource =
            await File.ReadAllBytesAsync(workspace.SubjectPath);

        var restore = await workspace.RestoreAsync(feed.Source);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var first = await workspace.RebuildAsync();
        AssertPackedBuildOutcome(first);
        var firstOracle = await ProbeArtifact.ReadAsync(
            workspace.PackedProbeArtifactPath);
        var firstManifest = await CompilerManifestArtifact.ReadAsync(
            workspace.CompilerManifestPath);
        AssertManifestBindsProbeInputs(firstOracle, firstManifest);
        Assert.That(
            firstOracle.SyntaxTreePaths,
            Has.Some.EndsWith(CompilerProbeContract.GlobalUsingsHintName));
        Assert.That(
            firstOracle.SyntaxTreePaths,
            Has.Some.EndsWith(CompilerProbeContract.ContractHintName));
        Assert.That(
            firstManifest.ClaimPaths,
            Has.Some.EndsWith(CompilerProbeContract.ContractHintName));
        var firstGeneratedChecksum = firstOracle.GetTreeChecksum(
            CompilerProbeContract.ContractHintName);
        var firstHandwrittenChecksum =
            firstOracle.GetTreeChecksum("Subject.cs");

        var noOp = await workspace.RebuildAsync();
        AssertPackedBuildOutcome(noOp);
        Assert.That(
            await File.ReadAllBytesAsync(workspace.CompilerManifestPath),
            Is.EqualTo(firstManifest.Bytes));
        var noOpOracle = await ProbeArtifact.ReadAsync(
            workspace.PackedProbeArtifactPath);
        Assert.That(
            noOpOracle.GetTreeChecksum(
                CompilerProbeContract.ContractHintName),
            Is.EqualTo(firstGeneratedChecksum));

        workspace.WriteProbeInput("changed-generator-input");
        var changed = await workspace.RebuildAsync();
        AssertPackedBuildOutcome(changed);
        var changedOracle = await ProbeArtifact.ReadAsync(
            workspace.PackedProbeArtifactPath);
        var changedManifest = await CompilerManifestArtifact.ReadAsync(
            workspace.CompilerManifestPath);
        using (Assert.EnterMultipleScope())
        {
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
                changedManifest.ClaimPaths,
                Has.Some.EndsWith(CompilerProbeContract.ContractHintName));
            Assert.That(
                changedManifest.CompilationSha256,
                Is.Not.EqualTo(firstManifest.CompilationSha256));
        }
        if (IsSupportedWorkerHost)
        {
            var verification = await workspace.VerifyPackedArtifactAsync();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    verification.ExitCode,
                    Is.Zero,
                    verification.Output);
                Assert.That(
                    verification.Output,
                    Does.Contain("SharpProof Proven"));
            }
        }
    }

    [Test]
    public async Task PackedCollectorEmitsManifestBeforeUnsupportedHostRejection()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = ProbeWorkspace.Create();
        workspace.WritePackedConsumer(feed.Version);

        var restore = await workspace.RestoreAsync(feed.Source);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);

        var build = await workspace.RebuildAsync(
            forceUnsupportedWorkerHost: true);
        Assert.That(build.ExitCode, Is.Not.Zero, build.Output);
        Assert.That(
            build.Output,
            Does.Contain(
                "canonical Linux amd64 container"));
        _ = await ProbeArtifact.ReadAsync(workspace.PackedProbeArtifactPath);
        _ = await CompilerManifestArtifact.ReadAsync(
            workspace.InvocationCompilerManifestPath);
    }

    [Test]
    public async Task GeneratedRefutationTraversesArtifactReplayAndCache()
    {
        if (!IsSupportedWorkerHost)
        {
            Assert.Ignore(
                "The verifier is supported only in the canonical Linux amd64 container.");
        }

        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = ProbeWorkspace.Create();
        workspace.WritePackedConsumer(feed.Version);
        workspace.WriteProbeInput("refute:whole-pipeline");

        var restore = await workspace.RestoreAsync(feed.Source);
        Assert.That(restore.ExitCode, Is.Zero, restore.Output);
        var build = await workspace.RebuildAsync();
        Assert.That(build.ExitCode, Is.Not.Zero, build.Output);
        var firstResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(workspace.VerifyResultPath))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                firstResponse.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(firstResponse.ClaimResults, Has.Length.EqualTo(1));
            Assert.That(
                firstResponse.ClaimResults[0].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(firstResponse.ClaimResults[0].Model, Is.Not.Empty);
            Assert.That(
                firstResponse.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
        }

        var second = await workspace.VerifyPackedArtifactAsync();
        Assert.That(second.ExitCode, Is.Not.Zero, second.Output);
        var secondResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(workspace.VerifyResultPath))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                secondResponse.ClaimResults.Select(static result =>
                    (result.ClaimId, result.Outcome, result.Reason)),
                Is.EqualTo(firstResponse.ClaimResults.Select(static result =>
                    (result.ClaimId, result.Outcome, result.Reason))));
            Assert.That(
                secondResponse.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Hit));
        }
    }

    [TestCase(ProbeSuppression.DesignTimeBuild)]
    [TestCase(ProbeSuppression.ProfileOff)]
    [TestCase(ProbeSuppression.MissingControl)]
    public async Task SuppressedCompilationDoesNotWriteAnArtifact(
        ProbeSuppression suppression)
    {
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

    private static void AssertPackedBuildOutcome(ProcessResult result)
    {
        if (IsSupportedWorkerHost)
        {
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            return;
        }

        Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
        Assert.That(
            result.Output,
            Does.Contain(
                "canonical Linux amd64 container"));
    }

    private static void AssertManifestBindsProbeInputs(
        ProbeArtifact probe,
        CompilerManifestArtifact manifest)
    {
        using var document = JsonDocument.Parse(manifest.Bytes);
        var compilation = document.RootElement.GetProperty("compilation");
        var trees = compilation.GetProperty("syntaxTrees")
            .EnumerateArray()
            .Select(tree => (
                Path: tree.GetProperty("path").GetString() ?? string.Empty,
                Sha256: tree.GetProperty("sha256").GetString() ?? string.Empty))
            .ToArray();
        foreach (var expectedSuffix in new[] { "Subject.cs", CompilerProbeContract.ContractHintName })
        {
            var probeHash = probe.GetTreeChecksum(expectedSuffix);
            var manifestHash = trees.Single(tree => tree.Path.EndsWith(
                expectedSuffix, StringComparison.OrdinalIgnoreCase)).Sha256;
            Assert.That(manifestHash, Is.EqualTo(probeHash),
                "compiler manifest syntax-tree provenance: " + expectedSuffix);
        }

        var additionalPath = compilation.GetProperty("additionalFiles")
            .EnumerateArray()
            .Single(file => (file.GetProperty("path").GetString() ?? string.Empty)
                .EndsWith(CompilerProbeContract.AdditionalFileName, StringComparison.OrdinalIgnoreCase));
        var expectedAdditionalHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("initial-generator-input\n")));
        Assert.That(
            string.Equals(
                additionalPath.GetProperty("sha256").GetString(),
                expectedAdditionalHash,
                StringComparison.OrdinalIgnoreCase),
            Is.True,
            "compiler manifest additional-file provenance");
    }

    public enum ProbeSuppression
    {
        DesignTimeBuild,
        ProfileOff,
        MissingControl
    }

    private sealed class ProbeArtifact
    {
        private readonly SyntaxTreeRow[] _syntaxTreeRows;

        private ProbeArtifact(
            string targetFramework,
            string options,
            string[] syntaxTrees,
            SyntaxTreeRow[] syntaxTreeRows,
            string[] portableReferences,
            string[] additionalFiles)
        {
            TargetFramework = targetFramework;
            Options = options;
            SyntaxTrees = syntaxTrees;
            _syntaxTreeRows = syntaxTreeRows;
            PortableReferences = portableReferences;
            AdditionalFiles = additionalFiles;
        }

        internal string TargetFramework
        {
            get;
        }
        internal string Options
        {
            get;
        }
        internal string[] SyntaxTrees
        {
            get;
        }
        internal string[] PortableReferences
        {
            get;
        }
        internal string[] AdditionalFiles
        {
            get;
        }
        internal string[] SyntaxTreePaths =>
            [.. _syntaxTreeRows.Select(static tree => tree.Path)];
        internal string[] FrameworkReferences =>
            [.. PortableReferences.Where(static reference =>
                    !reference.Contains(
                        "SharpProof.Attributes.dll",
                        StringComparison.OrdinalIgnoreCase))];

        internal static async Task<ProbeArtifact> ReadAsync(string path)
        {
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
            {
                throw new InvalidDataException(
                    "The probe artifact has no target-framework directory.");
            }

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
            var syntaxTreeRows = GetCanonicalSyntaxTrees(root, path);
            return new ProbeArtifact(
                targetFramework!,
                root.GetProperty("options").GetRawText(),
                [.. syntaxTreeRows.Select(static tree => tree.Raw)],
                syntaxTreeRows,
                GetCanonicalRawRows(
                    root,
                    "portableReferences",
                    path),
                GetCanonicalRawRows(root, "additionalFiles", path));
        }

        internal string GetTreeChecksum(string pathSuffix)
        {
            var matches = _syntaxTreeRows
                .Where(tree => tree.Path.EndsWith(
                    pathSuffix,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.That(
                matches,
                Has.Length.EqualTo(1),
                "syntax tree suffix: " + pathSuffix);
            return matches[0].TextSha256;
        }

        private static SyntaxTreeRow[] GetCanonicalSyntaxTrees(
            JsonElement root,
            string path)
        {
            var trees = root.GetProperty("syntaxTrees")
                .EnumerateArray()
                .Select(tree => new
                {
                    Path = tree.GetProperty("path").GetString() ?? "",
                    Ordinal = tree.GetProperty("ordinal").GetInt32(),
                    TextSha256 = tree.GetProperty("textSha256").GetString() ??
                        string.Empty,
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
            return [.. trees.Select(static tree => new SyntaxTreeRow(
                tree.Path,
                tree.Ordinal,
                tree.TextSha256,
                tree.Raw))];
        }

        private static string[] GetCanonicalRawRows(
            JsonElement root,
            string propertyName,
            string path)
        {
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

        private sealed record SyntaxTreeRow(
            string Path,
            int Ordinal,
            string TextSha256,
            string Raw);
    }

    private sealed record CompilerManifestArtifact(
        byte[] Bytes,
        string CompilationSha256,
        string[] ClaimPaths)
    {
        internal static async Task<CompilerManifestArtifact> ReadAsync(
            string path)
        {
            Assert.That(File.Exists(path), Is.True, path);
            var bytes = await File.ReadAllBytesAsync(path);
            var text = Encoding.UTF8.GetString(bytes);
            Assert.That(text, Does.Not.Contain('\r'), path);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var compilationSha256 =
                root.GetProperty("compilationSha256").GetString();
            var claimPaths = root.GetProperty("manifest")
                .GetProperty("claims")
                .EnumerateArray()
                .Select(static claim => claim.GetProperty("location")
                    .GetProperty("path").GetString() ?? string.Empty)
                .ToArray();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    root.GetProperty("schema").GetString(),
                    Is.EqualTo("SharpProof.CompilerManifest"),
                    path);
                Assert.That(
                    root.GetProperty("schemaVersion").GetInt32(),
                    Is.EqualTo(CurrentCompilerArtifactSchemaVersion),
                    path);
                Assert.That(
                    compilationSha256,
                    Does.Match("^[0-9a-f]{64}$"),
                    path);
            }
            return new CompilerManifestArtifact(
                bytes,
                compilationSha256!,
                claimPaths);
        }

        private static int CurrentCompilerArtifactSchemaVersion
        {
            get
            {
                const string assemblyName = "SharpProof.CompilerArtifact";
                const string typeName =
                    assemblyName + ".CompilerManifestArtifactVersions";
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .SingleOrDefault(static candidate =>
                        candidate.GetName().Name == assemblyName) ??
                    System.Reflection.Assembly.Load(assemblyName);
                var versionType = assembly.GetType(
                    typeName,
                    throwOnError: true)!;
                var current = versionType.GetField(
                    "Current",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic);
                return current?.GetRawConstantValue() as int? ??
                    throw new InvalidDataException(
                        "The compiler-artifact schema constant was not found.");
            }
        }
    }

    private sealed class ProbeWorkspace : IDisposable
    {
        private static readonly string s_workspaceParent = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.FinalProbe");
        private static readonly string s_sharedPackageCache = Path.Combine(
            s_workspaceParent,
            "package-cache-" + Guid.NewGuid().ToString("N"));
        private readonly string _root;
        private string _sharedCompilationServerId;

        private ProbeWorkspace(string root)
        {
            _root = root;
            _sharedCompilationServerId = CreateSharedCompilationServerId(
                "direct");
            ProjectPath = Path.Combine(root, "Consumer.csproj");
            ArtifactDirectory = Path.Combine(root, "probe");
            PackageCache = s_sharedPackageCache;
            CompilerManifestPath = Path.Combine(
                root,
                "published",
                "compiler-manifest.json");
            InvocationCompilerManifestPath = Path.Combine(
                root,
                "invocation",
                "compiler-manifest.json");
            PackedProbeArtifactPath = Path.Combine(
                root,
                "probe",
                NetTargetFramework,
                "final-compilation.json");
            VerifyResultPath = Path.Combine(
                root,
                "published",
                "result.json");
            SubjectPath = Path.Combine(root, "Subject.cs");
        }

        internal string ProjectPath
        {
            get;
        }
        internal string ArtifactDirectory
        {
            get;
        }
        internal string PackageCache
        {
            get;
        }
        internal string CompilerManifestPath
        {
            get;
        }
        internal string InvocationCompilerManifestPath
        {
            get;
        }
        internal string PackedProbeArtifactPath
        {
            get;
        }
        internal string VerifyResultPath
        {
            get;
        }
        internal string SubjectPath
        {
            get;
        }

        internal static ProbeWorkspace Create()
        {
            var root = Path.Combine(
                s_workspaceParent,
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.Copy(
                Path.Combine(TestRepository.FindRoot(), "global.json"),
                Path.Combine(root, "global.json"));
            return new ProbeWorkspace(root);
        }

        internal static void DisposeSharedPackageCache()
        {
            TestRepository.DeleteOwnedTemporaryDirectory(
                s_sharedPackageCache,
                "SharpProof.FinalProbe",
                "Refusing to remove an unexpected shared package cache.");
        }

        internal void WriteConsumer(
            string targetFrameworks,
            bool enableProbe,
            string profile = "advisory",
            bool designTimeBuild = false)
        {
            _sharedCompilationServerId = CreateSharedCompilationServerId(
                "direct");
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

        internal void WritePackedConsumer(string packageVersion)
        {
            _sharedCompilationServerId = CreateSharedCompilationServerId(
                "packed");
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

        internal void WriteProbeInput(string value)
        {
            File.WriteAllText(
                Path.Combine(
                    _root,
                    CompilerProbeContract.AdditionalFileName),
                value + "\n",
                new UTF8Encoding(false));
        }

        internal Task<ProcessResult> BuildAsync()
        {
            return RunDotNetAsync([
                "build",
                ProjectPath,
                "-c",
                "Release",
                "--nologo",
                "/nodeReuse:false"
            ]);
        }

        internal Task<ProcessResult> RebuildAsync(
            bool forceUnsupportedWorkerHost = false)
        {
            var arguments = new List<string> {
                "build",
                ProjectPath,
                "-t:Rebuild",
                "-c",
                "Release",
                "--no-restore",
                "--nologo",
                "/nodeReuse:false"
            };
            if (forceUnsupportedWorkerHost)
            {
                arguments.Add("-p:_SharpProofVerifierHostSupported=false");
            }

            return RunDotNetAsync([.. arguments]);
        }

        internal Task<ProcessResult> VerifyPackedArtifactAsync()
        {
            var invocationId = Guid.NewGuid().ToString("N");
            var runDirectory = Path.Combine(_root, "verify-run");
            var publishDirectory = Path.Combine(_root, "published");
            Directory.CreateDirectory(runDirectory);
            var invocationManifestPath = Path.Combine(
                runDirectory,
                "compiler-manifest.json");
            File.Copy(
                CompilerManifestPath,
                invocationManifestPath,
                overwrite: true);
            return RunDotNetAsync([
                "msbuild",
                ProjectPath,
                "/t:_SharpProofVerifyCore",
                "/nologo",
                "/nodeReuse:false",
                "-p:Configuration=Release",
                "-p:TargetFramework=" + NetTargetFramework,
                "-p:SharpProofVerify=true",
                "-p:_SharpProofCompilerManifestPath=" +
                    invocationManifestPath,
                "-p:_SharpProofInvocationId=" + invocationId,
                "-p:SharpProofVerifyRequestFile=" +
                    Path.Combine(publishDirectory, "request.json"),
                "-p:SharpProofVerifyResultFile=" +
                    Path.Combine(publishDirectory, "result.json"),
                "-p:SharpProofCompilerManifestFile=" +
                    CompilerManifestPath,
                "-p:SharpProofVerifyCacheDirectory=" +
                    Path.Combine(publishDirectory, "cache")
            ]);
        }

        internal Task<ProcessResult> RestoreAsync(string packageSource)
        {
            var nugetConfig = IsolatedPackageFeedConfiguration.Write(
                _root,
                packageSource);
            return RunDotNetAsync([
                "restore",
                ProjectPath,
                "--nologo",
                "/nodeReuse:false",
                "--configfile",
                nugetConfig,
                "--packages",
                PackageCache
            ]);
        }

        private async Task<ProcessResult> RunDotNetAsync(
            string[] arguments,
            string? workingDirectory = null)
        {
            var startInfo = ProcessRunner.CreateStartInfo(
                workingDirectory ?? _root,
                "dotnet",
                arguments);
            startInfo.Environment["SharedCompilationId"] =
                _sharedCompilationServerId;

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

        private static string CreateSharedCompilationServerId(string role)
        {
            return "sharpproof-final-probe-" + role + "-" +
                Guid.NewGuid().ToString("N");
        }

        internal string[] GetArtifactPaths()
        {
            return Directory.Exists(ArtifactDirectory)
                ? Directory.GetFiles(
                    ArtifactDirectory,
                    "*.json",
                    SearchOption.AllDirectories)
                : [];
        }

        public void Dispose()
        {
            TestRepository.DeleteOwnedTemporaryDirectory(
                _root,
                "SharpProof.FinalProbe",
                "Refusing to remove an unexpected test directory.");
        }

        private static string CreateProjectXml(
            string targetFrameworks,
            bool enableProbe,
            string profile,
            bool designTimeBuild)
        {
            var targetFrameworkProperty =
                targetFrameworks.Contains(';', StringComparison.Ordinal)
                    ? "TargetFrameworks"
                    : "TargetFramework";
            var control = enableProbe
                ? "<EmitSharpProofProbe>true</EmitSharpProofProbe>"
                : "";
            var analyzerPath = Escape(
                ProductBuildOutputs.CompilerProbeAssemblyPath());
            var attributesPath = Escape(
                ProductBuildOutputs.AttributesAssemblyPath());
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

        private string CreatePackedProjectXml(string packageVersion)
        {
            var analyzerPath = Escape(
                ProductBuildOutputs.CompilerProbeAssemblyPath());
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
                    <SharpProofVerify>true</SharpProofVerify>
                    <SharpProofVerifyRequestFile>{Escape(Path.Combine(_root, "published", "request.json"))}</SharpProofVerifyRequestFile>
                    <SharpProofVerifyResultFile>{Escape(VerifyResultPath)}</SharpProofVerifyResultFile>
                    <SharpProofCompilerManifestFile>{Escape(Path.Combine(_root, "published", "compiler-manifest.json"))}</SharpProofCompilerManifestFile>
                    <_SharpProofCompilerManifestPath>{Escape(InvocationCompilerManifestPath)}</_SharpProofCompilerManifestPath>
                    <SharpProofVerifyCacheDirectory>{Escape(Path.Combine(_root, "published", "cache"))}</SharpProofVerifyCacheDirectory>
                    <_SharpProofCompilationTargetFramework>$(TargetFramework)</_SharpProofCompilationTargetFramework>
                    <_SharpProofProjectDirectory>$(MSBuildProjectDirectory)</_SharpProofProjectDirectory>
                    <{outputProperty}>{Escape(PackedProbeArtifactPath)}</{outputProperty}>
                    <{globalProperty}>$(TargetFramework)</{globalProperty}>
                    <WarningsAsErrors>AD0001;CS8032;CS8785</WarningsAsErrors>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="SharpProof.Verifier"
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

        private static string Escape(string value)
        {
            return PackageTestXml.EscapeOrThrow(
                value,
                "Failed to escape an MSBuild value.");
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
