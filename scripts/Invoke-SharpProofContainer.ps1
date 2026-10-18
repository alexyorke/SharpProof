[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('lint', 'doctor', 'quick', 'pr', 'nightly', 'security', 'contract', 'restore', 'build', 'self-apply', 'check', 'pr-gates', 'test', 'test-changed', 'semantic-tests', 'canonical-cohort-gate', 'portable-tests', 'worker-tests', 'package-tests', 'package-consumers', 'samples', 'corpus', 'corpus-update', 'coverage', 'coverage-shard', 'coverage-gate', 'fuzz-nightly', 'dependency-audit', 'pack', 'format-check', 'docs', 'docs-update')]
    [string]$Command,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$Target = 'SharpProof.slnx',

    [string]$PackageSource = '',

    [string]$TestFilter = '',

    [ValidateSet('All', 'Remaining', 'Gates')]
    [string]$SemanticCohort = 'All',

    [string]$TestResultsDirectory = '',

    # CI runs the package phase of pr in a separate parallel job.
    [switch]$SkipPackageTests,

    [ValidateSet('all', 'semantic-gates', 'semantic-worker', 'semantic-projects', 'package')]
    [string]$CoveragePhase = 'all',

    [switch]$NoBuild,

    [switch]$ReuseTestHarness,

    [switch]$Fast,

    [ValidateSet('Text', 'Json')]
    [string]$Format = 'Text',

    [string]$TargetFramework = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repositoryRoot

Import-Module (Join-Path `
        $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force

if (-not $IsLinux -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
    throw 'SharpProof container commands require Linux x64.'
}
$containerContractPath = $env:SHARPPROOF_CONTAINER_CONTRACT
if ($env:SHARPPROOF_CONTAINER -cne '1' -or
    [string]::IsNullOrWhiteSpace($containerContractPath) -or
    -not (Test-Path -LiteralPath $containerContractPath -PathType Leaf)) {
    throw 'SharpProof container commands require the canonical container contract.'
}

$reusableTestCommands = @(
    'test', 'test-changed', 'semantic-tests', 'portable-tests',
    'worker-tests', 'package-tests')
if ($NoBuild -and $Command -notin $reusableTestCommands) {
    throw (
        '-NoBuild is supported only for test commands that can reuse an ' +
        'existing build in the current container workspace.')
}
if ($Fast -and $Command -notin $reusableTestCommands) {
    throw '-Fast is supported only for non-qualifying test commands.'
}
if (($PSBoundParameters.ContainsKey('SemanticCohort') -or
        $PSBoundParameters.ContainsKey('TestResultsDirectory')) -and
    $Command -notin @('pr', 'pr-gates', 'semantic-tests')) {
    throw 'Canonical cohort options are supported only for pr, pr-gates, and semantic-tests.'
}
if ($SkipPackageTests -and $Command -notin @('pr', 'pr-gates')) {
    throw '-SkipPackageTests is supported only for pr and pr-gates.'
}
if ($Command -in @('pr', 'pr-gates') -and $SemanticCohort -eq 'Gates') {
    throw 'The Gates-only cohort uses semantic-tests; pr retains its package phases.'
}
# CI collects each coverage shard on its own runner; the gate requires all.
$coverageShardNames = @('semantic-gates', 'semantic-worker', 'semantic-projects', 'package')
if ($PSBoundParameters.ContainsKey('CoveragePhase') -and
    $Command -notin @('coverage-shard')) {
    throw '-CoveragePhase is supported only for coverage-shard.'
}
Assert-SharpProofTestSwitches -Fast:$Fast -NoBuild:$NoBuild
if ($ReuseTestHarness -and $Command -ne 'package-tests') {
    throw '-ReuseTestHarness is supported only for package-tests.'
}
$fastBuildArguments = if ($Fast) {
    @('-p:RunAnalyzersDuringBuild=false')
}
else {
    @()
}

# Build the repository-only analyzer once before any project builds. The
# solution can build projects in parallel, so individual projects must not
# race to write its bin/obj directories.
if ($Command -notin @('doctor', 'contract', 'restore', 'dependency-audit', 'format-check', 'lint') -and
    -not $NoBuild -and -not $Fast) {
    $defensiveAnalyzerProject = Join-Path $repositoryRoot `
        'eng/DefensiveCodingAnalyzer/DefensiveCodingAnalyzer.csproj'
    Invoke-SharpProofRequiredDotnet @(
        'restore', $defensiveAnalyzerProject, '--locked-mode')
    # Some top-level commands build a Release project after entering with the
    # default Debug configuration (for example, security).
    foreach ($analyzerConfiguration in @('Debug', 'Release')) {
        Invoke-SharpProofRequiredDotnet @(
            'build', $defensiveAnalyzerProject, '--configuration', $analyzerConfiguration,
            '--no-restore', '-p:RunAnalyzersDuringBuild=false')
    }
}

function New-TestInvocationArguments([hashtable]$Additional = @{}) {
    $arguments = @{ Configuration = $Configuration }
    foreach ($entry in $Additional.GetEnumerator()) {
        $arguments[$entry.Key] = $entry.Value
    }
    if ($NoBuild) { $arguments.NoBuild = $true }
    if ($ReuseTestHarness) { $arguments.ReuseTestHarness = $true }
    if ($Fast) { $arguments.Fast = $true }
    return $arguments
}

function Invoke-RequiredScript(
    [string]$RelativePath,
    [string]$Failure,
    [hashtable]$Arguments = @{}) {
    & (Join-Path $repositoryRoot $RelativePath) @Arguments
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}

function Invoke-PipelineCommand(
    [string]$PipelineCommand,
    [string]$PipelineConfiguration,
    [string[]]$AdditionalArguments = @()) {
    & $PSCommandPath -Command $PipelineCommand `
        -Configuration $PipelineConfiguration @AdditionalArguments
}

function Invoke-TestProject([string]$ProjectPath) {
    if (-not $NoBuild) {
        Invoke-SharpProofRequiredDotnet @('restore', $ProjectPath, '--locked-mode')
        $buildArguments = @(
            'build', $ProjectPath, '--configuration', $Configuration,
            '--no-restore')
        $buildArguments += $fastBuildArguments
        Invoke-SharpProofRequiredDotnet $buildArguments
    }

    $assembly = Get-SharpProofTestAssemblyPath `
        -ProjectPath $ProjectPath `
        -Configuration $Configuration
    $arguments = @('vstest', $assembly)
    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $arguments += '/TestCaseFilter:' + $TestFilter
    }
    Invoke-SharpProofRequiredDotnet $arguments
}

function Invoke-PackageTests {
    $packageArguments = New-TestInvocationArguments -Additional @{
        TestFilter    = $TestFilter
        PackageSource = $PackageSource
    }
    Invoke-RequiredScript 'scripts/Invoke-SharpProofPackageTests.ps1' `
        'Package tests failed.' $packageArguments
}

function Invoke-SolutionTests([string]$SolutionPath) {
    if (-not $NoBuild) {
        Invoke-SharpProofRequiredDotnet @('restore', $SolutionPath, '--locked-mode')
    }
    $isMainSolution = [IO.Path]::GetFileName($SolutionPath) -ceq
    'SharpProof.slnx'
    $runPackageTestsSeparately = $isMainSolution -and
    [string]::IsNullOrWhiteSpace($TestFilter)
    $testProjectParallelism = Get-SharpProofTestProjectParallelism `
        -RepositoryRoot $repositoryRoot
    $arguments = @(
        'test', $SolutionPath, '--configuration', $Configuration,
        '--no-restore')
    $arguments += $fastBuildArguments
    if ($NoBuild) {
        $arguments += '--no-build'
    }
    $arguments += "/m:$testProjectParallelism"
    if ($runPackageTestsSeparately) {
        $arguments += @(
            '--filter', 'FullyQualifiedName!~SharpProof.Package.Test')
    }
    elseif (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $arguments += @('--filter', $TestFilter)
    }
    Invoke-SharpProofRequiredDotnet $arguments

    if ($runPackageTestsSeparately) {
        $packageArguments = @{
            Configuration = $Configuration
            PackageSource = $PackageSource
        }
        if ($NoBuild) {
            $packageArguments.NoBuild = $true
        }
        else {
            $packageArguments.ReuseTestHarness = $true
        }
        if ($Fast) {
            $packageArguments.Fast = $true
        }
        Invoke-RequiredScript 'scripts/Invoke-SharpProofPackageTests.ps1' `
            'Package tests failed.' $packageArguments
    }
}

function Invoke-SharpProofSolutionBuild(
    [string]$BuildConfiguration,
    [string[]]$AdditionalBuildArguments = @()) {
    Invoke-SharpProofRequiredDotnet @('restore', 'SharpProof.slnx', '--locked-mode')
    $buildArguments = @(
        'build', 'SharpProof.slnx', '--configuration', $BuildConfiguration,
        '--no-restore')
    $buildArguments += $AdditionalBuildArguments
    Invoke-SharpProofRequiredDotnet $buildArguments
}

function Invoke-SharpProofCoverageAuthorityBuild {
    # The gate recomputes every production PDB and source identity. Its build
    # need not also compile the test projects already exercised by the shards
    # and the required PR gate. Derive this complete set from the same inventory
    # authority as the validator instead of maintaining another project list.
    $selectionPath = Join-Path ([IO.Path]::GetTempPath()) (
        'sharpproof-coverage-build-' + [Guid]::NewGuid().ToString('N') + '.json')
    $filterPath = Join-Path $repositoryRoot (
        '.sharpproof-coverage-build-' + [Guid]::NewGuid().ToString('N') + '.slnf')
    try {
        Invoke-RequiredScript 'scripts/Get-SharpProofProductionInventory.ps1' `
            'Coverage build project selection failed.' @{
            RepositoryRoot = $repositoryRoot
            Configuration  = 'Release'
            OutputPath     = $selectionPath
        }
        $selection = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json
        $projects = @($selection.projects | ForEach-Object { [string]$_.projectPath })
        if ($projects.Count -eq 0) { throw 'Coverage build project selection is empty.' }
        [ordered]@{ solution = [ordered]@{
                path = 'SharpProof.slnx'; projects = $projects
            }
        } | ConvertTo-Json -Depth 5 |
            Set-Content -LiteralPath $filterPath -Encoding utf8NoBOM
        Invoke-SharpProofRequiredDotnet @('restore', $filterPath, '--locked-mode')
        # Analyzers do not change binaries or PDBs, and the canonical PR build
        # already enforces them; skip them as the shards do.
        Invoke-SharpProofRequiredDotnet @(
            'build', $filterPath, '--configuration', 'Release', '--no-restore',
            '-p:ContinuousIntegrationBuild=true',
            '-p:RunAnalyzersDuringBuild=false')
    }
    finally {
        foreach ($temporaryPath in @($filterPath, $selectionPath)) {
            if (Test-Path -LiteralPath $temporaryPath) {
                Remove-Item -LiteralPath $temporaryPath -Force
            }
        }
    }
}

function Invoke-DependencyAudit {
    Invoke-SharpProofRequiredDotnet @('restore', 'SharpProof.slnx', '--locked-mode')
    $output = Join-Path $repositoryRoot (
        'artifacts/dependency-audit/dependency-audit.json')
    Invoke-RequiredScript 'scripts/Test-SharpProofDependencyAudit.ps1' `
        'Dependency audit failed.' `
    @{
        SolutionPath = Join-Path $repositoryRoot 'SharpProof.slnx'
        NuGetConfigurationPath = Join-Path $repositoryRoot 'NuGet.Config'; OutputPath = $output
    }
}

switch ($Command) {
    'lint' {
        & (Join-Path $PSScriptRoot 'Invoke-SharpProofLint.ps1')
    }
    'format-check' {
        Invoke-RequiredScript 'scripts/Format-Scripts.ps1' `
            'Repository script linting or formatting failed.' @{ VerifyNoChanges = $true }
        Invoke-RequiredScript 'scripts/Test-SharpProofScriptTools.ps1' `
            'Script-tool regression tests failed.'
        Invoke-RequiredScript 'scripts/Format-CSharp.ps1' `
            'Repository formatting check failed.' @{ VerifyNoChanges = $true }
    }
    'doctor' {
        $doctorTarget = if ($PSBoundParameters.ContainsKey('Target')) { $Target } else { '' }
        & (Join-Path $PSScriptRoot 'Invoke-SharpProofDoctor.ps1') -Target $doctorTarget -Format $Format -TargetFramework $TargetFramework
    }
    'docs' {
        $global:LASTEXITCODE = 0
        Invoke-RequiredScript 'scripts/Sync-SharpProofDocumentation.ps1' 'Source reference drift detected.'
        Invoke-RequiredScript 'scripts/Test-SharpProofDocumentation.ps1' 'Documentation links failed.'
        Invoke-RequiredScript 'scripts/Test-SharpProofDocumentationAutomation.ps1' 'Documentation automation regressions failed.'
        & bash (Join-Path $repositoryRoot 'scripts/Test-SharpProofDocumentationUpdater.sh')
        if ($LASTEXITCODE -ne 0) { throw 'Documentation updater lifecycle regressions failed.' }
    }
    'docs-update' {
        $global:LASTEXITCODE = 0
        Invoke-RequiredScript 'scripts/Sync-SharpProofDocumentation.ps1' 'Source reference generation failed.' @{
            Update     = $true
            OutputPath = (Join-Path $repositoryRoot 'artifacts/docs/source-reference.md')
        }
        Invoke-RequiredScript 'scripts/Test-SharpProofDocumentation.ps1' 'Documentation links failed.'
        Invoke-RequiredScript 'scripts/Test-SharpProofDocumentationAutomation.ps1' 'Documentation automation regressions failed.'
        & bash (Join-Path $repositoryRoot 'scripts/Test-SharpProofDocumentationUpdater.sh')
        if ($LASTEXITCODE -ne 0) { throw 'Documentation updater lifecycle regressions failed.' }
    }
    'quick' {
        Invoke-PipelineCommand 'test-changed' 'Debug' @('-Fast')
    }
    'pr' {
        & $PSCommandPath -Command 'pr-gates' -Configuration 'Release' `
            -SemanticCohort $SemanticCohort -TestResultsDirectory $TestResultsDirectory `
            -SkipPackageTests:$SkipPackageTests
    }
    'nightly' {
        Invoke-PipelineCommand 'dependency-audit' 'Release'
        Invoke-PipelineCommand 'corpus' 'Release'
        Invoke-PipelineCommand 'fuzz-nightly' 'Release'
    }
    'security' {
        Invoke-DependencyAudit
        Invoke-SharpProofRequiredDotnet @(
            'build', 'SharpProof.slnx', '--configuration', 'Release',
            '--no-restore')
    }
    'contract' {
        & (Join-Path $repositoryRoot `
                'scripts/Test-SharpProofContainerContract.ps1')
    }
    'restore' {
        Invoke-SharpProofRequiredDotnet @('restore', $Target, '--locked-mode')
    }
    'build' {
        Invoke-SharpProofRequiredDotnet @('restore', $Target, '--locked-mode')
        Invoke-SharpProofRequiredDotnet @(
            'build', $Target, '--configuration', $Configuration, '--no-restore')
    }
    'self-apply' {
        $trackedProjects = @(
            & git -C $repositoryRoot ls-files -- '*.csproj' |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                ForEach-Object {
                    Join-Path $repositoryRoot $_
                } |
                Sort-Object
        )
        if ($LASTEXITCODE -ne 0 -or $trackedProjects.Count -eq 0) {
            throw 'self-apply requires a Git-backed repository with tracked project files.'
        }

        $sourceProjects = @(
            $trackedProjects |
                Where-Object {
                    $relative = [IO.Path]::GetRelativePath(
                        $repositoryRoot, $_).Replace('\', '/')
                    $relative -notlike 'samples/*'
                }
        )
        if ($sourceProjects.Count -eq 0) {
            throw 'self-apply found no source-tree projects to analyze.'
        }

        # Build the complete source tree once with the self lane disabled so
        # every analyzer and generator output is available as a stable input.
        # Analyzer diagnostics are not needed here: each per-project
        # self-application build below runs every analyzer again.
        Invoke-SharpProofRequiredDotnet @('restore', 'SharpProof.slnx', '--locked-mode')
        Invoke-SharpProofRequiredDotnet @(
            'build', 'SharpProof.slnx', '--configuration', $Configuration,
            '--no-restore', '--nologo',
            '-p:RunAnalyzersDuringBuild=false',
            '-p:SharpProofSelfApplication=false',
            '-p:SharpProofProfile=off',
            '-p:SharpProofVerify=false',
            '-p:GeneratePackageOnBuild=false')

        $ordinal = 0
        foreach ($project in $sourceProjects) {
            $ordinal++
            Write-Host ("Self-applying SharpProof ({0}/{1}): {2}" -f
                $ordinal, $sourceProjects.Count,
                [IO.Path]::GetRelativePath($repositoryRoot, $project))
            Invoke-SharpProofRequiredDotnet @(
                'build', $project, '--configuration', $Configuration,
                '--no-restore', '--no-dependencies', '--nologo',
                '-p:SharpProofSelfApplication=true',
                '-p:SharpProofProfile=advisory',
                '-p:SharpProofFeatures=all',
                '-p:SharpProofVerify=false',
                '-p:GeneratePackageOnBuild=false')
        }

        # Package-backed samples exercise the same analyzer payload through
        # the supported package-consumer path.  The sample harness creates and
        # cleans its own isolated local feed and temporary build roots.
        Invoke-RequiredScript 'scripts/Test-SharpProofSamples.ps1' `
            'SharpProof self-application sample validation failed.' `
        @{ Configuration = $Configuration }
    }
    'check' {
        & (Join-Path $repositoryRoot 'scripts/Invoke-SharpProofDevCheck.ps1') `
            -Configuration $Configuration
    }
    'pr-gates' {
        Invoke-PipelineCommand 'docs' $Configuration
        if ($Configuration -ne 'Release') {
            throw "pr-gates requires -Configuration Release. " +
            "Use the test command for Debug validation."
        }
        & (Join-Path $repositoryRoot `
                'scripts/Test-SharpProofContainerContract.ps1')
        Invoke-SharpProofSolutionBuild -BuildConfiguration $Configuration

        $prTestFilter = 'TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus'
        $prTestArguments = @{
            Configuration = $Configuration; NoBuild = $true
            TestFilter = $prTestFilter
        }
        $semanticTestArguments = $prTestArguments.Clone()
        $semanticTestArguments.SemanticCohort = $SemanticCohort
        $semanticTestArguments.TestResultsDirectory = $TestResultsDirectory
        Invoke-RequiredScript 'scripts/Invoke-SharpProofSemanticTests.ps1' `
            'PR semantic validation failed.' $semanticTestArguments

        if (-not $SkipPackageTests) {
            Invoke-RequiredScript 'scripts/Invoke-SharpProofPackageTests.ps1' `
                'PR package validation failed.' $prTestArguments
        }
    }
    'test' {
        $targetName = [IO.Path]::GetFileName($Target)
        $isPackageTestTarget = $targetName -in @(
            'SharpProof.Package.Test',
            'SharpProof.Package.Test.csproj')
        $canUsePackageScheduler = -not $NoBuild -or
        -not [string]::IsNullOrWhiteSpace($PackageSource)
        if ($isPackageTestTarget -and
            [string]::IsNullOrWhiteSpace($TestFilter) -and
            $canUsePackageScheduler) {
            Invoke-PackageTests
            break
        }
        $directProjectTest =
        $Target.EndsWith(
            '.csproj', [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($Target) -cne
        'SharpProof.Package.Test.csproj'
        if ($directProjectTest) {
            Invoke-TestProject $Target
            break
        }
        if ($Target.EndsWith('.sln', [StringComparison]::OrdinalIgnoreCase) -or
            $Target.EndsWith('.slnx', [StringComparison]::OrdinalIgnoreCase) -or
            $Target.EndsWith('.slnf', [StringComparison]::OrdinalIgnoreCase)) {
            Invoke-SolutionTests $Target
            break
        }
        if (-not $NoBuild) {
            Invoke-SharpProofRequiredDotnet @('restore', $Target, '--locked-mode')
        }
        $arguments = @(
            'test', $Target, '--configuration', $Configuration, '--no-restore')
        $arguments += $fastBuildArguments
        if ($NoBuild) {
            $arguments += '--no-build'
        }
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $arguments += @('--filter', $TestFilter)
        }
        Invoke-SharpProofRequiredDotnet $arguments
    }
    'test-changed' {
        $changedArguments = New-TestInvocationArguments
        & (Join-Path `
                $repositoryRoot 'scripts/Invoke-SharpProofChangedTests.ps1') `
            @changedArguments
    }
    'semantic-tests' {
        $semanticArguments = New-TestInvocationArguments
        $semanticArguments.SemanticCohort = $SemanticCohort
        $semanticArguments.TestResultsDirectory = $TestResultsDirectory
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $semanticArguments.TestFilter = $TestFilter
        }
        & (Join-Path `
                $repositoryRoot 'scripts/Invoke-SharpProofSemanticTests.ps1') `
            @semanticArguments
    }
    'canonical-cohort-gate' {
        . (Join-Path $PSScriptRoot 'SharpProof.CanonicalCohortEvidence.ps1')
        Assert-SharpProofCanonicalCohorts `
            -Root (Join-Path $repositoryRoot 'artifacts/canonical-cohorts') `
            -RepositoryRoot $repositoryRoot
    }
    'portable-tests' {
        Invoke-SolutionTests 'SharpProof.Portable.Tests.slnf'
    }
    'worker-tests' {
        $workerTestProject =
        'SharpProof.Worker.Test/SharpProof.Worker.Test.csproj'
        Invoke-TestProject $workerTestProject
    }
    'package-tests' {
        Invoke-PackageTests
    }
    'package-consumers' {
        if ([string]::IsNullOrWhiteSpace($PackageSource)) {
            throw 'package-consumers requires -PackageSource.'
        }
        Invoke-SharpProofRequiredDotnet @('restore', 'SharpProof.slnx', '--locked-mode')
        $consumerArguments = @{
            Configuration = $Configuration
            PackageSource = $PackageSource
        }
        Invoke-RequiredScript 'scripts/Test-SharpProofPackageConsumers.ps1' `
            'Package consumer validation failed.' $consumerArguments
        $toolchain = Get-Content -LiteralPath (Join-Path `
                $repositoryRoot 'eng/container/toolchain.json') -Raw |
            ConvertFrom-Json
        $minimumConsumerArguments = $consumerArguments.Clone()
        $minimumConsumerArguments.ConsumerSdkVersion =
        [string]$toolchain.dotnet.minimumSdkVersion
        $minimumConsumerArguments.FrameworkConsumersOnly = $true
        Invoke-RequiredScript 'scripts/Test-SharpProofPackageConsumers.ps1' `
            'Minimum-SDK package consumer validation failed.' `
            $minimumConsumerArguments
    }
    'samples' {
        Invoke-RequiredScript 'scripts/Test-SharpProofSamples.ps1' `
            'Sample validation failed.' `
        @{ Configuration = $Configuration; PackageSource = $PackageSource }
    }
    { $_ -in @('corpus', 'corpus-update') } {
        $gateMode = $Command
        $gateProject = 'SharpProof.Gates/SharpProof.Gates.csproj'
        Invoke-SharpProofRequiredDotnet @('restore', $gateProject, '--locked-mode')
        Invoke-SharpProofRequiredDotnet @(
            'run', '--project', $gateProject,
            '--configuration', $Configuration,
            '--no-restore', '--', $gateMode)
    }
    'coverage' {
        if ([string]::IsNullOrWhiteSpace(
                $env:SHARPPROOF_COVERAGE_COMPARISON_REF)) {
            throw (
                'SHARPPROOF_COVERAGE_COMPARISON_REF is required for ' +
                'changed-TCB coverage enforcement.')
        }
        $comparisonRef = $env:SHARPPROOF_COVERAGE_COMPARISON_REF
        Invoke-SharpProofSolutionBuild -BuildConfiguration 'Release'
        $coverageRoot = Join-Path $repositoryRoot (
            'artifacts/coverage/container-' + [Guid]::NewGuid().ToString('N'))
        $coverageCollectionArguments = @{
            ResultsDirectory = $coverageRoot
        }
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $coverageCollectionArguments.TestFilter = $TestFilter
        }
        Invoke-RequiredScript 'scripts/Invoke-SharpProofCoverage.ps1' `
            'Coverage collection failed.' $coverageCollectionArguments
        $summaryPath = Join-Path $coverageRoot 'SharpProof.coverage.json'
        $coverageArguments = @{
            CoverageRoot  = $coverageRoot
            ComparisonRef = $comparisonRef
            SummaryPath   = $summaryPath
        }
        if (-not [string]::IsNullOrWhiteSpace(
                (& git status --porcelain))) {
            $coverageArguments.IncludeWorkingTree = $true
        }
        $coverageValidationTimer = [Diagnostics.Stopwatch]::StartNew()
        try {
            Invoke-RequiredScript 'scripts/Test-SharpProofCoverage.ps1' `
                'Coverage validation failed.' $coverageArguments
        }
        finally {
            $coverageValidationTimer.Stop()
            Write-Host ('Coverage phase: name=validation-total elapsedMilliseconds={0}' -f $coverageValidationTimer.ElapsedMilliseconds)
        }
    }
    'coverage-shard' {
        # One runner collects one part of the default coverage run. The
        # coverage-gate job merges every shard and enforces the thresholds.
        if ($CoveragePhase -notin $coverageShardNames) {
            throw ('coverage-shard requires -CoveragePhase ' + ($coverageShardNames -join ', ') + '.')
        }
        # Identical sources in different private container workspaces must
        # produce the same MVID/PDB identity even outside GitHub Actions.
        # Analyzers do not change those binaries and the canonical PR build
        # already enforces them, so shards and the gate skip them.
        Invoke-SharpProofSolutionBuild -BuildConfiguration 'Release' `
            -AdditionalBuildArguments @(
            '-p:ContinuousIntegrationBuild=true',
            '-p:RunAnalyzersDuringBuild=false')
        $coverageShardArguments = @{
            ResultsDirectory = (Join-Path $repositoryRoot (
                    'artifacts/coverage/' + $CoveragePhase))
            CoveragePhase    = $CoveragePhase
        }
        Invoke-RequiredScript 'scripts/Invoke-SharpProofCoverage.ps1' `
            'Coverage shard collection failed.' $coverageShardArguments
    }
    'coverage-gate' {
        . (Join-Path $PSScriptRoot 'SharpProof.CoverageShardEvidence.ps1')
        if ([string]::IsNullOrWhiteSpace(
                $env:SHARPPROOF_COVERAGE_COMPARISON_REF)) {
            throw (
                'SHARPPROOF_COVERAGE_COMPARISON_REF is required for ' +
                'changed-TCB coverage enforcement.')
        }
        $comparisonRef = $env:SHARPPROOF_COVERAGE_COMPARISON_REF
        Invoke-SharpProofCoverageAuthorityBuild
        $coverageRoot = Join-Path $repositoryRoot 'artifacts/coverage'
        $mergedAuthorityPath = Join-Path $coverageRoot 'coverage-authority.json'
        if (Test-Path -LiteralPath $mergedAuthorityPath) {
            throw 'coverage-gate requires a coverage root without merged authority evidence.'
        }
        foreach ($report in @(Get-ChildItem -LiteralPath $coverageRoot -Recurse -Filter '*.cobertura*.xml' -File |
                    Where-Object { Test-SharpProofCoverageReportName -Path $_.Name })) {
            $relativeReport = [IO.Path]::GetRelativePath($coverageRoot, $report.FullName).Replace('\', '/')
            if (($relativeReport -split '/')[0] -cnotin $coverageShardNames) {
                throw 'coverage-gate found a report outside the authenticated shards.'
            }
        }

        # Every shard must contribute reports and must have recorded the same
        # authority as the others. Differences fail closed here, before the
        # validator compares each envelope with the gate's own recomputation.
        $authorityKeys = [Collections.Generic.List[string]]::new()
        $authorityFiles = [Collections.Generic.List[string]]::new()
        foreach ($shardName in $coverageShardNames) {
            $shardRoot = Join-Path $coverageRoot $shardName
            Assert-SharpProofCoverageShardManifest -Root $shardRoot -Phase $shardName
            $shardAuthorityPath = Join-Path $shardRoot 'coverage-authority.json'
            if (-not (Test-Path -LiteralPath $shardAuthorityPath -PathType Leaf)) {
                throw "Coverage shard '$shardName' is missing its authority evidence."
            }
            $shardReports = @(Get-ChildItem `
                    -LiteralPath $shardRoot `
                    -Recurse `
                    -Filter '*.cobertura*.xml' `
                    -File |
                    Where-Object { Test-SharpProofCoverageReportName -Path $_.Name })
            if ($shardReports.Count -eq 0) {
                throw "Coverage shard '$shardName' produced no Cobertura reports."
            }
            $shardAuthority = Get-Content `
                -LiteralPath $shardAuthorityPath `
                -Raw | ConvertFrom-Json
            $shardModules = @($shardAuthority.modules | ForEach-Object {
                    [string]$_.project + ':' + [string]$_.assemblyName + ':' +
                    [string]$_.moduleMvid + ':' + [string]$_.pdbCodeViewGuid
                } | Sort-Object)
            $shardProjects = @($shardAuthority.projects | ForEach-Object {
                    [string]$_.name
                } | Sort-Object)
            $authorityKeys.Add(
                [string]$shardAuthority.commit + '|' +
                [string]$shardAuthority.configuration + '|' +
                ($shardProjects -join ',') + '|' +
                ($shardModules -join ','))
            $authorityFiles.Add($shardAuthorityPath)
        }
        if (@($authorityKeys | Select-Object -Unique).Count -ne 1) {
            throw 'Coverage shards recorded different authority evidence; the gate fails closed.'
        }
        Copy-Item `
            -LiteralPath $authorityFiles[0] `
            -Destination $mergedAuthorityPath

        $summaryPath = Join-Path $coverageRoot 'SharpProof.coverage.json'
        $coverageArguments = @{
            CoverageRoot  = $coverageRoot
            ComparisonRef = $comparisonRef
            SummaryPath   = $summaryPath
        }
        if (-not [string]::IsNullOrWhiteSpace(
                (& git status --porcelain))) {
            $coverageArguments.IncludeWorkingTree = $true
        }
        $coverageValidationTimer = [Diagnostics.Stopwatch]::StartNew()
        try {
            Invoke-RequiredScript 'scripts/Test-SharpProofCoverage.ps1' `
                'Coverage validation failed.' $coverageArguments
        }
        finally {
            $coverageValidationTimer.Stop()
            Write-Host ('Coverage phase: name=validation-total elapsedMilliseconds={0}' -f $coverageValidationTimer.ElapsedMilliseconds)
        }
    }
    'fuzz-nightly' {
        if ($Configuration -ne 'Release') {
            throw 'fuzz-nightly requires -Configuration Release.'
        }
        Invoke-SharpProofSolutionBuild -BuildConfiguration 'Release'
        Invoke-RequiredScript 'scripts/Invoke-SharpProofFuzzCampaign.ps1' `
            'Nightly fuzz campaign failed.' `
        @{ OutputDirectory = 'artifacts/fuzz/nightly' }
    }
    'dependency-audit' {
        Invoke-DependencyAudit
    }
    'pack' {
        $output = Join-Path $repositoryRoot 'artifacts/container-packages'
        $artifactsRoot = [IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot 'artifacts'))
        $resolvedOutput = [IO.Path]::GetFullPath($output)
        if (-not $resolvedOutput.StartsWith(
                $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::Ordinal)) {
            throw "Refusing to clean package output outside artifacts: $resolvedOutput"
        }
        if ([IO.Directory]::Exists($resolvedOutput)) {
            [IO.Directory]::Delete($resolvedOutput, $true)
        }
        [System.IO.Directory]::CreateDirectory($output) | Out-Null
        $repositoryCommit = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repositoryCommit)) {
            throw 'Could not resolve the repository commit for package provenance.'
        }
        $repositoryCommitProperty = "/p:RepositoryCommit=$repositoryCommit"
        Invoke-SharpProofSolutionBuild `
            -BuildConfiguration 'Release' `
            -AdditionalBuildArguments @(
            '/p:GeneratePackageOnBuild=false',
            $repositoryCommitProperty)
        Invoke-SharpProofRequiredDotnet @(
            'pack', 'SharpProof.slnx', '--configuration', 'Release',
            '--output', $output, '--no-build', '--no-restore',
            '/p:GeneratePackageOnBuild=false',
            $repositoryCommitProperty)
        Invoke-RequiredScript 'scripts/Test-SharpProofPackageConsumers.ps1' `
            'Package graph validation failed.' `
        @{ PackageSource = $output; ValidatePackageSourceOnly = $true }
    }
}
