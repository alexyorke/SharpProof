using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ReleaseChecksumAuthorityTests
{
    [Test]
    public async Task ReleaseBundleAuthorityGuardsEveryReleaseConsumerAndUpload()
    {
        var root = FindRepositoryRoot();
        foreach (var relativePath in new[]
        {
            "scripts/New-SharpProofReleaseEvidence.ps1",
            "scripts/Test-SharpProofReleaseArtifacts.ps1",
            "scripts/Publish-SharpProofRelease.ps1"
        })
        {
            var text = await File.ReadAllTextAsync(Path.Combine(
                root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(
                text,
                Does.Contain("Test-SharpProofReleaseBundleTopology"),
                relativePath);
        }

        var workflow = await File.ReadAllTextAsync(Path.Combine(
            root, ".github", "workflows", "package-consumers.yml"));
        var pack = workflow.IndexOf("tooling pack", StringComparison.Ordinal);
        var upload = workflow.IndexOf(
            "name: Upload exact NuGet artifacts", StringComparison.Ordinal);
        Assert.That(pack, Is.GreaterThanOrEqualTo(0));
        Assert.That(upload, Is.GreaterThan(pack));

        var dispatcher = await File.ReadAllTextAsync(Path.Combine(
            root, "scripts", "Invoke-SharpProofContainer.ps1"));
        var generation = dispatcher.IndexOf(
            "New-SharpProofReleaseEvidence.ps1", StringComparison.Ordinal);
        var validation = dispatcher.IndexOf(
            "Test-SharpProofReleaseArtifacts.ps1", StringComparison.Ordinal);
        Assert.That(validation, Is.GreaterThan(generation));
    }

    [Test]
    public async Task PackageArchiveCanonicalizationIsDeterministic()
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
            "Test-SharpProofPackageReproducibilityFixtures.ps1"));
        using var process = Process.Start(info)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(
            process.ExitCode,
            Is.Zero,
            output + Environment.NewLine + error);
    }

    [TestCase("canonical", true)]
    [TestCase("bom", false)]
    [TestCase("utf16le", false)]
    [TestCase("utf16be", false)]
    [TestCase("invalid-utf8", false)]
    [TestCase("crlf", false)]
    [TestCase("mixed", false)]
    [TestCase("cr", false)]
    [TestCase("missing-terminal", false)]
    [TestCase("double-terminal", false)]
    [TestCase("upper-digest", false)]
    [TestCase("separator", false)]
    [TestCase("spacing", false)]
    [TestCase("reordered", false)]
    [TestCase("extra", false)]
    [TestCase("missing", false)]
    [TestCase("duplicate", false)]
    [TestCase("bundle-canonical", true)]
    [TestCase("bundle-extra", false)]
    [TestCase("bundle-nested-extra", false)]
    [TestCase("bundle-alternate-sbom", false)]
    [TestCase("bundle-symlink", false)]
    [TestCase("bundle-empty", false)]
    [TestCase("bundle-missing-manifest", false)]
    [TestCase("bundle-missing-checksums", false)]
    [TestCase("bundle-missing-package", false)]
    [TestCase("bundle-case-collision", false)]
    [TestCase("bundle-empty-directory", false)]
    [TestCase("bundle-hardlink-alias", false)]
    [TestCase("bundle-atomic-replacement", true)]
    [TestCase("bundle-atomic-failure-cleanup", true)]
    public async Task ChecksumBytesAreExact(string mutation, bool expectedSuccess)
    {
        var result = await RunFixtureAsync(mutation);
        Assert.That(
            result.ExitCode == 0,
            Is.EqualTo(expectedSuccess),
            result.Output);
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
            root, "scripts", "Test-SharpProofReleaseChecksumFixtures.ps1"));
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
