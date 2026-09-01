using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ReleaseConfigurationScriptTests
{
    [Test]
    public async Task EffectiveReleaseRefSetsMustEqualTheContract()
    {
        var script = Path.Combine(
            TestRepository.FindRoot(),
            "scripts",
            "Test-SharpProofReleaseConfigurationFixtures.ps1");
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);

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
