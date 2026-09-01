using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ReleaseAuthorityClosureTests
{
    [TestCase("Test-SharpProofReleaseAuthorityClosure.ps1")]
    [TestCase("Test-SharpProofReleaseAuthorityClosureFixtures.ps1")]
    public async Task ReleaseAuthorityClosureIsIndependentAndMutationDiscriminating(
        string scriptName)
    {
        var root = TestRepository.FindRoot();
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(root, "scripts", scriptName));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero,
            (await output) + Environment.NewLine + await error);
    }

}
