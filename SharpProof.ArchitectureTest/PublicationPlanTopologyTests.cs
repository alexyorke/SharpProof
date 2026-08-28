using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class PublicationPlanTopologyTests
{
    [TestCase("valid-disjoint", true)]
    [TestCase("existing-output", true)]
    [TestCase("main-package", false)]
    [TestCase("symbol-package", false)]
    [TestCase("manifest", false)]
    [TestCase("sbom", false)]
    [TestCase("checksums", false)]
    [TestCase("fixture-input", false)]
    [TestCase("relative-dot-alias", false)]
    [TestCase("absolute-alias", false)]
    [TestCase("symlink-alias", false)]
    [TestCase("hardlink-alias", false)]
    [TestCase("reserved-name", false)]
    [TestCase("package-subdirectory", false)]
    [TestCase("fixture-subdirectory", false)]
    [TestCase("writer-failure", false)]
    [TestCase("post-write-mutation", false)]
    public async Task PlanOutputCannotAliasOrInvalidateCertifiedInputs(
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
    public async Task PublisherUsesAtomicTopologyAuthorityBeforeValidation()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "scripts", "Publish-SharpProofRelease.ps1"));
        var resolve = script.IndexOf(
            "Resolve-SharpProofPublicationPlanOutput",
            StringComparison.Ordinal);
        var validate = script.IndexOf(
            "Get-ValidatedRelease",
            resolve + 1,
            StringComparison.Ordinal);
        Assert.That(resolve, Is.GreaterThanOrEqualTo(0));
        Assert.That(validate, Is.GreaterThan(resolve));
        Assert.That(script, Does.Contain("Write-SharpProofPublicationPlanAtomic"));
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
            root, "scripts", "Test-SharpProofPublicationPlanTopologyFixtures.ps1"));
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
