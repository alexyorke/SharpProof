using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class PilotAuthorityTests
{
    [Test]
    public async Task PilotPackagesAndOutputsUseExactCandidateAuthority()
    {
        var root = TestRepository.FindRoot();
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-File",
                     Path.Combine(root, "scripts", "Test-SharpProofPilotAuthorityFixtures.ps1") })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero,
            (await output) + Environment.NewLine + await error);
    }

}
