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
            Assert.That(
                evidence,
                Does.Contain(
                    "foreach ($stalePath in @($resolvedOutput, $rawOutput, $standardError))"));
            Assert.That(
                evidence,
                Does.Contain("Remove-Item -LiteralPath $stalePath -Force"));
            Assert.That(producer, Does.Contain("CreateStandaloneEnvelope"));
            Assert.That(producer, Does.Contain("AcceptanceContractSha256"));
            Assert.That(producer, Does.Contain("ModuleVersionId"));
            Assert.That(producer, Does.Contain("PdbSha256"));
        }
    }

    [Test]
    public void StandaloneGateProducerInvalidatesEvidenceBeforePreflight()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "scripts",
            "Invoke-SharpProofGateEvidence.ps1"));
        var invalidation = script.IndexOf(
            "foreach ($stalePath in @($resolvedOutput, $rawOutput, $standardError))",
            StringComparison.Ordinal);
        var parallelism = script.IndexOf(
            "$parallelism = Get-SharpProofTestProjectParallelism",
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(invalidation, Is.GreaterThanOrEqualTo(0));
            Assert.That(parallelism, Is.GreaterThan(invalidation));
        }
    }

    [Test]
    public void PerformanceDispatcherInvalidatesEvidenceBeforePreflight()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "scripts",
            "Invoke-SharpProofContainer.ps1"));
        var invalidation = script.IndexOf(
            "Remove-SharpProofPerformanceEvidence",
            StringComparison.Ordinal);
        var parallelism = script.IndexOf(
            "$testProjectParallelism = Get-SharpProofTestProjectParallelism",
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(invalidation, Is.GreaterThanOrEqualTo(0));
            Assert.That(parallelism, Is.GreaterThan(invalidation));
        }
    }

    [Test]
    public void PackInvalidatesCanonicalOutputBeforePreflight()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "scripts",
            "Invoke-SharpProofContainer.ps1"));
        var start = script.IndexOf("    'pack' {", StringComparison.Ordinal);
        var end = script.IndexOf("    'pilots' {", start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        var pack = script[start..end];
        var preflight = pack.IndexOf(
            "Generate-Readme.ps1",
            StringComparison.Ordinal);
        var invalidation = pack.IndexOf(
            "[IO.Directory]::Delete($resolvedOutput, $true)",
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preflight, Is.GreaterThanOrEqualTo(0));
            Assert.That(invalidation, Is.GreaterThanOrEqualTo(0));
            Assert.That(invalidation, Is.LessThan(preflight));
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
