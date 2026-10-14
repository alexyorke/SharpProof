using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.Gates.Test;

[TestFixture]
[Platform("Linux")]
public sealed class OrdinaryGateProcessWorkflowTests
{
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
