using System.Diagnostics;
using System.Security;
using System.Xml.Linq;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Worker;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerMsBuildIntegrationTests {
    [Test]
    public void WorkerContainmentIsMandatoryOnTheSupportedHost() {
        if (!OperatingSystem.IsWindows()) {
            Assert.Throws<PlatformNotSupportedException>(
                (Action)(() =>
                    WindowsJob.CreateRequired(
                        WorkerBudgets.DefaultProcessMemoryLimitBytes,
                        WorkerBudgets.MaximumParallelism)));
            return;
        }

        using var boundary = WindowsJob.CreateRequired(
            WorkerBudgets.DefaultProcessMemoryLimitBytes,
            WorkerBudgets.MaximumParallelism);
        Assert.That(boundary, Is.Not.Null);
    }

    [Test]
    public async Task VerificationIsOffByDefaultAndDuringDesignTimeBuilds() {
        using var project = ConsumerProject.Create(IdentitySource);
        var projectDirectory = Path.GetDirectoryName(project.ProjectPath)!;
        var normal = await project.BuildAsync(
            verify: null,
            (
                "SharpProofWorkerPath",
                Path.Combine(projectDirectory, "missing-worker.dll")),
            (
                "SharpProofLauncherPath",
                Path.Combine(projectDirectory, "missing-launcher.dll")));
        Assert.That(normal.ExitCode, Is.Zero, normal.Output);
        Assert.That(File.Exists(project.RequestPath), Is.False);
        Assert.That(File.Exists(project.ResultPath), Is.False);

        var designTime = await project.BuildAsync(
            verify: true,
            ("DesignTimeBuild", "true"));
        Assert.That(designTime.ExitCode, Is.Zero, designTime.Output);
        Assert.That(File.Exists(project.RequestPath), Is.False);
        Assert.That(File.Exists(project.ResultPath), Is.False);
    }

    [Test]
    public async Task OptInBuildUsesRealSourceAndReferencePaths() {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var build = await project.BuildAsync(verify: true);
        Assert.That(build.ExitCode, Is.Zero, build.Output);
        Assert.That(build.Output, Does.Contain("SharpProof Proven"));
        Assert.That(File.Exists(project.RequestPath), Is.True);
        Assert.That(File.Exists(project.ResultPath), Is.True);

        var request = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        Assert.That(request.SourceFiles, Is.Not.Empty);
        Assert.That(request.ReferenceAssemblies, Is.Not.Empty);
        Assert.That(
            request.SourceFiles.All(Path.IsPathFullyQualified),
            Is.True);
        Assert.That(
            request.ReferenceAssemblies.All(Path.IsPathFullyQualified),
            Is.True);
        Assert.That(
            request.SourceFiles.All(File.Exists),
            Is.True);
        Assert.That(
            request.ReferenceAssemblies.All(File.Exists),
            Is.True);
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                request.Budgets.QueryRlimit,
                Is.EqualTo(WorkerBudgets.DefaultQueryRlimit));
            Assert.That(
                request.Budgets.MethodRlimit,
                Is.EqualTo(WorkerBudgets.DefaultMethodRlimit));
            Assert.That(
                request.Budgets.MethodWallTimeMilliseconds,
                Is.EqualTo(
                    WorkerBudgets.DefaultMethodWallTimeMilliseconds));
            Assert.That(
                request.Budgets.ProjectWallTimeMilliseconds,
                Is.EqualTo(
                    WorkerBudgets.DefaultProjectWallTimeMilliseconds));
            Assert.That(
                request.Budgets.MaxParallelism,
                Is.EqualTo(WorkerBudgets.MaximumParallelism));
            Assert.That(
                request.Budgets.MaximumExpressionDepth,
                Is.EqualTo(
                    WorkerBudgets.DefaultMaximumExpressionDepth));
            Assert.That(
                request.Budgets.ProcessMemoryLimitBytes,
                Is.EqualTo(
                    WorkerBudgets.DefaultProcessMemoryLimitBytes));
            Assert.That(
                request.Budgets.MaxWorkerProcesses,
                Is.EqualTo(WorkerBudgets.MaximumParallelism));
            Assert.That(
                request.Compilation.TargetFramework,
                Is.EqualTo("net8.0"));
            Assert.That(
                request.Compilation.LanguageVersion,
                Is.EqualTo("12.0"));
            Assert.That(
                request.Compilation.NullableContext,
                Is.EqualTo(WorkerNullableContext.Disabled));
            Assert.That(
                request.Compilation.Optimization,
                Is.EqualTo(WorkerOptimizationLevel.Release));
            Assert.That(request.Compilation.CheckOverflow, Is.False);
            Assert.That(request.Compilation.AllowUnsafe, Is.False);
            Assert.That(request.Compilation.Deterministic, Is.True);
            Assert.That(
                request.Compilation.OutputKind,
                Is.EqualTo(
                    WorkerOutputKind.DynamicallyLinkedLibrary));
            Assert.That(
                request.Compilation.Platform,
                Is.EqualTo(WorkerPlatform.AnyCpu));
            Assert.That(request.Cache.Enabled, Is.True);
            Assert.That(
                request.Cache.MaximumBytes,
                Is.EqualTo(WorkerCacheOptions.DefaultMaximumBytes));
        }

        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        Assert.That(response.Errors, Is.Empty);
        Assert.That(
            response.Records.Single().Status,
            Is.EqualTo(WorkerVerificationStatus.Proven));
    }

    [Test]
    public async Task IncrementalBuildIsDeterministicAndKeepsResultStable() {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var first = await project.BuildAsync(verify: true);
        Assert.That(first.ExitCode, Is.Zero, first.Output);
        var firstJson = await File.ReadAllTextAsync(project.ResultPath);
        var firstWrite = File.GetLastWriteTimeUtc(project.ResultPath);

        await Task.Delay(1_100);
        var second = await project.BuildAsync(verify: true);
        Assert.That(second.ExitCode, Is.Zero, second.Output);
        var secondJson = await File.ReadAllTextAsync(project.ResultPath);
        var secondWrite = File.GetLastWriteTimeUtc(project.ResultPath);

        Assert.That(secondJson, Is.EqualTo(firstJson));
        Assert.That(secondWrite, Is.GreaterThan(firstWrite));
        Assert.That(second.Output, Does.Contain("SharpProof Proven"));

        await Task.Delay(1_100);
        var changedMethodRlimit =
            WorkerBudgets.DefaultMethodRlimit - 1;
        var changed = await project.BuildAsync(
            verify: true,
            (
                "SharpProofVerifyMethodRlimit",
                changedMethodRlimit.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(changed.ExitCode, Is.Zero, changed.Output);
        var changedRequest = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        var changedResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        var firstResponse = WorkerProtocolJson.DeserializeResponse(firstJson)!;
        Assert.That(
            changedRequest.Budgets.MethodRlimit,
            Is.EqualTo(changedMethodRlimit));
        Assert.That(
            changedResponse.InputHash,
            Is.Not.EqualTo(firstResponse.InputHash));
        Assert.That(
            File.GetLastWriteTimeUtc(project.ResultPath),
            Is.GreaterThan(secondWrite));
    }

    [Test]
    public async Task CompilerOptionChangesInvalidateIncrementalVerification() {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var first = await project.BuildAsync(verify: true);
        Assert.That(first.ExitCode, Is.Zero, first.Output);
        var firstResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        var firstWrite = File.GetLastWriteTimeUtc(project.ResultPath);

        await Task.Delay(1_100);
        var changed = await project.BuildAsync(
            verify: true,
            ("LangVersion", "13.0"),
            ("Nullable", "annotations"),
            ("CheckForOverflowUnderflow", "true"),
            ("Optimize", "false"),
            ("AllowUnsafeBlocks", "true"),
            ("Deterministic", "false"),
            ("PlatformTarget", "x64"));
        Assert.That(changed.ExitCode, Is.Zero, changed.Output);
        var changedRequest = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        var changedResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;

        using (Assert.EnterMultipleScope()) {
            Assert.That(
                changedRequest.Compilation.LanguageVersion,
                Is.EqualTo("13.0"));
            Assert.That(
                changedRequest.Compilation.NullableContext,
                Is.EqualTo(WorkerNullableContext.Annotations));
            Assert.That(
                changedRequest.Compilation.Optimization,
                Is.EqualTo(WorkerOptimizationLevel.Debug));
            Assert.That(
                changedRequest.Compilation.CheckOverflow,
                Is.True);
            Assert.That(changedRequest.Compilation.AllowUnsafe, Is.True);
            Assert.That(
                changedRequest.Compilation.Deterministic,
                Is.False);
            Assert.That(
                changedRequest.Compilation.Platform,
                Is.EqualTo(WorkerPlatform.X64));
            Assert.That(
                changedResponse.InputHash,
                Is.Not.EqualTo(firstResponse.InputHash));
            Assert.That(
                File.GetLastWriteTimeUtc(project.ResultPath),
                Is.GreaterThan(firstWrite));
        }
    }

    [Test]
    public async Task VerificationFailsExplicitlyOnUnsupportedHosts() {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("The packaged worker is supported on Windows.");
        using var project = ConsumerProject.Create(IdentitySource);

        var build = await project.BuildAsync(verify: true);

        Assert.That(build.ExitCode, Is.Not.Zero);
        Assert.That(
            build.Output,
            Does.Contain(
                "SharpProof out-of-process verification is supported only on Windows"));
    }

    [Test]
    public void PackagePropertiesMatchProtocolDefaults() {
        var repository = ConsumerProject.FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            repository,
            "SharpProof.Package",
            "buildTransitive",
            "SharpProof.props"));
        var properties = document
            .Descendants()
            .Where(static element =>
                element.Parent?.Name.LocalName == "PropertyGroup")
            .ToDictionary(
                static element => element.Name.LocalName,
                static element => element.Value,
                StringComparer.Ordinal);

        using (Assert.EnterMultipleScope()) {
            Assert.That(
                properties["SharpProofVerify"],
                Is.EqualTo("false"));
            Assert.That(
                uint.Parse(properties["SharpProofVerifyQueryRlimit"]),
                Is.EqualTo(WorkerBudgets.DefaultQueryRlimit));
            Assert.That(
                uint.Parse(properties["SharpProofVerifyMethodRlimit"]),
                Is.EqualTo(WorkerBudgets.DefaultMethodRlimit));
            Assert.That(
                int.Parse(properties[
                    "SharpProofVerifyMethodWallTimeMilliseconds"]),
                Is.EqualTo(
                    WorkerBudgets.DefaultMethodWallTimeMilliseconds));
            Assert.That(
                int.Parse(properties[
                    "SharpProofVerifyProjectWallTimeMilliseconds"]),
                Is.EqualTo(
                    WorkerBudgets.DefaultProjectWallTimeMilliseconds));
            Assert.That(
                int.Parse(properties["SharpProofVerifyMaxParallelism"]),
                Is.EqualTo(WorkerBudgets.MaximumParallelism));
            Assert.That(
                int.Parse(properties[
                    "SharpProofVerifyMaximumExpressionDepth"]),
                Is.EqualTo(
                    WorkerBudgets.DefaultMaximumExpressionDepth));
            Assert.That(
                long.Parse(properties[
                    "SharpProofVerifyProcessMemoryLimitBytes"]),
                Is.EqualTo(
                    WorkerBudgets.DefaultProcessMemoryLimitBytes));
            Assert.That(
                int.Parse(properties["SharpProofVerifyMaxWorkerProcesses"]),
                Is.EqualTo(WorkerBudgets.MaximumParallelism));
            Assert.That(
                int.Parse(properties[
                    "SharpProofVerifyTerminationGraceMilliseconds"]),
                Is.EqualTo(
                    WorkerLauncherDefaults.TerminationGraceMilliseconds));
            Assert.That(
                bool.Parse(properties["SharpProofVerifyCacheEnabled"]),
                Is.True);
            Assert.That(
                long.Parse(properties[
                    "SharpProofVerifyCacheMaximumBytes"]),
                Is.EqualTo(WorkerCacheOptions.DefaultMaximumBytes));
        }
    }

    [Test]
    public async Task SpacesAndEscapedIdentifiersSurviveTheTargetBoundary() {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long @class(long @value) {
                    Contract.Ensures(Contract.Result<long>() == @value);
                    return @value;
                }
            }
            """,
            useSpaces: true);
        var build = await project.BuildAsync(verify: true);
        Assert.That(build.ExitCode, Is.Zero, build.Output);
        var request = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        Assert.That(
            request.SourceFiles,
            Has.Some.Contains("consumer project"));
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        Assert.That(
            response.Records.Single().Status,
            Is.EqualTo(WorkerVerificationStatus.Proven));
    }

    [Test]
    public async Task RefutationAndHardBoundaryFailuresFailClosed() {
        RequireWindowsWorker();
        using var refuted = ConsumerProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Broken(long value) {
                    Contract.Ensures(Contract.Result<long>() > value);
                    return value;
                }
            }
            """);
        var refutedBuild = await refuted.BuildAsync(verify: true);
        Assert.That(refutedBuild.ExitCode, Is.Not.Zero);
        Assert.That(refutedBuild.Output, Does.Contain("exited with code 5"));
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(refuted.ResultPath))!;
        Assert.That(
            response.Records.Single().Status,
            Is.EqualTo(WorkerVerificationStatus.Refuted));

        var repeatedRefutation = await refuted.BuildAsync(verify: true);
        Assert.That(
            repeatedRefutation.ExitCode,
            Is.Not.Zero,
            repeatedRefutation.Output);
        Assert.That(
            repeatedRefutation.Output,
            Does.Contain("exited with code 5"));

        using var timedOut = ConsumerProject.Create(IdentitySource);
        var timedOutBuild = await timedOut.BuildAsync(
            verify: true,
            ("SharpProofVerifyMethodWallTimeMilliseconds", "1"),
            ("SharpProofVerifyProjectWallTimeMilliseconds", "1"),
            ("SharpProofVerifyTerminationGraceMilliseconds", "1"));
        Assert.That(timedOutBuild.ExitCode, Is.Not.Zero);
        Assert.That(
            timedOutBuild.Output,
            Does.Contain("failed closed"));
    }

    private const string IdentitySource =
        """
        using SharpProof.Attributes;
        public static class Subject {
            public static long Identity(long value) {
                Contract.Ensures(Contract.Result<long>() == value);
                return value;
            }
        }
        """;

    private static void RequireWindowsWorker() {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore(
                "The packaged worker is supported only on Windows.");
    }

    private sealed class ConsumerProject : IDisposable {
        private readonly string _root;

        private ConsumerProject(string root) {
            _root = root;
            ProjectPath = Path.Combine(root, "Consumer.csproj");
            RequestPath = Path.Combine(
                root,
                "obj",
                "Release",
                "net8.0",
                "SharpProof",
                "request.json");
            ResultPath = Path.Combine(
                root,
                "obj",
                "Release",
                "net8.0",
                "SharpProof",
                "result.json");
        }

        internal string ProjectPath { get; }
        internal string RequestPath { get; }
        internal string ResultPath { get; }

        internal static ConsumerProject Create(
            string source,
            bool useSpaces = false) {
            var name = useSpaces
                ? "consumer project " + Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N");
            var root = Path.Combine(
                Path.GetTempPath(),
                "SharpProof.Package.Test",
                name);
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, "Subject.cs"),
                source,
                new System.Text.UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(root, "Consumer.csproj"),
                CreateProjectXml(),
                new System.Text.UTF8Encoding(false));
            return new ConsumerProject(root);
        }

        internal async Task<BuildResult> BuildAsync(
            bool? verify,
            params (string Name, string Value)[] properties) {
            var startInfo = new ProcessStartInfo {
                FileName = "dotnet",
                WorkingDirectory = _root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("build");
            startInfo.ArgumentList.Add(ProjectPath);
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("Release");
            startInfo.ArgumentList.Add("--nologo");
            startInfo.ArgumentList.Add("/nodeReuse:false");
            startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
            if (verify.HasValue)
                startInfo.ArgumentList.Add(
                    "-p:SharpProofVerify=" +
                    (verify.Value ? "true" : "false"));
            foreach (var property in properties)
                startInfo.ArgumentList.Add(
                    "-p:" + property.Name + "=" + property.Value);
            using var process = Process.Start(startInfo)!;
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new BuildResult(
                process.ExitCode,
                (await standardOutput) + Environment.NewLine +
                (await standardError));
        }

        public void Dispose() {
            var resolved = Path.GetFullPath(_root);
            var expectedRoot = Path.GetFullPath(
                Path.Combine(
                    Path.GetTempPath(),
                    "SharpProof.Package.Test"));
            if (!resolved.StartsWith(
                    expectedRoot + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected test directory.");
            if (Directory.Exists(resolved))
                Directory.Delete(resolved, recursive: true);
        }

        private static string CreateProjectXml() {
            var repository = FindRepositoryRoot();
            var attributes = SecurityElement.Escape(
                Path.Combine(
                    repository,
                    "SharpProof.Attributes",
                    "SharpProof.Attributes.csproj"));
            var props = SecurityElement.Escape(
                Path.Combine(
                    repository,
                    "SharpProof.Package",
                    "buildTransitive",
                    "SharpProof.props"));
            var targets = SecurityElement.Escape(
                Path.Combine(
                    repository,
                    "SharpProof.Package",
                    "buildTransitive",
                    "SharpProof.targets"));
            var worker = SecurityElement.Escape(
                typeof(SharpProofWorker).Assembly.Location);
            var launcher = SecurityElement.Escape(
                typeof(LauncherMarker).Assembly.Location);
            return
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="{props}" />
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <LangVersion>12.0</LangVersion>
                    <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
                    <SharpProofWorkerPath>{worker}</SharpProofWorkerPath>
                    <SharpProofLauncherPath>{launcher}</SharpProofLauncherPath>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{attributes}" />
                  </ItemGroup>
                  <Import Project="{targets}" />
                </Project>
                """;
        }

        internal static string FindRepositoryRoot() {
            var directory = new DirectoryInfo(
                typeof(LauncherMarker).Assembly.Location);
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
    }

    private readonly record struct BuildResult(
        int ExitCode,
        string Output);
}
