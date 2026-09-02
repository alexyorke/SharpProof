using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ReleaseJsonAuthorityTests
{
    [Test]
    public async Task ReleaseJsonFixturesRejectNoncanonicalStructures()
    {
        var root = TestRepository.FindRoot();
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(
            root, "scripts", "Test-SharpProofReleaseJsonFixtures.ps1"));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await output;
        Assert.That(process.ExitCode, Is.Zero, stdout + Environment.NewLine + await error);
        using var result = JsonDocument.Parse(stdout);
        Assert.That(result.RootElement.GetProperty("passed").GetInt32(),
            Is.EqualTo(result.RootElement.GetProperty("total").GetInt32()));
        Assert.That(result.RootElement.GetProperty("total").GetInt32(),
            Is.GreaterThanOrEqualTo(10));
    }

    [Test]
    public async Task EveryReleaseConsumerUsesTheSharedStrictJsonAuthority()
    {
        var root = TestRepository.FindRoot();
        foreach (var path in new[]
        {
            "scripts/New-SharpProofReleaseEvidence.ps1",
            "scripts/Test-SharpProofReleaseArtifacts.ps1",
            "scripts/Publish-SharpProofRelease.ps1"
        })
        {
            var text = await File.ReadAllTextAsync(Path.Combine(
                root, path.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(text, Does.Contain("SharpProof.ReleaseJson.ps1"), path);
            Assert.That(text, Does.Contain("Read-SharpProofCanonicalReleaseJson"), path);
        }
    }

}
