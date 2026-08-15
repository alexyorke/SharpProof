using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class PublicationPlanIdentityTests
{
    [TestCase("canonical", true)]
    [TestCase("two-bundle", true)]
    [TestCase("changed-symbol", false)]
    [TestCase("stale-manifest", false)]
    [TestCase("stale-sbom", false)]
    [TestCase("stale-checksums", false)]
    [TestCase("missing-identity", false)]
    [TestCase("duplicate-identity", false)]
    public async Task ReplayRehashesEveryImmutablePlanInput(
        string mutation,
        bool expectedValid)
    {
        var result = await RunFixtureAsync(mutation);
        Assert.That(result.ExitCode == 0, Is.EqualTo(expectedValid), result.Output);
    }

    [Test]
    public async Task PublisherValidatesCurrentIdentitiesBeforeAndAfterWritingPlan()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "scripts", "Publish-SharpProofRelease.ps1"));
        var create = script.IndexOf(
            "New-SharpProofPublicationPlanIdentities", StringComparison.Ordinal);
        var validate = script.IndexOf(
            "Test-SharpProofPublicationPlanIdentity -Plan $plan", StringComparison.Ordinal);
        var write = script.IndexOf(
            "Write-PublicationPlan `", validate, StringComparison.Ordinal);
        var replay = script.IndexOf(
            "Test-SharpProofPublicationPlan.ps1", write, StringComparison.Ordinal);
        Assert.That(create, Is.GreaterThanOrEqualTo(0));
        Assert.That(validate, Is.GreaterThan(create));
        Assert.That(write, Is.GreaterThan(validate));
        Assert.That(replay, Is.GreaterThan(write));
    }

    private static async Task<(int ExitCode, string Output)> RunFixtureAsync(
        string mutation)
    {
        var root = FindRepositoryRoot();
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
            root, "scripts", "Test-SharpProofPublicationPlanIdentityFixtures.ps1"));
        info.ArgumentList.Add("-Mutation");
        info.ArgumentList.Add(mutation);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + Environment.NewLine + await error);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
