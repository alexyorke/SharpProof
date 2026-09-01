using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class StandaloneGateEvidenceTests
{
    [Test]
    public void StandaloneGateDecoderRejectsUnauthenticatedEvidence()
    {
        var root = TestRepository.FindRoot();
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
        var root = TestRepository.FindRoot();
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
            Assert.That(evidence, Does.Contain("SharpProofSourceCommit"));
            Assert.That(evidence, Does.Contain("GetMetadataReader"));
            Assert.That(producer, Does.Contain("CreateStandaloneEnvelope"));
            Assert.That(producer, Does.Contain("AcceptanceContractSha256"));
            Assert.That(producer, Does.Contain("ModuleVersionId"));
            Assert.That(producer, Does.Contain("PdbSha256"));
        }
    }

}
