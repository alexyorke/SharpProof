using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.Gates.Test;

[TestFixture]
[Platform("Linux")]
public sealed class OrdinaryGateProcessWorkflowTests
{
    [Test]
    public void ActualClrRefusalToTerminateCallingProcessIsHandled()
    {
        using var process = Process.GetCurrentProcess();
        var processId = process.Id;
        TestContext.Out.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Assert.That(process.HasExited, Is.False);

        // The CLR rejects its own process tree before issuing termination signals.
        var refusal = Assert.Throws<InvalidOperationException>(
            (Action)(() => process.Kill(entireProcessTree: true)));
        Assert.That(refusal, Is.Not.Null);
        TestContext.Out.WriteLine($"Actual CLR refusal: {refusal!.Message}");

        Assert.DoesNotThrow((Action)(() => GateProcess.KillTree(process)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(process.Id, Is.EqualTo(processId));
            Assert.That(process.Id, Is.EqualTo(Environment.ProcessId));
            Assert.That(process.HasExited, Is.False);
        }
    }

    [Test]
    public async Task CompletedOrdinaryProcessNeedsNoTermination()
    {
        var start = GateProcess.CreateCaptured("dotnet", RepositoryLayout.FindRoot());
        start.ArgumentList.Add("--version");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        GateProcess.KillTree(process);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(process.HasExited, Is.True);
            Assert.That(process.ExitCode, Is.Zero);
            Assert.That(await output, Is.Not.Empty);
            Assert.That(await error, Is.Empty);
        }
    }
}
