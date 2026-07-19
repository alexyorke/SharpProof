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
        foreach (var scriptName in new[] { "build.ps1", "build-nuget.ps1", "build-vsix.ps1" })
        {
            var source = File.ReadAllText(Path.Combine(repositoryRoot, scriptName));
            Assert.That(source, Does.Contain("Invoke-SharpProofDotnet.ps1"), scriptName);
            Assert.That(source, Does.Not.Contain("Invoke-ProcessUnderJobObject"), scriptName);
            Assert.That(source, Does.Not.Contain("MSBuild.exe"), scriptName);
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
            "\"build\", \".\\SharpProof.Attributes"));
        Assert.That(source, Does.Not.Contain(
            "\"build\", \".\\SharpProof.Analyzer"));
        Assert.That(source, Does.Not.Contain(
            "\"build\", \".\\SharpProof.CodeFixes"));
    }

    [Test]
    public void FilteredTestLaneRoutingUsesRequestedProjectBoundary()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Invoke-SharpProofTests.ps1"));

        Assert.That(source, Does.Contain("'Main' { return @($mainProject) }"));
        Assert.That(source, Does.Contain("default { return @($mainProject, $toolingProject) }"));
        Assert.That(source, Does.Not.Contain("SharpProof\\.(?:Test|ToolingTest)\\."));
    }

    [Test]
    public void BuildBackedToolsRejectNonzeroChildExitBeforeReadingSarif()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SharpProof.Tooling.Core",
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
            Assert.That(source, Does.Contain("using var materializedInputs = await " +
                                             "DotnetSarifBuildRunner.MaterializeAsync("), relativePath);
            Assert.That(source, Does.Not.Contain("DotnetSarifBuildRunner.RunAsync("), relativePath);
            Assert.That(source, Does.Not.Contain("Process.Start("), relativePath);
        }

        Assert.That(runnerSource, Does.Contain("temporaryPaths.Add(sarifPath)"));
        Assert.That(runnerSource, Does.Contain("catch"));
        Assert.That(runnerSource, Does.Contain("DeleteAll(temporaryPaths)"));
        Assert.That(runnerSource, Does.Contain("startInfo.ArgumentList.Add(\"--no-incremental\")"));
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
        var inventorySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "SharpProofSourceInventory.ps1"));
        Assert.That(inventorySource, Does.Contain("Path is outside the repository root"));
        Assert.That(inventorySource, Does.Contain("StartsWith($rootPrefix"));
        var metricsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Get-SharpProofProductionMetrics.ps1"));
        Assert.That(metricsSource, Does.Contain("SharpProofSourceInventory.ps1"));
        Assert.That(metricsSource, Does.Not.Contain("function Convert-ToRepoPath"));

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
    public void DemoAndTestImpactInventoryScriptsFailClosed()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        var demoScript = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "demo-sharpproof.ps1"));
        Assert.That(demoScript, Does.Contain("Assert-NativeCommandSucceeded"));
        Assert.That(demoScript, Does.Contain("failed with exit code"));

        var impactScript = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Get-SharpProofTestImpactInventory.ps1"));
        Assert.That(impactScript, Does.Contain("StartsWith($repoPrefix"));
        Assert.That(impactScript, Does.Contain("Path is outside the repository root"));
    }

    [Test]
    public void ConfigurationReferenceGenerationUsesCompiledRegistry()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Generate-ConfigurationReference.ps1"));
        var command = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Tools",
            "SharpProof.SymbolicCli",
            "ConfigurationReferenceCommand.cs"));

        Assert.That(script, Does.Contain("Invoke-SharpProofDotnet.ps1"));
        Assert.That(script, Does.Contain("--generate-configuration-reference"));
        Assert.That(script, Does.Not.Contain("Get-BalancedArguments"));
        Assert.That(command, Does.Contain("AnalyzerConfigurationOptionRegistry.All"));
    }

    [Test]
    public void ReleaseValidationUsesCanonicalFullBuildAndReductionLedger()
    {
        var repositoryRoot = EffectSummaryToolTests.GetRepositoryRoot();
        var releaseValidation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Invoke-SharpProofReleaseValidation.ps1"));
        var build = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Invoke-SharpProofBuild.ps1"));

        Assert.That(releaseValidation, Does.Contain("Invoke-SharpProofBuild.ps1"));
        Assert.That(releaseValidation, Does.Contain("-Full"));
        Assert.That(releaseValidation, Does.Not.Contain("SharpProof.ProofCore"));
        Assert.That(build, Does.Contain("if ($Full)"));
        Assert.That(build, Does.Contain("-p:EnableVsixPackaging=true"));
        var effectSummaryHelper = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SharpProof.ToolingTest",
            "EffectSummaryToolTests.Helpers.cs"));
        Assert.That(effectSummaryHelper, Does.Contain("AppContext.BaseDirectory"));
        Assert.That(effectSummaryHelper, Does.Not.Contain("startInfo.ArgumentList.Add(\"build\")"));
        Assert.That(File.Exists(Path.Combine(
            repositoryRoot,
            "scripts",
            "Get-SharpProofRefactoringMetrics.ps1")), Is.False);
        Assert.That(File.Exists(Path.Combine(
            repositoryRoot,
            "scripts",
            "refactoring-baseline.json")), Is.False);
    }
}
