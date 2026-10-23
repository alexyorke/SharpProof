using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Tool;

namespace SharpProof.Tool.Test;

[TestFixture]
public sealed class ProjectDoctorTests
{
    [TestCase("advisory", "false")]
    [TestCase("strict", "true")]
    public async Task SingleFacadeReferencePassesWithoutDirectDependencyReferences(string profile, string verify)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(verify);
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofProfile"] = profile;
        fixture.Properties["SharpProofVerify"] = verify;
        Assert.That(await fixture.RunAsync(null), Is.Zero, fixture.Output.ToString());
        Assert.That(fixture.Commands, Has.Count.EqualTo(1));
        Assert.That(fixture.Commands[0], Does.StartWith("dotnet msbuild ").And.Not.Contain("-target:").And.Not.Contain("-restore"));
        Assert.That(fixture.Output.ToString(), Does.Contain("PASS Resolved package compatibility")
            .And.Not.Contain("private").And.Not.Contain("PASS Docker"));
    }

    [TestCase("", "false", "FAIL Profile")]
    [TestCase("unknown", "false", "FAIL Profile")]
    [TestCase("strict", "", "FAIL Strict verification")]
    [TestCase("strict", "false", "FAIL Strict verification")]
    [TestCase("advisory", "perhaps", "FAIL Verification setting")]
    public async Task InvalidEvaluatedActivationNeverPasses(string profile, string verify, string expected)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(verify);
        ArgumentNullException.ThrowIfNull(expected);
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofProfile"] = profile;
        fixture.Properties["SharpProofVerify"] = verify;
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain(expected));
    }

    [TestCase("advisory", "false", "9.0.300", 0)]
    [TestCase("advisory", "false", "9.0.299", 1)]
    [TestCase("strict", "true", "9.0.400", 1)]
    [TestCase("strict", "true", "10.0.100", 0)]
    public async Task CompilerRequirementUsesEvaluatedSdkAndVerification(string profile, string verify, string sdk, int expected)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(verify);
        ArgumentNullException.ThrowIfNull(sdk);
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofProfile"] = profile;
        fixture.Properties["SharpProofVerify"] = verify;
        fixture.Properties["NETCoreSdkVersion"] = sdk;
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(expected), fixture.Output.ToString());
    }

    [TestCase("DEBUG; SHARPPROOF_CONTRACTS ;TRACE")]
    [TestCase("DEBUG,SHARPPROOF_CONTRACTS,TRACE")]
    public async Task ReservedContractSymbolRejectsWhitespaceAndCommaSeparators(string constants)
    {
        ArgumentNullException.ThrowIfNull(constants);
        using var fixture = new DoctorFixture();
        fixture.Properties["DefineConstants"] = constants;
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Static contract configuration"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ResolvedGraphRequiresEveryPackageAndOneVersion(bool mismatch)
    {
        using var fixture = new DoctorFixture();
        if (mismatch)
        {
            fixture.Libraries.Remove("SharpProof.Verifier/1.2.3");
            fixture.Libraries.Add("SharpProof.Verifier/2.0.0", new { type = "package" });
        }
        else
        {
            fixture.Libraries.Remove("SharpProof.Attributes/1.2.3");
        }
        fixture.WriteAssets();
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Resolved package compatibility"));
    }

    [Test]
    public async Task ContainerModeRequiresVerificationToBeActive()
    {
        using var fixture = new DoctorFixture();
        Assert.That(await fixture.RunAsync("container"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Verification activation"));
    }

    [Test]
    public async Task MalformedDigestCannotTriggerImageInspection()
    {
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofVerify"] = "true";
        fixture.Image = "ghcr.io/alexyorke/sharpproof@sha256:";
        Assert.That(await fixture.RunAsync("container"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Pinned canonical image"));
        Assert.That(fixture.Commands.Any(static command => { ArgumentNullException.ThrowIfNull(command); return command.Contains("image inspect", StringComparison.Ordinal); }), Is.False);
    }

    [TestCase("1.2.3", "amd64", 0)]
    [TestCase("2.0.0", "amd64", 1)]
    [TestCase("1.2.3", "arm64", 1)]
    public async Task ImageMustMatchResolvedPackageVersionAndPlatform(string version, string architecture, int expected)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(architecture);
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofVerify"] = "true";
        fixture.ImageVersion = version;
        fixture.ImageArchitecture = architecture;
        Assert.That(await fixture.RunAsync("container"), Is.EqualTo(expected), fixture.Output.ToString());
        Assert.That(fixture.Commands.Any(static command => { ArgumentNullException.ThrowIfNull(command); return command.Contains("image inspect", StringComparison.Ordinal); }), Is.True);
        Assert.That(fixture.Commands.Any(static command => { ArgumentNullException.ThrowIfNull(command); return command.Contains(" pull ", StringComparison.Ordinal); }), Is.False);
    }

    [Test]
    public async Task MissingPackageEvidenceCannotMatchNullImageVersion()
    {
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofVerify"] = "true";
        fixture.ImageVersion = null;
        File.Delete(fixture.Properties["ProjectAssetsFile"]);
        Assert.That(await fixture.RunAsync("container"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Restored package evidence").And.Contain("FAIL Image/package compatibility"));
    }

    [Test]
    public async Task ContainerMarkerDoesNotClaimContractQualification()
    {
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofVerify"] = "true";
        Assert.That(await fixture.RunAsync("container", containerMarkerAvailable: true), Is.Zero, fixture.Output.ToString());
        Assert.That(fixture.Output.ToString(), Does.Contain("WARN Container marker available").And.Not.Contain("PASS Canonical container"));
        Assert.That(fixture.Commands, Has.Count.EqualTo(1));
    }

    [Test]
    public void AlreadyCanceledProcessDoesNotStartAnExecutable()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () =>
            _ = await ToolProcess.RunAsync("must-not-start", Path.GetTempPath(), cancellation.Token)));
    }

    [Test]
    public async Task ProcessPreservesJsonBeyondSdkProbeLimitWhileDrainingDiagnostics()
    {
        if (!OperatingSystem.IsLinux())
        { Assert.Ignore("The process workflow runs in the canonical Linux container."); }
        var result = await ToolProcess.RunAsync("/bin/sh", Path.GetTempPath(), TestContext.CurrentContext.CancellationToken,
            "-c", "printf '{\"value\":\"'; printf '%8192s' x; printf '\"}'; printf '%262144s' y >&2");
        using var document = JsonDocument.Parse(result.Output);
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(document.RootElement.GetProperty("value").GetString(), Has.Length.EqualTo(8192));
    }

    [Test]
    public void OutputOverflowStopsTheChildWithoutWaitingForItsDeadline()
    {
        if (!OperatingSystem.IsLinux())
        { Assert.Ignore("The process workflow runs in the canonical Linux container."); }
        var failure = Assert.ThrowsAsync<IOException>(new Func<Task>(async () =>
            _ = await ToolProcess.RunAsync("/bin/sh", Path.GetTempPath(), TestContext.CurrentContext.CancellationToken,
                "-c", "printf '%16777217s' x; sleep 60")));
        Assert.That(failure!.Message, Does.Contain("output exceeded"));
    }

    [Test]
    public async Task CancellationJoinsTheOwnedChild()
    {
        if (!OperatingSystem.IsLinux())
        { Assert.Ignore("The process workflow runs in the canonical Linux container."); }
        using var fixture = new DoctorFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CurrentContext.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(1));
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () =>
            _ = await ToolProcess.RunAsync("/bin/sh", fixture.DirectoryPath, cancellation.Token,
                "-c", "printf '%s' \"$$\" > process.pid; exec sleep 60")));
        var processId = int.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.DirectoryPath, "process.pid")), CultureInfo.InvariantCulture);
        var alive = false;
        try
        {
            using var process = Process.GetProcessById(processId);
            alive = !process.HasExited;
        }
        catch (ArgumentException) { }
        Assert.That(alive, Is.False);
    }

    [TestCase("effects", 0)]
    [TestCase("contracts", 0)]
    [TestCase("all", 0)]
    [TestCase("invalid", 1)]
    public async Task FeatureSelectionFollowsTheConsumerContract(string features, int expected)
    {
        ArgumentNullException.ThrowIfNull(features);
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofFeatures"] = features;
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(expected), fixture.Output.ToString());
    }

    [TestCase("advisory", 0)]
    [TestCase("warn-on-unknown", 0)]
    [TestCase("require-proven", 0)]
    [TestCase("invalid", 1)]
    public async Task ActiveVerificationValidatesPolicyWithoutInvokingNativeCode(string policy, int expected)
    {
        ArgumentNullException.ThrowIfNull(policy);
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofVerify"] = "true";
        fixture.Properties["SharpProofVerifyPolicy"] = policy;
        Assert.That(await fixture.RunAsync(null), Is.EqualTo(expected), fixture.Output.ToString());
        Assert.That(fixture.Commands, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DisabledProfileDoesNotRequireAnAnalyzerSdkOrNativeHost()
    {
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofProfile"] = " OFF ";
        fixture.Properties["SharpProofVerify"] = "true";
        fixture.Properties["NETCoreSdkVersion"] = "not-an-sdk-version";
        Assert.That(await fixture.RunAsync(null), Is.Zero, fixture.Output.ToString());
        Assert.That(fixture.Output.ToString(), Does.Not.Contain("Native host").And.Not.Contain("Verification policy"));
        Assert.That(fixture.Commands, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DisabledProfileCannotActivateContainerProof()
    {
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofProfile"] = "off";
        fixture.Properties["SharpProofVerify"] = "true";
        Assert.That(await fixture.RunAsync("container"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Verification activation"));
    }

    [Test]
    public async Task MalformedSdkVersionFailsTheCompilerCheck()
    {
        using var fixture = new DoctorFixture();
        fixture.Properties["NETCoreSdkVersion"] = "not-an-sdk-version";
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Compiler SDK"));
    }

    [Test]
    public async Task CompatibilityVerifierReferenceCanSupplyItsTransitiveAttributes()
    {
        using var fixture = new DoctorFixture();
        fixture.ReferenceIds.Clear();
        fixture.ReferenceIds.Add("SharpProof.Verifier");
        fixture.Libraries.Remove("SharpProof/1.2.3");
        fixture.WriteAssets();
        Assert.That(await fixture.RunAsync("portable"), Is.Zero, fixture.Output.ToString());
    }

    [Test]
    public async Task AttributesAloneDoNotClaimAnalyzerInstallation()
    {
        using var fixture = new DoctorFixture();
        fixture.ReferenceIds.Clear();
        fixture.ReferenceIds.Add("SharpProof.Attributes");
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL SharpProof package"));
    }

    [TestCase("SharpProof/1.2.3")]
    [TestCase("SharpProof.Verifier/1.2.3")]
    public async Task DirectFacadeRequiresItsOwnAndVerifierRestoreEvidence(string removed)
    {
        ArgumentNullException.ThrowIfNull(removed);
        using var fixture = new DoctorFixture();
        fixture.Libraries.Remove(removed);
        fixture.WriteAssets();
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Resolved package compatibility"));
    }

    [Test]
    public async Task GraphIgnoresUnrelatedAndProjectLibrariesAndMatchesPackageIdsWithoutCase()
    {
        using var fixture = new DoctorFixture();
        fixture.Libraries.Clear();
        fixture.Libraries.Add("sharpproof/1.2.3", new { type = "package" });
        fixture.Libraries.Add("sharpproof.attributes/1.2.3", new { type = "package" });
        fixture.Libraries.Add("sharpproof.verifier/1.2.3", new { type = "package" });
        fixture.Libraries.Add("SharpProof.Verifier/9.9.9", new { type = "project" });
        fixture.Libraries.Add("Unrelated/2.0.0", new { type = "package" });
        fixture.Libraries.Add("NoVersionSeparator", new { type = "project" });
        fixture.WriteAssets();
        Assert.That(await fixture.RunAsync("portable"), Is.Zero, fixture.Output.ToString());
    }

    [Test]
    public async Task BlankResolvedVersionIsNotCompatibleEvidence()
    {
        using var fixture = new DoctorFixture();
        fixture.Libraries.Clear();
        fixture.Libraries.Add("SharpProof/", new { type = "package" });
        fixture.Libraries.Add("SharpProof.Attributes/", new { type = "package" });
        fixture.Libraries.Add("SharpProof.Verifier/", new { type = "package" });
        fixture.WriteAssets();
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Resolved package compatibility"));
    }

    [Test]
    public async Task RelativeAssetsAreResolvedFromTheProjectAndMultitargetingRemainsAWarning()
    {
        using var fixture = new DoctorFixture();
        fixture.Properties["ProjectAssetsFile"] = "project.assets.json";
        fixture.Properties["TargetFrameworks"] = "net9.0;net10.0";
        Assert.That(await fixture.RunAsync("portable"), Is.Zero, fixture.Output.ToString());
        Assert.That(fixture.Output.ToString(), Does.Contain("WARN Multitarget project").And.Contain("PASS Resolved package compatibility"));
    }

    [Test]
    public async Task NullOptionalPropertyValuesRemainEmptyInsteadOfCrashing()
    {
        using var fixture = new DoctorFixture();
        var evaluation = JsonNode.Parse(fixture.EvaluationJson)!.AsObject();
        evaluation["Properties"]!["DefineConstants"] = null;
        evaluation["Properties"]!["TargetFrameworks"] = null;
        fixture.Responses.Add("msbuild", (0, evaluation.ToJsonString()));
        Assert.That(await fixture.RunAsync("portable"), Is.Zero, fixture.Output.ToString());
    }

    [Test]
    public async Task EvaluationFailureDoesNotParseOrRevealItsDiagnosticOutput()
    {
        using var fixture = new DoctorFixture();
        fixture.Responses.Add("msbuild", (1, "synthetic-private-evaluation-detail"));
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Project evaluation").And.Not.Contain("synthetic-private"));
    }

    [TestCase("not-json")]
    [TestCase("{}")]
    [TestCase("{\"Properties\":[]}")]
    public async Task InvalidEvaluatedMetadataReportsAnInspectionFailure(string metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        using var fixture = new DoctorFixture();
        fixture.Responses.Add("msbuild", (0, metadata));
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Setup inspection"));
    }

    [TestCase("not-json")]
    [TestCase("{}")]
    [TestCase("{\"libraries\":[]}")]
    public async Task InvalidRestoreMetadataReportsAnInspectionFailure(string metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        using var fixture = new DoctorFixture();
        await File.WriteAllTextAsync(fixture.Properties["ProjectAssetsFile"], metadata, TestContext.CurrentContext.CancellationToken);
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Setup inspection"));
    }

    [TestCase("missing-executable")]
    [TestCase("io")]
    [TestCase("access")]
    [TestCase("state")]
    [TestCase("range")]
    [TestCase("index")]
    public async Task ExpectedInspectionFailuresDoNotExposeExceptionDetails(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        using var fixture = new DoctorFixture();
        const string detail = "synthetic-private-runner-detail";
        if (kind == "index")
        {
            var values = Array.Empty<string>();
            try
            { _ = values[kind.Length]; }
            catch (IndexOutOfRangeException exception)
            { fixture.RunnerFailure = exception; }
            Assert.That(fixture.RunnerFailure, Is.TypeOf<IndexOutOfRangeException>());
        }
        else
        {
            fixture.RunnerFailure = kind switch
            {
                "missing-executable" => new Win32Exception(detail),
                "io" => new IOException(detail),
                "access" => new UnauthorizedAccessException(detail),
                "state" => new InvalidOperationException(detail),
                "range" => new ArgumentOutOfRangeException(nameof(kind), detail),
                _ => throw new AssertionException("Unknown failure fixture.")
            };
        }
        Assert.That(await fixture.RunAsync("portable"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Setup inspection").And.Not.Contain(detail));
    }

    [TestCase(1, "unavailable")]
    [TestCase(0, "windows")]
    public async Task DockerMustBeAnAvailableLinuxEngine(int exitCode, string engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofVerify"] = "true";
        fixture.Responses.Add("info", (exitCode, engine));
        Assert.That(await fixture.RunAsync("container"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Docker Linux engine"));
    }

    [Test]
    public async Task FailedComposeConfigurationDoesNotInspectOrPullAnImage()
    {
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofVerify"] = "true";
        fixture.Responses.Add("compose", (1, "synthetic-private-compose-detail"));
        Assert.That(await fixture.RunAsync("container"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Consumer container configuration").And.Not.Contain("synthetic-private"));
        Assert.That(fixture.Commands, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task MissingLocalImageRequiresAnExplicitPullOutsideDoctor()
    {
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofVerify"] = "true";
        fixture.Responses.Add("image", (1, "synthetic-private-inspect-detail"));
        Assert.That(await fixture.RunAsync("container"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Local consumer image").And.Contain("Doctor does not pull images")
            .And.Not.Contain("synthetic-private"));
        Assert.That(fixture.Commands, Has.Count.EqualTo(4));
    }

    [TestCase("null-image")]
    [TestCase("wrong-platform")]
    [TestCase("missing-service")]
    public async Task UnusableComposeServiceNeverTriggersImageInspection(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofVerify"] = "true";
        var image = JsonSerializer.Serialize(fixture.Image);
        var metadata = kind switch
        {
            "null-image" => "{\"services\":{\"verify\":{\"image\":null,\"platform\":\"linux/amd64\"}}}",
            "wrong-platform" => "{\"services\":{\"verify\":{\"image\":" + image + ",\"platform\":\"linux/arm64\"}}}",
            "missing-service" => "{\"services\":{}}",
            _ => throw new AssertionException("Unknown compose fixture.")
        };
        fixture.Responses.Add("compose", (0, metadata));
        Assert.That(await fixture.RunAsync("container"), Is.EqualTo(1));
        Assert.That(fixture.Commands, Has.Count.EqualTo(3));
    }

    [TestCase("[]")]
    [TestCase("{}")]
    [TestCase("[{\"Config\":{\"Labels\":null},\"Os\":\"linux\",\"Architecture\":\"amd64\"}]")]
    [TestCase("[{\"Config\":{\"Labels\":{}},\"Os\":\"linux\",\"Architecture\":\"amd64\"}]")]
    [TestCase("[{\"Config\":{\"Labels\":{}},\"Os\":\"windows\",\"Architecture\":\"amd64\"}]")]
    public async Task IncompleteOrWrongPlatformImageMetadataCannotPass(string metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        using var fixture = new DoctorFixture();
        fixture.Properties["SharpProofVerify"] = "true";
        fixture.Responses.Add("image", (0, metadata));
        Assert.That(await fixture.RunAsync("container"), Is.EqualTo(1));
        Assert.That(fixture.Output.ToString(), Does.Not.Contain("PASS Image/package compatibility"));
    }

    [TestCase("", false)]
    [TestCase("ghcr.io/alexyorke/sharpproof:latest", false)]
    [TestCase("ghcr.io/otheryorke/sharpproof@sha256:", false)]
    [TestCase("ghcr.io/alexyorke/sharpproof@sha256:", true)]
    public void ConsumerImageRequiresTheExactRepositoryAndHexDigest(string prefix, bool expected)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        Assert.That(SetupDoctor.IsPinnedConsumerImage(prefix + new string('a', 64)), Is.EqualTo(expected));
    }

    [TestCase('A')]
    [TestCase('g')]
    [TestCase('/')]
    public void ConsumerImageRejectsNonCanonicalDigestCharacters(char character)
    {
        Assert.That(SetupDoctor.IsPinnedConsumerImage("ghcr.io/alexyorke/sharpproof@sha256:" + new string(character, 64)), Is.False);
    }

    [Test]
    public void ConsumerImageValidationRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(new Action(() => SetupDoctor.IsPinnedConsumerImage(null!)));
    }

    [Test]
    public void ProjectDoctorRejectsInvalidArgumentsBeforeStartingTheRunner()
    {
        using var fixture = new DoctorFixture();
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () => _ = await fixture.RunAsync("unsupported")));
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () => _ = await SetupDoctor.RunAsync(
            Path.Combine(fixture.DirectoryPath, "missing.csproj"), null, fixture.Output, TestContext.CurrentContext.CancellationToken)));
        var wrongExtension = Path.Combine(fixture.DirectoryPath, "consumer.txt");
        File.WriteAllText(wrongExtension, string.Empty);
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () => _ = await SetupDoctor.RunAsync(
            wrongExtension, null, fixture.Output, TestContext.CurrentContext.CancellationToken)));
        Assert.That(fixture.Commands, Is.Empty);
    }

    [Test]
    public void ProjectDoctorRejectsNullBoundaryArguments()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.ThrowsAsync<ArgumentNullException>(new Func<Task>(async () =>
            _ = await SetupDoctor.RunAsync(null!, null, output, TestContext.CurrentContext.CancellationToken)));
        Assert.ThrowsAsync<ArgumentNullException>(new Func<Task>(async () =>
            _ = await SetupDoctor.RunAsync("project.csproj", null, null!, TestContext.CurrentContext.CancellationToken)));
        Assert.ThrowsAsync<ArgumentNullException>(new Func<Task>(async () => _ = await SetupDoctor.RunAsync(
            "project.csproj", null, output, null!, false, TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void AlreadyCanceledProjectDoctorDoesNotInvokeTheRunner()
    {
        using var fixture = new DoctorFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () => _ = await SetupDoctor.RunAsync(
            Path.Combine(fixture.DirectoryPath, "Consumer.csproj"), null, fixture.Output,
            static (_, _, _, _) => throw new AssertionException("Runner must not start."), false, cancellation.Token)));
        Assert.That(fixture.Output.ToString(), Is.Empty);
    }

    [Test]
    public void RunnerCancellationIsNotConvertedToAnInspectionFailure()
    {
        using var fixture = new DoctorFixture();
        fixture.RunnerFailure = new OperationCanceledException(TestContext.CurrentContext.CancellationToken);
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () => _ = await fixture.RunAsync("portable")));
        Assert.That(fixture.Output.ToString(), Is.Empty);
    }

    private sealed class DoctorFixture : IDisposable
    {
        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "sharpproof-doctor-" + Guid.NewGuid().ToString("N"));
        internal Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, object> Libraries { get; } = new(StringComparer.Ordinal)
        {
            ["SharpProof/1.2.3"] = new { type = "package" },
            ["SharpProof.Attributes/1.2.3"] = new { type = "package" },
            ["SharpProof.Verifier/1.2.3"] = new { type = "package" }
        };
        internal StringWriter Output { get; } = new(CultureInfo.InvariantCulture);
        internal List<string> Commands { get; } = [];
        internal List<string> ReferenceIds { get; } = ["SharpProof"];
        internal Dictionary<string, (int ExitCode, string Output)> Responses { get; } = new(StringComparer.Ordinal);
        internal Exception? RunnerFailure { get; set; }
        internal string EvaluationJson => JsonSerializer.Serialize(new
        {
            Properties,
            Items = new
            {
                PackageReference = ReferenceIds.Select(static identity =>
            {
                ArgumentNullException.ThrowIfNull(identity);
                return new { Identity = identity };
            }).ToArray()
            }
        });
        internal string Image { get; set; } = "ghcr.io/alexyorke/sharpproof@sha256:" + new string('a', 64);
        internal string? ImageVersion { get; set; } = "1.2.3";
        internal string ImageArchitecture { get; set; } = "amd64";

        internal DoctorFixture()
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(Path.Combine(DirectoryPath, "Consumer.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Properties.Add("NETCoreSdkVersion", "10.0.402");
            Properties.Add("SharpProofProfile", "advisory");
            Properties.Add("SharpProofFeatures", "all");
            Properties.Add("SharpProofVerify", "false");
            Properties.Add("SharpProofVerifyPolicy", "require-proven");
            Properties.Add("DefineConstants", "DEBUG;TRACE");
            Properties.Add("ProjectAssetsFile", Path.Combine(DirectoryPath, "project.assets.json"));
            Properties.Add("TargetFramework", "net10.0");
            Properties.Add("TargetFrameworks", string.Empty);
            WriteAssets();
        }

        internal void WriteAssets()
        {
            File.WriteAllText(Properties["ProjectAssetsFile"], JsonSerializer.Serialize(new { libraries = Libraries }));
        }

        internal Task<int> RunAsync(string? verification, bool containerMarkerAvailable = false)
        {
            return SetupDoctor.RunAsync(Path.Combine(DirectoryPath, "Consumer.csproj"), verification, Output,
                RunProcessAsync, containerMarkerAvailable, TestContext.CurrentContext.CancellationToken);
        }

        private Task<(int ExitCode, string Output)> RunProcessAsync(string command, string directory,
            CancellationToken cancellationToken, params string[] arguments)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(directory);
            ArgumentNullException.ThrowIfNull(arguments);
            cancellationToken.ThrowIfCancellationRequested();
            Assert.That(directory, Is.EqualTo(DirectoryPath));
            Commands.Add(command + " " + string.Join(' ', arguments));
            if (RunnerFailure != null)
            { throw RunnerFailure; }
            if (Responses.TryGetValue(arguments[0], out var response))
            { return Task.FromResult(response); }
            var output = arguments[0] switch
            {
                "msbuild" => EvaluationJson,
                "info" => "linux\n",
                "compose" => JsonSerializer.Serialize(new { services = new { verify = new { image = Image, platform = "linux/amd64" } } }),
                "image" => JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        Os = "linux", Architecture = ImageArchitecture,
                        Config = new { Labels = new Dictionary<string, string?> { ["io.sharpproof.consumer.version"] = ImageVersion } }
                    }
                }),
                _ => throw new AssertionException("Unexpected process operation.")
            };
            return Task.FromResult((0, output));
        }

        public void Dispose()
        {
            Output.Dispose();
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
