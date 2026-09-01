using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class FuzzRunnerEvidenceTests
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromMinutes(2);

    [Test]
    public async Task FuzzRunnerEvidenceUsesStrictSchemaFourDecoder()
    {
        var root = RepositoryRoot();
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(
            root,
            "scripts",
            "Test-SharpProofFuzzRunnerResult.ps1"));
        var result = await RunAsync(start);
        Assert.That(
            result.ExitCode,
            Is.Zero,
            result.Output + Environment.NewLine + result.Error);

        var campaign = await File.ReadAllTextAsync(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofFuzzCampaign.ps1"));
        using (Assert.EnterMultipleScope())
        {
            var decoderCall = campaign.IndexOf(
                "Assert-SharpProofFuzzRunnerResult `",
                StringComparison.Ordinal);
            var decoderPath = campaign.IndexOf(
                "-Path $standardOutput",
                StringComparison.Ordinal);
            Assert.That(decoderCall, Is.GreaterThanOrEqualTo(0));
            Assert.That(decoderPath, Is.GreaterThan(decoderCall));
            Assert.That(campaign, Does.Contain("schemaVersion = 4"));
        }
    }

    [Test]
    public async Task FuzzCampaignEvidenceLifecycleIsFailClosedAndAtomic()
    {
        var root = RepositoryRoot();
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(
            root,
            "scripts",
            "Test-SharpProofFuzzEvidenceLifecycle.ps1"));
        var result = await RunAsync(start);
        Assert.That(
            result.ExitCode,
            Is.Zero,
            result.Output + Environment.NewLine + result.Error);

        var campaign = await File.ReadAllTextAsync(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofFuzzCampaign.ps1"));
        var lifecycle = await File.ReadAllTextAsync(Path.Combine(
            root,
            "scripts",
            "SharpProof.FuzzEvidenceLifecycle.ps1"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                campaign,
                Does.Contain("Initialize-SharpProofFuzzEvidence"));
            Assert.That(
                campaign,
                Does.Contain("Complete-SharpProofFuzzEvidence"));
            Assert.That(
                lifecycle,
                Does.Contain("Publish-SharpProofFuzzEvidence"));
            Assert.That(
                campaign.IndexOf(
                    "Initialize-SharpProofFuzzEvidence",
                    StringComparison.Ordinal),
                Is.LessThan(campaign.IndexOf(
                    "retained-seeds.json",
                    StringComparison.Ordinal)));
        }
    }

    private static async Task<ProcessResult> RunAsync(
        ProcessStartInfo start)
    {
        using var process = Process.Start(start) ??
            throw new InvalidOperationException(
                $"Could not start '{start.FileName}'.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(ScriptTimeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
            return new ProcessResult(
                process.ExitCode,
                await output.WaitAsync(cancellation.Token),
                await error.WaitAsync(cancellation.Token));
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
            }

            throw new TimeoutException(
                $"'{start.FileName}' did not exit within " +
                $"{ScriptTimeout.TotalSeconds:N0} seconds.");
        }
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SharpProof.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);
}
