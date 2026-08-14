using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class PublicationDestinationAuthorityTests
{
    [TestCase("registry-inherited", true)]
    [TestCase("registry-distinct", true)]
    [TestCase("targetless", true)]
    [TestCase("fixture", true)]
    [TestCase("http", false)]
    [TestCase("relative", false)]
    [TestCase("userinfo", false)]
    [TestCase("query", false)]
    [TestCase("fragment", false)]
    [TestCase("symbol-without-main", false)]
    [TestCase("fixture-uri-conflict", false)]
    [TestCase("missing-fixture", false)]
    [TestCase("changed-fixture", false)]
    [TestCase("removed-symbol-projection", false)]
    public async Task PublicationDestinationModesAreExactAndAuthenticated(
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
    public async Task PublisherProjectsBothDestinationsBeforePlanReturn()
    {
        var text = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "scripts", "Publish-SharpProofRelease.ps1"));
        Assert.That(text, Does.Contain("New-SharpProofPublicationDestinationAuthority"));
        Assert.That(text, Does.Contain("publicationDestination ="));
        Assert.That(text, Does.Not.Contain("source = if ("));
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
            root, "scripts", "Test-SharpProofPublicationDestinationFixtures.ps1"));
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
