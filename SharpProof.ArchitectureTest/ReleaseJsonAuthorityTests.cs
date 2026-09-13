using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ReleaseJsonAuthorityTests
{
    [Test]
    public async Task ReleaseJsonFixturesRejectNoncanonicalStructures()
    {
        var root = TestRepository.FindRoot();
        var process = await ArchitectureRepository.RunScriptAsync(
            root, "Test-SharpProofReleaseJsonFixtures.ps1");
        Assert.That(process.ExitCode, Is.Zero, process.CombinedOutput);
        using var result = JsonDocument.Parse(process.Output);
        Assert.That(result.RootElement.GetProperty("passed").GetInt32(),
            Is.EqualTo(result.RootElement.GetProperty("total").GetInt32()));
        Assert.That(result.RootElement.GetProperty("total").GetInt32(),
            Is.GreaterThanOrEqualTo(10));
    }

    [Test]
    public async Task EveryReleaseConsumerUsesTheSharedStrictJsonAuthority()
    {
        var root = TestRepository.FindRoot();
        foreach (var path in new[]
        {
            "scripts/New-SharpProofReleaseEvidence.ps1",
            "scripts/Test-SharpProofReleaseArtifacts.ps1",
            "scripts/Publish-SharpProofRelease.ps1"
        })
        {
            var text = await File.ReadAllTextAsync(Path.Combine(
                root, path.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(text, Does.Contain("SharpProof.ReleaseJson.ps1"), path);
            Assert.That(text, Does.Contain("Read-SharpProofCanonicalReleaseJson"), path);
        }
    }

    [Test]
    public async Task SharedSdkPolicyReaderPreservesJsonShape()
    {
        using var temporary = new TempDirectory("SharpProof.SdkPolicy-");
        var policyPath = Path.Combine(temporary.FullName, "global.json");
        await File.WriteAllTextAsync(
            policyPath,
            """
            {
              "sdk": {
                "version": "9.0.316",
                "rollForward": "disable",
                "nested": { "enabled": true }
              },
              "extra": "retained"
            }
            """);

        var result = await RunSdkPolicyReaderAsync(
            temporary.FullName,
            policyPath);
        Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);

        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        var sdk = root.GetProperty("sdk");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sdk.GetProperty("version").GetString(),
                Is.EqualTo("9.0.316"));
            Assert.That(sdk.GetProperty("rollForward").GetString(),
                Is.EqualTo("disable"));
            Assert.That(sdk.GetProperty("nested").GetProperty("enabled")
                .GetBoolean(), Is.True);
            Assert.That(root.GetProperty("extra").GetString(),
                Is.EqualTo("retained"));
        }
    }

    [Test]
    public async Task SharedSdkPolicyReaderPreservesJsonValueTypes()
    {
        using var temporary = new TempDirectory("SharpProof.SdkPolicy-");
        var policyPath = Path.Combine(temporary.FullName, "global.json");
        await File.WriteAllTextAsync(
            policyPath,
            """
            {
              "sdk": {
                "version": 9.0,
                "rollForward": "disable"
              }
            }
            """);

        var result = await RunSdkPolicyReaderAsync(
            temporary.FullName,
            policyPath);
        Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);

        using var document = JsonDocument.Parse(result.Output);
        var version = document.RootElement
            .GetProperty("sdk")
            .GetProperty("version");
        Assert.That(version.ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(version.GetDouble(), Is.EqualTo(9.0));
    }

    [TestCase("malformed")]
    [TestCase("missing")]
    public async Task SharedSdkPolicyReaderPropagatesInputFailures(
        string fixture)
    {
        using var temporary = new TempDirectory("SharpProof.SdkPolicy-");
        var policyPath = Path.Combine(temporary.FullName, "global.json");
        if (fixture == "malformed")
        {
            await File.WriteAllTextAsync(policyPath, "{");
        }

        var result = await RunSdkPolicyReaderAsync(
            temporary.FullName,
            policyPath);
        Assert.That(result.ExitCode, Is.Not.Zero, result.CombinedOutput);
        Assert.That(result.CombinedOutput, Is.Not.Empty);
    }

    [Test]
    public async Task SdkPolicyConsumersUseTheSharedDeserializer()
    {
        var root = TestRepository.FindRoot();
        foreach (var path in new[]
        {
            "scripts/Publish-SharpProofRelease.ps1",
            "scripts/Test-SharpProofContainerContract.ps1"
        })
        {
            var text = await File.ReadAllTextAsync(Path.Combine(
                root, path.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(text, Does.Contain("Read-SharpProofSdkPolicy.ps1"), path);
            Assert.That(text, Does.Contain("Read-SharpProofSdkPolicy"), path);
        }
    }

    private static async Task<ProcessRunnerResult> RunSdkPolicyReaderAsync(
        string workingDirectory,
        string policyPath)
    {
        var root = TestRepository.FindRoot();
        var helperPath = Path.Combine(
            root, "scripts", "Read-SharpProofSdkPolicy.ps1");
        var harnessPath = Path.Combine(
            workingDirectory, "Read-Sdk-Policy.ps1");
        var script = string.Join(
            Environment.NewLine,
            new[]
            {
                "Set-StrictMode -Version Latest",
                "$ErrorActionPreference = 'Stop'",
                ". " + QuotePowerShellLiteral(helperPath),
                "$policy = Read-SharpProofSdkPolicy -Path " +
                    QuotePowerShellLiteral(policyPath),
                "$policy | ConvertTo-Json -Depth 10 -Compress"
            });
        await File.WriteAllTextAsync(harnessPath, script);
        return await ArchitectureRepository.RunProcessAsync(
            workingDirectory,
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            harnessPath);
    }

    private static string QuotePowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

}
