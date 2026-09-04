using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class FuzzRunnerEvidenceProcessSafetyTests
{
    [Test]
    public void EvidenceScriptsUseBoundedConcurrentProcessIO()
    {
        var root = TestRepository.FindRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "SharpProof.ArchitectureTest",
            "FuzzRunnerEvidenceTests.cs"));
        var runner = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "testing",
            "ProcessRunner.cs"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(source, Does.Not.Contain("ReadToEnd();"));
            Assert.That(source, Does.Not.Contain("WaitForExit();"));
            Assert.That(source, Does.Contain("ScriptTimeout"));
            Assert.That(runner, Does.Contain("ReadToEndAsync("));
            Assert.That(runner, Does.Contain("WaitForExitAsync("));
            Assert.That(runner, Does.Contain("CancellationToken"));
            Assert.That(
                runner,
                Does.Contain("Kill(entireProcessTree: true)"));
        }
    }

}
