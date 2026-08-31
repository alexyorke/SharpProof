using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class SbomReleaseIdentityTests
{
    private static readonly TimeSpan FixtureProcessTimeout =
        TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessTerminationTimeout =
        TimeSpan.FromSeconds(5);

    [Test]
    public void SbomFixtureProcessHasAnInternalWallTimeLimit()
    {
        var info = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("Start-Sleep -Seconds 30");

        var stopwatch = Stopwatch.StartNew();
        var exception = Assert.ThrowsAsync<TimeoutException>((Func<Task>)(
            async () => _ = await InvokeProcessRunnerAsync(
                info,
                TimeSpan.FromMilliseconds(250))));
        stopwatch.Stop();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("pwsh"));
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(TimeSpan.FromSeconds(5)));
        }
    }

    [TestCase("canonical", true)]
    [TestCase("stale-commit", false)]
    [TestCase("stale-timestamp", false)]
    [TestCase("equivalent-offset-timestamp", false)]
    [TestCase("equivalent-fractional-timestamp", false)]
    [TestCase("malformed-namespace", false)]
    [TestCase("wrong-name", false)]
    [TestCase("wrong-version", false)]
    [TestCase("creator-scalar", false)]
    [TestCase("creator-null", false)]
    [TestCase("creator-object", false)]
    [TestCase("creator-extra", false)]
    [TestCase("creation-extra", false)]
    [TestCase("creation-case", false)]
    public async Task SbomReleaseIdentityIsExact(
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
    public async Task EverySbomAuthorityConsumerUsesTheSharedValidator()
    {
        var root = FindRepositoryRoot();
        var generator = await File.ReadAllTextAsync(Path.Combine(
            root,
            "scripts",
            "New-SharpProofReleaseEvidence.ps1"));
        Assert.That(
            CountOrdinal(generator, "Test-SharpProofSbomReleaseIdentity"),
            Is.EqualTo(2),
            "Generation must validate both generated and supplied SBOMs.");
        Assert.That(
            generator,
            Does.Contain("Get-SharpProofSbomReleaseIdentity"));

        foreach (var relative in new[]
                 {
                     "scripts/Test-SharpProofReleaseArtifacts.ps1",
                     "scripts/Publish-SharpProofRelease.ps1"
                 })
        {
            var text = await File.ReadAllTextAsync(Path.Combine(
                root,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(
                CountOrdinal(text, "Test-SharpProofSbomReleaseIdentity"),
                Is.EqualTo(1),
                relative);
        }
    }

    private static int CountOrdinal(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(
                   needle,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }
        return count;
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
            root,
            "scripts",
            "Test-SharpProofSbomReleaseIdentityFixtures.ps1"));
        info.ArgumentList.Add("-Mutation");
        info.ArgumentList.Add(mutation);
        return await RunProcessAsync(info, FixtureProcessTimeout);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        ProcessStartInfo info,
        TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The fixture process timeout must be positive.");
        }

        using var process = Process.Start(info) ??
            throw new InvalidOperationException(
                $"Could not start '{info.FileName}'.");
        var output = process.StandardOutput.ReadToEndAsync(
            CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(
            CancellationToken.None);
        using var boundary = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(boundary.Token);
            var streams = await Task.WhenAll(output, error)
                .WaitAsync(boundary.Token);
            return (
                process.ExitCode,
                streams[0] + Environment.NewLine + streams[1]);
        }
        catch (OperationCanceledException exception)
            when (boundary.IsCancellationRequested)
        {
            await TerminateProcessAsync(process, output, error);
            throw new TimeoutException(
                $"'{info.FileName}' did not exit within " +
                $"{timeout.TotalSeconds:0.###} seconds.",
                exception);
        }
        catch
        {
            await TerminateProcessAsync(process, output, error);
            throw;
        }
    }

    private static async Task TerminateProcessAsync(
        Process process,
        Task<string> output,
        Task<string> error)
    {
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
            }
        }

        using var cleanup = new CancellationTokenSource(
            ProcessTerminationTimeout);
        try
        {
            await process.WaitForExitAsync(cleanup.Token);
            _ = await Task.WhenAll(output, error)
                .WaitAsync(cleanup.Token);
        }
        catch (OperationCanceledException)
            when (cleanup.IsCancellationRequested)
        {
        }
    }

    private static Task<(int ExitCode, string Output)> InvokeProcessRunnerAsync(
        ProcessStartInfo info,
        TimeSpan timeout)
    {
        var method = typeof(SbomReleaseIdentityTests).GetMethod(
            "RunProcessAsync",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static,
            binder: null,
            [typeof(ProcessStartInfo), typeof(TimeSpan)],
            modifiers: null) ??
            throw new InvalidOperationException(
                "Could not find the timeout-aware SBOM fixture runner.");
        return (Task<(int ExitCode, string Output)>)method.Invoke(
            null,
            [info, timeout])!;
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
