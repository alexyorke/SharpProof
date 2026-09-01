using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class FuzzRunnerEvidenceProcessSafetyTests
{
    [Test]
    public void EvidenceScriptsUseBoundedConcurrentProcessIO()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "SharpProof.ArchitectureTest",
            "FuzzRunnerEvidenceTests.cs"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(source, Does.Not.Contain("ReadToEnd();"));
            Assert.That(source, Does.Not.Contain("WaitForExit();"));
            Assert.That(source, Does.Contain("ReadToEndAsync()"));
            Assert.That(source, Does.Contain("WaitForExitAsync("));
            Assert.That(source, Does.Contain("CancelAfter("));
            Assert.That(
                source,
                Does.Contain("Kill(entireProcessTree: true)"));
        }
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(
            TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "SharpProof.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not find repository root.");
    }
}
