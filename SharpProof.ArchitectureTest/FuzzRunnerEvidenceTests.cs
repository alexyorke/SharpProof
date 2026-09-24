using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class FuzzRunnerEvidenceTests
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromMinutes(2);

    [Test]
    public async Task FuzzRunnerEvidenceUsesStrictSchemaFourDecoderAndVersionedCampaignSummary()
    {
        var root = TestRepository.FindRoot();
        var start = ProcessRunner.CreateStartInfo(root, "pwsh",
            ["-NoLogo", "-NoProfile", "-File",
                Path.Combine(root, "scripts", "Test-SharpProofFuzzRunnerResult.ps1")]);
        var result = await RunAsync(start);
        Assert.That(
            result.ExitCode,
            Is.Zero,
            result.Output + Environment.NewLine + result.Error);

        var campaign = await File.ReadAllTextAsync(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofFuzzCampaign.ps1"));
        var validator = await File.ReadAllTextAsync(Path.Combine(
            root,
            "scripts",
            "Assert-SharpProofFuzzRunnerResult.ps1"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(validator, Does.Contain("if ($schema -ne 4)"));
            Assert.That(campaign, Does.Contain("schemaVersion = 5"));
        }
    }

    [Test]
    public async Task FuzzCampaignEvidenceLifecycleIsFailClosedAndAtomic()
    {
        var root = TestRepository.FindRoot();
        var start = ProcessRunner.CreateStartInfo(root, "pwsh",
            ["-NoLogo", "-NoProfile", "-File",
                Path.Combine(root, "scripts", "Test-SharpProofFuzzEvidenceLifecycle.ps1")]);
        var result = await RunAsync(start);
        Assert.That(
            result.ExitCode,
            Is.Zero,
            result.Output + Environment.NewLine + result.Error);

        var campaign = await File.ReadAllTextAsync(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofFuzzCampaign.ps1"));
        var lifecycle = await File.ReadAllTextAsync(Path.Combine(
            root,
            "scripts",
            "SharpProof.FuzzEvidenceLifecycle.ps1"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                campaign,
                Does.Contain("Initialize-SharpProofFuzzEvidence"));
            Assert.That(
                campaign,
                Does.Contain("Complete-SharpProofFuzzEvidence"));
            Assert.That(
                lifecycle,
                Does.Contain("Publish-SharpProofFuzzEvidence"));
            Assert.That(
                campaign.IndexOf(
                    "Initialize-SharpProofFuzzEvidence",
                    StringComparison.Ordinal),
                Is.LessThan(campaign.IndexOf(
                    "retained-seeds.json",
                    StringComparison.Ordinal)));
        }
    }

    private static Task<ProcessRunnerResult> RunAsync(
        ProcessStartInfo start)
    {
        return ArchitectureRepository.RunProcessAsync(start, ScriptTimeout);
    }
}
