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
    private static readonly string[] s_publicPolicyProperties = [
        "SharpProofProfile",
        "SharpProofFeatures",
        "SharpProofVerifyPolicy",
        "SharpProofAssumptionPolicy",
        "SharpProofMode"
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
    public async Task ProjectBodyConfigurationUsesNewPropertiesAndLegacyAliases()
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

        Assert.That(legacyBuild.ExitCode, Is.Zero, legacyBuild.Output);
        Assert.That(
            legacyBuild.Output,
            Does.Contain("SharpProofMode='contracts' is deprecated"));
        var legacyRequest = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(legacy.RequestPath))!;
        var legacyArtifact = await CompilerManifestArtifact.ReadAsync(
            legacyRequest.CompilerManifest.Path);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                legacyArtifact.Features,
                Is.EqualTo(WorkerFeatureSet.Contracts));
            Assert.That(
                legacyRequest.VerifyPolicy,
                Is.EqualTo(WorkerVerifyPolicy.Advisory));
        }
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
            ("SharpProofVerifyResultFile", collisionCompanion));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.Not.Zero);
            Assert.That(
                failed.Output.Contains(
                    "SharpProof launcher input is invalid: ArgumentException",
                    StringComparison.Ordinal),
                Is.True);
            Assert.That(File.Exists(collisionWorker), Is.True);
            Assert.That(File.Exists(collisionCompanion), Is.True);
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
        var invocation = verifyCore.Descendants("Exec").Single();
        var command = invocation.Attribute("Command")?.Value ??
            string.Empty;
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
                command,
                Does.Contain("--compiler-manifest")
                    .And.Contain("$(_SharpProofCompilerManifestPath)")
                    .And.Contain("--publish-compiler-manifest")
                    .And.Contain("$(SharpProofCompilerManifestFile)"));
            Assert.That(
                s_reconstructionArguments.Where(command.Contains),
                Is.Empty);
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
                    .Select(static reference => new CompilerReference(
                        reference.GetProperty("path").GetString() ??
                            string.Empty))]);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    root.GetProperty("schema").GetString(),
                    Is.EqualTo("SharpProof.CompilerManifest"));
                Assert.That(
                    root.GetProperty("schemaVersion").GetInt32(),
                    Is.EqualTo(9));
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
        string NullableContext);

    private sealed record CompilerSyntaxTree(
        string Path,
        string LanguageVersion,
        string[] PreprocessorSymbols);

    private sealed record CompilerReference(string Path);

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

        private static ConsumerProject CreateCore(
            string source,
            bool useSpaces,
            (string Name, string Value)[] properties)
        {
            var name = useSpaces
                ? "consumer project " + Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N");
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

        private async Task<BuildResult> RunDotNetAsync(
            IEnumerable<string> arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = _root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
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
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="SharpProof.Attributes">
                      <HintPath>{attributes}</HintPath>
                      <Private>true</Private>
                    </Reference>
                  </ItemGroup>
                  <Import Project="{targets}" />
                  <Import Project="{verifierTargets}" />
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
