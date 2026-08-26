using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class StandaloneGateEvidenceTests
{
    [Test]
    public void StandaloneGateDecoderRejectsUnauthenticatedEvidence()
    {
        var root = RepositoryRoot();
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(
            root,
            "scripts",
            "Test-SharpProofStandaloneGateEvidence.ps1"));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.That(
            process.ExitCode,
            Is.Zero,
            output + Environment.NewLine + error);
    }

    [Test]
    public void StandaloneGateProducerIsFreshBuildAndIdentityBound()
    {
        var root = RepositoryRoot();
        var evidence = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofGateEvidence.ps1"));
        var producer = File.ReadAllText(Path.Combine(
            root,
            "SharpProof.Gates",
            "Program.cs"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(evidence, Does.Contain("-t:Rebuild"));
            Assert.That(evidence, Does.Not.Contain("'--no-build'"));
            Assert.That(
                evidence,
                Does.Contain("Assert-SharpProofStandaloneGateResult"));
            Assert.That(
                evidence,
                Does.Contain("The gate result failed validation:"));
            Assert.That(
                evidence,
                Does.Not.Contain("The gate result was not valid JSON:"));
            Assert.That(evidence, Does.Contain("SharpProofSourceCommit"));
            Assert.That(evidence, Does.Contain("GetMetadataReader"));
            Assert.That(producer, Does.Contain("CreateStandaloneEnvelope"));
            Assert.That(producer, Does.Contain("AcceptanceContractSha256"));
            Assert.That(producer, Does.Contain("ModuleVersionId"));
            Assert.That(producer, Does.Contain("PdbSha256"));
        }
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SharpProof.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
