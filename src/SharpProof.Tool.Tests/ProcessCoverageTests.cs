using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using NUnit.Framework;
using SharpProof.Tool;

namespace SharpProof.Tool.Test;

[TestFixture]
public sealed class ProcessCoverageTests
{
    [Test]
    public void ToolProcessRejectsInvalidBoundariesBeforeStartingAProcess()
    {
        Assert.ThrowsAsync<ArgumentNullException>(new Func<Task>(async () =>
            _ = await ToolProcess.RunAsync(null!, Path.GetTempPath(), TestContext.CurrentContext.CancellationToken)));
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () =>
            _ = await ToolProcess.RunAsync(string.Empty, Path.GetTempPath(), TestContext.CurrentContext.CancellationToken)));
        Assert.ThrowsAsync<ArgumentNullException>(new Func<Task>(async () =>
            _ = await ToolProcess.RunAsync("must-not-start", null!, TestContext.CurrentContext.CancellationToken)));
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () =>
            _ = await ToolProcess.RunAsync("must-not-start", string.Empty, TestContext.CurrentContext.CancellationToken)));
        Assert.ThrowsAsync<ArgumentNullException>(new Func<Task>(async () =>
            _ = await ToolProcess.RunAsync("must-not-start", Path.GetTempPath(), TestContext.CurrentContext.CancellationToken, null!)));
    }

    [Test]
    public void UnavailableExecutablePreservesTheLaunchFailure()
    {
        var command = Path.Combine(Path.GetTempPath(), "sharpproof-absent-" + Guid.NewGuid().ToString("N"));
        Assert.ThrowsAsync<Win32Exception>(new Func<Task>(async () =>
            _ = await ToolProcess.RunAsync(command, Path.GetTempPath(), TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public async Task NonzeroExitPreservesStdoutWithoutReturningPrivateDiagnostics()
    {
        RequireLinux();
        var result = await ToolProcess.RunAsync("/bin/sh", Path.GetTempPath(), TestContext.CurrentContext.CancellationToken,
            "-c", "printf 'ordinary-output'; printf 'synthetic-private-stderr' >&2; exit 7");
        Assert.That(result.ExitCode, Is.EqualTo(7));
        Assert.That(result.Output, Is.EqualTo("ordinary-output").And.Not.Contain("synthetic-private"));
    }

    [Test]
    public async Task EmptyStdoutAndStderrAreValidProcessResults()
    {
        RequireLinux();
        var result = await ToolProcess.RunAsync("/bin/sh", Path.GetTempPath(), TestContext.CurrentContext.CancellationToken, "-c", "exit 0");
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Output, Is.Empty);
    }

    [Test]
    public async Task ArgumentsKeepWhitespaceAndShellMetacharactersLiteral()
    {
        RequireLinux();
        var result = await ToolProcess.RunAsync("/bin/sh", Path.GetTempPath(), TestContext.CurrentContext.CancellationToken,
            "-c", "printf '%s\\n' \"$1\" \"$2\"", "argument-check", "value with spaces", "$(literal);\"quoted\"");
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Output, Is.EqualTo("value with spaces\n$(literal);\"quoted\"\n"));
    }

    [Test]
    public async Task OutputAtTheExactLimitIsReturnedWithoutTruncation()
    {
        RequireLinux();
        var result = await ToolProcess.RunAsync("/bin/sh", Path.GetTempPath(), TestContext.CurrentContext.CancellationToken,
            "-c", "printf '%16777216s' x");
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Output, Has.Length.EqualTo(ToolProcess.MaximumOutputCharacters));
        Assert.That(result.Output[^1], Is.EqualTo('x'));
    }

    [Test]
    public void ToolDeadlineStopsTheChildAndDoesNotExposeStderr()
    {
        RequireLinux();
        // Exercise the production 30-second deadline rather than replacing it with caller cancellation.
        var failure = Assert.ThrowsAsync<IOException>(new Func<Task>(async () =>
            _ = await ToolProcess.RunAsync("/bin/sh", Path.GetTempPath(), TestContext.CurrentContext.CancellationToken,
                "-c", "printf 'synthetic-private-timeout-detail' >&2; exec sleep 60")));
        Assert.That(failure!.Message, Does.Contain("did not finish within 30 seconds").And.Not.Contain("synthetic-private"));
    }

    [Test]
    public async Task ActualSdkDiscoveryUsesTheSelectedDotnetHost()
    {
        var result = await SdkProbe.ReadAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(Version.TryParse(result.Trim().Split('-')[0], out var version), Is.True);
        Assert.That(version, Is.GreaterThanOrEqualTo(new Version(9, 0, 300)));
    }

    [Test]
    public async Task CleanupAcceptsAnAlreadyExitedProcess()
    {
        RequireLinux();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("exit 0");
        Assert.That(process.Start(), Is.True);
        await process.WaitForExitAsync(TestContext.CurrentContext.CancellationToken);
        ToolProcess.TerminateAndJoin(process);
        Assert.That(process.HasExited, Is.True);
    }

    [Test]
    public void CleanupRejectsAnAbsentProcess()
    {
        Assert.Throws<ArgumentNullException>(new Action(() => ToolProcess.TerminateAndJoin(null!)));
    }

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux())
        { Assert.Ignore("The process workflow runs in the canonical Linux container."); }
    }
}
