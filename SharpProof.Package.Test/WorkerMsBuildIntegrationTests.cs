using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerMsBuildIntegrationTests
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);

    private static readonly string[] s_publicPolicyProperties = [
        "SharpProofProfile",
        "SharpProofFeatures",
        "SharpProofVerifyPolicy",
        "SharpProofAssumptionPolicy"
    ];
    private static readonly string[] s_compilerManifestProperties = [
        "_SharpProofCompilerManifestPath",
        "_SharpProofCompilationTargetFramework",
        "_SharpProofProjectDirectory"
    ];
    private static readonly string[] s_reconstructionArguments = [
        "--project-directory", "--assembly-name", "--sources", "--references",
        "--constants", "--target-framework", "--language-version", "--nullable",
        "--checked-overflow", "--optimize", "--allow-unsafe", "--deterministic",
        "--output-type", "--platform-target", "--prefer-32-bit", "--features"
    ];
    private static readonly string[] s_reconstructionListArtifacts = [
        "_SharpProofWriteOptionsInput", "_SharpProofSourceList",
        "_SharpProofReferenceList", "_SharpProofConstantList",
        "WriteLinesToFile", "sources.list", "references.list",
        "constants.list", "options.input"
    ];
    private static readonly string[] s_manualConsumerElementOrder = [
        "Import",
        "Import",
        "PropertyGroup",
        "ItemGroup",
        "Import",
        "Import",
        "Target",
        "Target"
    ];
    private static readonly string[] s_manualConsumerImportOrder = [
        "SharpProof.props",
        "SharpProof.Verifier.Win-x64.props",
        "SharpProof.targets",
        "SharpProof.Verifier.Win-x64.targets"
    ];

    [Test]
    public void WorkerContainmentIsMandatoryOnTheSupportedHost()
    {
        Assert.That(
            WindowsJob.KillsProcessesOnDispose,
            Is.True,
            "Closing the required Job Object must terminate every worker process.");
        if (!OperatingSystem.IsWindows() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            RuntimeInformation.OSArchitecture != Architecture.X64)
        {
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
    public async Task WorkerCannotReachModuleInitializerBeforeResume()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var worker = await project.CreateResultlessWorkerAsync();
        var marker = Path.Combine(
            Path.GetDirectoryName(worker)!, "pre-main.marker");
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
            throw new InvalidOperationException(
                "The test host did not disclose its dotnet host path.");
        var eventName = "Local\\SharpProof.Worker.Test." +
            Guid.NewGuid().ToString("N");
        using var start = new EventWaitHandle(
            false, EventResetMode.ManualReset, eventName);
        using var boundary = WindowsJob.CreateRequired(
            WorkerBudgets.DefaultProcessMemoryLimitBytes,
            WorkerBudgets.MaximumParallelism);
        using var process = boundary.StartSuspended(
            host,
            [worker, "verify", "--request", "unused-request.json",
                "--result", "unused-result.json", "--start-event", eventName,
                "--pre-main-marker", marker],
            Path.GetDirectoryName(worker)!);

        try
        {
            await Task.Delay(250);
            Assert.That(File.Exists(marker), Is.False,
                "A suspended worker must not execute its module initializer.");

            process.Resume();
            Assert.That(
                SpinWait.SpinUntil(() => File.Exists(marker), 5_000),
                Is.True,
                "The worker did not execute after resume.");
        }
        finally
        {
            boundary.Terminate(124);
            Assert.That(process.WaitForExit(5_000), Is.True);
        }
    }

    [Test]
    public void ManualConsumerImportsFollowSplitPackageOrder()
    {
        using var project = ConsumerProject.Create(IdentitySource);
        var document = XDocument.Load(project.ProjectPath);
        var elements = document.Root?.Elements().ToArray() ??
            throw new InvalidDataException(
                "The generated consumer project has no root.");

        Assert.That(
            elements.Select(static element => element.Name.LocalName),
            Is.EqualTo(s_manualConsumerElementOrder));
        Assert.That(
            elements.Where(static element =>
                    element.Name.LocalName == "Import")
                .Select(static import => Path.GetFileName(
                    import.Attribute("Project")?.Value)),
            Is.EqualTo(s_manualConsumerImportOrder));
    }

    [Test]
    public async Task VerificationIsOffByDefaultAndDuringDesignTimeBuilds()
    {
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
    public async Task NonBuildingEvaluationPreservesPublishedVerificationFiles()
    {
        using var project = ConsumerProject.Create(IdentitySource);
        var sarifPath = project.VerifyOutputPath("net8.0", "result.sarif");
        Directory.CreateDirectory(Path.GetDirectoryName(project.ResultPath)!);
        var request = new byte[] { 1, 2, 3 };
        var result = new byte[] { 4, 5, 6 };
        var manifest = new byte[] { 7, 8, 9 };
        var sarif = new byte[] { 10, 11, 12 };
        await File.WriteAllBytesAsync(project.RequestPath, request);
        await File.WriteAllBytesAsync(project.ResultPath, result);
        await File.WriteAllBytesAsync(project.CompilerManifestPath, manifest);
        await File.WriteAllBytesAsync(sarifPath, sarif);

        var evaluation = await project.RunNonBuildingInitializationAsync(sarifPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(evaluation.ExitCode, Is.Zero, evaluation.Output);
            Assert.That(await File.ReadAllBytesAsync(project.RequestPath), Is.EqualTo(request));
            Assert.That(await File.ReadAllBytesAsync(project.ResultPath), Is.EqualTo(result));
            Assert.That(await File.ReadAllBytesAsync(project.CompilerManifestPath), Is.EqualTo(manifest));
            Assert.That(await File.ReadAllBytesAsync(sarifPath), Is.EqualTo(sarif));
        }
    }

    [Test]
    public async Task OptInBuildUsesFinalCompilerArtifact()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var sarifPath = project.VerifyOutputPath("net8.0", "result.sarif");
        var build = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifySarifFile", sarifPath));
        Assert.That(build.ExitCode, Is.Zero, build.Output);
        Assert.That(build.Output, Does.Contain("SharpProof Proven"));
        Assert.That(File.Exists(project.RequestPath), Is.True);
        Assert.That(File.Exists(project.ResultPath), Is.True);
        Assert.That(File.Exists(sarifPath), Is.True);
        using (var sarif = JsonDocument.Parse(
                   await File.ReadAllTextAsync(sarifPath)))
        {
            var run = sarif.RootElement.GetProperty("runs")[0];
            var result = run.GetProperty("results")[0];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    sarif.RootElement.GetProperty("version").GetString(),
                    Is.EqualTo("2.1.0"));
                Assert.That(
                    run.GetProperty("invocations")[0]
                        .GetProperty("executionSuccessful").GetBoolean(),
                    Is.True);
                Assert.That(
                    result.GetProperty("ruleId").GetString(),
                    Is.EqualTo("SharpProof.Proven"));
                Assert.That(
                    result.GetProperty("kind").GetString(),
                    Is.EqualTo("pass"));
            }
        }

        var request = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        var artifact = await CompilerManifestArtifact.ReadAsync(
            request.CompilerManifest.Path);
        Assert.That(artifact.SyntaxTrees, Is.Not.Empty);
        Assert.That(artifact.References, Is.Not.Empty);
        var source = artifact.SyntaxTrees.Single(static tree =>
            Path.GetFileName(tree.Path) == "Subject.cs");
        Assert.That(
            Path.IsPathFullyQualified(source.Path),
            Is.True);
        Assert.That(
            artifact.References.All(static reference =>
                Path.IsPathFullyQualified(reference.Path)),
            Is.True);
        Assert.That(
            File.Exists(source.Path),
            Is.True);
        Assert.That(
            artifact.References.All(static reference =>
                File.Exists(reference.Path)),
            Is.True);
        Assert.That(
            artifact.SyntaxTrees.SelectMany(static tree =>
                tree.PreprocessorSymbols),
            Does.Contain("NET8_0"));
        using (Assert.EnterMultipleScope())
        {
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
                artifact.TargetFramework,
                Is.EqualTo("net8.0"));
            Assert.That(
                artifact.CompilerVersion,
                Is.EqualTo("4.14.0.0"));
            Assert.That(
                artifact.SyntaxTrees.Select(static tree =>
                    tree.LanguageVersion),
                Has.All.EqualTo("CSharp12"));
            Assert.That(
                artifact.Options.NullableContext,
                Is.EqualTo("Disable"));
            Assert.That(
                artifact.Options.OptimizationLevel,
                Is.EqualTo("Release"));
            Assert.That(artifact.Options.CheckOverflow, Is.False);
            Assert.That(artifact.Options.AllowUnsafe, Is.False);
            Assert.That(artifact.Options.Deterministic, Is.True);
            Assert.That(
                artifact.Options.MetadataImportOptions,
                Is.EqualTo("Public"));
            Assert.That(artifact.Options.WarningLevel, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                artifact.Options.GeneralDiagnosticOption,
                Is.EqualTo("Default"));
            Assert.That(
                artifact.Options.SpecificDiagnosticOptions.Zip(
                    artifact.Options.SpecificDiagnosticOptions.Skip(1),
                    static (left, right) => StringComparer.Ordinal.Compare(
                        left.Id, right.Id) < 0).All(static ordered => ordered),
                Is.True);
            Assert.That(
                artifact.Options.AssemblyIdentityComparer,
                Is.EqualTo("Desktop"));
            Assert.That(artifact.Options.Usings, Is.Not.Null);
            Assert.That(
                artifact.Options.ResolverPolicy,
                Is.EqualTo("EvidenceOnly"));
            Assert.That(
                artifact.Options.OutputKind,
                Is.EqualTo("DynamicallyLinkedLibrary"));
            Assert.That(
                artifact.Options.Platform,
                Is.EqualTo("AnyCpu"));
            Assert.That(request.Cache.Enabled, Is.True);
            Assert.That(
                request.Cache.MaximumBytes,
                Is.EqualTo(WorkerCacheOptions.DefaultMaximumBytes));
            Assert.That(
                request.VerifyPolicy,
                Is.EqualTo(WorkerVerifyPolicy.Advisory));
            Assert.That(artifact.Features, Is.EqualTo(WorkerFeatureSet.All));
            Assert.That(
                request.AssumptionPolicy,
                Is.EqualTo(WorkerAssumptionPolicy.Allow));
            Assert.That(
                request.CompilerManifest.Path,
                Does.EndWith("compiler-manifest.json"));
            Assert.That(
                request.CompilerManifest.Sha256,
                Does.Match("^[0-9a-f]{64}$"));
        }

        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        await AssertPublicationBindingAsync(request, response);
        var manifestBytes = await File.ReadAllBytesAsync(project.CompilerManifestPath);
        using var manifestDocument = JsonDocument.Parse(manifestBytes);
        Assert.That(response.Errors, Is.Empty);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                string.Concat(SHA256.HashData(manifestBytes).Select(
                    static value => value.ToString(
                        "x2",
                        CultureInfo.InvariantCulture))),
                Is.EqualTo(request.CompilerManifest.Sha256));
            Assert.That(
                manifestDocument.RootElement.GetProperty("manifest")
                    .GetProperty("hash").GetString(),
                Is.EqualTo(response.Manifest.Hash));
            Assert.That(
                response.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                request.CompilerManifest.Path,
                Is.EqualTo(Path.GetFullPath(project.CompilerManifestPath)));
        }
    }

    [Test]
    public async Task VerifierLaunchPreservesPercentCharactersInPaths()
    {
        RequireWindowsWorker();
        var visualStudioMsBuild = ConsumerProject.FindVisualStudioMsBuild();
        if (visualStudioMsBuild == null)
        {
            Assert.Ignore("Visual Studio MSBuild is not installed.");
            return;
        }
        using var project = ConsumerProject.CreateWithPercentPath(
            IdentitySource,
            ("TargetFrameworks", "netstandard2.0"));
        var requestPath = project.VerifyOutputPath(
            "netstandard2.0",
            "request.json");
        var resultPath = project.VerifyOutputPath(
            "netstandard2.0",
            "result.json");
        var manifestPath = project.VerifyOutputPath(
            "netstandard2.0",
            "compiler-manifest.json");

        var build = await project.BuildAsync(verify: true);
        var visualStudio = await project.BuildWithVisualStudioMsBuildAsync(
            visualStudioMsBuild);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(build.ExitCode, Is.Zero, build.Output);
            Assert.That(
                visualStudio.ExitCode,
                Is.Zero,
                visualStudio.Output);
            Assert.That(File.Exists(requestPath), Is.True, build.Output);
            Assert.That(File.Exists(resultPath), Is.True, build.Output);
            Assert.That(File.Exists(manifestPath), Is.True, build.Output);
        }
    }

    [Test]
    public async Task LongLocalPublicationPathsWorkInDotNetAndVisualStudioMsBuild()
    {
        RequireWindowsWorker();
        var visualStudioMsBuild = ConsumerProject.FindVisualStudioMsBuild();
        if (visualStudioMsBuild == null)
        {
            Assert.Ignore("Visual Studio MSBuild is not installed.");
            return;
        }
        using var project = ConsumerProject.CreateConfigured(
            IdentitySource,
            ("TargetFrameworks", "netstandard2.0"));
        var segment = new string('l', 48);
        var publicationDirectory = Path.Combine(
            project.Root,
            segment + "1",
            segment + "2",
            segment + "3",
            segment + "4",
            segment + "5");
        Directory.CreateDirectory(publicationDirectory);
        var requestPath = Path.Combine(publicationDirectory, "request.json");
        var resultPath = Path.Combine(publicationDirectory, "result.json");
        var manifestPath = Path.Combine(publicationDirectory, "manifest.json");
        var sarifPath = Path.Combine(publicationDirectory, "result.sarif");
        var cachePath = Path.Combine(publicationDirectory, "cache");
        Assert.That(resultPath.Length, Is.GreaterThan(260));

        var dotnet = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyRequestFile", requestPath),
            ("SharpProofVerifyResultFile", resultPath),
            ("SharpProofCompilerManifestFile", manifestPath),
            ("SharpProofVerifyCacheDirectory", cachePath),
            ("SharpProofVerifySarifFile", sarifPath));
        var visualStudio = await project.BuildWithVisualStudioMsBuildAsync(
            visualStudioMsBuild,
            ("SharpProofVerifyRequestFile", requestPath),
            ("SharpProofVerifyResultFile", resultPath),
            ("SharpProofCompilerManifestFile", manifestPath),
            ("SharpProofVerifyCacheDirectory", cachePath),
            ("SharpProofVerifySarifFile", sarifPath));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dotnet.ExitCode, Is.Zero, dotnet.Output);
            Assert.That(visualStudio.ExitCode, Is.Zero, visualStudio.Output);
            Assert.That(File.Exists(requestPath), Is.True);
            Assert.That(File.Exists(resultPath), Is.True);
            Assert.That(File.Exists(manifestPath), Is.True);
            Assert.That(File.Exists(sarifPath), Is.True);
        }
    }

    [Test]
    public async Task OverlongProjectDirectoryFailsBeforeCompilerLaunch()
    {
        RequireWindowsWorker();
        var visualStudioMsBuild = ConsumerProject.FindVisualStudioMsBuild();
        if (visualStudioMsBuild == null)
        {
            Assert.Ignore("Visual Studio MSBuild is not installed.");
            return;
        }
        using var project = ConsumerProject.CreateWithLongPath(IdentitySource);
        Assert.That(project.Root.Length, Is.GreaterThan(239));

        var restore = await project.RestoreAsync();
        var dotnet = await project.BuildAsync(verify: true);
        var visualStudio = await project.BuildWithVisualStudioMsBuildAsync(
            visualStudioMsBuild);
        const string expected =
            "SharpProof verification requires MSBuildProjectDirectory to be at most 239 characters";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restore.ExitCode, Is.Zero, restore.Output);
            Assert.That(dotnet.ExitCode, Is.Not.Zero, dotnet.Output);
            Assert.That(dotnet.Output, Does.Contain(expected));
            Assert.That(visualStudio.ExitCode, Is.Not.Zero, visualStudio.Output);
            Assert.That(visualStudio.Output, Does.Contain(expected));
        }
    }

    [Test]
    public async Task VerifierHostMustBeTheDirectDotNetMuxer()
    {
        RequireWindowsWorker();
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
            throw new InvalidOperationException(
                "The test host did not disclose its dotnet host path.");
        using (var direct = ConsumerProject.Create(IdentitySource))
        {
            var build = await direct.BuildAsync(
                verify: true,
                ("SharpProofDotNetHost", host));
            Assert.That(build.ExitCode, Is.Zero, build.Output);
        }

        using var wrapper = ConsumerProject.Create(IdentitySource);
        var rejected = await wrapper.BuildAsync(
            verify: true,
            ("SharpProofDotNetHost", typeof(Program).Assembly.Location));

        Assert.That(rejected.ExitCode, Is.Not.Zero, rejected.Output);
        Assert.That(
            rejected.Output,
            Does.Contain(
                "SharpProofDotNetHost must name the direct dotnet.exe muxer."));

        var fakeDirectory = Path.Combine(
            Path.GetDirectoryName(wrapper.ProjectPath)!, "fake-dotnet");
        Directory.CreateDirectory(Path.Combine(fakeDirectory, "host", "fxr"));
        var fakeHost = Path.Combine(fakeDirectory, "dotnet.exe");
        File.Copy(
            Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe"),
            fakeHost);
        var fake = await wrapper.BuildAsync(
            verify: true,
            ("SharpProofDotNetHost", fakeHost));
        Assert.That(fake.ExitCode, Is.Not.Zero, fake.Output);
        Assert.That(
            fake.Output,
            Does.Contain(
                "SharpProofDotNetHost must match the trusted current dotnet.exe muxer."));
    }

    [Test]
    public void UnrelatedLegacyPublicationLockDoesNotBlockVerification()
    {
        RequireWindowsWorker();
        using var legacyLock = new Mutex(
            initiallyOwned: true,
            "Local\\SharpProof.Publish",
            out var ownsLegacyLock);
        Assert.That(ownsLegacyLock, Is.True);
        try
        {
            using var project = ConsumerProject.Create(IdentitySource);
            var build = project.BuildAsync(verify: true)
                .GetAwaiter().GetResult();

            Assert.That(build.ExitCode, Is.Zero, build.Output);
            Assert.That(File.Exists(project.ResultPath), Is.True, build.Output);
        }
        finally
        {
            legacyLock.ReleaseMutex();
        }
    }

    [Test]
    public void PublicationLockIsGlobalAndStableAcrossReplacement()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var directory = Path.GetDirectoryName(project.ProjectPath)!;
        var result = Path.Combine(directory, "publication.json");

        var before = WindowsPathIdentity.PublicationMutexName(result);
        File.WriteAllText(result, "first");
        var existing = WindowsPathIdentity.PublicationMutexName(result);
        File.WriteAllText(result, "replacement");
        var replaced = WindowsPathIdentity.PublicationMutexName(result);

        Assert.That(before, Does.StartWith("Global\\SharpProof.Publish."));
        Assert.That(existing, Is.EqualTo(before));
        Assert.That(replaced, Is.EqualTo(before));
    }

    [Test]
    public async Task ChangingOneMemberOfAPublishedSetRequiresCleanOutputMetadata()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var first = await project.BuildAsync(verify: true);
        Assert.That(first.ExitCode, Is.Zero, first.Output);
        var alternateResult = Path.Combine(
            Path.GetDirectoryName(project.ResultPath)!,
            "alternate-result.json");

        var second = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyResultFile", alternateResult));

        Assert.That(second.ExitCode, Is.Not.Zero, second.Output);
        Assert.That(second.Output, Does.Contain("partially overlap"));
        Assert.That(File.Exists(alternateResult), Is.False);
    }

    [Test]
    public async Task VerificationPublishesCompilerManifestPerTargetFramework()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);

        var build = await project.BuildAsync(verify: true);

        Assert.That(build.ExitCode, Is.Zero, build.Output);
        Assert.That(File.Exists(project.CompilerManifestPath), Is.True);
        var manifest = await File.ReadAllTextAsync(project.CompilerManifestPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                project.CompilerManifestPath,
                Does.Contain(
                    Path.Combine("Release", "net8.0", "SharpProof")));
            Assert.That(
                manifest,
                Does.Contain("\"schema\":\"SharpProof.CompilerManifest\""));
            Assert.That(manifest, Does.Contain("\"targetFramework\":\"net8.0\""));
            Assert.That(manifest, Does.Not.Contain('\r'));
        }
    }

    [Test]
    public async Task WhitespaceCacheDirectoryUsesFrameworkScopedDefault()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var build = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyCacheDirectory", " "));
        Assert.That(build.ExitCode, Is.Zero, build.Output);

        var request = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        Assert.That(
            request.Cache.Directory,
            Is.EqualTo(Path.Combine(
                Path.GetDirectoryName(project.RequestPath)!, "cache")));
    }

    [Test]
    public async Task MultiTargetVerificationPublishesOneBoundTriplePerFramework()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.CreateConfigured(
            IdentitySource, ("TargetFrameworks", "net8.0;net9.0"));

        var build = await project.BuildAsync(verify: true);

        Assert.That(build.ExitCode, Is.Zero, build.Output);
        foreach (var framework in new[] { "net8.0", "net9.0" })
        {
            var request = WorkerProtocolJson.DeserializeRequest(
                await File.ReadAllTextAsync(
                    project.VerifyOutputPath(framework, "request.json")))!;
            var response = WorkerProtocolJson.DeserializeResponse(
                await File.ReadAllTextAsync(
                    project.VerifyOutputPath(framework, "result.json")))!;
            var artifact = await AssertPublicationBindingAsync(
                request, response);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(artifact.TargetFramework,
                    Is.EqualTo(framework));
                Assert.That(
                    request.CompilerManifest.Path,
                    Is.EqualTo(Path.GetFullPath(
                        project.VerifyOutputPath(
                            framework, "compiler-manifest.json"))));
            }
        }
    }

    [Test]
    public async Task MissingCompilerManifestFailsBeforeWorkerLaunch()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: false);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var build = await project.RunVerificationTargetAsync();

        Assert.That(build.ExitCode, Is.Not.Zero);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(build.Output, Does.Contain("SP0049"));
            Assert.That(
                build.Output,
                Does.Contain(
                    "required final compiler manifest"));
            Assert.That(File.Exists(project.RequestPath), Is.False);
            Assert.That(File.Exists(project.ResultPath), Is.False);
        }
    }

    [Test]
    public async Task PrepublicationFailuresInvalidatePriorStableResult()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var stableResult = await File.ReadAllBytesAsync(project.ResultPath);
        var sarifPath = project.VerifyOutputPath(
            "net8.0", "stale-result.sarif");

        await AssertInvalidatedAsync(
            ("_SharpProofCompilerManifestPath",
                project.CompilerManifestPath + ".missing"));
        await AssertInvalidatedAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofLauncherPath",
                project.CompilerManifestPath + ".missing-launcher.dll"));
        await AssertInvalidatedAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofWorkerPath",
                project.CompilerManifestPath + ".missing-worker.dll"));
        await AssertInvalidatedAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofVerifyPolicy", "invalid"));
        await AssertInvalidatedAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofVerifyQueryRlimit", "0"));

        async Task AssertInvalidatedAsync(
            params (string Name, string Value)[] properties)
        {
            await File.WriteAllBytesAsync(project.ResultPath, stableResult);
            await File.WriteAllTextAsync(sarifPath, "stale");

            var failure = await project.RunVerificationTargetAsync([
                .. properties,
                ("SharpProofVerifySarifFile", sarifPath)
            ]);

            Assert.That(failure.ExitCode, Is.Not.Zero, failure.Output);
            Assert.That(
                File.Exists(project.ResultPath),
                Is.False,
                failure.Output);
            Assert.That(File.Exists(sarifPath), Is.False, failure.Output);
        }
    }

    [Test]
    public async Task WorkerExitWithoutResultProducesTypedFailure()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var resultlessWorker = await project.CreateResultlessWorkerAsync();

        var build = await project.BuildAsync(
            verify: true,
            ("SharpProofWorkerPath", resultlessWorker));

        Assert.That(build.ExitCode, Is.Not.Zero, build.Output);
        Assert.That(File.Exists(project.ResultPath), Is.True, build.Output);
        var request = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        await AssertPublicationBindingAsync(
            request,
            response,
            resultlessWorker);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(build.Output, Does.Contain("worker.no_result"));
            Assert.That(
                build.Output,
                Does.Contain("verifier failed with exit code 3"));
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.MalformedResult));
            Assert.That(response.Errors, Has.Length.EqualTo(1));
            Assert.That(
                response.Errors[0].Code,
                Is.EqualTo("worker.no_result"));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task StrictProfileEnablesRequireProvenAndRejectsAssumptionsByDefault()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);

        var build = await project.BuildAsync(
            verify: null,
            ("SharpProofProfile", "strict"));

        Assert.That(build.ExitCode, Is.Zero, build.Output);
        var request = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                request.VerifyPolicy,
                Is.EqualTo(WorkerVerifyPolicy.RequireProven));
            Assert.That(
                request.AssumptionPolicy,
                Is.EqualTo(WorkerAssumptionPolicy.Error));
        }
    }

    [Test]
    public async Task StrictProfileCannotDisableVerification()
    {
        using var project = ConsumerProject.Create(IdentitySource);

        var build = await project.BuildAsync(
            verify: false,
            ("SharpProofProfile", "strict"));

        Assert.That(build.ExitCode, Is.Not.Zero);
        Assert.That(
            build.Output,
            Does.Contain(
                "SharpProofProfile=strict requires SharpProofVerify=true"));
    }

    [Test]
    public async Task ProjectBodyConfigurationRejectsRetiredMode()
    {
        RequireWindowsWorker();
        using var strict = ConsumerProject.CreateConfigured(
            IdentitySource,
            ("SharpProofProfile", "strict"),
            ("SharpProofFeatures", "contracts"));

        var strictBuild = await strict.BuildAsync(verify: null);

        Assert.That(strictBuild.ExitCode, Is.Zero, strictBuild.Output);
        var strictRequest = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(strict.RequestPath))!;
        var strictArtifact = await CompilerManifestArtifact.ReadAsync(
            strictRequest.CompilerManifest.Path);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                strictArtifact.Features,
                Is.EqualTo(WorkerFeatureSet.Contracts));
            Assert.That(
                strictRequest.VerifyPolicy,
                Is.EqualTo(WorkerVerifyPolicy.RequireProven));
            Assert.That(
                strictRequest.AssumptionPolicy,
                Is.EqualTo(WorkerAssumptionPolicy.Error));
        }

        using var legacy = ConsumerProject.CreateConfigured(
            IdentitySource,
            ("SharpProofMode", "contracts"));
        var legacyBuild = await legacy.BuildAsync(verify: true);

        Assert.That(legacyBuild.ExitCode, Is.Not.Zero);
        Assert.That(
            legacyBuild.Output,
            Does.Contain("SharpProofMode was removed before preview.1"));
        Assert.That(File.Exists(legacy.RequestPath), Is.False);
    }

    [Test]
    public async Task UnknownClaimSeverityFollowsVerificationPolicy()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Normalize(long value) {
                    Contract.Ensures(Contract.Result<long>() >= 0);
                    while (value < 0) value++;
                    return value;
                }
            }
            """);

        var advisory = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyPolicy", "advisory"));
        Assert.That(advisory.ExitCode, Is.Zero, advisory.Output);
        Assert.That(advisory.Output, Does.Contain("info SP0047"));

        var warning = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyPolicy", "warn-on-unknown"));
        Assert.That(warning.ExitCode, Is.Zero, warning.Output);
        Assert.That(warning.Output, Does.Contain("warning SP0047"));

        var required = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyPolicy", "require-proven"));
        Assert.That(required.ExitCode, Is.Not.Zero);
        Assert.That(required.Output, Does.Contain("error SP0047"));
    }

    [Test]
    public async Task ConcurrentInvocationsUseIsolatedWorkerFiles()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                public static int Selected() {
                    Func<int> value = () => 1;
                    return value();
                }
            }
            """);
        var firstTask = project.BuildIsolatedAsync("first", "effects");
        var secondTask = project.BuildIsolatedAsync("second", "contracts");
        var results = await Task.WhenAll(firstTask, secondTask);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].ExitCode, Is.Zero, results[0].Output);
            Assert.That(results[1].ExitCode, Is.Zero, results[1].Output);
        }

        var publishedRequest = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        var publishedResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        var publishedArtifact = await AssertPublicationBindingAsync(
            publishedRequest, publishedResponse);
        Assert.That(
            publishedResponse.Manifest.Callables.Length,
            Is.EqualTo(
                publishedArtifact.Features == WorkerFeatureSet.Effects
                    ? 1
                    : 0),
            "The stable request/result files must describe one completed invocation.");
    }

    [Test]
    public async Task VisualStudioMsBuildSerializesCooperativePublications()
    {
        RequireWindowsWorker();
        var visualStudioMsBuild = ConsumerProject.FindVisualStudioMsBuild();
        if (visualStudioMsBuild == null)
        {
            Assert.Ignore("Visual Studio MSBuild is not installed.");
            return;
        }
        using var project = ConsumerProject.CreateConfigured(
            IdentitySource,
            ("TargetFrameworks", "netstandard2.0"));
        var publication = Directory.CreateDirectory(
            Path.Combine(project.Root, "publication"));
        var request = Path.Combine(publication.FullName, "request.json");
        var result = Path.Combine(publication.FullName, "result.json");
        var manifest = Path.Combine(publication.FullName, "manifest.json");
        var sarif = Path.Combine(publication.FullName, "result.sarif");

        Task<BuildResult> BuildAsync(string name, string features)
        {
            return project.BuildWithVisualStudioMsBuildAsync(
                visualStudioMsBuild,
                ("BaseIntermediateOutputPath",
                    Path.Combine(project.Root, "obj-" + name) +
                    Path.DirectorySeparatorChar),
                ("BaseOutputPath",
                    Path.Combine(project.Root, "bin-" + name) +
                    Path.DirectorySeparatorChar),
                ("SharpProofFeatures", features),
                ("SharpProofVerifyRequestFile", request),
                ("SharpProofVerifyResultFile", result),
                ("SharpProofCompilerManifestFile", manifest),
                ("SharpProofVerifySarifFile", sarif));
        }

        var firstIntermediate = Path.Combine(project.Root, "obj-first") +
            Path.DirectorySeparatorChar;
        var secondIntermediate = Path.Combine(project.Root, "obj-second") +
            Path.DirectorySeparatorChar;
        var restores = await Task.WhenAll(
            project.RestoreAsync(("BaseIntermediateOutputPath", firstIntermediate)),
            project.RestoreAsync(("BaseIntermediateOutputPath", secondIntermediate)));
        Assert.That(restores, Has.All.Matches<BuildResult>(
            static restore => restore.ExitCode == 0));

        var builds = await Task.WhenAll(
            BuildAsync("first", "effects"),
            BuildAsync("second", "contracts"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(builds[0].ExitCode, Is.Zero, builds[0].Output);
            Assert.That(builds[1].ExitCode, Is.Zero, builds[1].Output);
            Assert.That(File.Exists(request), Is.True);
            Assert.That(File.Exists(result), Is.True);
            Assert.That(File.Exists(manifest), Is.True);
            Assert.That(File.Exists(sarif), Is.True);
        }
        var publishedRequest = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(request))!;
        var publishedResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(result))!;
        await AssertPublicationBindingAsync(
            publishedRequest,
            publishedResponse);
    }

    [Test]
    public async Task MalformedWorkerOutputPreservesTheStablePublication()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var request = await File.ReadAllTextAsync(project.RequestPath);
        var result = await File.ReadAllTextAsync(project.ResultPath);
        var malformedWorker = await project.CreateMalformedWorkerAsync();
        var malformedManifest = project.CompilerManifestPath + ".malformed";
        var sarifPath = project.VerifyOutputPath(
            "net8.0", "malformed-result.sarif");

        var malformed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofCompilerManifestFile",
                malformedManifest),
            ("SharpProofWorkerPath", malformedWorker),
            ("SharpProofVerifySarifFile", sarifPath));

        Assert.That(malformed.ExitCode, Is.Not.Zero);
        Assert.That(malformed.Output, Does.Contain("unavailable or malformed"));
        var failedRequest = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        var failedResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        await AssertPublicationBindingAsync(
            failedRequest, failedResponse, malformedWorker);
        using var sarif = JsonDocument.Parse(
            await File.ReadAllTextAsync(sarifPath));
        var invocation = sarif.RootElement.GetProperty("runs")[0]
            .GetProperty("invocations")[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                failedResponse.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.MalformedResult));
            Assert.That(failedRequest.CompilerManifest.Path,
                Is.EqualTo(Path.GetFullPath(malformedManifest)));
            Assert.That(await File.ReadAllTextAsync(project.RequestPath),
                Is.Not.EqualTo(request));
            Assert.That(await File.ReadAllTextAsync(project.ResultPath),
                Is.Not.EqualTo(result));
            Assert.That(
                invocation.GetProperty("executionSuccessful").GetBoolean(),
                Is.False);
            Assert.That(
                invocation.GetProperty("toolExecutionNotifications")[0]
                    .GetProperty("descriptor").GetProperty("id").GetString(),
                Is.EqualTo("worker.malformed_result"));
        }
    }

    [Test]
    public async Task LauncherRepairsMalformedWorkerResultInProcess()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var requestPath = project.VerifyOutputPath(
            "net8.0", "in-process-malformed-request.json");
        var resultPath = project.VerifyOutputPath(
            "net8.0", "in-process-malformed-result.json");

        var exitCode = await Program.RunMain(
            [
                "verify",
                "--worker", WorkerOutputPath(),
                "--request", requestPath,
                "--result", resultPath,
                "--compiler-manifest", project.CompilerManifestPath,
                "--verify-policy", "advisory",
                "--assumption-policy", "allow"
            ],
            static path => WorkerBinaryIdentity.ComputeSha256(path),
            static (arguments, _, _, _) =>
            {
                File.WriteAllText(arguments.ResultPath, "not-json");
                return 0;
            });

        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(resultPath))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.MalformedResult));
            Assert.That(
                response.Errors.Select(static error => error.Code),
                Does.Contain("worker.malformed_result"));
        }
    }

    [Test]
    public async Task LauncherFailsClosedOnAnUnclassifiedException()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var requestPath = project.VerifyOutputPath(
            "net8.0", "in-process-unclassified-request.json");
        var resultPath = project.VerifyOutputPath(
            "net8.0", "in-process-unclassified-result.json");

        // An IOException is not one of the four types the launcher classifies.
        // It used to escape Main, so the process died leaving no result file at
        // all rather than a fail-closed response.
        var exitCode = await Program.RunMain(
            [
                "verify",
                "--worker", WorkerOutputPath(),
                "--request", requestPath,
                "--result", resultPath,
                "--compiler-manifest", project.CompilerManifestPath,
                "--verify-policy", "advisory",
                "--assumption-policy", "allow"
            ],
            static path => WorkerBinaryIdentity.ComputeSha256(path),
            static (_, _, _, _) => throw new IOException("device not ready"));

        Assert.That(File.Exists(resultPath), Is.True);
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(resultPath))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.InfrastructureFailure));
            Assert.That(
                response.Errors.Select(static error => error.Code),
                Does.Contain("launcher.infrastructure"));
        }
    }

    [Test]
    public async Task LauncherReportsStagedWorkerClosureHashMismatchInProcess()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var requestPath = project.VerifyOutputPath(
            "net8.0", "in-process-hash-mismatch-request.json");
        var resultPath = project.VerifyOutputPath(
            "net8.0", "in-process-hash-mismatch-result.json");

        var exitCode = await Program.RunMain(
            [
                "verify",
                "--worker", WorkerOutputPath(),
                "--request", requestPath,
                "--result", resultPath,
                "--compiler-manifest", project.CompilerManifestPath,
                "--verify-policy", "advisory",
                "--assumption-policy", "allow"
            ],
            static _ => new string('0', WorkerProtocolVersions.EmptySha256.Length));

        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(resultPath))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.ContainmentFailure));
            Assert.That(
                response.Errors.Select(static error => error.Code),
                Does.Contain("containment.unavailable"));
        }
    }

    [Test]
    public async Task LauncherReportsInProcessPublicationFailure()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var stableRequest = await File.ReadAllTextAsync(project.RequestPath);
        var requestPath = project.VerifyOutputPath(
            "net8.0", "in-process-publication-request.json");
        var resultPath = project.VerifyOutputPath(
            "net8.0", "in-process-publication-result.json");
        var publishedManifest = project.VerifyOutputPath(
            "net8.0", "in-process-published-manifest.json");

        using (File.Open(
                   project.RequestPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read))
        {
            var exitCode = await Program.RunMain(
                [
                    "verify",
                    "--worker", WorkerOutputPath(),
                    "--request", requestPath,
                    "--result", resultPath,
                    "--compiler-manifest", project.CompilerManifestPath,
                    "--verify-policy", "advisory",
                    "--assumption-policy", "allow",
                    "--publish-request", project.RequestPath,
                    "--publish-result", project.ResultPath,
                    "--publish-compiler-manifest", publishedManifest
                ],
                static path => WorkerBinaryIdentity.ComputeSha256(path),
                (arguments, _, _, _) =>
                {
                    var request = WorkerProtocolJson.DeserializeRequest(
                        File.ReadAllText(arguments.RequestPath))!;
                    var response = WorkerProtocolJson.DeserializeResponse(
                        File.ReadAllText(project.ResultPath))!;
                    response.RequestHash = WorkerProtocolJson.ComputeRequestHash(request);
                    response.InputHash = Program.ComputeExpectedInputHash(
                        arguments.WorkerPath,
                        request,
                        File.ReadAllBytes(arguments.CompilerManifestPath));
                    File.WriteAllText(
                        arguments.ResultPath,
                        WorkerProtocolJson.SerializeResponse(response));
                    return 0;
                });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(3));
                Assert.That(
                    await File.ReadAllTextAsync(project.RequestPath),
                    Is.EqualTo(stableRequest));
                Assert.That(File.Exists(project.ResultPath), Is.False);
            }
        }
    }

    [Test]
    public async Task HardTimeoutReplacesWorkerOwnedMalformedOutput()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var worker = await project.CreateMalformedThenHangWorkerAsync();

        var run = await project.BuildAsync(
            verify: true,
            ("SharpProofWorkerPath", worker),
            ("SharpProofVerifyMethodWallTimeMilliseconds", "1"),
            ("SharpProofVerifyProjectWallTimeMilliseconds", "100"),
            ("SharpProofVerifyTerminationGraceMilliseconds", "1000"));

        Assert.That(run.ExitCode, Is.Not.Zero, run.Output);
        Assert.That(File.Exists(project.ResultPath), Is.True, run.Output);
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(run.Output, Does.Contain("ProjectTimeout"));
            Assert.That(run.Output, Does.Not.Contain("worker.malformed_result"));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.TimedOut));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Summary.Versions.WorkerVersion,
                Is.EqualTo("launcher"));
            Assert.That(response.ClaimResults,
                Has.All.Property(nameof(WorkerClaimResult.Reason))
                    .EqualTo(WorkerClaimReason.ProjectTimeout));
        }
    }

    [Test]
    public async Task PublicationFailureLeavesStableResultAbsent()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var request = await File.ReadAllTextAsync(project.RequestPath);
        var sarifPath = project.VerifyOutputPath(
            "net8.0", "publication-failure.sarif");

        BuildResult failed;
        using (File.Open(
                   project.RequestPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read))
        {
            failed = await project.RunVerificationTargetAsync(
                ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
                ("SharpProofCompilerManifestFile",
                    project.CompilerManifestPath + ".failed"),
                ("SharpProofVerifyPolicy", "advisory"),
                ("SharpProofVerifySarifFile", sarifPath));
        }

        Assert.That(failed.ExitCode, Is.Not.Zero);
        Assert.That(failed.Output, Does.Contain("could not be published"));
        Assert.That(
            await File.ReadAllTextAsync(project.RequestPath),
            Is.EqualTo(request));
        Assert.That(File.Exists(project.ResultPath), Is.False);
        Assert.That(File.Exists(sarifPath), Is.False);
        Assert.That(File.Exists(project.CompilerManifestPath + ".failed"),
            Is.True);
    }

    [Test]
    public async Task AliasedPublicationPathsAreRejectedWithoutChangingStableOutputs()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var request = await File.ReadAllTextAsync(project.RequestPath);
        var result = await File.ReadAllTextAsync(project.ResultPath);

        var failed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofVerifyResultFile", project.RequestPath));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.Not.Zero);
            Assert.That(
                failed.Output,
                Does.Contain("SharpProof launcher input is invalid: ArgumentException"));
            Assert.That(
                await File.ReadAllTextAsync(project.RequestPath),
                Is.EqualTo(request));
            Assert.That(
                await File.ReadAllTextAsync(project.ResultPath),
                Is.EqualTo(result));
        }

        failed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofVerifySarifFile", project.RequestPath));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.Not.Zero);
            Assert.That(
                failed.Output,
                Does.Contain("SharpProof launcher input is invalid: ArgumentException"));
            Assert.That(
                await File.ReadAllTextAsync(project.RequestPath),
                Is.EqualTo(request));
            Assert.That(
                await File.ReadAllTextAsync(project.ResultPath),
                Is.EqualTo(result));
        }

        failed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofVerifySarifFile", project.ResultPath));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.Not.Zero);
            Assert.That(
                failed.Output,
                Does.Contain("SharpProof launcher input is invalid: ArgumentException"));
            Assert.That(
                await File.ReadAllTextAsync(project.RequestPath),
                Is.EqualTo(request));
            Assert.That(
                await File.ReadAllTextAsync(project.ResultPath),
                Is.EqualTo(result));
        }
    }

    [Test]
    public async Task WorkerRuntimeCompanionAliasIsRejectedBeforeInvalidationDeletesIt()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var sourceWorker = WorkerOutputPath();
        var collisionWorker = project.CollisionWorkerPath;
        Directory.CreateDirectory(Path.GetDirectoryName(collisionWorker)!);
        File.Copy(sourceWorker, collisionWorker, overwrite: true);
        foreach (var extension in new[] { ".deps.json", ".runtimeconfig.json" })
        {
            File.Copy(
                Path.ChangeExtension(sourceWorker, extension),
                Path.ChangeExtension(collisionWorker, extension),
                overwrite: true);
        }

        var collisionCompanion = Path.ChangeExtension(
            collisionWorker, ".deps.json");
        var failed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofWorkerPath", collisionWorker),
            ("SharpProofVerifyResultFile", collisionCompanion),
            ("_SharpProofSkipTestInvalidation", "true"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.Not.Zero);
            Assert.That(
                failed.Output.Contains(
                    "SharpProof launcher input is invalid: ArgumentException",
                    StringComparison.Ordinal),
                Is.True,
                failed.Output);
            Assert.That(File.Exists(collisionWorker), Is.True);
            Assert.That(File.Exists(collisionCompanion), Is.True);
        }
    }

    [Test]
    public async Task ExtendedWorkerCompanionAliasIsRejectedBeforeInvalidationDeletesIt()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var sourceWorker = WorkerOutputPath();
        var collisionWorker = project.CollisionWorkerPath;
        Directory.CreateDirectory(Path.GetDirectoryName(collisionWorker)!);
        File.Copy(sourceWorker, collisionWorker, overwrite: true);
        foreach (var extension in new[] { ".deps.json", ".runtimeconfig.json" })
        {
            File.Copy(
                Path.ChangeExtension(sourceWorker, extension),
                Path.ChangeExtension(collisionWorker, extension),
                overwrite: true);
        }

        var collisionCompanion = Path.ChangeExtension(
            collisionWorker, ".deps.json");
        var expectedBytes = await File.ReadAllBytesAsync(collisionCompanion);
        var extendedAlias = @"\\?\" + Path.GetFullPath(collisionCompanion);
        var failed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofWorkerPath", collisionWorker),
            ("SharpProofVerifyResultFile", extendedAlias));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.Not.Zero);
            Assert.That(File.Exists(collisionCompanion), Is.True);
            Assert.That(
                await File.ReadAllBytesAsync(collisionCompanion),
                Is.EqualTo(expectedBytes));
        }
    }

    [Test]
    public async Task HardLinkedWorkerCompanionIsRejectedBeforeInvalidationDeletesIt()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var sourceWorker = WorkerOutputPath();
        var collisionWorker = project.CollisionWorkerPath;
        Directory.CreateDirectory(Path.GetDirectoryName(collisionWorker)!);
        File.Copy(sourceWorker, collisionWorker, overwrite: true);
        foreach (var extension in new[] { ".deps.json", ".runtimeconfig.json" })
        {
            File.Copy(
                Path.ChangeExtension(sourceWorker, extension),
                Path.ChangeExtension(collisionWorker, extension),
                overwrite: true);
        }

        var collisionCompanion = Path.ChangeExtension(
            collisionWorker, ".deps.json");
        var expectedBytes = await File.ReadAllBytesAsync(collisionCompanion);
        var hardLink = Path.Combine(
            Path.GetDirectoryName(project.ResultPath)!,
            "hard-linked-result.json");
        if (!CreateHardLinkW(hardLink, collisionCompanion, IntPtr.Zero))
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error());
        }

        var failed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofWorkerPath", collisionWorker),
            ("SharpProofVerifyResultFile", hardLink));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.Not.Zero);
            Assert.That(
                failed.Output,
                Does.Contain("aliases a protected file identity"));
            Assert.That(File.Exists(collisionCompanion), Is.True);
            Assert.That(
                await File.ReadAllBytesAsync(collisionCompanion),
                Is.EqualTo(expectedBytes));
        }
    }

    [Test]
    public async Task LauncherProtocolAssetRemainsProtectedByTargets()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var build = await project.BuildAsync(verify: true);
        Assert.That(build.ExitCode, Is.Zero, build.Output);
        var protocolPath = LauncherProtocolOutputPath();
        var expectedBytes = await File.ReadAllBytesAsync(protocolPath);
        var isolatedLauncherDirectory = Path.GetDirectoryName(
            project.CollisionWorkerPath)!;
        Directory.CreateDirectory(isolatedLauncherDirectory);
        _ = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("_SharpProofLauncherPath", Path.Combine(
                isolatedLauncherDirectory,
                "isolated-launcher.dll")),
            ("SharpProofVerifyResultFile", protocolPath));
        Assert.That(await File.ReadAllBytesAsync(protocolPath),
            Is.EqualTo(expectedBytes));
    }

    [Test]
    public async Task LauncherProtocolAssetAliasIsRejectedBeforeInvalidationDeletesIt()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var launcherDirectory = Path.GetDirectoryName(
            LauncherProtocolOutputPath())!;
        foreach (var fileName in new[] {
                     "SharpProof.CompilerArtifact.dll",
                     "SharpProof.Ir.dll",
                     "SharpProof.Specs.dll",
                     "SharpProof.Worker.Protocol.dll",
                     "SharpProof.Worker.Launcher.exe",
                     "System.IO.Pipelines.dll",
                     "System.Text.Encodings.Web.dll",
                     "System.Text.Json.dll"
                 })
        {
            var collisionPath = Path.Combine(launcherDirectory, fileName);
            Assert.That(File.Exists(collisionPath), Is.True, collisionPath);
            var failed = await project.RunVerificationTargetAsync(
                ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
                ("SharpProofVerifyResultFile", collisionPath));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failed.ExitCode, Is.Not.Zero, collisionPath);
                Assert.That(
                    failed.Output.Contains(
                        "SharpProof launcher input is invalid: ArgumentException",
                        StringComparison.Ordinal),
                    Is.True,
                    collisionPath);
                Assert.That(File.Exists(collisionPath), Is.True, collisionPath);
            }
        }
    }

    [Test]
    public async Task WorkerCacheDirectoryAliasIsRejectedBeforeInvalidationDeletesIt()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var stableRequest = await File.ReadAllBytesAsync(project.RequestPath);
        var stableResult = await File.ReadAllBytesAsync(project.ResultPath);
        var stableManifest = await File.ReadAllBytesAsync(
            project.CompilerManifestPath);
        foreach (var cachePath in new[] {
            project.ResultPath,
            project.RequestPath,
            project.CompilerManifestPath,
            WorkerOutputPath()
        })
        {
            var failed = await project.RunVerificationTargetAsync(
                ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
                ("SharpProofVerifyCacheDirectory", cachePath));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failed.ExitCode, Is.Not.Zero, cachePath);
                Assert.That(
                    failed.Output,
                    Does.Contain(
                        "SharpProof launcher input is invalid: ArgumentException"),
                    cachePath);
                Assert.That(
                    failed.Output,
                    Does.Contain("SharpProof I/O paths must be distinct."),
                    cachePath);
                Assert.That(File.Exists(project.RequestPath), Is.True, cachePath);
                Assert.That(File.Exists(project.ResultPath), Is.True, cachePath);
                Assert.That(
                    File.Exists(project.CompilerManifestPath),
                    Is.True,
                    cachePath);
                Assert.That(
                    await File.ReadAllBytesAsync(project.RequestPath),
                    Is.EqualTo(stableRequest),
                    cachePath);
                Assert.That(
                    await File.ReadAllBytesAsync(project.ResultPath),
                    Is.EqualTo(stableResult),
                    cachePath);
                Assert.That(
                    await File.ReadAllBytesAsync(project.CompilerManifestPath),
                    Is.EqualTo(stableManifest),
                    cachePath);
            }
        }
    }

    [Test]
    public async Task DirectLauncherRejectsRelativeCacheAliasAfterManifestResolution()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var projectDirectory = Path.GetDirectoryName(project.ProjectPath)!;
        var relativeManifest = Path.GetRelativePath(
            projectDirectory,
            project.CompilerManifestPath);
        string[] arguments = [
            "verify",
            "--worker", WorkerOutputPath(),
            "--request", project.RequestPath,
            "--result", project.ResultPath,
            "--compiler-manifest", project.CompilerManifestPath,
            "--cache-directory", relativeManifest,
            "--verify-policy", "advisory",
            "--assumption-policy", "allow"
        ];

        Assert.That(
            LauncherArguments.TryParse(arguments, out var parsed),
            Is.True);
        Assert.That(
            (Action)(() => parsed.CreateRequest(out _, out _)),
            Throws.TypeOf<ArgumentException>());
    }

    [TestCase("")]
    [TestCase("cache")]
    public async Task DirectLauncherRejectsCacheInsideWorkerRuntimeDirectory(
        string relativeCacheSuffix)
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var workerDirectory = Path.GetDirectoryName(WorkerOutputPath())!;
        var cachePath = Path.Combine(workerDirectory, relativeCacheSuffix);
        var projectDirectory = Path.GetDirectoryName(project.ProjectPath)!;
        var relativeCache = Path.GetRelativePath(projectDirectory, cachePath);
        string[] arguments = [
            "verify",
            "--worker", WorkerOutputPath(),
            "--request", project.RequestPath,
            "--result", project.ResultPath,
            "--compiler-manifest", project.CompilerManifestPath,
            "--cache-directory", relativeCache,
            "--verify-policy", "advisory",
            "--assumption-policy", "allow"
        ];

        Assert.That(
            LauncherArguments.TryParse(arguments, out var parsed),
            Is.True);
        Assert.That(
            (Action)(() => parsed.CreateRequest(out _, out _)),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task WorkerRuntimeAssetAliasIsRejectedBeforeInvalidationDeletesIt()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var sourceWorker = WorkerOutputPath();
        var sourceDirectory = Path.GetDirectoryName(sourceWorker)!;
        var collisionWorker = project.CollisionWorkerPath;
        var collisionDirectory = Path.GetDirectoryName(collisionWorker)!;
        string collisionAsset;
        using (var sourceSnapshot = WorkerBinaryIdentity.CreateSnapshot(
                   sourceWorker))
        {
            foreach (var component in sourceSnapshot.ComponentPaths)
            {
                var relative = Path.GetRelativePath(sourceDirectory, component);
                var destination = Path.Combine(collisionDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(component, destination, overwrite: true);
            }
        }

        using (var collisionSnapshot = WorkerBinaryIdentity.CreateSnapshot(
                   collisionWorker))
        {
            var workerCompanions = new[] {
                collisionWorker,
                Path.ChangeExtension(collisionWorker, ".deps.json"),
                Path.ChangeExtension(collisionWorker, ".runtimeconfig.json")
            };
            collisionAsset = collisionSnapshot.ComponentPaths.First(
                path => !workerCompanions.Contains(
                    path, StringComparer.OrdinalIgnoreCase));
        }

        var failed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofWorkerPath", collisionWorker),
            ("SharpProofVerifyResultFile", collisionAsset));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.Not.Zero);
            Assert.That(
                failed.Output.Contains(
                    "SharpProof launcher input is invalid: ArgumentException",
                    StringComparison.Ordinal),
                Is.True);
            Assert.That(File.Exists(collisionAsset), Is.True);
        }
    }

    [Test]
    public async Task MissingWorkerDoesNotAllowInvalidationToDeleteWorkerTreeOutput()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var directory = Path.Combine(
            Path.GetDirectoryName(project.ProjectPath)!,
            "missing-worker-output");
        Directory.CreateDirectory(directory);
        var worker = Path.Combine(directory, "worker.dll");
        var result = Path.Combine(directory, "result.json");
        const string sentinel = "worker-tree-result-sentinel";
        File.Copy(
            Path.ChangeExtension(WorkerOutputPath(), ".deps.json"),
            Path.ChangeExtension(worker, ".deps.json"));
        await File.WriteAllTextAsync(result, sentinel);
        try
        {
            var failed = await project.RunVerificationTargetAsync(
                ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
                ("SharpProofWorkerPath", worker),
                ("SharpProofVerifyResultFile", result));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failed.ExitCode, Is.Not.Zero, failed.Output);
                Assert.That(File.Exists(result), Is.True, failed.Output);
                Assert.That(
                    await File.ReadAllTextAsync(result),
                    Is.EqualTo(sentinel));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task AssumptionSeverityIncludesUsedAndDeclaredEvidence()
    {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long NonNegative(long value) {
                    Contract.Assume(value >= 0);
                    Contract.Ensures(Contract.Result<long>() >= 0);
                    return value;
                }
            }
            """);

        var allowed = await project.BuildAsync(
            verify: true,
            ("SharpProofAssumptionPolicy", "allow"));
        Assert.That(allowed.ExitCode, Is.Zero, allowed.Output);
        Assert.That(allowed.Output, Does.Contain("info SP0048"));

        var warning = await project.BuildAsync(
            verify: true,
            ("SharpProofAssumptionPolicy", "warn"));
        Assert.That(warning.ExitCode, Is.Zero, warning.Output);
        Assert.That(warning.Output, Does.Contain("warning SP0048"));

        var error = await project.BuildAsync(
            verify: true,
            ("SharpProofAssumptionPolicy", "error"));
        Assert.That(error.ExitCode, Is.Not.Zero);
        Assert.That(error.Output, Does.Contain("error SP0048"));

        using var trusted = ConsumerProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                [SharpProofTrusted("reviewed boundary")]
                public static long Identity(long value) => value;
            }
            """);
        var declared = await trusted.BuildAsync(
            verify: true,
            ("SharpProofAssumptionPolicy", "error"));
        Assert.That(declared.ExitCode, Is.Not.Zero);
        Assert.That(declared.Output, Does.Contain("error SP0048"));
        Assert.That(
            declared.Output,
            Does.Contain("total=1, user=0, trusted=1"));
    }

    [Test]
    public async Task IncrementalBuildIsDeterministicAndKeepsResultStable()
    {
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
        var firstResponse = WorkerProtocolJson.DeserializeResponse(firstJson)!;
        var secondResponse =
            WorkerProtocolJson.DeserializeResponse(secondJson)!;

        Assert.That(
            SemanticPayload(secondResponse),
            Is.EqualTo(SemanticPayload(firstResponse)));
        Assert.That(secondWrite, Is.GreaterThan(firstWrite));
        Assert.That(second.Output, Does.Contain("SharpProof Proven"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                firstResponse.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Miss));
            Assert.That(
                secondResponse.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Miss));
            Assert.That(
                firstResponse.Summary.ElapsedMilliseconds,
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                secondResponse.Summary.ElapsedMilliseconds,
                Is.GreaterThanOrEqualTo(0));
        }

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
    public async Task CompilerOptionChangesInvalidateIncrementalVerification()
    {
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
        var changedArtifact = await AssertPublicationBindingAsync(
            changedRequest, changedResponse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                changedArtifact.SyntaxTrees.Select(static tree =>
                    tree.LanguageVersion),
                Has.All.EqualTo("CSharp13"));
            Assert.That(
                changedArtifact.Options.NullableContext,
                Is.EqualTo("Annotations"));
            Assert.That(
                changedArtifact.Options.OptimizationLevel,
                Is.EqualTo("Debug"));
            Assert.That(
                changedArtifact.Options.CheckOverflow,
                Is.True);
            Assert.That(changedArtifact.Options.AllowUnsafe, Is.True);
            Assert.That(
                changedArtifact.Options.Deterministic,
                Is.False);
            Assert.That(
                changedArtifact.Options.Platform,
                Is.EqualTo("X64"));
            Assert.That(
                changedResponse.InputHash,
                Is.Not.EqualTo(firstResponse.InputHash));
            Assert.That(
                File.GetLastWriteTimeUtc(project.ResultPath),
                Is.GreaterThan(firstWrite));
        }
    }

    [Test]
    public async Task VerificationFailsExplicitlyOnUnsupportedHosts()
    {
        if (OperatingSystem.IsWindows() &&
            RuntimeInformation.ProcessArchitecture ==
                Architecture.X64 &&
            RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            Assert.Ignore("The packaged worker is supported on Windows x64.");
        }

        using var project = ConsumerProject.Create(IdentitySource);

        var build = await project.BuildAsync(verify: true);

        Assert.That(build.ExitCode, Is.Not.Zero);
        var expected = OperatingSystem.IsWindows()
            ? "requires Windows x64"
            : "SharpProof out-of-process verification is supported only on Windows x64";
        Assert.That(
            build.Output,
            Does.Contain(expected));
    }

    [Test]
    public void PackagePropertiesMatchProtocolDefaults()
    {
        var repository = ConsumerProject.FindRepositoryRoot();
        using var acceptance = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository,
            "eng",
            "acceptance",
            "contract.json")));
        var maximumProjectDirectoryCharacters = acceptance.RootElement
            .GetProperty("worker")
            .GetProperty("maximumProjectDirectoryCharacters")
            .GetInt32();
        var portableProps = XDocument.Load(Path.Combine(
            repository,
            "SharpProof.Package",
            "buildTransitive",
            "SharpProof.props"));
        var verifierProps = XDocument.Load(Path.Combine(
            repository,
            "SharpProof.Verifier.Win-x64",
            "buildTransitive",
            "SharpProof.Verifier.Win-x64.props"));
        var properties = verifierProps
            .Descendants()
            .Where(static element =>
                element.Parent?.Name.LocalName == "PropertyGroup")
            .GroupBy(
                static element => element.Name.LocalName,
                StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last().Value,
                StringComparer.Ordinal);
        var compilerVisible = portableProps
            .Descendants("CompilerVisibleProperty")
            .Concat(verifierProps.Descendants("CompilerVisibleProperty"))
            .Select(static element =>
                element.Attribute("Include")?.Value)
            .Where(static value => value != null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                compilerVisible,
                Is.SupersetOf(s_publicPolicyProperties));
            Assert.That(
                uint.Parse(
                    properties["SharpProofVerifyQueryRlimit"],
                    CultureInfo.InvariantCulture),
                Is.EqualTo(WorkerBudgets.DefaultQueryRlimit));
            Assert.That(
                uint.Parse(
                    properties["SharpProofVerifyMethodRlimit"],
                    CultureInfo.InvariantCulture),
                Is.EqualTo(WorkerBudgets.DefaultMethodRlimit));
            Assert.That(
                int.Parse(properties[
                        "SharpProofVerifyMethodWallTimeMilliseconds"],
                    CultureInfo.InvariantCulture),
                Is.EqualTo(
                    WorkerBudgets.DefaultMethodWallTimeMilliseconds));
            Assert.That(
                int.Parse(properties[
                        "SharpProofVerifyProjectWallTimeMilliseconds"],
                    CultureInfo.InvariantCulture),
                Is.EqualTo(
                    WorkerBudgets.DefaultProjectWallTimeMilliseconds));
            Assert.That(
                int.Parse(
                    properties["SharpProofVerifyMaxParallelism"],
                    CultureInfo.InvariantCulture),
                Is.EqualTo(WorkerBudgets.MaximumParallelism));
            Assert.That(
                int.Parse(properties[
                        "SharpProofVerifyMaximumExpressionDepth"],
                    CultureInfo.InvariantCulture),
                Is.EqualTo(
                    WorkerBudgets.DefaultMaximumExpressionDepth));
            Assert.That(
                long.Parse(properties[
                        "SharpProofVerifyProcessMemoryLimitBytes"],
                    CultureInfo.InvariantCulture),
                Is.EqualTo(
                    WorkerBudgets.DefaultProcessMemoryLimitBytes));
            Assert.That(
                int.Parse(
                    properties["SharpProofVerifyMaxWorkerProcesses"],
                    CultureInfo.InvariantCulture),
                Is.EqualTo(WorkerBudgets.MaximumParallelism));
            Assert.That(
                int.Parse(properties[
                        "SharpProofVerifyTerminationGraceMilliseconds"],
                    CultureInfo.InvariantCulture),
                Is.EqualTo(
                    WorkerLauncherDefaults.TerminationGraceMilliseconds));
            Assert.That(
                bool.Parse(properties["SharpProofVerifyCacheEnabled"]),
                Is.True);
            Assert.That(
                long.Parse(properties[
                        "SharpProofVerifyCacheMaximumBytes"],
                    CultureInfo.InvariantCulture),
                Is.EqualTo(WorkerCacheOptions.DefaultMaximumBytes));
            Assert.That(
                int.Parse(properties[
                        "_SharpProofMaximumProjectDirectoryLength"],
                    CultureInfo.InvariantCulture),
                Is.EqualTo(maximumProjectDirectoryCharacters));
            Assert.That(
                properties.ContainsKey("SharpProofVerifySarifFile"),
                Is.False,
                "SARIF projection must remain opt-in.");
        }
    }

    [Test]
    public void CompilerManifestPropertiesAreVisibleBeforeEditorConfigGeneration()
    {
        var repository = ConsumerProject.FindRepositoryRoot();
        var packageProps = XDocument.Load(Path.Combine(
            repository,
            "SharpProof.Verifier.Win-x64",
            "buildTransitive",
            "SharpProof.Verifier.Win-x64.props"));
        var portableTargets = XDocument.Load(Path.Combine(
            repository,
            "SharpProof.Package",
            "buildTransitive",
            "SharpProof.targets"));
        var analyzerConsumerProps = XDocument.Load(Path.Combine(
            repository,
            "SharpProof.AnalyzerConsumer.props"));
        var targets = XDocument.Load(Path.Combine(
            repository,
            "SharpProof.Verifier.Win-x64",
            "buildTransitive",
            "SharpProof.Verifier.Win-x64.targets"));
        var initialize = targets
            .Descendants("Target")
            .Single(static target =>
                target.Attribute("Name")?.Value ==
                "_SharpProofInitializeVerify");
        var verifyCore = targets
            .Descendants("Target")
            .Single(static target =>
                target.Attribute("Name")?.Value ==
                "_SharpProofVerifyCore");
        var invocation = verifyCore
            .Descendants("SharpProof.BuildTasks.RunVerifier")
            .Single();
        var runnerTask = targets.Descendants("UsingTask")
            .Single(static task => task.Attribute("TaskName")?.Value ==
                "SharpProof.BuildTasks.RunVerifier");
        var arguments = string.Join(
            " ",
            verifyCore.Descendants("_SharpProofVerifierArgument")
                .Select(static argument => argument.Attribute("Include")?.Value));
        var targetXml = targets.ToString(SaveOptions.DisableFormatting);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CompilerVisibleProperties(packageProps),
                Is.SupersetOf(s_compilerManifestProperties));
            Assert.That(
                CompilerVisibleProperties(analyzerConsumerProps),
                Is.SupersetOf(s_compilerManifestProperties));
            Assert.That(
                portableTargets.Descendants("Target")
                    .Select(static target =>
                        target.Attribute("Name")?.Value),
                Has.None.EqualTo("_SharpProofVerifyCore"));
            Assert.That(
                initialize.Attribute("BeforeTargets")?.Value
                    .Split(';', StringSplitOptions.RemoveEmptyEntries),
                Does.Contain("GenerateMSBuildEditorConfigFile"));
            Assert.That(
                initialize.Attribute("BeforeTargets")?.Value
                    .Split(';', StringSplitOptions.RemoveEmptyEntries),
                Does.Contain(
                    "GenerateMSBuildEditorConfigFileShouldRun"));
            Assert.That(
                initialize.Descendants(
                    "_SharpProofCompilerManifestPath"),
                Is.Not.Empty);
            Assert.That(
                initialize.Descendants(
                    "_SharpProofCompilationTargetFramework"),
                Is.Not.Empty);
            Assert.That(
                initialize.Descendants(
                    "SharpProofCompilerManifestFile"),
                Is.Not.Empty);
            Assert.That(
                arguments,
                Does.Contain("--compiler-manifest")
                    .And.Contain("$(_SharpProofCompilerManifestPath)")
                    .And.Contain("--publish-compiler-manifest")
                    .And.Contain("$(SharpProofCompilerManifestFile)"));
            Assert.That(
                s_reconstructionArguments.Where(arguments.Contains),
                Is.Empty);
            Assert.That(
                invocation.Attribute("Executable")?.Value,
                Is.EqualTo("$(SharpProofDotNetHost)"));
            Assert.That(verifyCore.Descendants("Exec"), Is.Empty);
            Assert.That(
                runnerTask.Attribute("AssemblyFile")?.Value,
                Is.EqualTo("$(_SharpProofBuildTasksPath)"));
            Assert.That(
                s_reconstructionListArtifacts.Where(targetXml.Contains),
                Is.Empty);
            Assert.That(
                targetXml,
                Does.Not.Contain("SharpProofNativeZ3Path"));
            Assert.That(
                verifyCore.Descendants("WriteLinesToFile"),
                Is.Empty);
            Assert.That(
                verifyCore.Descendants("Copy"),
                Is.Empty);
        }
    }

    [Test]
    public void LauncherDistinguishesValidFailedAndMalformedResponses()
    {
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse
        {
            InputHash = new('0', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Failed,
            FailureReason = WorkerRunFailureReason.InfrastructureFailure,
            Summary = new WorkerVerificationSummary
            {
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "test",
                    ApiSpecVersion = "test"
                }
            },
            Errors = [
                new WorkerProtocolError {
                    Code = "infrastructure.test",
                    Message = "Deliberate failure."
                }
            ]
        };
        var path = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Package.Test",
            Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            File.WriteAllText(
                path,
                WorkerProtocolJson.SerializeResponse(response));

            var exitCode = Program.ValidateAndReport(
                path,
                new WorkerVerifyRequest(),
                null,
                null,
                out var validResponse,
                out var validatedResponse);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(3));
                Assert.That(validResponse, Is.True);
                Assert.That(validatedResponse, Is.Not.Null);
                Assert.That(
                    error.ToString(),
                    Does.Contain("infrastructure.test"));
                Assert.That(
                    error.ToString(),
                    Does.Contain("worker run Failed"));
            }

            error.GetStringBuilder().Clear();
            File.WriteAllText(path, "{}");
            exitCode = Program.ValidateAndReport(
                path,
                new WorkerVerifyRequest(),
                null,
                null,
                out validResponse,
                out validatedResponse);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(3));
                Assert.That(validResponse, Is.False);
                Assert.That(validatedResponse, Is.Null);
                Assert.That(
                    error.ToString(),
                    Does.Contain("unavailable or malformed"));
            }
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task SpacesAndEscapedIdentifiersSurviveTheTargetBoundary()
    {
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
        var artifact = await CompilerManifestArtifact.ReadAsync(
            request.CompilerManifest.Path);
        Assert.That(
            artifact.SyntaxTrees.Select(static tree => tree.Path),
            Has.Some.Contains("consumer project"));
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        await AssertPublicationBindingAsync(request, response);
        Assert.That(
            response.ClaimResults.Single().Outcome,
            Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [Test]
    public async Task RefutationAndHardBoundaryFailuresFailClosed()
    {
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
        Assert.That(refutedBuild.Output, Does.Contain("failed with exit code 5"));
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(refuted.ResultPath))!;
        var refutedRequest = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(refuted.RequestPath))!;
        await AssertPublicationBindingAsync(refutedRequest, response);
        Assert.That(
            response.ClaimResults.Single().Outcome,
            Is.EqualTo(WorkerClaimOutcome.Refuted));

        var repeatedRefutation = await refuted.BuildAsync(verify: true);
        Assert.That(
            repeatedRefutation.ExitCode,
            Is.Not.Zero,
            repeatedRefutation.Output);
        Assert.That(
            repeatedRefutation.Output,
            Does.Contain("failed with exit code 5"));

        using var timedOut = ConsumerProject.Create(IdentitySource);
        var timedOutBuild = await timedOut.BuildAsync(
            verify: true,
            ("SharpProofVerifyMethodWallTimeMilliseconds", "1"),
            ("SharpProofVerifyProjectWallTimeMilliseconds", "1"),
            ("SharpProofVerifyTerminationGraceMilliseconds", "1"));
        Assert.That(timedOutBuild.ExitCode, Is.Not.Zero);
        Assert.That(
            timedOutBuild.Output,
            Does.Contain("worker run TimedOut"));
        var timedOutResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(timedOut.ResultPath))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                timedOutResponse.RunStatus,
                Is.EqualTo(WorkerRunStatus.TimedOut));
            Assert.That(
                WorkerProtocolJson.Validate(timedOutResponse).IsValid,
                Is.True);
        }
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

    private static string SemanticPayload(WorkerVerifyResponse response)
    {
        return System.Text.Json.JsonSerializer.Serialize(
            new
            {
                response.Manifest,
                response.RunStatus,
                response.FailureReason,
                response.CallableResults,
                response.ClaimResults,
                response.Errors
            },
            WorkerProtocolJson.Options);
    }

    private static async Task<CompilerManifestArtifact>
        AssertPublicationBindingAsync(
        WorkerVerifyRequest request, WorkerVerifyResponse response,
        string? workerPath = null)
    {
        var artifact = await CompilerManifestArtifact.ReadAsync(
            request.CompilerManifest.Path);
        var digest = string.Concat(SHA256.HashData(artifact.Bytes).Select(
            static value => value.ToString(
                "x2", CultureInfo.InvariantCulture)));
        workerPath ??= WorkerOutputPath();
        var workerBinarySha256 = WorkerBinaryIdentity.ComputeSha256(workerPath);
        var expectedInputHash = Program.ComputeExpectedInputHash(
            workerPath, request, artifact.Bytes);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(digest, Is.EqualTo(request.CompilerManifest.Sha256));
            Assert.That(response.RequestHash,
                Is.EqualTo(WorkerProtocolJson.ComputeRequestHash(request)));
            Assert.That(response.InputHash, Is.EqualTo(expectedInputHash));
            Assert.That(
                response.Summary.Versions.WorkerBinarySha256,
                Is.EqualTo(response.RunStatus == WorkerRunStatus.Complete
                    ? workerBinarySha256
                    : WorkerProtocolVersions.EmptySha256));
            Assert.That(
                artifact.ManifestHash,
                Is.EqualTo(response.Manifest.Hash));
            Assert.That(WorkerProtocolJson.ValidateForRequest(
                response, response.RequestHash, expectedInputHash,
                response.Manifest, request.Budgets).IsValid,
                Is.True);
        }
        return artifact;
    }

    private static string WorkerOutputPath()
    {
        var configuration = new DirectoryInfo(Path.GetDirectoryName(
            typeof(WorkerMsBuildIntegrationTests).Assembly.Location)!)
            .Parent?.Name ?? throw new InvalidOperationException(
                "The test build configuration was not found.");
        return Path.Combine(ConsumerProject.FindRepositoryRoot(), "SharpProof.Worker",
            "bin", configuration, "net9.0", "SharpProof.Worker.dll");
    }

    private static string LauncherProtocolOutputPath()
    {
        var configuration = new DirectoryInfo(Path.GetDirectoryName(
            typeof(WorkerMsBuildIntegrationTests).Assembly.Location)!)
            .Parent?.Name ?? throw new InvalidOperationException(
                "The test build configuration was not found.");
        return Path.Combine(ConsumerProject.FindRepositoryRoot(),
            "SharpProof.Worker.Launcher", "bin", configuration, "net9.0",
            "SharpProof.Worker.Protocol.dll");
    }

    private static void RequireWindowsWorker()
    {
        if (!OperatingSystem.IsWindows() ||
            RuntimeInformation.ProcessArchitecture !=
                Architecture.X64 ||
            RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            Assert.Ignore(
                "The packaged worker is supported only on Windows x64.");
        }
    }

    private static IEnumerable<string?> CompilerVisibleProperties(
        XDocument document)
    {
        return document.Descendants("CompilerVisibleProperty")
            .Select(static property =>
                property.Attribute("Include")?.Value);
    }

    private sealed record CompilerManifestArtifact(
        byte[] Bytes,
        string ProjectDirectory,
        string AssemblyName,
        string TargetFramework,
        string CompilerVersion,
        WorkerFeatureSet Features,
        string CompilationSha256,
        string ManifestHash,
        CompilerCompilationOptions Options,
        CompilerSyntaxTree[] SyntaxTrees,
        CompilerReference[] References)
    {
        internal static async Task<CompilerManifestArtifact> ReadAsync(
            string path)
        {
            Assert.That(File.Exists(path), Is.True, path);
            var bytes = await File.ReadAllBytesAsync(path);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var compilation = root.GetProperty("compilation");
            var options = compilation.GetProperty("options");
            var artifact = new CompilerManifestArtifact(
                bytes,
                compilation.GetProperty("projectDirectory").GetString() ??
                    string.Empty,
                compilation.GetProperty("assemblyName").GetString() ??
                    string.Empty,
                compilation.GetProperty("targetFramework").GetString() ??
                    string.Empty,
                compilation.GetProperty("compilerVersion").GetString() ??
                    string.Empty,
                root.GetProperty("features")
                    .Deserialize<WorkerFeatureSet>(
                        WorkerProtocolJson.Options),
                root.GetProperty("compilationSha256").GetString() ??
                    string.Empty,
                root.GetProperty("manifest").GetProperty("hash")
                    .GetString() ?? string.Empty,
                new CompilerCompilationOptions(
                    options.GetProperty("outputKind").GetString() ??
                        string.Empty,
                    options.GetProperty("optimizationLevel").GetString() ??
                        string.Empty,
                    options.GetProperty("checkOverflow").GetBoolean(),
                    options.GetProperty("allowUnsafe").GetBoolean(),
                    options.GetProperty("deterministic").GetBoolean(),
                    options.GetProperty("platform").GetString() ??
                        string.Empty,
                    options.GetProperty("nullableContext").GetString() ??
                        string.Empty,
                    options.GetProperty("metadataImportOptions").GetString() ??
                        string.Empty,
                    options.GetProperty("warningLevel").GetInt32(),
                    options.GetProperty("generalDiagnosticOption").GetString() ??
                        string.Empty,
                    [.. options.GetProperty("specificDiagnosticOptions")
                        .EnumerateArray()
                        .Select(static option => new CompilerDiagnosticOption(
                            option.GetProperty("id").GetString() ?? string.Empty,
                            option.GetProperty("reportDiagnostic").GetString() ??
                                string.Empty))],
                    options.GetProperty("assemblyIdentityComparer").GetString() ??
                        string.Empty,
                    [.. options.GetProperty("usings").EnumerateArray()
                        .Select(static item => item.GetString() ?? string.Empty)],
                    options.GetProperty("resolverPolicy").GetString() ??
                        string.Empty),
                [.. compilation.GetProperty("syntaxTrees").EnumerateArray()
                    .Select(static tree => new CompilerSyntaxTree(
                        tree.GetProperty("path").GetString() ??
                            string.Empty,
                        tree.GetProperty("languageVersion").GetString() ??
                            string.Empty,
                        [.. tree.GetProperty("preprocessorSymbols")
                            .EnumerateArray()
                            .Select(static symbol => symbol.GetString() ??
                                string.Empty)]))],
                [.. compilation.GetProperty("references").EnumerateArray()
                    .SelectMany(static reference =>
                        reference.GetProperty("modules").EnumerateArray())
                    .Select(static module => new CompilerReference(
                        module.GetProperty("name").GetString() ?? string.Empty,
                        module.GetProperty("mvid").GetString() ?? string.Empty,
                        module.GetProperty("path").GetString() ?? string.Empty,
                        module.GetProperty("sha256").GetString() ?? string.Empty))]);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    root.GetProperty("schema").GetString(),
                    Is.EqualTo("SharpProof.CompilerManifest"));
                Assert.That(
                    root.GetProperty("schemaVersion").GetInt32(),
                    Is.EqualTo(10));
                Assert.That(
                    root.GetProperty("protocolVersion").GetString(),
                    Is.EqualTo(WorkerProtocolVersions.Current));
                Assert.That(
                    artifact.CompilationSha256,
                    Does.Match("^[0-9a-f]{64}$"));
                Assert.That(
                    Path.IsPathFullyQualified(artifact.ProjectDirectory),
                    Is.True);
                Assert.That(artifact.AssemblyName, Is.Not.Empty);
                Assert.That(artifact.TargetFramework, Is.Not.Empty);
                Assert.That(artifact.CompilerVersion, Is.Not.Empty);
                Assert.That(
                    artifact.References.All(static reference =>
                        !string.IsNullOrWhiteSpace(reference.Name) &&
                        Guid.TryParseExact(reference.Mvid, "D", out _) &&
                        reference.Sha256.Length == 64),
                    Is.True);
            }
            return artifact;
        }
    }

    private sealed record CompilerCompilationOptions(
        string OutputKind,
        string OptimizationLevel,
        bool CheckOverflow,
        bool AllowUnsafe,
        bool Deterministic,
        string Platform,
        string NullableContext,
        string MetadataImportOptions,
        int WarningLevel,
        string GeneralDiagnosticOption,
        CompilerDiagnosticOption[] SpecificDiagnosticOptions,
        string AssemblyIdentityComparer,
        string[] Usings,
        string ResolverPolicy);

    private sealed record CompilerDiagnosticOption(
        string Id,
        string ReportDiagnostic);

    private sealed record CompilerSyntaxTree(
        string Path,
        string LanguageVersion,
        string[] PreprocessorSymbols);

    private sealed record CompilerReference(
        string Name,
        string Mvid,
        string Path,
        string Sha256);

    private sealed class ConsumerProject : IDisposable
    {
        private readonly string _root;

        private ConsumerProject(string root)
        {
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
            CompilerManifestPath = Path.Combine(
                root,
                "obj",
                "Release",
                "net8.0",
                "SharpProof",
                "compiler-manifest.json");
            CollisionWorkerPath = Path.Combine(
                root,
                "obj",
                "collision-worker",
                "SharpProof.Worker.dll");
        }

        internal string ProjectPath
        {
            get;
        }
        internal string Root => _root;

        internal string RequestPath
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
        internal string CollisionWorkerPath
        {
            get;
        }
        internal string VerifyOutputPath(string framework, string fileName)
        {
            return Path.Combine(_root, "obj", "Release", framework, "SharpProof",
                fileName);
        }

        internal async Task<string> CreateResultlessWorkerAsync()
        {
            var root = Path.Combine(
                _root,
                "obj",
                "test-workers",
                "resultless-worker");
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "ResultlessWorker.csproj");
            await File.WriteAllTextAsync(
                project,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                new System.Text.UTF8Encoding(false));
            await File.WriteAllTextAsync(
                Path.Combine(root, "Program.cs"),
                """
                using System;
                using System.IO;
                using System.Runtime.CompilerServices;
                using System.Threading;
                var eventName = args[Array.IndexOf(args, "--start-event") + 1];
                using var start = EventWaitHandle.OpenExisting(eventName);
                start.WaitOne();

                internal static class PreMainProbe
                {
                    [ModuleInitializer]
                    internal static void Initialize()
                    {
                        var arguments = Environment.GetCommandLineArgs();
                        var markerIndex = Array.IndexOf(
                            arguments, "--pre-main-marker");
                        if (markerIndex >= 0)
                        {
                            File.WriteAllText(arguments[markerIndex + 1], "started");
                        }
                    }
                }
                """,
                new System.Text.UTF8Encoding(false));
            var build = await RunDotNetAsync([
                "build", project, "-c", "Release", "--nologo",
                "/nodeReuse:false", "-p:UseSharedCompilation=false"
            ]);
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException(build.Output);
            }

            return Path.Combine(
                root,
                "bin",
                "Release",
                "net8.0",
                "ResultlessWorker.dll");
        }

        internal async Task<string> CreateMalformedWorkerAsync()
        {
            var root = Path.Combine(_root, "malformed-worker");
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "MalformedWorker.csproj");
            await File.WriteAllTextAsync(
                project,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework Condition="'$(TargetFrameworks)' == ''">net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                new System.Text.UTF8Encoding(false));
            await File.WriteAllTextAsync(
                Path.Combine(root, "Program.cs"),
                """
                using System;
                using System.IO;
                using System.Threading;
                var result = args[Array.IndexOf(args, "--result") + 1];
                var eventName = args[Array.IndexOf(args, "--start-event") + 1];
                using var start = EventWaitHandle.OpenExisting(eventName);
                start.WaitOne();
                File.WriteAllText(result, "not-json");
                """,
                new System.Text.UTF8Encoding(false));
            var build = await RunDotNetAsync([
                "build", project, "-c", "Release", "--nologo",
                "/nodeReuse:false", "-p:UseSharedCompilation=false"
            ]);
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException(build.Output);
            }

            return Path.Combine(
                root,
                "bin",
                "Release",
                "net8.0",
                "MalformedWorker.dll");
        }

        internal async Task<string> CreateMalformedThenHangWorkerAsync()
        {
            var root = Path.Combine(
                _root,
                "obj",
                "test-workers",
                "malformed-hanging-worker");
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "MalformedHangingWorker.csproj");
            await File.WriteAllTextAsync(
                project,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
                new System.Text.UTF8Encoding(false));
            await File.WriteAllTextAsync(
                Path.Combine(root, "Program.cs"),
                """
                using System;
                using System.IO;
                using System.Threading;
                var result = args[Array.IndexOf(args, "--result") + 1];
                var eventName = args[Array.IndexOf(args, "--start-event") + 1];
                using var start = EventWaitHandle.OpenExisting(eventName);
                start.WaitOne();
                File.WriteAllText(result, "not-json");
                Thread.Sleep(Timeout.Infinite);
                """,
                new System.Text.UTF8Encoding(false));
            var build = await RunDotNetAsync([
                "build", project, "-c", "Release", "--nologo",
                "/nodeReuse:false", "-p:UseSharedCompilation=false"
            ]);
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException(build.Output);
            }

            return Path.Combine(
                root,
                "bin",
                "Release",
                "net8.0",
                "MalformedHangingWorker.dll");
        }

        internal static ConsumerProject Create(
            string source,
            bool useSpaces = false)
        {
            return CreateCore(source, useSpaces, []);
        }

        internal static ConsumerProject CreateConfigured(
            string source,
            params (string Name, string Value)[] properties)
        {
            return CreateCore(source, useSpaces: false, properties);
        }

        internal static ConsumerProject CreateWithPercentPath(
            string source,
            params (string Name, string Value)[] properties)
        {
            return CreateCore(
                source,
                useSpaces: false,
                properties,
                "consumer-%TEMP%-" + Guid.NewGuid().ToString("N"));
        }

        internal static ConsumerProject CreateWithLongPath(string source)
        {
            var segment = new string('l', 48);
            return CreateCore(
                source,
                useSpaces: false,
                [("TargetFrameworks", "netstandard2.0")],
                Path.Combine(
                    "consumer-long-" + Guid.NewGuid().ToString("N"),
                    segment + "1",
                    segment + "2",
                    segment + "3",
                    segment + "4",
                    segment + "5"));
        }

        internal static string? FindVisualStudioMsBuild()
        {
            var programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86);
            var vswhere = Path.Combine(
                programFiles,
                "Microsoft Visual Studio",
                "Installer",
                "vswhere.exe");
            if (!File.Exists(vswhere))
            {
                return null;
            }
            var startInfo = new ProcessStartInfo
            {
                FileName = vswhere,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
            {
                "-latest", "-products", "*",
                "-requires", "Microsoft.Component.MSBuild",
                "-find", @"MSBuild\**\Bin\MSBuild.exe"
            })
            {
                startInfo.ArgumentList.Add(argument);
            }
            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return null;
            }
            var defaultPath = output.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(File.Exists);
            if (defaultPath == null)
            {
                return null;
            }
            var amd64Path = Path.Combine(
                Path.GetDirectoryName(defaultPath)!,
                "amd64",
                "MSBuild.exe");
            return File.Exists(amd64Path) ? amd64Path : defaultPath;
        }

        private static ConsumerProject CreateCore(
            string source,
            bool useSpaces,
            (string Name, string Value)[] properties,
            string? explicitName = null)
        {
            var name = explicitName ?? (useSpaces
                ? "consumer project " + Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N"));
            var root = Path.Combine(
                Path.GetTempPath(),
                "SharpProof.Package.Test",
                name);
            Directory.CreateDirectory(root);
            File.Copy(
                Path.Combine(FindRepositoryRoot(), "global.json"),
                Path.Combine(root, "global.json"));
            File.WriteAllText(
                Path.Combine(root, "Subject.cs"),
                source,
                new System.Text.UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(root, "Consumer.csproj"),
                CreateProjectXml(properties),
                new System.Text.UTF8Encoding(false));
            return new ConsumerProject(root);
        }

        internal async Task<BuildResult> BuildAsync(
            bool? verify,
            params (string Name, string Value)[] properties)
        {
            var arguments = new List<string> {
                "build",
                ProjectPath,
                "-c",
                "Release",
                "--nologo",
                "/nodeReuse:false",
                "-p:UseSharedCompilation=false",
                "-p:GeneratePackageOnBuild=false"
            };
            if (verify.HasValue)
            {
                arguments.Add(
                    "-p:SharpProofVerify=" +
                    (verify.Value ? "true" : "false"));
            }

            arguments.AddRange(properties.Select(static property =>
                "-p:" + property.Name + "=" + property.Value));
            return await RunDotNetAsync(arguments);
        }

        internal Task<BuildResult> RestoreAsync(
            params (string Name, string Value)[] properties)
        {
            var arguments = new List<string>
            {
                "restore",
                ProjectPath,
                "--nologo",
                "/nodeReuse:false",
                "-p:SharpProofVerify=false"
            };
            arguments.AddRange(properties.Select(static property =>
                "-p:" + property.Name + "=" + property.Value));
            return RunDotNetAsync(arguments);
        }

        internal Task<BuildResult> BuildWithVisualStudioMsBuildAsync(
            string msBuildPath,
            params (string Name, string Value)[] properties)
        {
            var arguments = new List<string>
            {
                ProjectPath,
                "/t:Build",
                "/p:Configuration=Release",
                "/p:SharpProofVerify=true",
                "/p:MSBuildEnableWorkloadResolver=false",
                "/p:UseSharedCompilation=false",
                "/p:GeneratePackageOnBuild=false",
                "/nologo",
                "/nodeReuse:false"
            };
            arguments.AddRange(properties.Select(static property =>
                "/p:" + property.Name + "=" + property.Value));
            return RunProcessAsync(msBuildPath, arguments);
        }

        internal Task<BuildResult> BuildIsolatedAsync(
            string name, string features)
        {
            return BuildAsync(
                verify: true,
                ("BaseIntermediateOutputPath", Path.Combine(_root, "obj-" + name) +
                    Path.DirectorySeparatorChar),
                ("BaseOutputPath", Path.Combine(_root, "bin-" + name) +
                    Path.DirectorySeparatorChar),
                ("SharpProofFeatures", features),
                ("SharpProofVerifyRequestFile", RequestPath),
                ("SharpProofVerifyResultFile", ResultPath));
        }

        internal Task<BuildResult> RunVerificationTargetAsync(
            params (string Name, string Value)[] properties)
        {
            var invocationDirectory = Path.Combine(
                _root,
                "obj",
                "Release",
                "net8.0",
                "SharpProof",
                "runs",
                "direct-" + Guid.NewGuid().ToString("N"));
            var arguments = new List<string> {
                "msbuild",
                ProjectPath,
                "/t:_SharpProofVerifyCore",
                "/nologo",
                "/nodeReuse:false",
                "-p:Configuration=Release",
                "-p:SharpProofVerify=true",
                "-p:SharpProofVerifyRequestFile=" + RequestPath,
                "-p:SharpProofVerifyResultFile=" + ResultPath,
                "-p:SharpProofVerifyCacheDirectory=" +
                    Path.Combine(
                        _root,
                        "obj",
                        "Release",
                        "net8.0",
                        "SharpProof",
                        "cache"),
                "-p:_SharpProofInvocationDirectory=" +
                    invocationDirectory,
                "-p:_SharpProofInvocationRequestFile=" +
                    Path.Combine(invocationDirectory, "request.json"),
                "-p:_SharpProofInvocationResultFile=" +
                    Path.Combine(invocationDirectory, "result.json")
            };
            arguments.AddRange(properties.Select(static property =>
                "-p:" + property.Name + "=" + property.Value));
            return RunDotNetAsync(arguments);
        }

        internal Task<BuildResult> RunNonBuildingInitializationAsync(
            string sarifPath)
        {
            return RunDotNetAsync([
                "msbuild",
                ProjectPath,
                "/t:GenerateMSBuildEditorConfigFile",
                "/nologo",
                "/nodeReuse:false",
                "-p:Configuration=Release",
                "-p:TargetFramework=net8.0",
                "-p:SharpProofVerify=true",
                "-p:BuildingProject=false",
                "-p:SharpProofVerifyRequestFile=" + RequestPath,
                "-p:SharpProofVerifyResultFile=" + ResultPath,
                "-p:SharpProofCompilerManifestFile=" + CompilerManifestPath,
                "-p:SharpProofVerifySarifFile=" + sarifPath
            ]);
        }

        private async Task<BuildResult> RunDotNetAsync(
            IEnumerable<string> arguments)
        {
            return await RunProcessAsync("dotnet", arguments);
        }

        private async Task<BuildResult> RunProcessAsync(
            string executable,
            IEnumerable<string> arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = _root.Length < 240
                    ? _root
                    : Path.GetTempPath(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            if (string.Equals(
                    Path.GetFileName(executable),
                    "MSBuild.exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                foreach (var key in startInfo.Environment.Keys
                             .Where(static key =>
                                 key.StartsWith(
                                     "DOTNET_",
                                     StringComparison.OrdinalIgnoreCase) ||
                                 key.StartsWith(
                                     "MSBUILD",
                                     StringComparison.OrdinalIgnoreCase))
                             .ToArray())
                {
                    startInfo.Environment.Remove(key);
                }
                var dotnetHost = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ??
                    throw new InvalidOperationException(
                        "The test host did not disclose the dotnet host path.");
                using var globalJson = JsonDocument.Parse(
                    await File.ReadAllTextAsync(Path.Combine(
                        FindRepositoryRoot(),
                        "global.json")));
                var sdkVersion = globalJson.RootElement
                    .GetProperty("sdk")
                    .GetProperty("version")
                    .GetString() ?? throw new InvalidDataException(
                        "global.json does not declare an SDK version.");
                startInfo.Environment["MSBuildSDKsPath"] = Path.Combine(
                    Path.GetDirectoryName(dotnetHost) ??
                        throw new InvalidOperationException(
                            "The dotnet host path has no directory."),
                    "sdk",
                    sdkVersion,
                    "Sdks");
            }
            using var process = Process.Start(startInfo)!;
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new BuildResult(
                process.ExitCode,
                (await standardOutput) + Environment.NewLine +
                (await standardError));
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(_root);
            var expectedRoot = Path.GetFullPath(
                Path.Combine(
                    Path.GetTempPath(),
                    "SharpProof.Package.Test"));
            if (!resolved.StartsWith(
                    expectedRoot + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected test directory.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }

        private static string CreateProjectXml(
            IEnumerable<(string Name, string Value)> properties)
        {
            var repository = FindRepositoryRoot();
            var attributes = SecurityElement.Escape(
                ProductBuildOutputs.AttributesAssemblyPath());
            var props = SecurityElement.Escape(
                Path.Combine(
                    repository,
                    "SharpProof.Package",
                    "buildTransitive",
                    "SharpProof.props"));
            var verifierProps = SecurityElement.Escape(
                Path.Combine(
                    repository,
                    "SharpProof.Verifier.Win-x64",
                    "buildTransitive",
                    "SharpProof.Verifier.Win-x64.props"));
            var testConfiguration = new DirectoryInfo(
                Path.GetDirectoryName(
                    typeof(WorkerMsBuildIntegrationTests).Assembly.Location)!)
                .Parent?.Name ??
                throw new InvalidOperationException(
                    "The test build configuration was not found.");
            var analyzerDirectory = SecurityElement.Escape(Path.Combine(
                repository,
                "SharpProof.PortableAnalyzer",
                "bin",
                testConfiguration,
                "netstandard2.0"));
            var collectorDirectory = SecurityElement.Escape(Path.Combine(
                repository,
                "SharpProof.CompilerCollector",
                "bin",
                testConfiguration,
                "netstandard2.0"));
            var targets = SecurityElement.Escape(
                Path.Combine(
                    repository,
                    "SharpProof.Package",
                    "buildTransitive",
                    "SharpProof.targets"));
            var verifierTargets = SecurityElement.Escape(
                Path.Combine(
                    repository,
                    "SharpProof.Verifier.Win-x64",
                    "buildTransitive",
                    "SharpProof.Verifier.Win-x64.targets"));
            var worker = SecurityElement.Escape(
                Path.Combine(repository, "SharpProof.Worker", "bin",
                    testConfiguration, "net9.0", "SharpProof.Worker.dll"));
            var launcher = SecurityElement.Escape(
                Path.Combine(repository, "SharpProof.Worker.Launcher", "bin",
                    testConfiguration, "net9.0",
                    "SharpProof.Worker.Launcher.dll"));
            var protocol = SecurityElement.Escape(
                Path.Combine(repository, "SharpProof.Worker.Protocol", "bin",
                    testConfiguration, "netstandard2.0",
                    "SharpProof.Worker.Protocol.dll"));
            var buildTasks = SecurityElement.Escape(
                Path.Combine(repository, "SharpProof.BuildTasks", "bin",
                    testConfiguration, "netstandard2.0",
                    "SharpProof.BuildTasks.dll"));
            var configuredProperties = string.Join(
                Environment.NewLine,
                properties.Select(static property =>
                    "    <" + property.Name + ">" +
                    SecurityElement.Escape(property.Value) +
                    "</" + property.Name + ">"));
            return
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="{props}" />
                  <Import Project="{verifierProps}" />
                  <PropertyGroup>
                    <SharpProofAnalyzerDirectory>{analyzerDirectory}</SharpProofAnalyzerDirectory>
                    <_SharpProofAnalyzerDirectory>$([System.IO.Path]::GetFullPath('$(SharpProofAnalyzerDirectory)'))</_SharpProofAnalyzerDirectory>
                    <SharpProofPortableAnalyzerPath>{analyzerDirectory}/SharpProof.PortableAnalyzer.dll</SharpProofPortableAnalyzerPath>
                    <SharpProofCollectorDirectory>{collectorDirectory}</SharpProofCollectorDirectory>
                    <_SharpProofCollectorDirectory>$([System.IO.Path]::GetFullPath('$(SharpProofCollectorDirectory)'))</_SharpProofCollectorDirectory>
                    <SharpProofCompilerCollectorPath>{collectorDirectory}/SharpProof.CompilerCollector.dll</SharpProofCompilerCollectorPath>
                {configuredProperties}
                    <TargetFramework Condition="'$(TargetFrameworks)' == ''">net8.0</TargetFramework>
                    <LangVersion>12.0</LangVersion>
                    <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
                    <SharpProofWorkerPath>{worker}</SharpProofWorkerPath>
                    <_SharpProofWorkerPath>$([System.IO.Path]::GetFullPath('$(SharpProofWorkerPath)'))</_SharpProofWorkerPath>
                    <SharpProofLauncherPath>{launcher}</SharpProofLauncherPath>
                    <_SharpProofLauncherPath>$([System.IO.Path]::GetFullPath('$(SharpProofLauncherPath)'))</_SharpProofLauncherPath>
                    <_SharpProofWorkerProtocolPath>{protocol}</_SharpProofWorkerProtocolPath>
                    <_SharpProofBuildTasksPath>{buildTasks}</_SharpProofBuildTasksPath>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="SharpProof.Attributes">
                      <HintPath>{attributes}</HintPath>
                      <Private>true</Private>
                    </Reference>
                  </ItemGroup>
                  <Import Project="{targets}" />
                  <Import Project="{verifierTargets}" />
                  <Target Name="_SharpProofTestInvalidatePublishedResult"
                          BeforeTargets="_SharpProofVerifyCore"
                          Condition="'$(BuildingProject)' == 'false' and
                                     '$(_SharpProofSkipTestInvalidation)' != 'true'">
                    <SharpProof.BuildTasks.InvalidatePublishedResult
                        ResultPath="$(SharpProofVerifyResultFile)"
                        ProjectDirectory="$(MSBuildProjectDirectory)"
                        RequestPath="$(SharpProofVerifyRequestFile)"
                        ManifestPath="$(SharpProofCompilerManifestFile)"
                        SarifPath="$(SharpProofVerifySarifFile)"
                        InvocationRequestPath="$(_SharpProofInvocationRequestFile)"
                        InvocationResultPath="$(_SharpProofInvocationResultFile)"
                        InvocationManifestPath="$(_SharpProofCompilerManifestPath)"
                        WorkerPath="$(_SharpProofWorkerPath)"
                        LauncherPath="$(_SharpProofLauncherPath)"
                        WorkerProtocolPath="$(_SharpProofWorkerProtocolPath)"
                        CachePath="$(_SharpProofEffectiveCacheDirectory)" />
                  </Target>
                  <Target Name="_RemoveSharpProofAnalyzersForWorkerTargetTest"
                          BeforeTargets="CoreCompile">
                    <ItemGroup>
                      <Analyzer Remove="$(SharpProofPortableAnalyzerPath)" />
                    </ItemGroup>
                  </Target>
                </Project>
                """;
        }

        internal static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(
                typeof(LauncherMarker).Assembly.Location);
            while (directory != null)
            {
                if (File.Exists(
                        Path.Combine(
                            directory.FullName,
                            "SharpProof.Release.props")))
                {
                    return directory.FullName;
                }

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
