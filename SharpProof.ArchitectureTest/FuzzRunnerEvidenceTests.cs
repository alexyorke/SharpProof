using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class FuzzRunnerEvidenceTests
{
    [Test]
    public void FuzzRunnerEvidenceUsesStrictSchemaFourDecoder()
    {
        var root = RepositoryRoot();
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(
            root,
            "scripts",
            "Test-SharpProofFuzzRunnerResult.ps1"));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.That(
            process.ExitCode,
            Is.Zero,
            output + Environment.NewLine + error);

        var campaign = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofFuzzCampaign.ps1"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                campaign,
                Does.Contain("Assert-SharpProofFuzzRunnerResult"));
            Assert.That(campaign, Does.Contain("schemaVersion = 4"));
        }
    }

    [Test]
    public void FuzzCampaignEvidenceLifecycleIsFailClosedAndAtomic()
    {
        var root = RepositoryRoot();
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(
            root,
            "scripts",
            "Test-SharpProofFuzzEvidenceLifecycle.ps1"));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.That(
            process.ExitCode,
            Is.Zero,
            output + Environment.NewLine + error);

        var campaign = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofFuzzCampaign.ps1"));
        var lifecycle = File.ReadAllText(Path.Combine(
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

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SharpProof.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
