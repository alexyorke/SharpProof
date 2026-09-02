using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ReleaseVersionAuthorityTests
{
    [TestCase("canonical", true)]
    [TestCase("foreign-matching", false)]
    [TestCase("case-only-prerelease", false)]
    [TestCase("mixed-package", false)]
    [TestCase("stale-manifest", false)]
    [TestCase("stale-sbom", false)]
    [TestCase("stale-plan", false)]
    public async Task ReleaseVersionProjectionIsOwnedByReleaseProps(
        string mutation,
        bool expectedSuccess)
    {
        var result = await RunFixtureAsync(mutation);
        Assert.That(
            result.ExitCode == 0,
            Is.EqualTo(expectedSuccess),
            result.Output);
    }

    [Test]
    public async Task EveryReleaseEntryPointUsesTheSharedVersionAuthority()
    {
        var root = TestRepository.FindRoot();
        foreach (var relative in new[]
                 {
                     "scripts/New-SharpProofReleaseEvidence.ps1",
                     "scripts/Test-SharpProofReleaseArtifacts.ps1",
                     "scripts/Publish-SharpProofRelease.ps1",
                     "scripts/Invoke-SharpProofReleaseContainer.ps1"
                 })
        {
            var text = await File.ReadAllTextAsync(Path.Combine(
                root,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(
                text,
                Does.Contain("Get-SharpProofReleaseVersion"),
                relative);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunFixtureAsync(
        string mutation)
    {
        var root = TestRepository.FindRoot();
        var info = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(Path.Combine(
            root, "scripts", "Test-SharpProofReleaseVersionAuthorityFixtures.ps1"));
        info.ArgumentList.Add("-Mutation");
        info.ArgumentList.Add(mutation);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + Environment.NewLine + await error);
    }

}
