using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
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
            var output = arguments[0] switch
            {
                "msbuild" => JsonSerializer.Serialize(new
                {
                    Properties,
                    Items = new { PackageReference = new[] { new { Identity = "SharpProof" } } }
                }),
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
