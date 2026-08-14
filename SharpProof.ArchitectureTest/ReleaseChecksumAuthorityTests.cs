using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ReleaseChecksumAuthorityTests
{
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
