using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ReleaseTagValidationTests
{
    [Test]
    public async Task ReleaseTagAuthorityRejectsEveryNonExactIdentity()
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(
            TestRepository.FindRoot(),
            "scripts",
            "Test-SharpProofReleaseTagFixtures.ps1"));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(
            process.ExitCode,
            Is.Zero,
            (await output) + Environment.NewLine + await error);
    }

}
