using System.Diagnostics;
using NUnit.Framework;
using SharpProof.BuildTasks;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class VerifierProcessSupervisorBug202Tests
{
    [Test]
    [Platform("Linux")]
    public void RecycledSupervisorPidIsNotScannedAfterCleanupDeadline()
    {
        var opened = false;
        var cleanup = VerifierProcessSupervisor.StopDescendants(
            Environment.ProcessId,
            0,
            _ =>
            {
                opened = true;
                return -1;
            },
            (descriptor, signal) => -1,
            supervisorPidFd: 777);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(opened, Is.False);
            Assert.That(cleanup.Complete, Is.True);
        }
    }

    [Test]
    public void CleanupRetriesAreBounded()
    {
        var attempts = 0;
        var cleanup = VerifierProcessSupervisor.RetryCleanup(
            new VerifierProcessSupervisor.DescendantStopResult(
                HadDescendants: true,
                Complete: false),
            777,
            _ =>
            {
                attempts++;
                return new VerifierProcessSupervisor.DescendantStopResult(
                    HadDescendants: true,
                    Complete: false);
            },
            _ => { });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(attempts, Is.EqualTo(8));
            Assert.That(cleanup.HadDescendants, Is.True);
            Assert.That(cleanup.Complete, Is.False);
        }
    }

    [Test]
    [Platform("Linux")]
    public void RecycledSupervisorPidIsNotScanned()
    {
        using var descendant = Process.Start("/bin/sleep", "10");
        Assert.That(descendant, Is.Not.Null);
        try
        {
            var opened = false;
            var cleanup = VerifierProcessSupervisor.StopDescendants(
                Environment.ProcessId,
                100,
                _ =>
                {
                    opened = true;
                    return -1;
                },
                (descriptor, signal) =>
                    descriptor == 777 && signal == 0 ? -1 : 0,
                supervisorPidFd: 777);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(opened, Is.False);
                Assert.That(cleanup.Complete, Is.True);
            }
        }
        finally
        {
            if (descendant is { HasExited: false })
            {
                descendant.Kill(entireProcessTree: true);
                descendant.WaitForExit();
            }
        }
    }
}
