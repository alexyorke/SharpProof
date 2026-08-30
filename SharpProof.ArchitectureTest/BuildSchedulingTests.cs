using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class BuildSchedulingTests
{
    [Test]
    public void ProductionInventoryUsesBoundedCatalogOwnedParallelism()
    {
        var root = FindRepositoryRoot();
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "eng",
            "acceptance",
            "contract.json")));
        var inventory = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Get-SharpProofProductionInventory.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                contract.RootElement.GetProperty("automation")
                    .GetProperty("productionInventoryMaxParallelism")
                    .GetInt32(),
                Is.EqualTo(8));
            Assert.That(
                inventory,
                Does.Contain("Get-SharpProofTestProjectParallelism"));
            Assert.That(inventory, Does.Contain("ForEach-Object -Parallel"));
            Assert.That(inventory, Does.Contain("-ThrottleLimit $parallelism"));
        }
    }

    [Test]
    public void PackageLayoutFixtureUsesIsolatedProcessShards()
    {
        var packageTests = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "Invoke-SharpProofPackageTests.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(packageTests, Does.Contain("$packageLayoutClass"));
            Assert.That(packageTests, Does.Contain("$packageLayoutBuckets"));
            Assert.That(
                packageTests,
                Does.Contain("$priorPackageLayoutMethodMilliseconds"));
            Assert.That(
                packageTests,
                Does.Contain("packageLayoutMethods ="));
            Assert.That(packageTests, Does.Contain("'package-layout-'"));
            Assert.That(
                packageTests,
                Does.Contain("-MinimumCount 15"));
            Assert.That(
                packageTests,
                Does.Not.Contain("'PackageLayoutSmokeTests',"));
        }
    }

    [Test]
    public void ContainmentTestsUseExclusiveFreshProcesses()
    {
        var packageTests = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "Invoke-SharpProofPackageTests.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(packageTests,
                Does.Contain("Name = 'postflight-buildtask-main'"));
            Assert.That(packageTests,
                Does.Contain("$isolatedBuildTaskMethods"));
            Assert.That(packageTests,
                Does.Contain("OversizedVerifierOutputTriggersPromptBoundedContainment"));
            Assert.That(packageTests,
                Does.Contain("VerifierExecutionRetainsLiveIncompleteCleanupAnchor"));
            Assert.That(packageTests,
                Does.Contain("VerifierTaskBoundsTheWholeLauncherProcess"));
            Assert.That(packageTests,
                Does.Contain("FullyQualifiedName!~$buildTaskClass.$method"));
            Assert.That(packageTests,
                Does.Contain("Exclusive = $true"));
            Assert.That(packageTests,
                Does.Contain("$nextIsExclusive"));
        }
    }

    [Test]
    public void PackageBuildsReuseOutputsAndUseScopedCompilerServers()
    {
        var root = FindRepositoryRoot();
        var packageTests = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofPackageTests.ps1"));
        var execution = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "SharpProof.ContainerExecution.psm1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(packageTests, Does.Contain("Invoke-RequiredBuilds"));
            Assert.That(packageTests, Does.Contain("$buildParallelism"));
            Assert.That(packageTests,
                Does.Contain("Invoke-SharpProofParallelDotnetBuilds"));
            Assert.That(packageTests,
                Does.Contain("'package-products-release'"));
            Assert.That(packageTests,
                Does.Contain("SharpProof.Verifier/SharpProof.Verifier.csproj"));
            Assert.That(packageTests,
                Does.Contain("'--no-restore', '--no-build', '--nologo'"));
            Assert.That(packageTests, Does.Contain("phases = @($phaseTimings)"));
            Assert.That(execution,
                Does.Contain("function Get-SharpProofBuildParallelism"));
            Assert.That(execution,
                Does.Contain("function Invoke-SharpProofParallelDotnetBuilds"));
            Assert.That(execution,
                Does.Not.Contain("UseSharedCompilation=false"));
            Assert.That(execution,
                Does.Contain("SharedCompilationId"));
            Assert.That(execution,
                Does.Contain("$compilerServerScope"));
            Assert.That(execution,
                Does.Contain("Stop-SharpProofCompilerServer"));
            Assert.That(execution,
                Does.Contain("'-shutdown'"));
            Assert.That(execution,
                Does.Contain("Select-Object -First 1"));
            Assert.That(execution,
                Does.Contain("ResolveLinkTarget($true)"));
            Assert.That(execution,
                Does.Contain("'MSBUILDDISABLENODEREUSE'"));
        }
    }

    [Test]
    public void NestedPackageConsumersUseClosureScopedCompilerServers()
    {
        var root = FindRepositoryRoot();
        var fixtures = new[]
        {
            "FinalCompilationProbeTests.cs",
            "PackageLayoutSmokeTests.cs",
            "PackagedProductFeed.cs",
            "WorkerMsBuildIntegrationTests.cs"
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var fixture in fixtures)
            {
                var contents = File.ReadAllText(Path.Combine(
                    root,
                    "SharpProof.Package.Test",
                    fixture));
                Assert.That(
                    contents,
                    Does.Not.Contain("-p:UseSharedCompilation=false"),
                    fixture);
                Assert.That(
                    contents,
                    Does.Contain("SharedCompilationId"),
                    fixture);
                Assert.That(
                    contents,
                    Does.Contain("/nodeReuse:false"),
                    fixture);
            }
        }
    }

    private static readonly string[] BuildSolution =
        ["build", "SharpProof.sln", "--no-restore", "-graphBuild"];
    private static readonly string[] TestFilter =
        ["test", "SharpProof.Dev.Tests.slnf", "--no-build", "-graphBuild"];
    private static readonly string[] BuildProject =
        ["build", "SharpProof.Ir/SharpProof.Ir.csproj"];
    private static readonly string[] RestoreSolution =
        ["restore", "SharpProof.sln"];
    private static readonly string[] Existing =
        ["build", "SharpProof.sln", "-graphBuild"];

    [Test]
    public async Task SolutionBuildsAndTestsUseStaticGraphScheduling()
    {
        var root = FindRepositoryRoot();
        var module = Path.Combine(
            root, "scripts", "SharpProof.ContainerExecution.psm1");
        var escapedModule = module.Replace("'", "''", StringComparison.Ordinal);
        var command = $$"""
            Import-Module '{{escapedModule}}' -Force
            [ordered]@{
                buildSolution = @(Add-SharpProofStaticGraphArgument -Arguments @('build', 'SharpProof.sln', '--no-restore'))
                testFilter = @(Add-SharpProofStaticGraphArgument -Arguments @('test', 'SharpProof.Dev.Tests.slnf', '--no-build'))
                buildProject = @(Add-SharpProofStaticGraphArgument -Arguments @('build', 'SharpProof.Ir/SharpProof.Ir.csproj'))
                restoreSolution = @(Add-SharpProofStaticGraphArgument -Arguments @('restore', 'SharpProof.sln'))
                existing = @(Add-SharpProofStaticGraphArgument -Arguments @('build', 'SharpProof.sln', '-graphBuild'))
            } | ConvertTo-Json -Compress
            """;

        var info = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(command);

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero, await error);

        using var document = JsonDocument.Parse(await output);
        var rootElement = document.RootElement;
        Assert.That(Read(rootElement, "buildSolution"),
            Is.EqualTo(BuildSolution));
        Assert.That(Read(rootElement, "testFilter"),
            Is.EqualTo(TestFilter));
        Assert.That(Read(rootElement, "buildProject"),
            Is.EqualTo(BuildProject));
        Assert.That(Read(rootElement, "restoreSolution"),
            Is.EqualTo(RestoreSolution));
        Assert.That(Read(rootElement, "existing"),
            Is.EqualTo(Existing));

        var container = await File.ReadAllTextAsync(Path.Combine(
            root, "scripts", "Invoke-SharpProofContainer.ps1"));
        var wrapper = await File.ReadAllTextAsync(Path.Combine(
            root, "scripts", "Invoke-SharpProofDotnet.ps1"));
        Assert.That(container,
            Does.Contain("Add-SharpProofStaticGraphArgument"));
        Assert.That(wrapper,
            Does.Contain("Add-SharpProofStaticGraphArgument"));
    }

    [Test]
    public void WorkerTestsRestoreOnlyTheWorkerProjectClosure()
    {
        var root = FindRepositoryRoot();
        var container = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofContainer.ps1"));
        var match = Regex.Match(
            container,
            @"(?s)'worker-tests'\s*\{.*?Invoke-DotNet\s+@\(\s*'restore',\s*'([^']+)'");

        Assert.That(match.Success, Is.True,
            "worker-tests must have an explicit project-scoped restore.");
        Assert.That(match.Groups[1].Value, Is.EqualTo(
            "SharpProof.Worker.Test/SharpProof.Worker.Test.csproj"));
    }

    [Test]
    public void WorkerTestsCanReuseACompletedBuild()
    {
        var root = FindRepositoryRoot();
        var container = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofContainer.ps1"));
        var match = Regex.Match(
            container,
            @"(?s)'worker-tests'\s*\{(?<body>.*?)\r?\n\s*'package-tests'\s*\{");

        Assert.That(container, Does.Contain("[switch]$NoBuild"));
        Assert.That(match.Success, Is.True,
            "worker-tests must remain a distinct command block.");
        Assert.That(match.Groups["body"].Value,
            Does.Contain("if (-not $NoBuild)"));
        Assert.That(match.Groups["body"].Value,
            Does.Contain("'--no-build'"));
    }

    [Test]
    public void WarmTestCommandsForwardTheNoBuildSwitch()
    {
        var root = FindRepositoryRoot();
        var container = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofContainer.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(container, Does.Contain("$arguments += '--no-build'"));
            Assert.That(container, Does.Contain(
                "$semanticArguments.NoBuild = $true"));
            Assert.That(container, Does.Contain(
                "$semanticArguments.TestFilter = $TestFilter"));
            Assert.That(container, Does.Contain(
                "$packageArguments.NoBuild = $true"));
            Assert.That(container, Does.Contain(
                "'-NoBuild is supported only for test commands"));
            Assert.That(container, Does.Contain(
                "$changedArguments.NoBuild = $true"));
        }
    }

    [Test]
    public void FastTestBuildsSkipAnalyzersWithoutWeakeningQualification()
    {
        var root = FindRepositoryRoot();
        var container = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofContainer.ps1"));
        var semantic = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofSemanticTests.ps1"));
        var package = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofPackageTests.ps1"));
        var changed = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofChangedTests.ps1"));
        var documentation = File.ReadAllText(Path.Combine(
            root,
            "docs",
            "container-development.md"));

        using (Assert.EnterMultipleScope())
        {
            foreach (var script in new[] {
                         container,
                         semantic,
                         package,
                         changed
                     })
            {
                Assert.That(script,
                    Does.Contain("RunAnalyzersDuringBuild=false"));
            }
            Assert.That(container,
                Does.Contain("-Fast is supported only for non-qualifying"));
            Assert.That(container,
                Does.Contain("-Fast and -NoBuild cannot be combined"));
            Assert.That(documentation, Does.Contain("sp test-changed -Fast"));
            Assert.That(documentation,
                Does.Contain("It is non-qualifying"));
        }
    }

    [Test]
    public void NoBuildProjectTestsCanUseTheBuiltAssemblyDirectly()
    {
        var root = FindRepositoryRoot();
        var module = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "SharpProof.ContainerExecution.psm1"));
        var container = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofContainer.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(module,
                Does.Contain("function Get-SharpProofTestAssemblyPath"));
            Assert.That(module,
                Does.Contain("Direct vstest requires exactly one TargetFramework"));
            Assert.That(container,
                Does.Contain("Get-SharpProofTestAssemblyPath"));
            Assert.That(container,
                Does.Contain("$arguments = @('vstest', $assembly)"));
            Assert.That(container,
                Does.Contain("'/TestCaseFilter:' + $TestFilter"));
            Assert.That(container,
                Does.Contain("'SharpProof.Worker.Test/SharpProof.Worker.Test.csproj'"));
        }
    }

    [Test]
    public void PackageWorkerDiscoveryUsesTheBuiltAssemblyDirectly()
    {
        var root = FindRepositoryRoot();
        var package = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofPackageTests.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(package,
                Does.Contain("Get-SharpProofTestAssemblyPath"));
            Assert.That(package, Does.Contain("& dotnet vstest $Assembly"));
            Assert.That(package,
                Does.Contain("-Assembly $testAssembly"));
            Assert.That(package, Does.Contain("/ListTests"));
            Assert.That(package,
                Does.Not.Contain("$workerList = & dotnet test $testProject"));
        }
    }

    [Test]
    public void PackageShardsUseBuiltAssembliesOutsideCoverage()
    {
        var package = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "Invoke-SharpProofPackageTests.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(package,
                Does.Contain("$directVstest = -not $coverageEnabled -and"));
            Assert.That(package,
                Does.Not.Contain(
                    "$directVstest = $NoBuild -and -not $coverageEnabled"));
            Assert.That(package,
                Does.Contain("-not $nextIsExclusive"));
            Assert.That(package,
                Does.Contain("@('vstest', $testAssembly)"));
            Assert.That(package,
                Does.Contain("$resolvedDotnetHost"));
            Assert.That(package,
                Does.Contain("ResolveLinkTarget($true)"));
            Assert.That(package,
                Does.Contain(
                    "$startInfo.Environment['DOTNET_HOST_PATH']"));
        }
    }

    [Test]
    public void SemanticWorkerShardsUseBuiltAssembliesOutsideCoverage()
    {
        var root = FindRepositoryRoot();
        var semantic = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofSemanticTests.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(semantic, Does.Contain("$directVstest"));
            Assert.That(semantic,
                Does.Contain("Get-SharpProofTestAssemblyPath"));
            Assert.That(semantic, Does.Contain("'vstest', $assembly"));
            Assert.That(semantic,
                Does.Contain("-not $coverageEnabled"));
        }
    }

    [Test]
    public void SemanticArchitectureShardsCoverEveryFixture()
    {
        var root = FindRepositoryRoot();
        var semantic = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofSemanticTests.ps1"));
        var roster = Regex.Match(
            semantic,
            @"(?s)\$architectureFixtures\s*=\s*@\((?<body>.*?)\)");

        Assert.That(roster.Success, Is.True,
            "The semantic scheduler must declare its Architecture fixtures.");
        var configured = Regex.Matches(
                roster.Groups["body"].Value,
                @"'(?<name>[A-Za-z0-9_]+)'")
            .Select(static match => match.Groups["name"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = typeof(BuildSchedulingTests).Assembly.GetTypes()
            .Where(static type =>
                type.Namespace == "SharpProof.ArchitectureTest" &&
                type.GetCustomAttributesData().Any(static attribute =>
                    attribute.AttributeType == typeof(TestFixtureAttribute)))
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(configured, Is.Unique);
            Assert.That(configured, Is.EqualTo(expected));
            Assert.That(semantic,
                Does.Contain("$architectureShardingEnabled"));
            Assert.That(semantic,
                Does.Contain("$semanticProjectShardingEnabled"));
            Assert.That(semantic, Does.Contain("$semanticProjects"));
            Assert.That(semantic,
                Does.Contain("FullyQualifiedName!~"));
            Assert.That(semantic,
                Does.Contain("Slots = $mainParallelism"));
            Assert.That(semantic,
                Does.Contain("$availableSlots = $parallelism - $activeSlots"));
            Assert.That(semantic, Does.Contain("$pending.Remove($task)"));
            Assert.That(semantic,
                Does.Contain("$architectureFixtureSlots"));
            Assert.That(semantic,
                Does.Contain("ProductionInventoryAuthorityTests = 8"));
            Assert.That(
                semantic,
                Does.Contain(
                    "$startInfo.Environment['SHARPPROOF_TEST_PROJECT_PARALLELISM']"));
        }
    }

    [Test]
    public void ExpensiveScriptFixturesUseBoundedCaseParallelism()
    {
        var fixtures = new[]
        {
            typeof(BoundaryEnforcementTests),
            typeof(CoverageScriptTests),
            typeof(DocumentationSupportContractTests),
            typeof(PackageDependencyAuthorityTests),
            typeof(PublicationDestinationAuthorityTests),
            typeof(PublicationPlanIdentityTests),
            typeof(ReleaseChecksumAuthorityTests)
        };
        var workerAttribute = typeof(BuildSchedulingTests).Assembly
            .GetCustomAttributesData()
            .Single(static attribute =>
                attribute.AttributeType == typeof(LevelOfParallelismAttribute));

        using (Assert.EnterMultipleScope())
        {
            foreach (var fixture in fixtures)
            {
                var attribute = fixture.GetCustomAttributesData()
                    .Single(static candidate =>
                        candidate.AttributeType ==
                            typeof(ParallelizableAttribute));
                Assert.That(
                    Convert.ToInt32(
                        attribute.ConstructorArguments.Single().Value,
                        CultureInfo.InvariantCulture),
                    Is.EqualTo((int)ParallelScope.Children),
                    fixture.Name);
            }
            Assert.That(
                Convert.ToInt32(
                    workerAttribute.ConstructorArguments.Single().Value,
                    CultureInfo.InvariantCulture),
                Is.EqualTo(4));
        }
    }

    [Test]
    public void ChangedTestsCanReuseACompletedBuild()
    {
        var root = FindRepositoryRoot();
        var changed = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofChangedTests.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Does.Contain("[switch]$NoBuild"));
            Assert.That(changed, Does.Contain("if (-not $NoBuild)"));
            Assert.That(changed, Does.Contain(
                "$testArguments += '--no-build'"));
            Assert.That(changed, Does.Contain(
                "$NoBuild -and $selectedRelative.Count -eq 1"));
            Assert.That(changed, Does.Contain(
                "Get-SharpProofTestAssemblyPath"));
        }
    }

    private static string[] Read(JsonElement root, string property)
    {
        return root.GetProperty(property).EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
