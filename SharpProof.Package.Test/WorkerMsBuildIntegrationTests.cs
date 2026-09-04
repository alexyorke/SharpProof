using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Worker;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerMsBuildIntegrationTests
{
    private static readonly string s_sharedCompilationServerId =
        "sharpproof-worker-integration-" +
        typeof(WorkerMsBuildIntegrationTests).Assembly.ManifestModule
            .ModuleVersionId.ToString("N");
    private static readonly string[] s_publicPolicyProperties = [
        "SharpProofProfile",
        "SharpProofFeatures",
        "SharpProofVerifyPolicy",
        "SharpProofAssumptionPolicy",
        "SharpProofSpecificationPacks"
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
        "SharpProof.Verifier.props",
        "SharpProof.targets",
        "SharpProof.Verifier.targets"
    ];
    private static readonly string[] s_runtimeClosureProperties = [
        "SharpProofToolsDirectory",
        "SharpProofWorkerPath",
        "SharpProofLauncherPath"
    ];

    [Test]
    public void CanonicalContainerIsMandatoryForTheVerifier()
    {
        if (OperatingSystem.IsLinux() &&
            RuntimeInformation.ProcessArchitecture == Architecture.X64 &&
            string.Equals(
                Environment.GetEnvironmentVariable("SHARPPROOF_CONTAINER"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.That(ContainerContract.ValidateRequired(), Is.Not.Null);
            return;
        }

        Assert.Throws<PlatformNotSupportedException>(
            (Action)(() => ContainerContract.ValidateRequired()));
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

    [TestCase(false, "advisory")]
    [TestCase(true, "off")]
    public async Task DisabledVerificationInvalidatesPriorPublishedResult(
        bool verify,
        string profile)
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var verified = await project.BuildAsync(verify: true);
        Assert.That(verified.ExitCode, Is.Zero, verified.Output);
        Assert.That(File.Exists(project.ResultPath), Is.True);

        var disabled = await project.BuildAsync(
            verify,
            ("SharpProofProfile", profile));

        Assert.That(disabled.ExitCode, Is.Zero, disabled.Output);
        Assert.That(File.Exists(project.ResultPath), Is.False);
    }

    [TestCaseSource(nameof(s_runtimeClosureProperties))]
    public async Task RuntimeClosureOverridesAreRejected(string property)
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var foreign = Path.Combine(
            Path.GetDirectoryName(project.ProjectPath)!,
            "foreign-runtime");
        Directory.CreateDirectory(foreign);
        var value = property == "SharpProofToolsDirectory"
            ? foreign
            : project.CompilerManifestPath;

        var build = await project.BuildAsync(
            verify: true,
            (property, value));

        Assert.That(build.ExitCode, Is.Not.Zero, build.Output);
        Assert.That(build.Output, Does.Contain("exact package-owned runtime closure"));
        Assert.That(File.Exists(project.ResultPath), Is.False);
    }

    [TestCaseSource(nameof(s_runtimeClosureProperties))]
    public async Task ProjectBodyRuntimeClosureOverridesAreRejectedBeforePublication(
        string property)
    {
        RequireContainerWorker();
        var foreign = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.ForeignRuntime",
            Guid.NewGuid().ToString("N"));
        var value = property == "SharpProofToolsDirectory"
            ? foreign
            : Path.Combine(foreign, "foreign.dll");
        using var project = ConsumerProject.CreateConfigured(
            IdentitySource,
            (property, value));

        var build = await project.BuildAsync(verify: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(build.ExitCode, Is.Not.Zero, build.Output);
            Assert.That(
                build.Output,
                Does.Contain("exact package-owned runtime closure"));
            Assert.That(File.Exists(project.ResultPath), Is.False);
            Assert.That(
                File.Exists(LinuxPathIdentity.PublicationMarkerPath(
                    project.ResultPath)),
                Is.False);
        }
    }

    [Test]
    public async Task ProjectBodyAnalyzerAndCollectorOverridesNormalizeLate()
    {
        var analyzerDirectory = Path.Combine("relative", "analyzers");
        var collectorDirectory = Path.Combine(
            Path.GetTempPath(),
            "absolute-collector");
        using var project = ConsumerProject.CreateConfigured(
            IdentitySource,
            ("SharpProofAnalyzerDirectory", analyzerDirectory),
            ("SharpProofCollectorDirectory", collectorDirectory),
            ("SharpProofCompilerCollectorPath", " "),
            ("_SharpProofTestContractForGeneratorPath", " "));

        var properties = await project.EvaluatePropertiesAsync(
            "_SharpProofAnalyzerDirectory",
            "_SharpProofAnalyzerPath",
            "_SharpProofContractForGeneratorPath",
            "_SharpProofCollectorDirectory",
            "SharpProofCompilerCollectorPath");

        var expectedAnalyzerDirectory = Path.GetFullPath(
            Path.Combine(project.Root, analyzerDirectory));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                properties["_SharpProofAnalyzerDirectory"],
                Is.EqualTo(expectedAnalyzerDirectory));
            Assert.That(
                properties["_SharpProofAnalyzerPath"],
                Is.EqualTo(Path.Combine(
                    expectedAnalyzerDirectory,
                    "SharpProof.Analyzer.dll")));
            Assert.That(
                properties["_SharpProofContractForGeneratorPath"],
                Is.EqualTo(Path.Combine(
                    expectedAnalyzerDirectory,
                    "SharpProof.ContractForGenerator.dll")));
            Assert.That(
                properties["_SharpProofCollectorDirectory"],
                Is.EqualTo(Path.GetFullPath(collectorDirectory)));
            Assert.That(
                properties["SharpProofCompilerCollectorPath"],
                Is.EqualTo(Path.Combine(
                    Path.GetFullPath(collectorDirectory),
                    "SharpProof.CompilerCollector.dll")));
        }
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
        RequireContainerWorker();
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
                WorkerProtocolJson.ComputeSha256(manifestBytes),
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
    public async Task RelationalSpecificationPackIsExplicitAndPackaged()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Maximum(int left, int right) {
                    Contract.Ensures(
                        Contract.Result<int>() ==
                        (left >= right ? left : right));
                    return Math.Max(left, right);
                }
            }
            """);

        var disabledBuild = await project.BuildAsync(verify: true);
        Assert.That(disabledBuild.ExitCode, Is.Zero, disabledBuild.Output);
        var disabled = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;

        var enabledBuild = await project.BuildAsync(
            verify: true,
            ("SharpProofSpecificationPacks", "dotnet.scalar"));
        Assert.That(enabledBuild.ExitCode, Is.Zero, enabledBuild.Output);
        var enabled = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                disabled.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                disabled.ClaimResults.Single().Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(
                enabled.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                enabled.ClaimResults.Single().ProofCore.Any(
                    static item => item.StartsWith(
                        "spec-pack:dotnet.scalar@1:",
                        StringComparison.Ordinal)),
                Is.True);
        }
    }

    [Test]
    public async Task VerifierLaunchPreservesPercentCharactersInPaths()
    {
        RequireContainerWorker();
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(build.ExitCode, Is.Zero, build.Output);
            Assert.That(File.Exists(requestPath), Is.True, build.Output);
            Assert.That(File.Exists(resultPath), Is.True, build.Output);
            Assert.That(File.Exists(manifestPath), Is.True, build.Output);
        }
    }

    [Test]
    public async Task LongLocalPublicationPathsWorkInTheContainer()
    {
        RequireContainerWorker();
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
        var configuredRequestPath = Path.Combine(
            publicationDirectory, "request.json");
        var configuredResultPath = Path.Combine(
            publicationDirectory, "result.json");
        var configuredManifestPath = Path.Combine(
            publicationDirectory, "manifest.json");
        var effectiveDirectory = Path.Combine(
            publicationDirectory, "netstandard2.0");
        var requestPath = Path.Combine(effectiveDirectory, "request.json");
        var resultPath = Path.Combine(effectiveDirectory, "result.json");
        var manifestPath = Path.Combine(effectiveDirectory, "manifest.json");
        var sarifPath = Path.Combine(publicationDirectory, "result.sarif");
        var effectiveSarifPath = Path.Combine(
            publicationDirectory,
            "netstandard2.0",
            Path.GetFileName(sarifPath));
        var cachePath = Path.Combine(publicationDirectory, "cache");
        Assert.That(resultPath.Length, Is.GreaterThan(260));

        var dotnet = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyRequestFile", configuredRequestPath),
            ("SharpProofVerifyResultFile", configuredResultPath),
            ("SharpProofCompilerManifestFile", configuredManifestPath),
            ("SharpProofVerifyCacheDirectory", cachePath),
            ("SharpProofVerifySarifFile", sarifPath));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dotnet.ExitCode, Is.Zero, dotnet.Output);
            Assert.That(File.Exists(requestPath), Is.True);
            Assert.That(File.Exists(resultPath), Is.True);
            Assert.That(File.Exists(manifestPath), Is.True);
            Assert.That(File.Exists(effectiveSarifPath), Is.True);
        }
    }

    [Test]
    public async Task LongProjectDirectoryWorksInTheContainer()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.CreateWithLongPath(IdentitySource);
        Assert.That(project.Root.Length, Is.GreaterThan(239));

        var restore = await project.RestoreAsync();
        var build = await project.BuildAsync(verify: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restore.ExitCode, Is.Zero, restore.Output);
            Assert.That(build.ExitCode, Is.Zero, build.Output);
            Assert.That(
                File.Exists(project.VerifyOutputPath(
                    "netstandard2.0",
                    "result.json")),
                Is.True,
                build.Output);
        }
    }

    [Test]
    public async Task PublicationStagesAFullSetBeforeCommittingLongSarifBasename()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.CreateConfigured(
            IdentitySource,
            ("TargetFrameworks", "netstandard2.0"));
        var publicationDirectory = Directory.CreateDirectory(
            Path.Combine(project.Root, "long-basename-publication"));
        var configuredRequestPath = Path.Combine(
            publicationDirectory.FullName, "request.json");
        var configuredResultPath = Path.Combine(
            publicationDirectory.FullName, "result.json");
        var configuredManifestPath = Path.Combine(
            publicationDirectory.FullName, "compiler-manifest.json");
        var effectiveDirectory = Path.Combine(
            publicationDirectory.FullName, "netstandard2.0");
        var requestPath = Path.Combine(effectiveDirectory, "request.json");
        var resultPath = Path.Combine(effectiveDirectory, "result.json");
        var manifestPath = Path.Combine(
            effectiveDirectory, "compiler-manifest.json");
        var sarifPath = Path.Combine(
            publicationDirectory.FullName,
            new string('s', 220) + ".sarif");
        var effectiveSarifPath = Path.Combine(
            publicationDirectory.FullName,
            "netstandard2.0",
            Path.GetFileName(sarifPath));
        Assert.That(Path.GetFileName(sarifPath).Length, Is.EqualTo(226));

        var first = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyRequestFile", configuredRequestPath),
            ("SharpProofVerifyResultFile", configuredResultPath),
            ("SharpProofCompilerManifestFile", configuredManifestPath),
            ("SharpProofVerifySarifFile", sarifPath));
        var second = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyRequestFile", configuredRequestPath),
            ("SharpProofVerifyResultFile", configuredResultPath),
            ("SharpProofCompilerManifestFile", configuredManifestPath),
            ("SharpProofVerifySarifFile", sarifPath));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ExitCode, Is.Zero, first.Output);
            Assert.That(second.ExitCode, Is.Zero, second.Output);
            Assert.That(File.Exists(requestPath), Is.True, second.Output);
            Assert.That(File.Exists(resultPath), Is.True, second.Output);
            Assert.That(File.Exists(manifestPath), Is.True, second.Output);
            Assert.That(File.Exists(effectiveSarifPath), Is.True, second.Output);
        }

        var request = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(requestPath))!;
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(resultPath))!;
        await AssertPublicationBindingAsync(request, response);
    }

    [Test]
    public void PublicationLockPathIsStableAcrossReplacement()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var directory = Path.GetDirectoryName(project.ProjectPath)!;
        var result = Path.Combine(directory, "publication.json");

        var before = LinuxPathIdentity.PublicationLockName(result);
        File.WriteAllText(result, "first");
        var existing = LinuxPathIdentity.PublicationLockName(result);
        File.WriteAllText(result, "replacement");
        var replaced = LinuxPathIdentity.PublicationLockName(result);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Path.GetExtension(before), Is.EqualTo(".lock"));
            Assert.That(
                Path.GetFileName(Path.GetDirectoryName(before)),
                Is.EqualTo(".sharpproof-publication"));
            Assert.That(Path.GetFileNameWithoutExtension(before), Has.Length.EqualTo(64));
            Assert.That(existing, Is.EqualTo(before));
            Assert.That(replaced, Is.EqualTo(before));
        }
    }

    [Test]
    [Platform("Linux")]
    public void PublicationLocksRejectSymlinksAndNonRegularFilesWithoutTouchingTargets()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var directory = Path.GetDirectoryName(project.ProjectPath)!;

        var missingResult = Path.Combine(directory, "missing-result.json");
        var missingLock = LinuxPathIdentity.PublicationLockName(missingResult);
        var missingTarget = Path.Combine(directory, "missing-lock-target");
        if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(missingLock)!,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
        File.CreateSymbolicLink(missingLock, missingTarget);

        Assert.Throws<IOException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [missingResult],
                TimeSpan.FromSeconds(1));
        }));
        Assert.That(File.Exists(missingTarget), Is.False);

        var existingResult = Path.Combine(directory, "existing-result.json");
        var existingLock = LinuxPathIdentity.PublicationLockName(existingResult);
        var existingTarget = Path.Combine(directory, "existing-lock-target");
        const string sentinel = "user-owned lock target bytes";
        File.WriteAllText(existingTarget, sentinel);
        File.CreateSymbolicLink(existingLock, existingTarget);

        Assert.Throws<IOException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [existingResult],
                TimeSpan.FromSeconds(1));
        }));
        Assert.That(File.ReadAllText(existingTarget), Is.EqualTo(sentinel));

        var directoryResult = Path.Combine(directory, "directory-result.json");
        Directory.CreateDirectory(
            LinuxPathIdentity.PublicationLockName(directoryResult));
        Assert.Throws<IOException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [directoryResult],
                TimeSpan.FromSeconds(1));
        }));

        var normalResult = Path.Combine(directory, "normal-result.json");
        using (LinuxPathIdentity.AcquirePublicationSet(
                   [normalResult],
                   TimeSpan.FromSeconds(1)))
        {
        }
        var normalLock = LinuxPathIdentity.PublicationLockName(normalResult);
        using var normalStream = new FileStream(
            normalLock,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite);
        Assert.That(normalStream.Length, Is.Zero);
    }

    [Test]
    public async Task ChangingOneMemberOfAPublishedSetRequiresCleanOutputMetadata()
    {
        RequireContainerWorker();
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
        RequireContainerWorker();
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
        RequireContainerWorker();
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
        RequireContainerWorker();
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
    public async Task MultiTargetConfiguredPublicationTripleIsFrameworkScoped()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.CreateConfigured(
            IdentitySource,
            ("TargetFrameworks", "net8.0;net9.0"),
            ("BuildInParallel", "false"),
            ("SharpProofVerifyRequestFile", "evidence/request.json"),
            ("SharpProofVerifyResultFile", "evidence/result.json"),
            ("SharpProofCompilerManifestFile", "evidence/compiler-manifest.json"));

        var build = await project.BuildAsync(verify: true);

        Assert.That(build.ExitCode, Is.Zero, build.Output);
        foreach (var framework in new[] { "net8.0", "net9.0" })
        {
            var directory = Path.Combine(project.Root, "evidence", framework);
            var requestPath = Path.Combine(directory, "request.json");
            var resultPath = Path.Combine(directory, "result.json");
            var manifestPath = Path.Combine(directory, "compiler-manifest.json");
            var request = WorkerProtocolJson.DeserializeRequest(
                await File.ReadAllTextAsync(requestPath))!;
            var response = WorkerProtocolJson.DeserializeResponse(
                await File.ReadAllTextAsync(resultPath))!;
            var artifact = await AssertPublicationBindingAsync(request, response);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(artifact.TargetFramework, Is.EqualTo(framework));
                Assert.That(request.CompilerManifest.Path,
                    Is.EqualTo(Path.GetFullPath(manifestPath)));
                Assert.That(File.Exists(manifestPath), Is.True, build.Output);
            }
        }
    }

    [Test]
    public async Task MultiTargetConfiguredSarifIsFrameworkScoped()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.CreateConfigured(
            IdentitySource,
            ("TargetFrameworks", "net8.0;net9.0"),
            ("SharpProofVerifySarifFile", "evidence/result.sarif"));

        var build = await project.BuildAsync(verify: true);

        Assert.That(build.ExitCode, Is.Zero, build.Output);
        foreach (var framework in new[] { "net8.0", "net9.0" })
        {
            var sarif = Path.Combine(
                project.Root,
                "evidence",
                framework,
                "result.sarif");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(sarif), Is.True, build.Output);
                Assert.That(
                    File.Exists(LinuxPathIdentity.PublicationMarkerPath(sarif)),
                    Is.True,
                    build.Output);
                Assert.That(
                    await File.ReadAllBytesAsync(
                        LinuxPathIdentity.PublicationMarkerPath(sarif)),
                    Is.EqualTo(await File.ReadAllBytesAsync(
                        LinuxPathIdentity.PublicationMarkerPath(
                            project.VerifyOutputPath(
                                framework,
                                "result.json")))));
            }
        }
    }

    [Test]
    public async Task ThreeTargetAbsoluteSarifSurvivesSerialIncrementalAndCleanBuilds()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.CreateConfigured(
            IdentitySource,
            ("TargetFrameworks", "net9.0;net8.0;netstandard2.0"),
            ("BuildInParallel", "false"));
        var configured = Path.Combine(
            project.Root,
            "absolute-evidence",
            "verification.sarif");

        var first = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifySarifFile", configured));
        var incremental = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifySarifFile", configured));
        var clean = await project.CleanAsync(
            ("SharpProofVerifySarifFile", configured));
        var rebuilt = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifySarifFile", configured));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ExitCode, Is.Zero, first.Output);
            Assert.That(incremental.ExitCode, Is.Zero, incremental.Output);
            Assert.That(clean.ExitCode, Is.Zero, clean.Output);
            Assert.That(rebuilt.ExitCode, Is.Zero, rebuilt.Output);
        }
        var markerIdentities = new List<string>();
        foreach (var framework in new[]
                 {
                     "net9.0", "net8.0", "netstandard2.0"
                 })
        {
            var sarif = Path.Combine(
                project.Root,
                "absolute-evidence",
                framework,
                "verification.sarif");
            Assert.That(File.Exists(sarif), Is.True, rebuilt.Output);
            markerIdentities.Add(await File.ReadAllTextAsync(
                LinuxPathIdentity.PublicationMarkerPath(sarif)));
        }
        Assert.That(markerIdentities, Is.Unique);
    }

    [Test]
    public async Task MissingCompilerManifestFailsBeforeWorkerLaunch()
    {
        RequireContainerWorker();
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
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var sarifPath = project.VerifyOutputPath(
            "net8.0", "stale-result.sarif");
        var baseline = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifySarifFile", sarifPath));
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var stableResult = await File.ReadAllBytesAsync(project.ResultPath);

        await AssertInvalidatedAsync(
            ("_SharpProofCompilerManifestPath",
                project.CompilerManifestPath + ".missing"));
        await AssertInvalidatedAsync(
            ("_SharpProofCompilerManifestPath",
                project.CompilerManifestPath + ".invocation"),
            ("SharpProofLauncherPath",
                project.CompilerManifestPath + ".missing-launcher.dll"));
        await AssertInvalidatedAsync(
            ("_SharpProofCompilerManifestPath",
                project.CompilerManifestPath + ".invocation"),
            ("SharpProofWorkerPath",
                project.CompilerManifestPath + ".missing-worker.dll"));
        await AssertInvalidatedAsync(
            ("_SharpProofCompilerManifestPath",
                project.CompilerManifestPath + ".invocation"),
            ("SharpProofVerifyPolicy", "invalid"));
        await AssertInvalidatedAsync(
            ("_SharpProofCompilerManifestPath",
                project.CompilerManifestPath + ".invocation"),
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
    public async Task PublicationRejectsCompilerOwnedOutputsBeforeMutation()
    {
        RequireContainerWorker();
        var collisions = new[]
        {
            ("SharpProofVerifyResultFile", "target"),
            ("SharpProofVerifyRequestFile", "intermediate"),
            ("SharpProofCompilerManifestFile", "documentation"),
            ("SharpProofVerifySarifFile", "debug-symbols"),
            ("SharpProofVerifyRequestFile", "reference-assembly"),
            ("SharpProofCompilerManifestFile", "generated-editorconfig")
        };
        foreach (var (publicationProperty, outputKind) in collisions)
        {
            using var project = ConsumerProject.Create(IdentitySource);
            var compilerOutput = project.CompilerOutputPath(outputKind);
            var properties = new List<(string Name, string Value)>
            {
                (publicationProperty, compilerOutput)
            };
            if (outputKind == "documentation")
            {
                properties.Add(("DocumentationFile", compilerOutput));
            }

            var build = await project.BuildAsync(
                verify: true,
                properties.ToArray());

            Assert.That(
                build.ExitCode,
                Is.Not.Zero,
                $"{publicationProperty} -> {outputKind}{Environment.NewLine}" +
                build.Output);
            Assert.That(
                build.Output,
                Does.Contain("compiler-owned outputs"),
                $"{publicationProperty} -> {outputKind}");
            Assert.That(
                File.Exists(
                    LinuxPathIdentity.PublicationMarkerPath(compilerOutput)),
                Is.False,
                $"{publicationProperty} -> {outputKind}");
            Assert.That(
                File.Exists(compilerOutput),
                Is.False,
                $"{publicationProperty} -> {outputKind}");
        }

        using var incremental = ConsumerProject.Create(IdentitySource);
        var baseline = await incremental.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var targetPath = incremental.CompilerOutputPath("target");
        var targetBytes = await File.ReadAllBytesAsync(targetPath);

        var rejected = await incremental.BuildAsync(
            verify: true,
            ("SharpProofVerifyResultFile", targetPath));

        Assert.That(rejected.ExitCode, Is.Not.Zero, rejected.Output);
        Assert.That(
            await File.ReadAllBytesAsync(targetPath),
            Is.EqualTo(targetBytes));
        Assert.That(
            File.Exists(LinuxPathIdentity.PublicationMarkerPath(targetPath)),
            Is.False);
    }

    [TestCase("intermediate-apphost")]
    [TestCase("final-apphost")]
    public async Task PublicationRejectsAppHostOutputsBeforeMutation(
        string outputKind)
    {
        RequireContainerWorker();
        using var project = ConsumerProject.CreateConfigured(
            ExecutableIdentitySource,
            ("OutputType", "Exe"),
            ("UseAppHost", "true"));
        var compilerOutput = project.CompilerOutputPath(outputKind);

        var build = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyResultFile", compilerOutput));

        Assert.That(build.ExitCode, Is.Not.Zero, build.Output);
        Assert.That(build.Output, Does.Contain("compiler-owned outputs"));
        Assert.That(
            File.Exists(LinuxPathIdentity.PublicationMarkerPath(compilerOutput)),
            Is.False);
        Assert.That(File.Exists(compilerOutput), Is.False);
    }

    [Test]
    public async Task WorkerExitWithoutResultProducesTypedFailure()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var resultlessWorker = await project.CreateResultlessWorkerAsync();

        var build = await project.BuildAsync(
            verify: true,
            ("SharpProofWorkerPath", resultlessWorker),
            ("_SharpProofTestWorkerPath", resultlessWorker));

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
        RequireContainerWorker();
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
        RequireContainerWorker();
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
        RequireContainerWorker();
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

        var promotedWarning = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyPolicy", "warn-on-unknown"),
            ("MSBuildWarningsAsErrors", "SP0047"));
        Assert.That(
            promotedWarning.ExitCode,
            Is.Not.Zero,
            promotedWarning.Output);
        Assert.That(promotedWarning.Output, Does.Contain("SP0047"));

        var required = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyPolicy", "require-proven"));
        Assert.That(required.ExitCode, Is.Not.Zero);
        Assert.That(required.Output, Does.Contain("error SP0047"));
        Assert.That(
            required.Output,
            Does.Not.Contain("SharpProof verifier failed with exit code"));
    }

    [Test]
    public async Task VerificationPoliciesIgnoreSurroundingWhitespace()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);

        var build = await project.BuildAsync(
            verify: true,
            ("SharpProofVerifyPolicy", " Advisory "),
            ("SharpProofAssumptionPolicy", " Allow "));

        Assert.That(build.ExitCode, Is.Zero, build.Output);
        var request = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(project.RequestPath))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                request.VerifyPolicy,
                Is.EqualTo(WorkerVerifyPolicy.Advisory));
            Assert.That(
                request.AssumptionPolicy,
                Is.EqualTo(WorkerAssumptionPolicy.Allow));
        }
    }

    [Test]
    public async Task ConcurrentInvocationsUseIsolatedWorkerFiles()
    {
        RequireContainerWorker();
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
    public async Task DotNetMsBuildSerializesCooperativePublications()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.CreateConfigured(
            IdentitySource,
            ("TargetFramework", "netstandard2.0"));
        var publication = Directory.CreateDirectory(
            Path.Combine(project.Root, "publication"));
        var request = Path.Combine(publication.FullName, "request.json");
        var result = Path.Combine(publication.FullName, "result.json");
        var manifest = Path.Combine(publication.FullName, "manifest.json");
        var sarif = Path.Combine(publication.FullName, "result.sarif");

        Task<BuildResult> BuildAsync(string name, string features)
        {
            return project.BuildAsync(
                verify: true,
                ("BaseIntermediateOutputPath",
                    Path.Combine(project.Root, "obj-" + name) +
                    Path.DirectorySeparatorChar),
                ("BaseOutputPath",
                    Path.Combine(project.Root, "bin-" + name) +
                    Path.DirectorySeparatorChar),
                ("DefaultItemExcludesInProjectFolder",
                    "obj-*/**"),
                ("SharedCompilationId",
                    s_sharedCompilationServerId + "-" + name),
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
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var malformedManifest = project.CompilerManifestPath + ".malformed";
        var sarifPath = project.VerifyOutputPath(
            "net8.0", "malformed-result.sarif");
        var baseline = await project.BuildAsync(
            verify: true,
            ("SharpProofCompilerManifestFile", malformedManifest),
            ("SharpProofVerifySarifFile", sarifPath));
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var request = await File.ReadAllTextAsync(project.RequestPath);
        var result = await File.ReadAllTextAsync(project.ResultPath);
        var malformedWorker = await project.CreateMalformedWorkerAsync();
        var malformedInvocationManifest =
            project.CompilerManifestPath + ".malformed-invocation";
        File.Copy(malformedManifest, malformedInvocationManifest);

        var malformed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", malformedInvocationManifest),
            ("SharpProofCompilerManifestFile",
                malformedManifest),
            ("SharpProofWorkerPath", malformedWorker),
            ("_SharpProofTestWorkerPath", malformedWorker),
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
                Is.EqualTo(request));
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
        RequireContainerWorker();
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
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var requestPath = project.VerifyOutputPath(
            "net8.0", "in-process-unclassified-request.json");
        var resultPath = project.VerifyOutputPath(
            "net8.0", "in-process-unclassified-result.json");

        // An exception outside the containment and request categories used to
        // escape Main, so the process died without a fail-closed result.
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
            static (_, _, _, _) => throw new FormatException("invalid state"));

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
    public async Task LauncherPropagatesWorkerCancellation()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var requestPath = project.VerifyOutputPath(
            "net8.0", "in-process-canceled-request.json");
        var resultPath = project.VerifyOutputPath(
            "net8.0", "in-process-canceled-result.json");

        Assert.ThrowsAsync<OperationCanceledException>(
            (Func<Task>)(async () => await Program.RunMain(
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
                static (_, _, _, _) => throw new OperationCanceledException(
                    "canceled"))));

        Assert.That(File.Exists(resultPath), Is.False);
    }

    [Test]
    public async Task LauncherReportsStagedWorkerClosureHashMismatchInProcess()
    {
        RequireContainerWorker();
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
    public async Task AbnormalWorkerExitCannotPublishCompleteEvidence()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var requestPath = project.VerifyOutputPath(
            "net8.0", "abnormal-exit-request.json");
        var resultPath = project.VerifyOutputPath(
            "net8.0", "abnormal-exit-result.json");
        var publicationDirectory = Path.Combine(
            project.Root,
            "abnormal-exit-publication");
        Directory.CreateDirectory(publicationDirectory);
        var publishRequestPath = Path.Combine(
            publicationDirectory,
            "request.json");
        var publishResultPath = Path.Combine(
            publicationDirectory,
            "result.json");
        var publishManifestPath = Path.Combine(
            publicationDirectory,
            "compiler-manifest.json");
        var publishSarifPath = Path.Combine(
            publicationDirectory,
            "result.sarif");

        var exitCode = await Program.RunMain(
            [
                "verify",
                "--worker", WorkerOutputPath(),
                "--request", requestPath,
                "--result", resultPath,
                "--compiler-manifest", project.CompilerManifestPath,
                "--verify-policy", "advisory",
                "--assumption-policy", "allow",
                "--publish-request", publishRequestPath,
                "--publish-result", publishResultPath,
                "--publish-compiler-manifest", publishManifestPath,
                "--publish-sarif", publishSarifPath
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
                return 42;
            });

        var publishedResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(publishResultPath))!;
        using var sarif = JsonDocument.Parse(
            await File.ReadAllTextAsync(publishSarifPath));
        var invocation = sarif.RootElement.GetProperty("runs")[0]
            .GetProperty("invocations")[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(3));
            Assert.That(
                publishedResponse.RunStatus,
                Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                publishedResponse.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.MalformedResult));
            Assert.That(
                publishedResponse.Errors.Select(static error => error.Code),
                Does.Contain("worker.malformed_result"));
            Assert.That(
                invocation.GetProperty("executionSuccessful").GetBoolean(),
                Is.False);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task LauncherReportsInProcessPublicationFailure()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var stableRequest = await File.ReadAllTextAsync(project.RequestPath);
        var requestPath = project.VerifyOutputPath(
            "net8.0", "in-process-publication-request.json");
        var resultPath = project.VerifyOutputPath(
            "net8.0", "in-process-publication-result.json");
        var invocationManifest = project.VerifyOutputPath(
            "net8.0", "in-process-invocation-manifest.json");
        File.Copy(project.CompilerManifestPath, invocationManifest);
        var publicationDirectory = Path.Combine(
            project.Root,
            "read-only-publication");
        Directory.CreateDirectory(publicationDirectory);
        var publishRequestPath = Path.Combine(
            publicationDirectory,
            "request.json");
        var publishResultPath = Path.Combine(
            publicationDirectory,
            "result.json");
        var publishManifestPath = Path.Combine(
            publicationDirectory,
            "compiler-manifest.json");
        using (LinuxPathIdentity.AcquirePublicationSet(
                   [
                       publishRequestPath,
                       publishResultPath,
                       publishManifestPath
                   ],
                   TimeSpan.FromSeconds(5)))
        {
        }
        await File.WriteAllTextAsync(publishRequestPath, stableRequest);
        File.SetUnixFileMode(
            publicationDirectory,
            UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var exitCode = await Program.RunMain(
                [
                    "verify",
                    "--worker", WorkerOutputPath(),
                    "--request", requestPath,
                    "--result", resultPath,
                    "--compiler-manifest", invocationManifest,
                    "--verify-policy", "advisory",
                    "--assumption-policy", "allow",
                    "--publish-request", publishRequestPath,
                    "--publish-result", publishResultPath,
                    "--publish-compiler-manifest", publishManifestPath
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
                    await File.ReadAllTextAsync(publishRequestPath),
                    Is.EqualTo(stableRequest));
                Assert.That(File.Exists(publishResultPath), Is.False);
            }
        }
        finally
        {
            File.SetUnixFileMode(
                publicationDirectory,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
    }

    [Test]
    public async Task HardTimeoutReplacesWorkerOwnedMalformedOutput()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var worker = await project.CreateMalformedThenHangWorkerAsync();

        var run = await project.BuildAsync(
            verify: true,
            ("SharpProofWorkerPath", worker),
            ("_SharpProofTestWorkerPath", worker),
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
            Assert.That(
                project.InvocationRunRoots,
                Is.Empty,
                run.Output);
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.TimedOut));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Summary.Versions.WorkerVersion,
                Is.EqualTo(FileVersionInfo.GetVersionInfo(worker).ProductVersion));
            Assert.That(response.ClaimResults,
                Has.All.Property(nameof(WorkerClaimResult.Reason))
                    .EqualTo(WorkerClaimReason.ProjectTimeout));
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task InvocationRunRootIsCleanedOnPrelaunchAndLaunchFailure()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var nativeZ3Path = ContainerContract.ResolveZ3LibraryRequired();
        var baseline = await project.BuildAsync(
            verify: false,
            ("_SharpProofPackageNativeZ3Path", nativeZ3Path));
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var prelaunchId = Guid.NewGuid().ToString("N");
        var prelaunchRoot = project.CreateInvocationRunRoot(prelaunchId);
        var prelaunch = await project.RunVerificationTargetWithInvocationIdAsync(
            prelaunchId,
            ("_SharpProofPackageNativeZ3Path", nativeZ3Path),
            ("SharpProofVerifyPolicy", "invalid"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prelaunch.ExitCode, Is.Not.Zero, prelaunch.Output);
            Assert.That(
                prelaunch.Output,
                Does.Contain("SharpProofVerifyPolicy must be advisory"));
            Assert.That(Directory.Exists(prelaunchRoot), Is.False,
                prelaunch.Output);
        }

        var launchFailureId = Guid.NewGuid().ToString("N");
        var launchFailureRoot = project.CreateInvocationRunRoot(launchFailureId);
        await File.WriteAllTextAsync(
            Path.Combine(launchFailureRoot, "compiler-manifest.json"),
            "{}");
        var launchFailure =
            await project.RunVerificationTargetWithInvocationIdAsync(
                launchFailureId,
                ("_SharpProofPackageNativeZ3Path", nativeZ3Path),
                ("_SharpProofCompilerManifestPath", Path.Combine(
                    launchFailureRoot,
                    "compiler-manifest.json")),
                ("_SharpProofDotNetHost",
                    Path.Combine(launchFailureRoot, "not-dotnet")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(launchFailure.ExitCode, Is.Not.Zero,
                launchFailure.Output);
            Assert.That(
                launchFailure.Output,
                Does.Contain("SharpProof verifier launch failed"));
            Assert.That(Directory.Exists(launchFailureRoot), Is.False,
                launchFailure.Output);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task NoncanonicalInvocationIdCannotEscapeRunsRoot()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var nativeZ3Path = ContainerContract.ResolveZ3LibraryRequired();
        var baseline = await project.BuildAsync(
            verify: false,
            ("_SharpProofPackageNativeZ3Path", nativeZ3Path));
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var sentinelDirectory = Path.Combine(
            project.Root,
            "invocation-escape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sentinelDirectory);
        var sentinel = Path.Combine(sentinelDirectory, "harmless-sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "preserve");
        var traversalId = Path.GetRelativePath(
                project.InvocationRunsDirectory,
                sentinelDirectory)
            .Replace(Path.DirectorySeparatorChar, '/');

        var result = await project.RunVerificationTargetWithInvocationIdAsync(
            traversalId,
            ("_SharpProofPackageNativeZ3Path", nativeZ3Path),
            ("_SharpProofInvocationIdIsSafe", "True"),
            ("_SharpProofInvocationDirectoryIsContained", "True"),
            ("_SharpProofCleanupInvocationIdIsSafe", "True"),
            ("_SharpProofCleanupInvocationDirectoryIsContained", "True"),
            ("SharpProofVerifyPolicy", "invalid"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
            Assert.That(
                result.Output,
                Does.Contain("invocation ID must be an exact safe identifier"));
            Assert.That(Directory.Exists(sentinelDirectory), Is.True,
                result.Output);
            Assert.That(File.Exists(sentinel), Is.True, result.Output);
        }

        var safeId = Guid.NewGuid().ToString("N");
        var noncontained = await project.RunCleanupTargetAsync(
            safeId,
            sentinelDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(noncontained.ExitCode, Is.Not.Zero, noncontained.Output);
            Assert.That(
                noncontained.Output,
                Does.Contain(
                    "invocation cleanup directory must resolve canonically"));
            Assert.That(Directory.Exists(sentinelDirectory), Is.True,
                noncontained.Output);
            Assert.That(File.Exists(sentinel), Is.True, noncontained.Output);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task InvocationCleanupFailurePreservesDiagnosticAndRecovers()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var nativeZ3Path = ContainerContract.ResolveZ3LibraryRequired();
        var baseline = await project.BuildAsync(
            verify: false,
            ("_SharpProofPackageNativeZ3Path", nativeZ3Path));
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var invocationId = Guid.NewGuid().ToString("N");
        var invocationRoot = project.CreateInvocationRunRoot(invocationId);
        var runsMode = File.GetUnixFileMode(project.InvocationRunsDirectory);
        File.SetUnixFileMode(
            project.InvocationRunsDirectory,
            UnixFileMode.UserRead | UnixFileMode.UserExecute);
        BuildResult failure;
        try
        {
            failure = await project.RunVerificationTargetWithInvocationIdAsync(
                invocationId,
                ("_SharpProofPackageNativeZ3Path", nativeZ3Path),
                ("SharpProofVerifyPolicy", "invalid"));
        }
        finally
        {
            File.SetUnixFileMode(project.InvocationRunsDirectory, runsMode);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.ExitCode, Is.Not.Zero, failure.Output);
            Assert.That(
                failure.Output,
                Does.Contain("SharpProofVerifyPolicy must be advisory"));
            Assert.That(Directory.Exists(invocationRoot), Is.True,
                failure.Output);
        }

        var recovery = await project.RunCleanupTargetAsync(invocationId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(recovery.ExitCode, Is.Zero, recovery.Output);
            Assert.That(Directory.Exists(invocationRoot), Is.False,
                recovery.Output);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task InvocationRunRootsAreCleanedAfterRepeatedSuccessfulBuilds()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var nativeZ3Path = ContainerContract.ResolveZ3LibraryRequired();

        var first = await project.BuildAsync(
            verify: true,
            ("_SharpProofPackageNativeZ3Path", nativeZ3Path));
        var second = await project.BuildAsync(
            verify: true,
            ("_SharpProofPackageNativeZ3Path", nativeZ3Path));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ExitCode, Is.Zero, first.Output);
            Assert.That(second.ExitCode, Is.Zero, second.Output);
            Assert.That(project.InvocationRunRoots, Is.Empty);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task CompilerFailuresDoNotAccumulateInvocationRunRoots()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(
            "public static class Subject { public static int Broken() { int unused; return 0; } }");

        var first = await project.BuildAsync(
            verify: true,
            ("WarningsAsErrors", "CS0168"));
        var second = await project.BuildAsync(
            verify: true,
            ("WarningsAsErrors", "CS0168"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ExitCode, Is.Not.Zero, first.Output);
            Assert.That(second.ExitCode, Is.Not.Zero, second.Output);
            Assert.That(first.Output, Does.Contain("CS0168"));
            Assert.That(second.Output, Does.Contain("CS0168"));
            Assert.That(project.InvocationRunRoots, Is.Empty);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task PublicationFailureLeavesStableResultAbsent()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var sarifPath = project.VerifyOutputPath(
            "net8.0", "publication-failure.sarif");
        var failedManifestPath = project.CompilerManifestPath + ".failed";
        var baseline = await project.BuildAsync(
            verify: true,
            ("SharpProofCompilerManifestFile", failedManifestPath),
            ("SharpProofVerifySarifFile", sarifPath));
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);
        var request = await File.ReadAllTextAsync(project.RequestPath);
        var failedInvocationManifest =
            project.CompilerManifestPath + ".failed-invocation";
        File.Copy(failedManifestPath, failedInvocationManifest);
        File.Delete(project.ResultPath);
        File.Delete(sarifPath);
        var publicationDirectory = Path.GetDirectoryName(
            project.RequestPath)!;
        var invocationId = Guid.NewGuid().ToString("N");
        var invocationDirectory = project.InvocationRunRoot(invocationId);
        Directory.CreateDirectory(invocationDirectory);
        File.SetUnixFileMode(
            publicationDirectory,
            UnixFileMode.UserRead | UnixFileMode.UserExecute);
        BuildResult failed;
        try
        {
            failed = await project.RunVerificationTargetWithInvocationIdAsync(
                invocationId,
                ("_SharpProofCompilerManifestPath", failedInvocationManifest),
                ("SharpProofCompilerManifestFile", failedManifestPath),
                ("SharpProofVerifyPolicy", "advisory"),
                ("SharpProofVerifySarifFile", sarifPath),
                ("SharpProofVerifyCacheEnabled", "false"));
        }
        finally
        {
            File.SetUnixFileMode(
                publicationDirectory,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }

        Assert.That(failed.ExitCode, Is.Not.Zero);
        Assert.That(failed.Output, Does.Contain("could not be published"));
        Assert.That(
            await File.ReadAllTextAsync(project.RequestPath),
            Is.EqualTo(request));
        Assert.That(File.Exists(project.ResultPath), Is.False);
        Assert.That(File.Exists(sarifPath), Is.False);
        Assert.That(File.Exists(failedManifestPath),
            Is.True);
    }

    [Test]
    public async Task AliasedPublicationPathsAreRejectedWithoutChangingStableOutputs()
    {
        RequireContainerWorker();
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
                Does.Contain("SharpProof output paths must not alias input paths."));
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
                Does.Contain("SharpProof output paths must not alias input paths."));
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
                Does.Contain("SharpProof output paths must be distinct."));
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
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var collisionWorker = project.CollisionWorkerPath;
        var collisionCompanion = await StageCollisionWorkerAsync(project);
        var failed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofWorkerPath", collisionWorker),
            ("_SharpProofTestWorkerPath", collisionWorker),
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
    public async Task SymlinkedWorkerCompanionIsRejectedBeforeInvalidationDeletesIt()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var collisionWorker = project.CollisionWorkerPath;
        var collisionCompanion = await StageCollisionWorkerAsync(project);
        var expectedBytes = await File.ReadAllBytesAsync(collisionCompanion);
        var symbolicAlias = Path.Combine(
            Path.GetDirectoryName(project.ResultPath)!,
            "symlinked-result.json");
        File.CreateSymbolicLink(symbolicAlias, collisionCompanion);
        var failed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofWorkerPath", collisionWorker),
            ("_SharpProofTestWorkerPath", collisionWorker),
            ("SharpProofVerifyResultFile", symbolicAlias));

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
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var collisionWorker = project.CollisionWorkerPath;
        var collisionCompanion = await StageCollisionWorkerAsync(project);
        var expectedBytes = await File.ReadAllBytesAsync(collisionCompanion);
        var hardLink = Path.Combine(
            Path.GetDirectoryName(project.ResultPath)!,
            "hard-linked-result.json");
        var linkStart = new ProcessStartInfo
        {
            FileName = "/usr/bin/ln",
            UseShellExecute = false
        };
        linkStart.ArgumentList.Add(collisionCompanion);
        linkStart.ArgumentList.Add(hardLink);
        using (var link = Process.Start(linkStart) ??
               throw new InvalidOperationException("The hard-link helper did not start."))
        {
            await link.WaitForExitAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(link.ExitCode, Is.Zero);
        }

        var failed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("SharpProofWorkerPath", collisionWorker),
            ("_SharpProofTestWorkerPath", collisionWorker),
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

    private static async Task<string> StageCollisionWorkerAsync(
        ConsumerProject project)
    {
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

        return Path.ChangeExtension(collisionWorker, ".deps.json");
    }

    [Test]
    public async Task LauncherProtocolAssetRemainsProtectedByTargets()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var build = await project.BuildAsync(verify: true);
        Assert.That(build.ExitCode, Is.Zero, build.Output);
        var isolatedLauncherDirectory = Path.GetDirectoryName(
            project.CollisionWorkerPath)!;
        Directory.CreateDirectory(isolatedLauncherDirectory);
        var protocolPath = Path.Combine(
            isolatedLauncherDirectory,
            "isolated-protocol.dll");
        var isolatedRequestPath = Path.Combine(
            isolatedLauncherDirectory,
            "request.json");
        var isolatedManifestPath = Path.Combine(
            isolatedLauncherDirectory,
            "compiler-manifest.json");
        using (LinuxPathIdentity.AcquirePublicationSet(
                   [
                       isolatedRequestPath,
                       protocolPath,
                       isolatedManifestPath
                   ],
                   TimeSpan.FromSeconds(5)))
        {
        }
        File.Copy(LauncherProtocolOutputPath(), protocolPath);
        var expectedBytes = await File.ReadAllBytesAsync(protocolPath);
        var failed = await project.RunVerificationTargetAsync(
            ("_SharpProofCompilerManifestPath", project.CompilerManifestPath),
            ("_SharpProofLauncherPath", Path.Combine(
                isolatedLauncherDirectory,
                "isolated-launcher.dll")),
            ("_SharpProofWorkerProtocolPath", protocolPath),
            ("_SharpProofPackageWorkerProtocolPath", protocolPath),
            ("SharpProofVerifyRequestFile", isolatedRequestPath),
            ("SharpProofCompilerManifestFile", isolatedManifestPath),
            ("SharpProofVerifyResultFile", protocolPath));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.Not.Zero, failed.Output);
            var reportsProtectedAlias = failed.Output.Contains(
                "SharpProof output paths must not alias input paths.",
                StringComparison.Ordinal);
            Assert.That(
                reportsProtectedAlias,
                Is.True);
            var protocolExists = File.Exists(protocolPath);
            Assert.That(protocolExists, Is.True);
            if (protocolExists)
            {
                Assert.That(
                    await File.ReadAllBytesAsync(protocolPath),
                    Is.EqualTo(expectedBytes));
            }
        }
    }

    [Test]
    public async Task LauncherProtocolAssetAliasIsRejectedBeforeInvalidationDeletesIt()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var baseline = await project.BuildAsync(verify: true);
        Assert.That(baseline.ExitCode, Is.Zero, baseline.Output);

        var launcherDirectory = Path.GetDirectoryName(
            LauncherProtocolOutputPath())!;
        foreach (var fileName in
                 SharpProof.BuildTasks.LauncherRuntimeCompanionInventory
                     .FileNames)
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
                    failed.Output,
                    Does.Contain(
                        "SharpProof output paths must not alias input paths."),
                    collisionPath);
                Assert.That(File.Exists(collisionPath), Is.True, collisionPath);
            }
        }
    }

    [Test]
    public async Task WorkerCacheDirectoryAliasIsRejectedBeforeInvalidationDeletesIt()
    {
        RequireContainerWorker();
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
                        "SharpProof output, input, cache, and worker paths must be distinct."),
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
        RequireContainerWorker();
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
        RequireContainerWorker();
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
        RequireContainerWorker();
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
            ("_SharpProofTestWorkerPath", collisionWorker),
            ("SharpProofVerifyResultFile", collisionAsset));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.Not.Zero);
            Assert.That(
                failed.Output,
                Does.Contain(
                    "SharpProof output paths must not be inside the worker runtime."));
            Assert.That(File.Exists(collisionAsset), Is.True);
        }
    }

    [Test]
    public async Task MissingWorkerDoesNotAllowInvalidationToDeleteWorkerTreeOutput()
    {
        RequireContainerWorker();
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
                ("_SharpProofTestWorkerPath", worker),
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
        RequireContainerWorker();
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
        Assert.That(
            error.Output,
            Does.Not.Contain("SharpProof verifier failed with exit code"));

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
            Does.Not.Contain("SharpProof verifier failed with exit code"));
        Assert.That(
            declared.Output,
            Does.Contain("total=1, user=0, trusted=1"));
    }

    [Test]
    public async Task IncrementalBuildIsDeterministicAndKeepsResultStable()
    {
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var first = await project.BuildAsync(verify: true);
        Assert.That(first.ExitCode, Is.Zero, first.Output);
        var firstJson = await File.ReadAllTextAsync(project.ResultPath);
        var firstWrite = File.GetLastWriteTimeUtc(project.ResultPath);

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
        RequireContainerWorker();
        using var project = ConsumerProject.Create(IdentitySource);
        var first = await project.BuildAsync(verify: true);
        Assert.That(first.ExitCode, Is.Zero, first.Output);
        var firstResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(project.ResultPath))!;
        var firstWrite = File.GetLastWriteTimeUtc(project.ResultPath);

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
        if (OperatingSystem.IsLinux() &&
            RuntimeInformation.ProcessArchitecture ==
                Architecture.X64 &&
            RuntimeInformation.OSArchitecture == Architecture.X64 &&
            string.Equals(
                Environment.GetEnvironmentVariable("SHARPPROOF_CONTAINER"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Ignore("The packaged worker is supported in this container.");
        }

        using var project = ConsumerProject.Create(IdentitySource);

        var build = await project.BuildAsync(verify: true);

        Assert.That(build.ExitCode, Is.Not.Zero);
        Assert.That(
            build.Output,
            Does.Contain("canonical Linux amd64 container"));
    }

    [Test]
    public void PackagePropertiesMatchProtocolDefaults()
    {
        var repository = TestRepository.FindRoot();
        var portableProps = XDocument.Load(Path.Combine(
            repository,
            "SharpProof.Package",
            "buildTransitive",
            "SharpProof.props"));
        var verifierProps = XDocument.Load(Path.Combine(
            repository,
            "SharpProof.Verifier",
            "buildTransitive",
            "SharpProof.Verifier.props"));
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
        var compilerVisible = CompilerVisibleProperties(portableProps)
            .Concat(CompilerVisibleProperties(verifierProps));

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
        var repository = TestRepository.FindRoot();
        var packageProps = XDocument.Load(Path.Combine(
            repository,
            "SharpProof.Verifier",
            "buildTransitive",
            "SharpProof.Verifier.props"));
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
            "SharpProof.Verifier",
            "buildTransitive",
            "SharpProof.Verifier.targets"));
        var initialize = targets
            .Descendants("Target")
            .Single(static target =>
                target.Attribute("Name")?.Value ==
                "_SharpProofInitializeVerify");
        var resolvePaths = targets
            .Descendants("Target")
            .Single(static target =>
                target.Attribute("Name")?.Value ==
                "_SharpProofResolveVerificationPaths");
        var verifyCore = targets
            .Descendants("Target")
            .Single(static target =>
                target.Attribute("Name")?.Value ==
                "_SharpProofVerifyCore");
        var reset = targets
            .Descendants("Target")
            .Single(static target =>
                target.Attribute("Name")?.Value ==
                "SharpProofResetPublishedVerification");
        var cleanup = targets
            .Descendants("Target")
            .Single(static target =>
                target.Attribute("Name")?.Value ==
                "_SharpProofCleanupInvocation");
        var invocation = verifyCore
            .Descendants("SharpProof.BuildTasks.RunVerifier")
            .Single();
        var validation = verifyCore
            .Descendants(
                "SharpProof.BuildTasks.ValidatePublishedVerificationResult")
            .Single();
        var resetTask = reset
            .Descendants("SharpProof.BuildTasks.ResetPublishedVerification")
            .Single();
        var cleanupCall = verifyCore
            .Elements("CallTarget")
            .Single(static call =>
                call.Attribute("Targets")?.Value ==
                "_SharpProofCleanupInvocation");
        var cleanupOnError = verifyCore
            .Elements("OnError")
            .Single(static onError =>
                onError.Attribute("ExecuteTargets")?.Value ==
                "_SharpProofCleanupInvocation");
        var cleanupElements = cleanup.Elements().ToList();
        var cleanupRemove = cleanup.Elements("RemoveDir").Single();
        var cleanupSafetyErrors = cleanup.Elements("Error").ToArray();
        var verifyCoreElements = verifyCore.Elements().ToList();
        var runnerTask = targets.Descendants("UsingTask")
            .Single(static task => task.Attribute("TaskName")?.Value ==
                "SharpProof.BuildTasks.RunVerifier");
        var verifierExitError = verifyCore.Descendants("Error")
            .Single(static error => error.Attribute("Text")?.Value.StartsWith(
                "SharpProof verifier failed with exit code",
                StringComparison.Ordinal) == true);
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
                resolvePaths.Descendants(
                    "SharpProofCompilerManifestFile"),
                Is.Not.Empty);
            Assert.That(
                arguments,
                Does.Contain("--compiler-manifest")
                    .And.Contain("$(_SharpProofCompilerManifestPath)")
                    .And.Contain("--publish-compiler-manifest")
                    .And.Contain(
                        "$(_SharpProofEffectiveCompilerManifestFile)"));
            Assert.That(
                validation.Attribute("ProjectDirectory")?.Value,
                Is.EqualTo("$(MSBuildProjectDirectory)"));
            Assert.That(
                resetTask.Attribute("ProjectDirectory")?.Value,
                Is.EqualTo("$(MSBuildProjectDirectory)"));
            Assert.That(
                s_reconstructionArguments.Where(arguments.Contains),
                Is.Empty);
            Assert.That(
                invocation.Attribute("Executable")?.Value,
                Is.EqualTo("$(_SharpProofDotNetHost)"));
            Assert.That(
                invocation.Elements("Output").Any(static output =>
                    output.Attribute("TaskParameter")?.Value ==
                        "HasStructuredError" &&
                    output.Attribute("PropertyName")?.Value ==
                        "_SharpProofVerifierHasStructuredError"),
                Is.True);
            Assert.That(
                cleanup.Attribute("Condition")?.Value,
                Is.EqualTo("'$(_SharpProofInvocationId)' != ''"));
            Assert.That(
                cleanupRemove.Attribute("Directories")?.Value,
                Is.EqualTo("$(_SharpProofCleanupInvocationDirectoryFullPath)"));
            Assert.That(
                cleanupSafetyErrors.Count(static error =>
                    error.Attribute("Text")?.Value.Contains(
                        "exact safe identifier",
                        StringComparison.Ordinal) == true),
                Is.EqualTo(1));
            Assert.That(
                cleanupSafetyErrors.Count(static error =>
                    error.Attribute("Text")?.Value.Contains(
                        "resolve canonically",
                        StringComparison.Ordinal) == true),
                Is.EqualTo(1));
            Assert.That(
                cleanupSafetyErrors.Select(error =>
                    cleanupElements.IndexOf(error)),
                Is.All.LessThan(cleanupElements.IndexOf(cleanupRemove)),
                "Cleanup validation must run before RemoveDir.");
            Assert.That(
                cleanupCall.Attribute("Condition"),
                Is.Null,
                "Successful verification must not be the only cleanup path.");
            Assert.That(
                cleanupOnError.Attribute("ExecuteTargets")?.Value,
                Is.EqualTo("_SharpProofCleanupInvocation"));
            Assert.That(
                verifyCoreElements.IndexOf(cleanupCall),
                Is.GreaterThan(
                    verifyCore.Elements(
                            "SharpProof.BuildTasks.ValidatePublishedVerificationResult")
                        .Select(element => verifyCoreElements.IndexOf(element))
                        .Single()),
                "Cleanup must run after result validation and diagnostics.");
            Assert.That(
                verifierExitError.Attribute("Condition")?.Value,
                Does.Contain(
                    "'$(_SharpProofVerifierHasStructuredError)' != 'true'"));
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
                    Code = "worker.infrastructure",
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
                    Does.Contain("worker.infrastructure"));
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
        RequireContainerWorker();
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
        RequireContainerWorker();
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
            timedOutBuild.Output.Contains(
                "worker run TimedOut",
                StringComparison.Ordinal) ||
            timedOutBuild.Output.Contains(
                "worker run Failed (ContainmentFailure)",
                StringComparison.Ordinal),
            Is.True,
            timedOutBuild.Output);
        var timedOutResponse = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(timedOut.ResultPath))!;
        var publishedTimeout =
            timedOutResponse.RunStatus == WorkerRunStatus.TimedOut &&
            timedOutResponse.FailureReason == WorkerRunFailureReason.None;
        var failClosedContainment =
            timedOutResponse.RunStatus == WorkerRunStatus.Failed &&
            timedOutResponse.FailureReason ==
                WorkerRunFailureReason.ContainmentFailure;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                publishedTimeout || failClosedContainment,
                Is.True);
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

    private const string ExecutableIdentitySource =
        IdentitySource +
        """

        public static class Program {
            public static void Main() {
                _ = Subject.Identity(0);
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
        var digest = WorkerProtocolJson.ComputeSha256(artifact.Bytes);
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
                Is.EqualTo(workerBinarySha256));
            Assert.That(
                artifact.ManifestHash,
                Is.EqualTo(response.Manifest.Hash));
            Assert.That(WorkerProtocolJson.ValidateForRequest(
                response, response.RequestHash, expectedInputHash,
                response.Manifest, request,
                response.Summary.Versions).IsValid,
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
        return Path.Combine(TestRepository.FindRoot(), "SharpProof.Worker",
            "bin", configuration, "net9.0", "SharpProof.Worker.dll");
    }

    private static string LauncherProtocolOutputPath()
    {
        var configuration = new DirectoryInfo(Path.GetDirectoryName(
            typeof(WorkerMsBuildIntegrationTests).Assembly.Location)!)
            .Parent?.Name ?? throw new InvalidOperationException(
                "The test build configuration was not found.");
        return Path.Combine(TestRepository.FindRoot(),
            "SharpProof.Worker.Launcher", "bin", configuration, "net9.0",
            "SharpProof.Worker.Protocol.dll");
    }

    private static void RequireContainerWorker()
    {
        TestRepository.RequireCanonicalContainer();
        _ = ContainerContract.ValidateRequired();
    }

    private static IEnumerable<string?> CompilerVisibleProperties(
        XDocument document)
    {
        return document.Descendants("CompilerVisibleProperty")
            .SelectMany(static property =>
                (property.Attribute("Include")?.Value ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries));
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
                    Is.EqualTo(CompilerManifestArtifactVersions.Current));
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
        private bool _defaultRestoreCompleted;

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
        internal string InvocationRunsDirectory => Path.Combine(
            _root,
            "obj",
            "Release",
            "net8.0",
            "SharpProof",
            "runs");
        internal string[] InvocationRunRoots =>
            Directory.Exists(InvocationRunsDirectory)
                ? Directory.GetDirectories(InvocationRunsDirectory)
                : [];
        internal string CollisionWorkerPath
        {
            get;
        }
        internal string VerifyOutputPath(string framework, string fileName)
        {
            return Path.Combine(_root, "obj", "Release", framework, "SharpProof",
                fileName);
        }

        internal string InvocationRunRoot(string invocationId)
        {
            return Path.Combine(InvocationRunsDirectory, invocationId);
        }

        internal string CreateInvocationRunRoot(string invocationId)
        {
            var path = InvocationRunRoot(invocationId);
            Directory.CreateDirectory(path);
            File.WriteAllText(
                Path.Combine(path, "owned-state.txt"),
                "owned",
                new System.Text.UTF8Encoding(false));
            return path;
        }

        internal string CompilerOutputPath(string kind)
        {
            var intermediate = Path.Combine(
                _root, "obj", "Release", "net8.0");
            return kind switch
            {
                "target" => Path.Combine(
                    _root, "bin", "Release", "net8.0", "Consumer.dll"),
                "intermediate" => Path.Combine(intermediate, "Consumer.dll"),
                "documentation" => Path.Combine(intermediate, "Consumer.xml"),
                "debug-symbols" => Path.Combine(intermediate, "Consumer.pdb"),
                "reference-assembly" => Path.Combine(
                    intermediate, "ref", "Consumer.dll"),
                "generated-editorconfig" => Path.Combine(
                    intermediate,
                    "Consumer.GeneratedMSBuildEditorConfig.editorconfig"),
                "intermediate-apphost" => Path.Combine(
                    intermediate, "apphost"),
                "final-apphost" => Path.Combine(
                    _root, "bin", "Release", "net8.0", "Consumer"),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
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
                if (!string.Equals(
                        Console.ReadLine(),
                        "SharpProof.Start/1",
                        StringComparison.Ordinal))
                    return;

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
                "/nodeReuse:false"
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
                var result = args[Array.IndexOf(args, "--result") + 1];
                if (!string.Equals(
                        Console.ReadLine(),
                        "SharpProof.Start/1",
                        StringComparison.Ordinal))
                    return;
                File.WriteAllText(result, "not-json");
                """,
                new System.Text.UTF8Encoding(false));
            var build = await RunDotNetAsync([
                "build", project, "-c", "Release", "--nologo",
                "/nodeReuse:false"
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
                if (!string.Equals(
                        Console.ReadLine(),
                        "SharpProof.Start/1",
                        StringComparison.Ordinal))
                    return;
                File.WriteAllText(result, "not-json");
                Thread.Sleep(Timeout.Infinite);
                """,
                new System.Text.UTF8Encoding(false));
            var build = await RunDotNetAsync([
                "build", project, "-c", "Release", "--nologo",
                "/nodeReuse:false"
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
                Path.Combine(TestRepository.FindRoot(), "global.json"),
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
            var restoreSensitive = properties.Any(
                static property => IsRestoreSensitiveProperty(property.Name));
            if (restoreSensitive)
            {
                _defaultRestoreCompleted = false;
            }
            var skipRestore = _defaultRestoreCompleted &&
                !restoreSensitive &&
                File.Exists(Path.Combine(_root, "obj", "project.assets.json"));
            var arguments = new List<string> {
                "build",
                ProjectPath,
                "-c",
                "Release",
                "--nologo",
                "/m:1",
                "/nodeReuse:false",
                "-p:GeneratePackageOnBuild=false"
            };
            if (skipRestore)
            {
                arguments.Add("--no-restore");
            }
            if (verify.HasValue)
            {
                arguments.Add(
                    "-p:SharpProofVerify=" +
                    (verify.Value ? "true" : "false"));
            }

            arguments.AddRange(properties.Select(static property =>
                "-p:" + property.Name + "=" + property.Value));
            var result = await RunDotNetAsync(arguments);
            if (result.ExitCode == 0 && !restoreSensitive)
            {
                _defaultRestoreCompleted = true;
            }
            return result;
        }

        internal async Task<BuildResult> RestoreAsync(
            params (string Name, string Value)[] properties)
        {
            var restoreSensitive = properties.Any(
                static property => IsRestoreSensitiveProperty(property.Name));
            if (restoreSensitive)
            {
                _defaultRestoreCompleted = false;
            }
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
            var result = await RunDotNetAsync(arguments);
            if (result.ExitCode == 0 && !restoreSensitive)
            {
                _defaultRestoreCompleted = true;
            }
            return result;
        }

        private static bool IsRestoreSensitiveProperty(string name)
        {
            return name.Equals(
                       "BaseIntermediateOutputPath",
                       StringComparison.OrdinalIgnoreCase) ||
                name.Equals(
                    "MSBuildProjectExtensionsPath",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(
                    "RuntimeIdentifier",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Equals(
                    "RuntimeIdentifiers",
                    StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Restore", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Package", StringComparison.OrdinalIgnoreCase);
        }

        internal Task<BuildResult> CleanAsync(
            params (string Name, string Value)[] properties)
        {
            _defaultRestoreCompleted = false;
            var arguments = new List<string>
            {
                "clean",
                ProjectPath,
                "-c",
                "Release",
                "--nologo",
                "/nodeReuse:false",
                "-p:SharpProofVerify=false"
            };
            arguments.AddRange(properties.Select(static property =>
                "-p:" + property.Name + "=" + property.Value));
            return RunDotNetAsync(arguments);
        }

        internal async Task<IReadOnlyDictionary<string, string>>
            EvaluatePropertiesAsync(params string[] propertyNames)
        {
            var result = await RunDotNetAsync([
                "msbuild",
                ProjectPath,
                "--nologo",
                "-getProperty:" + string.Join(';', propertyNames)
            ]);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.Output);
            }
            using var document = JsonDocument.Parse(result.Output);
            return propertyNames.ToDictionary(
                static name => name,
                name => document.RootElement
                    .GetProperty("Properties")
                    .GetProperty(name)
                    .GetString() ?? string.Empty,
                StringComparer.Ordinal);
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
                ("DefaultItemExcludesInProjectFolder",
                    "obj-*/**"),
                ("SharpProofFeatures", features),
                ("SharpProofVerifyRequestFile", RequestPath),
                ("SharpProofVerifyResultFile", ResultPath),
                ("SharpProofCompilerManifestFile", CompilerManifestPath));
        }

        internal Task<BuildResult> RunVerificationTargetAsync(
            params (string Name, string Value)[] properties)
        {
            return RunVerificationTargetCoreAsync(
                Guid.NewGuid().ToString("N"),
                properties);
        }

        internal Task<BuildResult> RunVerificationTargetWithInvocationIdAsync(
            string invocationId,
            params (string Name, string Value)[] properties)
        {
            return RunVerificationTargetCoreAsync(invocationId, properties);
        }

        private Task<BuildResult> RunVerificationTargetCoreAsync(
            string invocationId,
            (string Name, string Value)[] properties)
        {
            var arguments = new List<string> {
                "msbuild",
                ProjectPath,
                "/t:_SharpProofVerifyCore",
                "/nologo",
                "/nodeReuse:false",
                "-p:Configuration=Release",
                "-p:TargetFramework=net8.0",
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
                "-p:_SharpProofInvocationId=" + invocationId
            };
            arguments.AddRange(properties.Select(static property =>
                "-p:" + property.Name + "=" + property.Value));
            return RunDotNetAsync(arguments);
        }

        internal Task<BuildResult> RunCleanupTargetAsync(
            string invocationId,
            string? invocationDirectory = null)
        {
            var arguments = new List<string> {
                "msbuild",
                ProjectPath,
                "/t:_SharpProofCleanupInvocation",
                "/nologo",
                "/nodeReuse:false",
                "-p:Configuration=Release",
                "-p:TargetFramework=net8.0",
                "-p:_SharpProofInvocationId=" + invocationId
            };
            if (!string.IsNullOrEmpty(invocationDirectory))
            {
                arguments.Add(
                    "-p:_SharpProofInvocationDirectory=" + invocationDirectory);
            }
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
            var startInfo = ProcessRunner.CreateStartInfo(
                _root,
                "dotnet",
                arguments);
            startInfo.Environment["SharedCompilationId"] =
                s_sharedCompilationServerId;
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
            TestRepository.DeleteOwnedTemporaryDirectory(
                _root,
                "SharpProof.Package.Test");
        }

        private static string CreateProjectXml(
            IEnumerable<(string Name, string Value)> properties)
        {
            var repository = TestRepository.FindRoot();
            var nativeZ3 = SecurityElement.Escape(
                ContainerContract.ResolveZ3LibraryRequired());
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
                    "SharpProof.Verifier",
                    "buildTransitive",
                    "SharpProof.Verifier.props"));
            var testConfiguration = new DirectoryInfo(
                Path.GetDirectoryName(
                    typeof(WorkerMsBuildIntegrationTests).Assembly.Location)!)
                .Parent?.Name ??
                throw new InvalidOperationException(
                    "The test build configuration was not found.");
            var analyzerDirectory = SecurityElement.Escape(Path.Combine(
                repository,
                "SharpProof.Analyzer",
                "bin",
                testConfiguration,
                "netstandard2.0"));
            var generatorDirectory = SecurityElement.Escape(Path.Combine(
                repository,
                "SharpProof.ContractForGenerator",
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
                    "SharpProof.Verifier",
                    "buildTransitive",
                    "SharpProof.Verifier.targets"));
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
                    testConfiguration, "net9.0",
                    "SharpProof.BuildTasks.dll"));
            var configuredProperties = string.Join(
                Environment.NewLine,
                properties.Select(static property =>
                    "    <" + property.Name + ">" +
                    SecurityElement.Escape(property.Value) +
                    "</" + property.Name + ">"));
            var nativeZ3Path = string.Empty;
            if (OperatingSystem.IsLinux() &&
                RuntimeInformation.ProcessArchitecture == Architecture.X64 &&
                string.Equals(
                    Environment.GetEnvironmentVariable("SHARPPROOF_CONTAINER"),
                    "1",
                    StringComparison.Ordinal))
            {
                nativeZ3Path = SecurityElement.Escape(
                    ContainerContract.ResolveZ3LibraryRequired());
            }
            var nativeZ3Property = string.IsNullOrEmpty(nativeZ3Path)
                ? string.Empty
                : "    <_SharpProofPackageNativeZ3Path>" + nativeZ3Path +
                    "</_SharpProofPackageNativeZ3Path>" +
                    Environment.NewLine;
            return
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="{props}" />
                  <Import Project="{verifierProps}" />
                  <PropertyGroup>
                    <SharpProofAnalyzerDirectory>{analyzerDirectory}</SharpProofAnalyzerDirectory>
                    <_SharpProofTestContractForGeneratorPath>{generatorDirectory}/SharpProof.ContractForGenerator.dll</_SharpProofTestContractForGeneratorPath>
                    <_SharpProofSharedDirectory>{collectorDirectory}</_SharpProofSharedDirectory>
                    <SharpProofCollectorDirectory>{collectorDirectory}</SharpProofCollectorDirectory>
                    <SharpProofCompilerCollectorPath>{collectorDirectory}/SharpProof.CompilerCollector.dll</SharpProofCompilerCollectorPath>
                    <LangVersion>12.0</LangVersion>
                    <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
                    <SharpProofWorkerPath>{worker}</SharpProofWorkerPath>
                    <SharpProofLauncherPath>{launcher}</SharpProofLauncherPath>
                    <_SharpProofTestWorkerProtocolPath>{protocol}</_SharpProofTestWorkerProtocolPath>
                    <_SharpProofTestBuildTasksPath>{buildTasks}</_SharpProofTestBuildTasksPath>
                    <_SharpProofPackageWorkerPath>{worker}</_SharpProofPackageWorkerPath>
                    <_SharpProofPackageWorkerPath Condition="'$(_SharpProofTestWorkerPath)' != ''">$([System.IO.Path]::GetFullPath('$(_SharpProofTestWorkerPath)'))</_SharpProofPackageWorkerPath>
                    <_SharpProofPackageLauncherPath>{launcher}</_SharpProofPackageLauncherPath>
                    <_SharpProofPackageWorkerProtocolPath>{protocol}</_SharpProofPackageWorkerProtocolPath>
                    <_SharpProofPackageBuildTasksPath>{buildTasks}</_SharpProofPackageBuildTasksPath>
                {nativeZ3Property}{configuredProperties}
                    <TargetFramework Condition="'$(TargetFrameworks)' == '' and '$(TargetFramework)' == ''">net8.0</TargetFramework>
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
                        CachePath="$(_SharpProofActiveCacheDirectory)" />
                  </Target>
                  <Target Name="_RemoveSharpProofAnalyzersForWorkerTargetTest"
                          BeforeTargets="CoreCompile">
                    <ItemGroup>
                      <Analyzer Remove="$(_SharpProofAnalyzerPath);$(_SharpProofContractForGeneratorPath)" />
                    </ItemGroup>
                  </Target>
                </Project>
                """;
        }

    }

    private readonly record struct BuildResult(
        int ExitCode,
        string Output);
}
