using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ProcessFixtureRunnerTests
{
    [Test]
    public async Task DrainsBothRedirectedStreamsConcurrently()
    {
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(
            "1..100000 | ForEach-Object { " +
            "[Console]::Error.WriteLine('stderr-filler') }; " +
            "[Console]::Write('stdout-sentinel')");

        var result = await ProcessFixtureRunner.RunAsync(
            start,
            TimeSpan.FromSeconds(10));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TimedOut, Is.False);
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("stdout-sentinel"));
            Assert.That(result.StandardError.Length, Is.GreaterThan(100_000));
        }
    }

    [Test]
    public async Task TimeoutKillsTheFixtureProcessTree()
    {
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("Start-Sleep -Seconds 30");

        var result = await ProcessFixtureRunner.RunAsync(
            start,
            TimeSpan.FromMilliseconds(100));

        Assert.That(result.TimedOut, Is.True);
    }
}
