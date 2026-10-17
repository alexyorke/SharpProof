using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
[NonParallelizable]
[Platform("Linux")]
[SupportedOSPlatform("linux")]
public sealed class OrdinaryCanceledProcessCleanupTests
{
    [Test]
    public async Task CapturedGateProcessReapsChildAfterCancellation()
    {
        using var directory = new TempDirectory("SharpProof-gate-process-cancel-");
        var executable = await CreateBlockingExecutableAsync(directory.FullName);
        var start = GateProcess.CreateCaptured(executable, directory.FullName);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.CurrentContext.CancellationToken);
        var operation = GateProcess.RunCapturedAsync(start, cancellation.Token);
        var processId = -1;
        try
        {
            processId = await WaitForChildAsync(directory.FullName);
            await cancellation.CancelAsync();
            Assert.CatchAsync<OperationCanceledException>((Func<Task>)(async () => { _ = await operation; }));
            Assert.That(await ChildExitedAsync(processId), Is.True);
        }
        finally
        {
            await cancellation.CancelAsync();
            try
            {
                _ = await operation;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                await StopChildIfRunningAsync(processId);
            }
        }
    }

    [Test]
    public async Task GitBlobReaderReapsChildAfterCancellation()
    {
        using var directory = new TempDirectory("SharpProof-git-blob-cancel-");
        var executable = await CreateBlockingExecutableAsync(directory.FullName);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.CurrentContext.CancellationToken);
        var operation = OpenSourceCorpusImporter.ReadGitBlobAsync(
            directory.FullName, "commit", "ordinary.cs", cancellation.Token, executable);
        var processId = -1;
        try
        {
            processId = await WaitForChildAsync(directory.FullName);
            await cancellation.CancelAsync();
            Assert.CatchAsync<OperationCanceledException>((Func<Task>)(async () => { _ = await operation; }));
            Assert.That(await ChildExitedAsync(processId), Is.True);
        }
        finally
        {
            await cancellation.CancelAsync();
            try
            {
                _ = await operation;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                await StopChildIfRunningAsync(processId);
            }
        }
    }

    private static async Task<string> CreateBlockingExecutableAsync(string directory)
    {
        System.ArgumentNullException.ThrowIfNull(directory);
        var executable = Path.Combine(directory, "blocking-git.sh");
        await File.WriteAllTextAsync(executable,
            "#!/bin/sh\nprintf '%s\\n' \"$$\" > child.pid\nwhile :; do sleep 1; done\n",
            TestContext.CurrentContext.CancellationToken);
        File.SetUnixFileMode(executable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return executable;
    }

    private static async Task<int> WaitForChildAsync(string directory)
    {
        var marker = Path.Combine(directory, "child.pid");
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (File.Exists(marker))
            {
                var contents = await File.ReadAllTextAsync(marker,
                    TestContext.CurrentContext.CancellationToken);
                if (int.TryParse(contents.Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var processId))
                {
                    return processId;
                }
            }
            await Task.Delay(10, TestContext.CurrentContext.CancellationToken);
        }
        Assert.Fail("The blocking child did not publish its PID.");
        return -1;
    }

    private static async Task<bool> ChildExitedAsync(int processId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (!ChildExists(processId))
            {
                return true;
            }
            await Task.Delay(10, TestContext.CurrentContext.CancellationToken);
        }
        return !ChildExists(processId);
    }

    private static bool ChildExists(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task StopChildIfRunningAsync(int processId)
    {
        if (processId <= 0 || !ChildExists(processId))
        {
            return;
        }
        using var process = Process.GetProcessById(processId);
        GateProcess.KillTree(process);
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(cleanup.Token);
    }
}
