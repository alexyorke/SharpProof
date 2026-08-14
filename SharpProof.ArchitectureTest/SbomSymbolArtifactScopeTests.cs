using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class SbomSymbolArtifactScopeTests
{
    [TestCase("canonical", true)]
    [TestCase("missing-symbol", false)]
    [TestCase("extra-symbol", false)]
    [TestCase("swapped-role", false)]
    [TestCase("symbol-checksum", false)]
    [TestCase("fabricated-symbol-row", false)]
    [TestCase("broad-workflow-glob", false)]
    [TestCase("symbol-workflow-glob", false)]
    [TestCase("purl-substituted", false)]
    [TestCase("purl-duplicate", false)]
    [TestCase("purl-omitted", false)]
    [TestCase("purl-encoded", false)]
    [TestCase("purl-case", false)]
    [TestCase("purl-extra-field", false)]
    [TestCase("third-party-purl", false)]
    public async Task SymbolPackagesAreProvenanceArtifactsButNotSbomSubjects(
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
    public async Task CheckedInWorkflowUsesTheMainPackageOnlySbomSubject()
    {
        var result = await RunFixtureAsync("checked-in-workflow");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
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
            root, "scripts", "Test-SharpProofSbomArtifactScopeFixtures.ps1"));
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
