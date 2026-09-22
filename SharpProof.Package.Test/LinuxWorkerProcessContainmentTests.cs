using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class LinuxWorkerProcessContainmentTests
{
    [Test]
    [NonParallelizable]
    [Platform("Linux")]
    public void TerminationHandlerDescendantsAreStoppedBeforeReturning()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            Assert.Ignore("The verifier process boundary is supported on Linux x64.");
        }

        using var temporary = new TempDirectory("sharpproof-worker-containment-");
        var marker = Path.Combine(temporary.FullName, "descendant.pid");
        const string script =
            "trap 'sleep 30 & echo $! > \"$1\"; exit 0' TERM; " +
            "while :; do :; done";

        using var worker = LinuxWorkerProcess.Start(
            "/bin/sh",
            ["-c", script, "worker", marker],
            temporary.FullName);
        var completion = worker.WaitForExit(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(1));

        int descendantId;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(completion.Kind, Is.EqualTo(LinuxWorkerCompletionKind.TimedOut));
            Assert.That(completion.ExitCode, Is.EqualTo(124));
            Assert.That(
                File.Exists(marker),
                Is.True,
                "The termination handler did not create its child marker.");
        }

        descendantId = int.Parse(
            File.ReadAllText(marker).Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
        try
        {
            Assert.That(
                IsRunning(descendantId),
                Is.False,
                "A child created by the termination handler escaped cleanup.");
        }
        finally
        {
            if (IsRunning(descendantId))
            {
                using var escaped = Process.GetProcessById(descendantId);
                escaped.Kill(entireProcessTree: true);
                escaped.WaitForExit();
            }
        }
    }

    private static bool IsRunning(int processId)
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
}
