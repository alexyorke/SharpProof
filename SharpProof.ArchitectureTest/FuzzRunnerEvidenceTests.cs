using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
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
            Assert.That(campaign, Does.Contain("schemaVersion = 3"));
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                campaign,
                Does.Contain("Initialize-SharpProofFuzzEvidence"));
            Assert.That(
                campaign,
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

    [Test]
    public void FuzzCampaignRequiresCleanExactCommitSource()
    {
        var campaign = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "scripts",
            "Invoke-SharpProofFuzzCampaign.ps1"));
        var sourceCheck = campaign.IndexOf(
            "status --porcelain=v1",
            StringComparison.Ordinal);
        var initialization = campaign.IndexOf(
            "Initialize-SharpProofFuzzEvidence",
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sourceCheck, Is.GreaterThanOrEqualTo(0));
            Assert.That(initialization, Is.GreaterThan(sourceCheck));
            Assert.That(campaign, Does.Contain("--untracked-files=all"));
            Assert.That(
                campaign,
                Does.Contain("requires clean exact-commit source"));
        }
    }

    [Test]
    public void FuzzCoverageThresholdIsSynchronizedAcrossAuthorities()
    {
        var root = RepositoryRoot();
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "eng",
            "acceptance",
            "contract.json")));
        var contractCases = contract.RootElement
            .GetProperty("fuzz")
            .GetProperty("pullRequestCases")
            .GetInt32();
        using var retained = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "eng",
            "fuzz",
            "retained-seeds.json")));
        var retainedCases = retained.RootElement
            .GetProperty("casesPerSeed")
            .GetInt32();
        var options = File.ReadAllText(Path.Combine(
            root,
            "Tools",
            "SharpProof.Fuzz",
            "FuzzOptions.cs"));
        var defaultMatches = Regex.Matches(
            options,
            @"DefaultCases\s*=\s*(\d+)",
            RegexOptions.CultureInvariant);
        var validator = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Assert-SharpProofFuzzRunnerResult.ps1"));
        var validatorMatches = Regex.Matches(
            validator,
            @"\$cases\s*-ge\s*(\d+)",
            RegexOptions.CultureInvariant);
        var runner = File.ReadAllText(Path.Combine(
            root,
            "Tools",
            "SharpProof.Fuzz",
            "FuzzRunner.cs"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(defaultMatches, Has.Count.EqualTo(1));
            Assert.That(validatorMatches, Has.Count.EqualTo(1));
            Assert.That(
                int.Parse(defaultMatches[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(contractCases));
            Assert.That(
                retainedCases,
                Is.EqualTo(contractCases));
            Assert.That(
                int.Parse(validatorMatches[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(contractCases));
            Assert.That(
                runner,
                Does.Contain("PullRequestCoverageBudget = FuzzOptions.DefaultCases"));
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
