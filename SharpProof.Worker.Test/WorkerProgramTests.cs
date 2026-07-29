using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerProgramTests
{
    [Test]
    public async Task DirectInvocationRequiresContainmentStartBarrier()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Worker.Test",
            Guid.NewGuid().ToString("N"));
        var resultPath = Path.Combine(directory, "result.json");

        var exitCode = await Program.Main([
            "verify",
            "--request",
            Path.Combine(directory, "request.json"),
            "--result",
            resultPath
        ]);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(File.Exists(resultPath), Is.False);
    }

    [Test]
    public void NativeBackendLoadFailuresAreClassified()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Program.IsBackendUnavailable(new DllNotFoundException()),
                Is.True);
            Assert.That(
                Program.IsBackendUnavailable(
                    new TypeInitializationException(
                        "Z3",
                        new EntryPointNotFoundException())),
                Is.True);
            Assert.That(
                Program.IsBackendUnavailable(new InvalidOperationException()),
                Is.False);
        }
    }
}
