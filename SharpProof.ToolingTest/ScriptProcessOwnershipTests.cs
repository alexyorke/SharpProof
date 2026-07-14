using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class ScriptProcessOwnershipTests
{
    [Test]
    public void TestRunnerReliesOnOwnedJobObjectsInsteadOfSweepingProcesses()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Invoke-SharpProofTests.ps1"));

        Assert.That(source, Does.Contain("Invoke-SharpProofDotnet.ps1"));
        Assert.That(source, Does.Not.Contain("Stop-Process"));
        Assert.That(source, Does.Not.Contain("Get-CimInstance Win32_Process"));
    }

    [Test]
    public void JobObjectAssignmentFailureTerminatesTheSuspendedProcess()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "JobObjectHelpers.ps1"));
        var assignmentFailure = source.IndexOf(
            "if (-not [SharpProof.JobObjectNative]::AssignProcessToJobObject",
            StringComparison.Ordinal);
        var directTermination = source.IndexOf(
            "[SharpProof.JobObjectNative]::TerminateProcess($processInformation.hProcess, 124)",
            assignmentFailure,
            StringComparison.Ordinal);

        Assert.That(assignmentFailure, Is.GreaterThanOrEqualTo(0));
        Assert.That(directTermination, Is.GreaterThan(assignmentFailure));
        Assert.That(source, Does.Contain("$processAssignedToJob = $true"));
    }

    [Test]
    public void BuildScriptsDoNotShadowTheConfiguredMemoryLimit()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        foreach (var scriptName in new[] { "build.ps1", "build-vsix.ps1" })
        {
            var source = File.ReadAllText(Path.Combine(repositoryRoot, scriptName));
            Assert.That(
                source,
                Does.Contain("Invoke-ProcessUnderJobObject"),
                scriptName);
            Assert.That(
                source,
                Does.Not.Contain("function Invoke-DotnetInRepo([string[]]$Arguments, [int]$MemoryLimitMb"),
                scriptName);
        }
    }

    [Test]
    public void ArtifactBuildReusesOneRestoredCompileGraph()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "build.ps1"));

        Assert.That(source, Does.Contain(".\\SharpProof.Dev.slnf"));
        Assert.That(source, Does.Contain("-p:GeneratePackageOnBuild=false"));
        Assert.That(source, Does.Contain("--no-restore"));
        Assert.That(source, Does.Contain("--no-build"));
        Assert.That(source, Does.Not.Contain(
            "Invoke-DotnetInRepo @(\"build\", \".\\SharpProof.Attributes"));
        Assert.That(source, Does.Not.Contain(
            "Invoke-DotnetInRepo @(\"build\", \".\\SharpProof.Analyzer"));
        Assert.That(source, Does.Not.Contain(
            "Invoke-DotnetInRepo @(\"build\", \".\\SharpProof.CodeFixes"));
    }

    [Test]
    public void TestLaneRoutingRecognizesBothTestNamespaces()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Invoke-SharpProofTests.ps1"));

        Assert.That(source, Does.Contain("SharpProof\\.(?:Test|ToolingTest)\\."));
        Assert.That(source, Does.Contain("if ($match.Groups[1].Value -eq 'SharpProof')"));
    }

    [Test]
    public void BuildBackedToolsRejectNonzeroChildExitBeforeReadingSarif()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Shared",
            "DotnetSarifBuildRunner.cs"));
        var exitCheck = runnerSource.IndexOf("if (process.ExitCode != 0)", StringComparison.Ordinal);
        var sarifCheck = runnerSource.IndexOf("if (!File.Exists(sarifPath))", StringComparison.Ordinal);

        Assert.That(exitCheck, Is.GreaterThanOrEqualTo(0));
        Assert.That(sarifCheck, Is.GreaterThan(exitCheck));
        foreach (var relativePath in new[]
                 {
                     Path.Combine("Tools", "SharpProof.Baseline", "Program.cs"),
                     Path.Combine("Tools", "SharpProof.CorpusReport", "Program.cs")
                 })
        {
            var source = File.ReadAllText(Path.Combine(repositoryRoot, relativePath));
            Assert.That(source, Does.Contain("DotnetSarifBuildRunner.RunAsync("), relativePath);
            Assert.That(source, Does.Not.Contain("Process.Start("), relativePath);
        }

        Assert.That(runnerSource, Does.Contain("await process.WaitForExitAsync("));
        Assert.That(runnerSource, Does.Not.Contain("GetAwaiter().GetResult()"));
    }

    [Test]
    public void VsixHarnessUsesRequestedBuildConfiguration()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        var harnessSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Tools",
            "VsixHarness",
            "Program.cs"));
        var buildScript = File.ReadAllText(Path.Combine(repositoryRoot, "build-vsix.ps1"));

        Assert.That(harnessSource, Does.Contain("GetConfiguration(args)"));
        Assert.That(harnessSource, Does.Contain("bin\", configuration"));
        Assert.That(harnessSource, Does.Contain("CreateSimulatedVsix(solutionRoot, configuration)"));
        Assert.That(harnessSource, Does.Contain("PublicKeyTokensEqual(loadedName, requestedName)"));
        Assert.That(harnessSource, Does.Contain("TryDeleteDirectory(tempDirectory.FullName)"));
        Assert.That(harnessSource, Does.Contain("TryDeleteDirectory(simulatedVsixDirectory)"));
        Assert.That(harnessSource, Does.Contain("entry.FullName.EndsWith(\"/\""));
        Assert.That(buildScript, Does.Contain("$vsix, $Configuration"));
    }

    [Test]
    public void RepositoryScriptsValidatePathsAndAggregateCompatibleSchemas()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        foreach (var scriptName in new[]
                 {
                     "Get-SharpProofRawSmtHotspots.ps1",
                     "Get-SharpProofProductionMetrics.ps1"
                 })
        {
            var source = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", scriptName));
            Assert.That(source, Does.Contain("Path is outside the repository root"), scriptName);
            Assert.That(source, Does.Contain("StartsWith($repoPrefix"), scriptName);
        }

        var dotnetWrapper = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Invoke-SharpProofDotnet.ps1"));
        Assert.That(dotnetWrapper, Does.Contain("[StringComparison]::OrdinalIgnoreCase"));
        Assert.That(dotnetWrapper, Does.Contain("-nodeReuse:false"));

        var aggregateScript = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Tools",
            "SharpProof.Fuzz",
            "Aggregate-FuzzRun.ps1"));
        Assert.That(aggregateScript, Does.Contain("Sort-Object -Unique"));
        Assert.That(aggregateScript, Does.Contain("incompatible schema versions"));
    }

    [Test]
    public void DemoAndInventoryScriptsFailClosed()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        var demoScript = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "demo-sharpproof.ps1"));
        Assert.That(demoScript, Does.Contain("Assert-NativeCommandSucceeded"));
        Assert.That(demoScript, Does.Contain("failed with exit code"));

        var auditScript = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Get-SharpProofAuditInventory.ps1"));
        Assert.That(auditScript, Does.Contain("$symbolicPath"));
        Assert.That(auditScript, Does.Contain("Measure-Object -Line"));
        Assert.That(auditScript, Does.Contain("Path is outside the repository root"));

        var impactScript = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Get-SharpProofTestImpactInventory.ps1"));
        Assert.That(impactScript, Does.Contain("StartsWith($repoPrefix"));
        Assert.That(impactScript, Does.Contain("Path is outside the repository root"));
    }
}
