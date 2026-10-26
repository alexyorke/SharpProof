[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('lint', 'doctor', 'quick', 'pr', 'nightly', 'security', 'layout', 'restore', 'build', 'self-apply', 'check', 'pr-gates', 'test', 'test-changed', 'semantic-tests', 'portable-tests', 'worker-tests', 'package-tests', 'samples', 'corpus', 'corpus-update', 'coverage', 'fuzz-nightly', 'dependency-audit', 'pack', 'pack-tool', 'format-check', 'docs')]
    [string]$Command,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$Target = 'SharpProof.slnx',

    [string]$PackageSource = '',

    [ValidateSet('All', 'Sources', 'Samples')][string]$SelfApplicationPhase = 'All',

    [ValidateRange(0, 3)][int]$PackagePartition = 0,

    [ValidateSet(1, 4)][int]$PackagePartitionCount = 1,

    [string]$TestFilter = '',

    [ValidateSet('All', 'Remaining', 'Gates')]
    [string]$SemanticCohort = 'All',

    # CI runs the Remaining cohort's Worker remainder shards and its other
    # tasks on separate runners; the cohort gate requires every part.
    [ValidateSet('All', 'Worker', 'WorkerA', 'WorkerB', 'Projects', 'GatesCorpus', 'GatesShards')]
    [string]$TaskGroup = 'All',

    [string]$TestResultsDirectory = '',

    # CI runs the package phase of pr in a separate parallel job.
    [switch]$SkipPackageTests,

    [switch]$NoBuild,

    [switch]$ReuseTestHarness,

    [switch]$Fast,

    [ValidateSet('Text', 'Json')]
    [string]$Format = 'Text',

    [string]$TargetFramework = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
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

New-Variable -Name reusableTestCommands -Option Constant -Value (
    [System.Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
            'test', 'test-changed', 'semantic-tests', 'portable-tests',
            'worker-tests', 'package-tests')))
if ($NoBuild -and $Command -notin $reusableTestCommands) {
    throw (
        '-NoBuild is supported only for test commands that can reuse an ' +
        'existing build in the current container workspace.')
}
if ($Fast -and $Command -notin $reusableTestCommands) {
    throw '-Fast is supported only for non-qualifying test commands.'
}
if (($PSBoundParameters.ContainsKey('SemanticCohort') -or
        $PSBoundParameters.ContainsKey('TaskGroup') -or
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
if ($PSBoundParameters.ContainsKey('SelfApplicationPhase') -and $Command -ne 'self-apply') {
    throw '-SelfApplicationPhase is supported only for self-apply.'
}
if (($PSBoundParameters.ContainsKey('PackagePartition') -or $PSBoundParameters.ContainsKey('PackagePartitionCount')) -and $Command -ne 'package-tests') {
    throw 'Package partition options are supported only for package-tests.'
}
Assert-SharpProofTestSwitches -Fast:$Fast -NoBuild:$NoBuild
if ($ReuseTestHarness -and $Command -ne 'package-tests') {
    throw '-ReuseTestHarness is supported only for package-tests.'
}
$fastBuildArguments = if ($Fast) {
    , [Collections.Immutable.ImmutableArray]::Create[string]('-p:RunAnalyzersDuringBuild=false')
}
else {
    , [Collections.Immutable.ImmutableArray[string]]::Empty
}

# Build the repository-only analyzer once before any project builds. The
# solution can build projects in parallel, so individual projects must not
# race to write its bin/obj directories.
if ($Command -notin @('doctor', 'layout', 'restore', 'dependency-audit', 'format-check', 'lint', 'docs') -and
    -not $NoBuild -and -not $Fast) {
    $defensiveAnalyzerProject = Join-Path $repositoryRoot `
        'src/DefensiveCodingAnalyzer/DefensiveCodingAnalyzer.csproj'
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

function New-ContainerArgumentBuilder([Collections.IDictionary]$Values = ([ordered]@{}).AsReadOnly()) {
    $builder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $Values.GetEnumerator()) { $builder[[string]$entry.Key] = $entry.Value }
    return , $builder
}

function New-TestInvocationArguments([Collections.IDictionary]$Additional = ([ordered]@{}).AsReadOnly()) {
    $arguments = New-ContainerArgumentBuilder -Values ([ordered]@{ Configuration = $Configuration }).AsReadOnly()
    foreach ($entry in $Additional.GetEnumerator()) {
        $arguments[[string]$entry.Key] = $entry.Value
    }
    if ($NoBuild) { $arguments['NoBuild'] = $true }
    if ($ReuseTestHarness) { $arguments['ReuseTestHarness'] = $true }
    if ($Fast) { $arguments['Fast'] = $true }
    return , $arguments.ToImmutable()
}

function Invoke-RequiredScript(
    [string]$RelativePath,
    [string]$Failure,
    [Collections.IDictionary]$Arguments = ([ordered]@{}).AsReadOnly()) {
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
        $buildArgumentBuilder = [System.Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $buildArgumentBuilder.AddRange([string[]]@(
                'build', $ProjectPath, '--configuration', $Configuration,
                '--no-restore'))
        $buildArgumentBuilder.AddRange([string[]]@($fastBuildArguments))
        Invoke-SharpProofRequiredDotnet ([string[]]@($buildArgumentBuilder.ToImmutable()))
        & (Join-Path $PSScriptRoot 'Export-SharpProofEmbeddedSymbols.ps1') -RepositoryRoot $repositoryRoot -Configuration $Configuration
    }

    $assembly = Get-SharpProofTestAssemblyPath `
        -ProjectPath $ProjectPath `
        -Configuration $Configuration
    $argumentBuilder = [System.Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $argumentBuilder.AddRange([string[]]@('vstest', $assembly))
    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $argumentBuilder.Add('/TestCaseFilter:' + $TestFilter)
    }
    Invoke-SharpProofRequiredDotnet ([string[]]@($argumentBuilder.ToImmutable()))
}

function Invoke-PackageTests {
    $packageArguments = New-TestInvocationArguments -Additional ([ordered]@{
            TestFilter            = $TestFilter
            PackageSource         = $PackageSource
            PackagePartition      = $PackagePartition
            PackagePartitionCount = $PackagePartitionCount
        }).AsReadOnly()
    Invoke-RequiredScript 'eng/scripts/Invoke-SharpProofPackageTests.ps1' `
        'Package tests failed.' $packageArguments
}

function Invoke-SolutionTests([string]$SolutionPath) {
    if (-not $NoBuild) {
        Invoke-SharpProofRequiredDotnet @('restore', $SolutionPath, '--locked-mode')
        $buildArgumentBuilder = [System.Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $buildArgumentBuilder.AddRange([string[]]@('build', $SolutionPath, '--configuration', $Configuration, '--no-restore'))
        $buildArgumentBuilder.AddRange([string[]]@($fastBuildArguments))
        Invoke-SharpProofRequiredDotnet ([string[]]@($buildArgumentBuilder.ToImmutable()))
        & (Join-Path $PSScriptRoot 'Export-SharpProofEmbeddedSymbols.ps1') -RepositoryRoot $repositoryRoot -Configuration $Configuration
    }
    $isMainSolution = [IO.Path]::GetFileName($SolutionPath) -ceq
    'SharpProof.slnx'
    $runPackageTestsSeparately = $isMainSolution -and
    [string]::IsNullOrWhiteSpace($TestFilter)
    $testProjectParallelism = Get-SharpProofTestProjectParallelism `
        -RepositoryRoot $repositoryRoot
    $argumentBuilder = [System.Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $argumentBuilder.AddRange([string[]]@(
            'test', $SolutionPath, '--configuration', $Configuration,
            '--no-restore'))
    $argumentBuilder.AddRange([string[]]@($fastBuildArguments))
    $argumentBuilder.Add('--no-build')
    $argumentBuilder.Add("/m:$testProjectParallelism")
    if ($runPackageTestsSeparately) {
        $argumentBuilder.AddRange([string[]]@(
                '--filter', 'FullyQualifiedName!~SharpProof.Package.Test'))
    }
    elseif (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $argumentBuilder.AddRange([string[]]@('--filter', $TestFilter))
    }
    Invoke-SharpProofRequiredDotnet ([string[]]@($argumentBuilder.ToImmutable()))

    if ($runPackageTestsSeparately) {
        $packageArgumentBuilder = New-ContainerArgumentBuilder -Values ([ordered]@{
                Configuration = $Configuration
                PackageSource = $PackageSource
            }).AsReadOnly()
        if ($NoBuild) {
            $packageArgumentBuilder['NoBuild'] = $true
        }
        else {
            $packageArgumentBuilder['ReuseTestHarness'] = $true
        }
        if ($Fast) {
            $packageArgumentBuilder['Fast'] = $true
        }
        $packageArguments = $packageArgumentBuilder.ToImmutable()
        Invoke-RequiredScript 'eng/scripts/Invoke-SharpProofPackageTests.ps1' `
            'Package tests failed.' $packageArguments
    }
}

function Invoke-SharpProofSolutionBuild(
    [string]$BuildConfiguration,
    [string[]]$AdditionalBuildArguments = @(),
    [string]$BuildTarget = 'SharpProof.slnx') {
    Invoke-SharpProofRequiredDotnet @('restore', $BuildTarget, '--locked-mode')
    $buildArgumentBuilder = [System.Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $buildArgumentBuilder.AddRange([string[]]@(
            'build', $BuildTarget, '--configuration', $BuildConfiguration,
            '--no-restore'))
    if ($null -ne $AdditionalBuildArguments) {
        $buildArgumentBuilder.AddRange($AdditionalBuildArguments)
    }
    $buildArgumentSnapshot = $buildArgumentBuilder.ToImmutable()
    $nativeBuildArguments = [string[]]::new($buildArgumentSnapshot.Length + [int]($null -eq $AdditionalBuildArguments))
    $buildArgumentSnapshot.CopyTo($nativeBuildArguments)
    Invoke-SharpProofRequiredDotnet $nativeBuildArguments
    & (Join-Path $PSScriptRoot 'Export-SharpProofEmbeddedSymbols.ps1') -RepositoryRoot $repositoryRoot -Configuration $BuildConfiguration
}

function Invoke-DependencyAudit {
    Invoke-SharpProofRequiredDotnet @('restore', 'SharpProof.slnx', '--locked-mode')
    $output = Join-Path $repositoryRoot (
        'artifacts/dependency-audit/dependency-audit.json')
    Invoke-RequiredScript 'eng/scripts/Test-SharpProofDependencyAudit.ps1' `
        'Dependency audit failed.' `
    ([ordered]@{
            SolutionPath = Join-Path $repositoryRoot 'SharpProof.slnx'
            NuGetConfigurationPath = Join-Path $repositoryRoot 'NuGet.config'; OutputPath = $output
        }).AsReadOnly()
}

function Invoke-ProductPack {
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
    $diagnostics = Join-Path $repositoryRoot 'artifacts/pack-diagnostics'
    [IO.Directory]::CreateDirectory($diagnostics) | Out-Null
    Write-Host "Pack diagnostics: $diagnostics"
    $repositoryCommitProperty = "/p:RepositoryCommit=$repositoryCommit"
    . (Join-Path $PSScriptRoot 'SharpProof.PackageBuildPlan.ps1')
    $packageBuildProjects = Get-SharpProofPackageBuildProjects `
        -RepositoryRoot $repositoryRoot
    $packageBuildFilter = Join-Path $repositoryRoot (
        '.sharpproof-package-build-' + [Guid]::NewGuid().ToString('N') + '.slnf')
    try {
        # The package catalog defines roots; MSBuild retains their full
        # product, analyzer and generator closure.
        ([ordered]@{
            solution = ([ordered]@{
                    path     = 'SharpProof.slnx'
                    projects = $packageBuildProjects
                }).AsReadOnly()
        }).AsReadOnly() | ConvertTo-Json -Depth 4 |
            Set-Content -LiteralPath $packageBuildFilter -Encoding utf8NoBOM
        Invoke-SharpProofSolutionBuild `
            -BuildConfiguration 'Release' -BuildTarget $packageBuildFilter `
            -AdditionalBuildArguments ([string[]]@(
                '/p:GeneratePackageOnBuild=false',
                "-bl:$(Join-Path $diagnostics 'build.binlog');ProjectImports=None",
                $repositoryCommitProperty))
        Invoke-SharpProofRequiredDotnet @(
            'pack', $packageBuildFilter, '--configuration', 'Release',
            '--output', $output, '--no-build', '--no-restore',
            '/p:GeneratePackageOnBuild=false',
            "-bl:$(Join-Path $diagnostics 'pack.binlog');ProjectImports=None",
            $repositoryCommitProperty)
    }
    finally {
        if (Test-Path -LiteralPath $packageBuildFilter) {
            Remove-Item -LiteralPath $packageBuildFilter -Force
        }
    }
}

switch ($Command) {
    'lint' {
        & (Join-Path $PSScriptRoot 'Invoke-SharpProofLint.ps1')
    }
    'format-check' {
        Invoke-RequiredScript 'eng/scripts/Format-Scripts.ps1' `
            'Repository script linting or formatting failed.' ([ordered]@{ VerifyNoChanges = $true }).AsReadOnly()
        Invoke-RequiredScript 'eng/scripts/Format-CSharp.ps1' `
            'Repository formatting check failed.' ([ordered]@{ VerifyNoChanges = $true }).AsReadOnly()
    }
    'doctor' {
        $doctorTarget = if ($PSBoundParameters.ContainsKey('Target')) { $Target } else { '' }
        & (Join-Path $PSScriptRoot 'Invoke-SharpProofDoctor.ps1') -Target $doctorTarget -Format $Format -TargetFramework $TargetFramework
    }
    'docs' {
        Invoke-RequiredScript 'eng/scripts/Test-SharpProofDocumentation.ps1' 'Documentation links failed.'
    }
    'quick' {
        Invoke-PipelineCommand 'test-changed' 'Debug' @('-Fast')
    }
    'pr' {
        & $PSCommandPath -Command 'pr-gates' -Configuration 'Release' `
            -SemanticCohort $SemanticCohort -TaskGroup $TaskGroup `
            -TestResultsDirectory $TestResultsDirectory `
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
    'layout' {
        & (Join-Path $repositoryRoot 'eng/scripts/Test-SharpProofRepositoryLayout.ps1') -EvaluateProjects
    }
    'restore' {
        Invoke-SharpProofRequiredDotnet @('restore', $Target, '--locked-mode')
    }
    'build' {
        Invoke-SharpProofRequiredDotnet @('restore', $Target, '--locked-mode')
        Invoke-SharpProofRequiredDotnet @(
            'build', $Target, '--configuration', $Configuration, '--no-restore')
        & (Join-Path $PSScriptRoot 'Export-SharpProofEmbeddedSymbols.ps1') -RepositoryRoot $repositoryRoot -Configuration $Configuration
    }
    'self-apply' {
        if ($SelfApplicationPhase -ne 'Samples') {
            $trackedProjects = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                    & git -C $repositoryRoot ls-files -- '*.csproj' |
                        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                        ForEach-Object {
                            Join-Path $repositoryRoot $_
                        } |
                        Sort-Object
                ))
            if ($LASTEXITCODE -ne 0 -or $trackedProjects.Length -eq 0) {
                throw 'self-apply requires a Git-backed repository with tracked project files.'
            }

            $sourceProjects = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                    $trackedProjects |
                        Where-Object {
                            $relative = [IO.Path]::GetRelativePath(
                                $repositoryRoot, $_).Replace('\', '/')
                            $relative.StartsWith('src/', [StringComparison]::Ordinal) -and
                            -not $relative.StartsWith('src/samples/', [StringComparison]::Ordinal)
                        }
                ))
            if ($sourceProjects.Length -eq 0) {
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
                    $ordinal, $sourceProjects.Length,
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

        }
        if ($SelfApplicationPhase -ne 'Sources') {
            # Package-backed samples exercise the same analyzer payload through
            # the supported package-consumer path.  The sample harness creates and
            # cleans its own isolated local feed and temporary build roots.
            Invoke-RequiredScript 'eng/scripts/Test-SharpProofSamples.ps1' `
                'SharpProof self-application sample validation failed.' `
            ([ordered]@{ Configuration = $Configuration }).AsReadOnly()
        }
    }
    'check' {
        & (Join-Path $repositoryRoot 'eng/scripts/Invoke-SharpProofDevCheck.ps1') `
            -Configuration $Configuration
    }
    'pr-gates' {
        if ($Configuration -ne 'Release') {
            throw "pr-gates requires -Configuration Release. " +
            "Use the test command for Debug validation."
        }
        Invoke-PipelineCommand 'docs' $Configuration
        & (Join-Path $repositoryRoot 'eng/scripts/Test-SharpProofRepositoryLayout.ps1') -EvaluateProjects
        Invoke-SharpProofSolutionBuild -BuildConfiguration $Configuration

        $prTestFilter = 'TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus'
        $prTestArguments = (New-ContainerArgumentBuilder -Values ([ordered]@{
                    Configuration = $Configuration; NoBuild = $true
                    TestFilter = $prTestFilter
                }).AsReadOnly()).ToImmutable()
        $semanticTestArgumentBuilder = $prTestArguments.ToBuilder()
        $semanticTestArgumentBuilder['SemanticCohort'] = $SemanticCohort
        $semanticTestArgumentBuilder['TaskGroup'] = $TaskGroup
        $semanticTestArgumentBuilder['TestResultsDirectory'] = $TestResultsDirectory
        $semanticTestArguments = $semanticTestArgumentBuilder.ToImmutable()
        Invoke-RequiredScript 'eng/scripts/Invoke-SharpProofSemanticTests.ps1' `
            'PR semantic validation failed.' $semanticTestArguments

        if (-not $SkipPackageTests) {
            Invoke-RequiredScript 'eng/scripts/Invoke-SharpProofPackageTests.ps1' `
                'PR package validation failed.' $prTestArguments
        }
    }
    'test' {
        $targetName = [IO.Path]::GetFileName($Target)
        $isPackageTestTarget = $targetName -in @(
            'SharpProof.Package.Test',
            'SharpProof.Package.IntegrationTests.csproj')
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
        'SharpProof.Package.IntegrationTests.csproj'
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
        $argumentBuilder = [System.Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $argumentBuilder.AddRange([string[]]@(
                'test', $Target, '--configuration', $Configuration, '--no-restore'))
        $argumentBuilder.AddRange([string[]]@($fastBuildArguments))
        if ($NoBuild) {
            $argumentBuilder.Add('--no-build')
        }
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $argumentBuilder.AddRange([string[]]@('--filter', $TestFilter))
        }
        Invoke-SharpProofRequiredDotnet ([string[]]@($argumentBuilder.ToImmutable()))
    }
    'test-changed' {
        $changedArguments = New-TestInvocationArguments
        & (Join-Path `
                $repositoryRoot 'eng/scripts/Invoke-SharpProofChangedTests.ps1') `
            @changedArguments
    }
    'semantic-tests' {
        $semanticArgumentBuilder = (New-TestInvocationArguments).ToBuilder()
        $semanticArgumentBuilder['SemanticCohort'] = $SemanticCohort
        $semanticArgumentBuilder['TaskGroup'] = $TaskGroup
        $semanticArgumentBuilder['TestResultsDirectory'] = $TestResultsDirectory
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $semanticArgumentBuilder['TestFilter'] = $TestFilter
        }
        $semanticArguments = $semanticArgumentBuilder.ToImmutable()
        & (Join-Path `
                $repositoryRoot 'eng/scripts/Invoke-SharpProofSemanticTests.ps1') `
            @semanticArguments
    }
    'portable-tests' {
        Invoke-SolutionTests 'SharpProof.Portable.Tests.slnf'
    }
    'worker-tests' {
        $workerTestProject =
        'src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj'
        Invoke-TestProject $workerTestProject
    }
    'package-tests' {
        Invoke-PackageTests
    }
    'samples' {
        Invoke-RequiredScript 'eng/scripts/Test-SharpProofSamples.ps1' `
            'Sample validation failed.' `
        ([ordered]@{ Configuration = $Configuration; PackageSource = $PackageSource }).AsReadOnly()
    }
    { $_ -in @('corpus', 'corpus-update') } {
        $gateMode = $Command
        $gateProject = 'tests/SharpProof.Gates/SharpProof.Gates.csproj'
        Invoke-SharpProofRequiredDotnet @('restore', $gateProject, '--locked-mode')
        Invoke-SharpProofRequiredDotnet @(
            'run', '--project', $gateProject,
            '--configuration', $Configuration,
            '--no-restore', '--', $gateMode)
    }
    'coverage' {
        Invoke-SharpProofSolutionBuild -BuildConfiguration 'Release'
        Invoke-RequiredScript 'eng/scripts/Invoke-SharpProofCoverage.ps1' `
            'Coverage collection failed.' ([ordered]@{ TestFilter = $TestFilter }).AsReadOnly()
    }
    'fuzz-nightly' {
        if ($Configuration -ne 'Release') {
            throw 'fuzz-nightly requires -Configuration Release.'
        }
        . (Join-Path $PSScriptRoot 'SharpProof.FuzzRunnerBuild.ps1')
        $fuzzBuildCommit = Get-SharpProofCleanFuzzSourceCommit -RepositoryRoot $repositoryRoot
        $fuzzBuildStartedUtc = [DateTime]::UtcNow
        Invoke-SharpProofSolutionBuild -BuildConfiguration 'Release'
        $fuzzBuildReceipt = New-SharpProofFuzzRunnerBuildReceipt `
            -RepositoryRoot $repositoryRoot -SourceCommit $fuzzBuildCommit `
            -BuildStartedUtc $fuzzBuildStartedUtc
        Invoke-RequiredScript 'eng/scripts/Invoke-SharpProofFuzzCampaign.ps1' `
            'Nightly fuzz campaign failed.' `
        ([ordered]@{
                OutputDirectory       = 'artifacts/fuzz/nightly'
                PreparedRunnerReceipt = $fuzzBuildReceipt
            }).AsReadOnly()
    }
    'dependency-audit' {
        Invoke-DependencyAudit
    }
    'pack-tool' {
        Invoke-RequiredScript 'eng/scripts/Pack-SharpProofTool.ps1' `
            'Optional Tool packing failed.' ([ordered]@{ Configuration = $Configuration }).AsReadOnly()
    }
    'pack' {
        Invoke-ProductPack
    }
}
