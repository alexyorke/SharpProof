using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class SbomReleaseIdentityTests
{
    [TestCase("canonical", true)]
    [TestCase("stale-commit", false)]
    [TestCase("stale-timestamp", false)]
    [TestCase("equivalent-offset-timestamp", false)]
    [TestCase("equivalent-fractional-timestamp", false)]
    [TestCase("malformed-namespace", false)]
    [TestCase("wrong-name", false)]
    [TestCase("wrong-version", false)]
    [TestCase("creator-scalar", false)]
    [TestCase("creator-null", false)]
    [TestCase("creator-object", false)]
    [TestCase("creator-extra", false)]
    [TestCase("creation-extra", false)]
    [TestCase("creation-case", false)]
    [Category("GitBound")]
    public async Task SbomReleaseIdentityIsExact(
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
    public async Task EverySbomAuthorityConsumerUsesTheSharedValidator()
    {
        var root = FindRepositoryRoot();
        var generator = await File.ReadAllTextAsync(Path.Combine(
            root,
            "scripts",
            "New-SharpProofReleaseEvidence.ps1"));
        Assert.That(
            CountOrdinal(generator, "Test-SharpProofSbomReleaseIdentity"),
            Is.EqualTo(2),
            "Generation must validate both generated and supplied SBOMs.");
        Assert.That(
            generator,
            Does.Contain("Get-SharpProofSbomReleaseIdentity"));

        foreach (var relative in new[]
                 {
                     "scripts/Test-SharpProofReleaseArtifacts.ps1",
                     "scripts/Publish-SharpProofRelease.ps1"
                 })
        {
            var text = await File.ReadAllTextAsync(Path.Combine(
                root,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(
                CountOrdinal(text, "Test-SharpProofSbomReleaseIdentity"),
                Is.EqualTo(1),
                relative);
        }
    }

    private static int CountOrdinal(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(
                   needle,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }
        return count;
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
            root,
            "scripts",
            "Test-SharpProofSbomReleaseIdentityFixtures.ps1"));
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
