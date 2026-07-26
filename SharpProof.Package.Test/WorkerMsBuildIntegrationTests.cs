using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
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
    private static readonly string[] s_publicPolicyProperties = [
        "SharpProofProfile",
        "SharpProofFeatures",
        "SharpProofVerifyPolicy",
        "SharpProofAssumptionPolicy",
        "SharpProofMode"
    ];

    [Test]
    public void WorkerContainmentIsMandatoryOnTheSupportedHost() {
        if (!OperatingSystem.IsWindows() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            RuntimeInformation.OSArchitecture != Architecture.X64) {
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
            Assert.That(
                request.VerifyPolicy,
                Is.EqualTo(WorkerVerifyPolicy.Advisory));
            Assert.That(request.Features, Is.EqualTo(WorkerFeatureSet.All));
            Assert.That(
                request.AssumptionPolicy,
                Is.EqualTo(WorkerAssumptionPolicy.Allow));
        }

        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        Assert.That(response.Errors, Is.Empty);
        Assert.That(
            response.ClaimResults.Single().Outcome,
            Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [Test]
    public async Task WorkerExitWithoutResultProducesTypedFailure() {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var invalidWorker = project.CreateInvalidWorker();

        var build = await project.BuildAsync(
            verify: true,
            ("SharpProofWorkerPath", invalidWorker));

        Assert.That(build.ExitCode, Is.Not.Zero);
        Assert.That(File.Exists(project.ResultPath), Is.True);
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.MalformedResult));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task StrictProfileEnablesRequireProvenAndRejectsAssumptionsByDefault() {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);

        var build = await project.BuildAsync(
            verify: null,
            ("SharpProofProfile", "strict"));

        Assert.That(build.ExitCode, Is.Zero, build.Output);
        var request = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                request.VerifyPolicy,
                Is.EqualTo(WorkerVerifyPolicy.RequireProven));
            Assert.That(
                request.AssumptionPolicy,
                Is.EqualTo(WorkerAssumptionPolicy.Error));
        }
    }

    [Test]
    public async Task StrictProfileCannotDisableVerification() {
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
    public async Task ProjectBodyConfigurationUsesNewPropertiesAndLegacyAliases() {
        RequireWindowsWorker();
        using var strict = ConsumerProject.CreateConfigured(
            IdentitySource,
            ("SharpProofProfile", "strict"),
            ("SharpProofFeatures", "contracts"));

        var strictBuild = await strict.BuildAsync(verify: null);

        Assert.That(strictBuild.ExitCode, Is.Zero, strictBuild.Output);
        var strictRequest = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(strict.RequestPath))!;
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                strictRequest.Features,
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
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                legacyRequest.Features,
                Is.EqualTo(WorkerFeatureSet.Contracts));
            Assert.That(
                legacyRequest.VerifyPolicy,
                Is.EqualTo(WorkerVerifyPolicy.Advisory));
        }
    }

    [Test]
    public async Task UnknownClaimSeverityFollowsVerificationPolicy() {
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
    public async Task ConcurrentFeatureSelectionsUseIsolatedWorkerFiles() {
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
        var baseline = await project.BuildAsync(verify: false);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var contractsTask = project.RunVerificationTargetAsync(
            ("SharpProofFeatures", "contracts"),
            ("SharpProofVerifyPolicy", "require-proven"));
        var effectsTask = project.RunVerificationTargetAsync(
            ("SharpProofFeatures", "effects"),
            ("SharpProofVerifyPolicy", "require-proven"));
        var results = await Task.WhenAll(contractsTask, effectsTask);

        using (Assert.EnterMultipleScope()) {
            Assert.That(results[0].ExitCode, Is.Zero, results[0].Output);
            Assert.That(results[1].ExitCode, Is.Not.Zero, results[1].Output);
            Assert.That(results[1].Output, Does.Contain("error SP0047"));
        }

        var publishedRequest = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        var publishedResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        Assert.That(
            publishedResponse.Manifest.Callables.Length,
            Is.EqualTo(
                publishedRequest.Features == WorkerFeatureSet.Effects
                    ? 1
                    : 0),
            "The stable request/result files must describe one completed invocation.");
    }

    [Test]
    public async Task MalformedWorkerOutputPreservesTheStablePublication() {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var request = await File.ReadAllTextAsync(project.RequestPath);
        var result = await File.ReadAllTextAsync(project.ResultPath);
        var malformedWorker = await project.CreateMalformedWorkerAsync();

        var malformed = await project.RunVerificationTargetAsync(
            ("SharpProofWorkerPath", malformedWorker),
            ("SharpProofFeatures", "effects"));

        Assert.That(malformed.ExitCode, Is.Not.Zero);
        Assert.That(malformed.Output, Does.Contain("unavailable or malformed"));
        Assert.That(
            await File.ReadAllTextAsync(project.RequestPath),
            Is.EqualTo(request));
        Assert.That(
            await File.ReadAllTextAsync(project.ResultPath),
            Is.EqualTo(result));
    }

    [Test]
    public async Task SecondPublicationFailureRollsBackTheStableRequest() {
        RequireWindowsWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var request = await File.ReadAllTextAsync(project.RequestPath);
        var result = await File.ReadAllTextAsync(project.ResultPath);

        BuildResult failed;
        using (File.Open(
                   project.ResultPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read))
            failed = await project.RunVerificationTargetAsync(
                ("SharpProofFeatures", "effects"));

        Assert.That(failed.ExitCode, Is.Not.Zero);
        Assert.That(failed.Output, Does.Contain("could not be published"));
        Assert.That(
            await File.ReadAllTextAsync(project.RequestPath),
            Is.EqualTo(request));
        Assert.That(
            await File.ReadAllTextAsync(project.ResultPath),
            Is.EqualTo(result));
    }

    [Test]
    public async Task AssumptionSeverityIncludesUsedAndDeclaredEvidence() {
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
        var firstResponse = WorkerProtocolJson.DeserializeResponse(firstJson)!;
        var secondResponse =
            WorkerProtocolJson.DeserializeResponse(secondJson)!;

        Assert.That(
            SemanticPayload(secondResponse),
            Is.EqualTo(SemanticPayload(firstResponse)));
        Assert.That(secondWrite, Is.GreaterThan(firstWrite));
        Assert.That(second.Output, Does.Contain("SharpProof Proven"));
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                firstResponse.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(
                secondResponse.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Hit));
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
        if (OperatingSystem.IsWindows() &&
            RuntimeInformation.ProcessArchitecture ==
                Architecture.X64 &&
            RuntimeInformation.OSArchitecture == Architecture.X64)
            Assert.Ignore("The packaged worker is supported on Windows x64.");
        using var project = ConsumerProject.Create(IdentitySource);

        var build = await project.BuildAsync(verify: true);

        Assert.That(build.ExitCode, Is.Not.Zero);
        Assert.That(
            build.Output,
            Does.Contain(
                "SharpProof out-of-process verification is supported only on Windows x64"));
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
            .GroupBy(
                static element => element.Name.LocalName,
                StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last().Value,
                StringComparer.Ordinal);
        var compilerVisible = document
            .Descendants("CompilerVisibleProperty")
            .Select(static element =>
                element.Attribute("Include")?.Value)
            .Where(static value => value != null);

        using (Assert.EnterMultipleScope()) {
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
        }
    }

    [Test]
    public void LauncherDistinguishesValidFailedAndMalformedResponses() {
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse {
            InputHash = new('0', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Failed,
            FailureReason = WorkerRunFailureReason.InfrastructureFailure,
            Summary = new WorkerVerificationSummary {
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary {
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
        try {
            Console.SetOut(output);
            Console.SetError(error);
            File.WriteAllText(
                path,
                WorkerProtocolJson.SerializeResponse(response));

            var exitCode = Program.ValidateAndReport(
                path,
                new WorkerVerifyRequest(),
                out var validResponse);

            using (Assert.EnterMultipleScope()) {
                Assert.That(exitCode, Is.EqualTo(3));
                Assert.That(validResponse, Is.True);
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
                out validResponse);
            using (Assert.EnterMultipleScope()) {
                Assert.That(exitCode, Is.EqualTo(3));
                Assert.That(validResponse, Is.False);
                Assert.That(
                    error.ToString(),
                    Does.Contain("unavailable or malformed"));
            }
        }
        finally {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
            if (File.Exists(path)) File.Delete(path);
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
            response.ClaimResults.Single().Outcome,
            Is.EqualTo(WorkerClaimOutcome.Proven));
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
        Assert.That(refutedBuild.Output, Does.Contain("failed with exit code 5"));
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(refuted.ResultPath))!;
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
        using (Assert.EnterMultipleScope()) {
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

    private static string SemanticPayload(WorkerVerifyResponse response) =>
        System.Text.Json.JsonSerializer.Serialize(
            new {
                response.Manifest,
                response.RunStatus,
                response.FailureReason,
                response.CallableResults,
                response.ClaimResults,
                response.Errors
            },
            WorkerProtocolJson.Options);

    private static void RequireWindowsWorker() {
        if (!OperatingSystem.IsWindows() ||
            RuntimeInformation.ProcessArchitecture !=
                Architecture.X64 ||
            RuntimeInformation.OSArchitecture != Architecture.X64)
            Assert.Ignore(
                "The packaged worker is supported only on Windows x64.");
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

        internal string CreateInvalidWorker() {
            var path = Path.Combine(_root, "invalid-worker.dll");
            File.WriteAllBytes(path, [0]);
            return path;
        }

        internal async Task<string> CreateMalformedWorkerAsync() {
            var root = Path.Combine(_root, "malformed-worker");
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "MalformedWorker.csproj");
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
                """,
                new System.Text.UTF8Encoding(false));
            var build = await RunDotNetAsync([
                "build", project, "-c", "Release", "--nologo",
                "/nodeReuse:false", "-p:UseSharedCompilation=false"
            ]);
            if (build.ExitCode != 0)
                throw new InvalidOperationException(build.Output);
            return Path.Combine(
                root,
                "bin",
                "Release",
                "net8.0",
                "MalformedWorker.dll");
        }

        internal static ConsumerProject Create(
            string source,
            bool useSpaces = false) =>
            CreateCore(source, useSpaces, []);

        internal static ConsumerProject CreateConfigured(
            string source,
            params (string Name, string Value)[] properties) =>
            CreateCore(source, useSpaces: false, properties);

        private static ConsumerProject CreateCore(
            string source,
            bool useSpaces,
            (string Name, string Value)[] properties) {
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
                CreateProjectXml(properties),
                new System.Text.UTF8Encoding(false));
            return new ConsumerProject(root);
        }

        internal async Task<BuildResult> BuildAsync(
            bool? verify,
            params (string Name, string Value)[] properties) {
            var arguments = new List<string> {
                "build",
                ProjectPath,
                "-c",
                "Release",
                "--nologo",
                "/nodeReuse:false",
                "-p:UseSharedCompilation=false"
            };
            if (verify.HasValue)
                arguments.Add(
                    "-p:SharpProofVerify=" +
                    (verify.Value ? "true" : "false"));
            arguments.AddRange(properties.Select(static property =>
                "-p:" + property.Name + "=" + property.Value));
            return await RunDotNetAsync(arguments);
        }

        internal Task<BuildResult> RunVerificationTargetAsync(
            params (string Name, string Value)[] properties) {
            var arguments = new List<string> {
                "msbuild",
                ProjectPath,
                "/t:_SharpProofVerifyCore",
                "/nologo",
                "/nodeReuse:false",
                "-p:Configuration=Release",
                "-p:SharpProofVerify=true"
            };
            arguments.AddRange(properties.Select(static property =>
                "-p:" + property.Name + "=" + property.Value));
            return RunDotNetAsync(arguments);
        }

        private async Task<BuildResult> RunDotNetAsync(
            IEnumerable<string> arguments) {
            var startInfo = new ProcessStartInfo {
                FileName = "dotnet",
                WorkingDirectory = _root,
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

        private static string CreateProjectXml(
            IEnumerable<(string Name, string Value)> properties) {
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
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <LangVersion>12.0</LangVersion>
                    <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
                    <SharpProofWorkerPath>{worker}</SharpProofWorkerPath>
                    <SharpProofLauncherPath>{launcher}</SharpProofLauncherPath>
                {configuredProperties}
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{attributes}" />
                  </ItemGroup>
                  <Import Project="{targets}" />
                  <Target Name="_RemoveSharpProofAnalyzersForWorkerTargetTest"
                          BeforeTargets="CoreCompile">
                    <ItemGroup>
                      <Analyzer Remove="@(Analyzer)"
                                Condition="'%(Analyzer.SharpProofAnalyzerRole)' != ''" />
                    </ItemGroup>
                  </Target>
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
