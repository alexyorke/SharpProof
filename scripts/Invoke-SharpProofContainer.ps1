[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('quick', 'pr', 'nightly', 'security', 'contract', 'restore', 'build', 'self-apply', 'check', 'pr-gates', 'test', 'test-changed', 'semantic-tests', 'portable-tests', 'worker-tests', 'package-tests', 'package-consumers', 'samples', 'corpus', 'corpus-update', 'performance', 'performance-smoke', 'gates', 'coverage', 'mutation', 'fuzz-nightly', 'dependency-audit', 'acceptance', 'pack', 'pilots', 'pilot-review', 'release-tag', 'release-baseline', 'release-plan', 'release-qualification', 'release-publish')]
    [string]$Command,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$Target = 'SharpProof.sln',

    [string]$PackageSource = '',

    [string]$TestFilter = '',

    [switch]$NoBuild,

    [switch]$ReuseTestHarness,

    [switch]$Fast
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repositoryRoot

Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
. (Join-Path $PSScriptRoot 'Get-SharpProofReleaseVersion.ps1')

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
if ($Fast -and $NoBuild) {
    throw '-Fast and -NoBuild cannot be combined.'
}
if ($ReuseTestHarness -and $Command -ne 'package-tests') {
    throw '-ReuseTestHarness is supported only for package-tests.'
}
$fastBuildArguments = if ($Fast) {
    @('-p:RunAnalyzersDuringBuild=false')
}
else {
    @()
}

function Invoke-DotNet([string[]]$Arguments) {
    $effectiveArguments = @(
        Add-SharpProofStaticGraphArgument -Arguments $Arguments
    )
    Invoke-SharpProofCheckedCommand `
        -Command 'dotnet' `
        -Arguments $effectiveArguments
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
        Invoke-DotNet @('restore', $ProjectPath, '--locked-mode')
        $buildArguments = @(
            'build', $ProjectPath, '--configuration', $Configuration,
            '--no-restore')
        $buildArguments += $fastBuildArguments
        Invoke-DotNet $buildArguments
    }

    $assembly = Get-SharpProofTestAssemblyPath `
        -ProjectPath $ProjectPath `
        -Configuration $Configuration
    $arguments = @('vstest', $assembly)
    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $arguments += '/TestCaseFilter:' + $TestFilter
    }
    Invoke-DotNet $arguments
}

function Invoke-SolutionTests([string]$SolutionPath) {
    if (-not $NoBuild) {
        Invoke-DotNet @('restore', $SolutionPath, '--locked-mode')
    }
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
    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $arguments += @('--filter', $TestFilter)
    }
    Invoke-DotNet $arguments
}

function Invoke-ForcedTerminationGateTest([string]$BuildConfiguration) {
    Invoke-DotNet @(
        'test',
        'SharpProof.Gates.Test/SharpProof.Gates.Test.csproj',
        '--configuration', $BuildConfiguration,
        '--no-build', '--no-restore',
        '--filter',
        'FullyQualifiedName~ForcedTerminationDeadlineIsStableAcrossLaunches')
}

function Invoke-SharpProofSolutionBuild(
    [string]$BuildConfiguration,
    [string[]]$AdditionalBuildArguments = @()) {
    Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
    $buildArguments = @(
        'build', 'SharpProof.sln', '--configuration', $BuildConfiguration,
        '--no-restore')
    $buildArguments += $AdditionalBuildArguments
    Invoke-DotNet $buildArguments
}

function Invoke-DependencyAudit {
    Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
    $output = Join-Path $repositoryRoot (
        'artifacts/dependency-audit/dependency-audit.json')
    Invoke-RequiredScript 'scripts/Test-SharpProofDependencyAudit.ps1' `
        'Dependency audit failed.' `
        @{
            SolutionPath = Join-Path $repositoryRoot 'SharpProof.sln'
            NuGetConfigurationPath = Join-Path $repositoryRoot 'NuGet.Config'; OutputPath = $output
        }
}

switch ($Command) {
    'quick' {
        Invoke-PipelineCommand 'test-changed' 'Debug' @('-Fast')
    }
    'pr' {
        Invoke-PipelineCommand 'pr-gates' 'Release'
    }
    'nightly' {
        Invoke-PipelineCommand 'mutation' 'Release'
        Invoke-PipelineCommand 'dependency-audit' 'Release'
        Invoke-PipelineCommand 'acceptance' 'Release'
        Invoke-PipelineCommand 'fuzz-nightly' 'Release'
    }
    'security' {
        Invoke-DependencyAudit
        Invoke-DotNet @(
            'build', 'SharpProof.sln', '--configuration', 'Release',
            '--no-restore')
    }
    'contract' {
        & (Join-Path $repositoryRoot `
            'scripts/Test-SharpProofContainerContract.ps1')
    }
    'restore' {
        Invoke-DotNet @('restore', $Target, '--locked-mode')
    }
    'build' {
        Invoke-DotNet @('restore', $Target, '--locked-mode')
        Invoke-DotNet @('build', $Target, '--configuration', $Configuration, '--no-restore')
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
                    $relative -notlike 'samples/*' -and
                        $relative -notlike 'eng/pilots/*'
                }
        )
        if ($sourceProjects.Count -eq 0) {
            throw 'self-apply found no source-tree projects to analyze.'
        }

        # Build the complete source tree once with the self lane disabled so
        # every analyzer and generator output is available as a stable input.
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        Invoke-DotNet @(
            'build', 'SharpProof.sln', '--configuration', $Configuration,
            '--no-restore', '--nologo',
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
            Invoke-DotNet @(
                'build', $project, '--configuration', $Configuration,
                '--no-restore', '--no-dependencies', '--nologo',
                '-p:SharpProofSelfApplication=true',
                '-p:SharpProofProfile=advisory',
                '-p:SharpProofFeatures=all',
                '-p:SharpProofVerify=false',
                '-p:GeneratePackageOnBuild=false')
        }

        if (-not [string]::IsNullOrWhiteSpace($PackageSource)) {
            # The source self-application builds can leave Roslyn's shared
            # compiler server holding source-built analyzer load contexts.
            # Stop it before package pilots so the package lane observes only
            # the candidate analyzer payload.
            Invoke-DotNet @('build-server', 'shutdown')
            $resolvedPackageSource = if ([IO.Path]::IsPathRooted($PackageSource)) {
                [IO.Path]::GetFullPath($PackageSource)
            }
            else {
                [IO.Path]::GetFullPath(
                    (Join-Path $repositoryRoot $PackageSource))
            }
            if (-not (Test-Path -LiteralPath $resolvedPackageSource -PathType Container)) {
                throw "self-apply package source is missing: '$resolvedPackageSource'."
            }
            Invoke-RequiredScript 'scripts/Test-SharpProofPilots.ps1' `
                'SharpProof self-application pilot validation failed.' `
                @{ PackageSource = $resolvedPackageSource }
        }

        # Package-backed samples exercise the same analyzer payload through
        # the supported package-consumer path.  The sample harness creates and
        # cleans its own isolated local feed and temporary build roots.  Keep
        # this after pilots because its pack restores may update lock files in
        # the disposable checkout, which would violate the pilot clean guard.
        Invoke-RequiredScript 'scripts/Test-SharpProofSamples.ps1' `
            'SharpProof self-application sample validation failed.' `
            @{ Configuration = $Configuration }
    }
    'check' {
        & (Join-Path $repositoryRoot 'scripts/Invoke-SharpProofDevCheck.ps1') `
            -Configuration $Configuration
    }
    'pr-gates' {
        if ($Configuration -ne 'Release') {
            throw "pr-gates requires -Configuration Release. " +
                "Use the test command for Debug validation."
        }
        & (Join-Path $repositoryRoot `
            'scripts/Test-SharpProofContainerContract.ps1')
        Invoke-RequiredScript `
            'scripts/Test-SharpProofGeneratedOutputs.ps1' `
            'Generated-output verification failed.'
        Invoke-SharpProofSolutionBuild -BuildConfiguration $Configuration

        $performanceOutput = Join-Path $repositoryRoot (
            'artifacts/ci/performance.json')
        Invoke-RequiredScript 'scripts/Invoke-SharpProofGateEvidence.ps1' `
            'PR performance validation failed.' `
            @{ Gate = 'performance'; OutputPath = $performanceOutput }

        Invoke-ForcedTerminationGateTest $Configuration
        $prTestFilter = 'TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus'
        $prTestArguments = @{
            Configuration = $Configuration; NoBuild = $true
            TestFilter = $prTestFilter
        }
        Invoke-RequiredScript 'scripts/Invoke-SharpProofSemanticTests.ps1' `
            'PR semantic validation failed.' $prTestArguments

        Invoke-RequiredScript 'scripts/Invoke-SharpProofPackageTests.ps1' `
            'PR package validation failed.' $prTestArguments
    }
    'test' {
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
            $Target.EndsWith('.slnf', [StringComparison]::OrdinalIgnoreCase)) {
            Invoke-SolutionTests $Target
            break
        }
        if (-not $NoBuild) {
            Invoke-DotNet @('restore', $Target, '--locked-mode')
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
        Invoke-DotNet $arguments
    }
    'test-changed' {
        $changedArguments = New-TestInvocationArguments
        & (Join-Path `
            $repositoryRoot 'scripts/Invoke-SharpProofChangedTests.ps1') `
            @changedArguments
    }
    'semantic-tests' {
        $semanticArguments = New-TestInvocationArguments
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $semanticArguments.TestFilter = $TestFilter
        }
        & (Join-Path `
            $repositoryRoot 'scripts/Invoke-SharpProofSemanticTests.ps1') `
            @semanticArguments
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
        $packageArguments = New-TestInvocationArguments -Additional @{
            TestFilter = $TestFilter
            PackageSource = $PackageSource
        }
        Invoke-RequiredScript 'scripts/Invoke-SharpProofPackageTests.ps1' `
            'Package tests failed.' $packageArguments
    }
    'package-consumers' {
        if ([string]::IsNullOrWhiteSpace($PackageSource)) {
            throw 'package-consumers requires -PackageSource.'
        }
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
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
        $consumerEvidence = Join-Path `
            $repositoryRoot `
            'artifacts/release-qualification/package-consumers.json'
        [IO.Directory]::CreateDirectory(
            [IO.Path]::GetDirectoryName($consumerEvidence)) | Out-Null
        [IO.File]::WriteAllText(
            $consumerEvidence,
            (([ordered]@{
                schemaVersion = 1
                status = 'passed'
                commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
                packageSource = [IO.Path]::GetRelativePath(
                    $repositoryRoot,
                    [IO.Path]::GetFullPath($PackageSource)).Replace('\', '/')
                packageArtifacts = @(
                    Get-ChildItem -LiteralPath $PackageSource -File |
                        Where-Object {
                            $_.Extension -in @('.nupkg', '.snupkg')
                        } |
                        Sort-Object Name |
                        ForEach-Object {
                            [ordered]@{
                                fileName = $_.Name
                                bytes = [int64]$_.Length
                            }
                        }
                )
            } | ConvertTo-Json) + "`n"),
            [Text.UTF8Encoding]::new($false))
        & (Join-Path $repositoryRoot `
            'scripts/Write-SharpProofQualificationReceipt.ps1') `
            -Gate package-consumers `
            -EvidencePath $consumerEvidence
    }
    'samples' {
        Invoke-RequiredScript 'scripts/Test-SharpProofSamples.ps1' `
            'Sample validation failed.' `
            @{ Configuration = $Configuration; PackageSource = $PackageSource }
    }
    { $_ -in @('corpus', 'corpus-update', 'gates', 'performance-smoke') } {
        $gateMode = if ($Command -ceq 'gates') { 'all' } else { $Command }
        $gateProject = 'SharpProof.Gates/SharpProof.Gates.csproj'
        Invoke-DotNet @('restore', $gateProject, '--locked-mode')
        Invoke-DotNet @(
            'run', '--project', $gateProject,
            '--configuration', $Configuration,
            '--no-restore', '--', $gateMode)
    }
    'performance' {
        Invoke-SharpProofSolutionBuild -BuildConfiguration 'Release'
        $output = Join-Path $repositoryRoot 'artifacts/ci/performance.json'
        Invoke-RequiredScript 'scripts/Invoke-SharpProofGateEvidence.ps1' `
            'Performance validation failed.' `
            @{ Gate = 'performance'; OutputPath = $output }
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
            CoverageRoot = $coverageRoot
            ComparisonRef = $comparisonRef
            SummaryPath = $summaryPath
        }
        if (-not [string]::IsNullOrWhiteSpace(
                (& git status --porcelain))) {
            $coverageArguments.IncludeWorkingTree = $true
        }
        Invoke-RequiredScript 'scripts/Test-SharpProofCoverage.ps1' `
            'Coverage validation failed.' $coverageArguments
        & (Join-Path $repositoryRoot `
            'scripts/Write-SharpProofQualificationReceipt.ps1') `
            -Gate coverage `
            -EvidencePath $summaryPath
    }
    'mutation' {
        $mutationOutput = 'artifacts/mutation/trusted-mutations.json'
        [IO.Directory]::CreateDirectory((Join-Path $repositoryRoot (
                    Split-Path -Parent $mutationOutput))) | Out-Null
        $commit = (& git rev-parse HEAD).Trim()
        Invoke-RequiredScript `
            'scripts/Invoke-SharpProofTrustedMutationsParallel.ps1' `
            'Trusted mutation validation failed.' `
            @{ Configuration = $Configuration; OutputPath = $mutationOutput; ExpectedCommit = $commit }
        & (Join-Path $repositoryRoot `
            'scripts/Write-SharpProofQualificationReceipt.ps1') `
            -Gate mutation `
            -EvidencePath (Join-Path $repositoryRoot $mutationOutput)
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
    'acceptance' {
        Invoke-RequiredScript 'eng/acceptance/Verify.ps1' `
            'Acceptance validation failed.' `
            @{ Configuration = $Configuration }
        if ($Configuration -ceq 'Release') {
            Invoke-ForcedTerminationGateTest 'Release'
        }
        & (Join-Path $repositoryRoot `
            'scripts/Write-SharpProofQualificationReceipt.ps1') `
            -Gate ('acceptance-' + $Configuration.ToLowerInvariant()) `
            -EvidencePath (Join-Path `
                $repositoryRoot `
                ('artifacts/timings/acceptance-' +
                    $Configuration.ToLowerInvariant() + '.json'))
    }
    'pack' {
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofReadme.ps1')
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
        $manifest = Get-Content (Join-Path $repositoryRoot 'scripts/package-projects.json') -Raw | ConvertFrom-Json
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
        foreach ($project in @($manifest.projects)) {
            Invoke-DotNet @(
                'pack', [string]$project, '--configuration', 'Release',
                '--output', $output, '--no-build', '--no-restore',
                '/p:GeneratePackageOnBuild=false',
                $repositoryCommitProperty)
        }
        Invoke-RequiredScript 'scripts/Test-SharpProofPackageConsumers.ps1' `
            'Package graph validation failed.' `
            @{ PackageSource = $output; ValidatePackageSourceOnly = $true }
        Invoke-RequiredScript 'scripts/New-SharpProofReleaseEvidence.ps1' `
            'Release evidence generation failed.' @{ PackageSource = $output }
        $version = Get-SharpProofReleaseVersion -RepositoryRoot $repositoryRoot
        Invoke-RequiredScript 'scripts/Test-SharpProofReleaseArtifacts.ps1' `
            'Release artifact validation failed.' `
            @{ PackageSource = $output; ExpectedTag = 'v' + $version }
    }
    'pilots' {
        if ([string]::IsNullOrWhiteSpace($PackageSource)) {
            $PackageSource = Join-Path $repositoryRoot 'artifacts/container-packages'
        }
        Invoke-RequiredScript 'scripts/Test-SharpProofPilots.ps1' `
            'Pilot validation failed.' @{ PackageSource = $PackageSource }
    }
    'pilot-review' {
        Invoke-RequiredScript 'scripts/Complete-SharpProofPilotReview.ps1' `
            'Pilot review validation failed.' `
            @{
                SourceReportPath = Join-Path $repositoryRoot 'artifacts/pilots/report.json'
                ReviewLedgerPath = Join-Path $repositoryRoot 'artifacts/pilots/review-ledger.json'
                OutputPath = Join-Path $repositoryRoot 'artifacts/pilots/reviewed-report.json'
            }
        & (Join-Path $repositoryRoot `
            'scripts/Write-SharpProofQualificationReceipt.ps1') `
            -Gate pilots `
            -EvidencePath (Join-Path $repositoryRoot 'artifacts/pilots/reviewed-report.json')
    }
    'release-tag' {
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofReleaseContainer.ps1') `
            -Mode ValidateTag
    }
    'release-baseline' {
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofReleaseContainer.ps1') `
            -Mode ResolveCoverageBaseline
    }
    'release-plan' {
        if ([string]::IsNullOrWhiteSpace($PackageSource)) {
            throw 'release-plan requires -PackageSource.'
        }
        $planDirectory = Join-Path `
            $repositoryRoot 'artifacts/release-qualification'
        [IO.Directory]::CreateDirectory($planDirectory) | Out-Null
        & (Join-Path $repositoryRoot `
            'scripts/Publish-SharpProofRelease.ps1') `
            -PackageSource $PackageSource `
            -PlanOnly `
            -PlanOutputPath (Join-Path $planDirectory 'publication-plan.json')
    }
    'release-qualification' {
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofReadme.ps1')
        $releaseArguments = @{ Mode = 'WriteQualificationEvidence' }
        if (-not [string]::IsNullOrWhiteSpace($PackageSource)) {
            $releaseArguments.PackageSource = $PackageSource
        }
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofReleaseContainer.ps1') `
            @releaseArguments
    }
    'release-publish' {
        $releaseArguments = @{ Mode = 'Publish' }
        if (-not [string]::IsNullOrWhiteSpace($PackageSource)) {
            $releaseArguments.PackageSource = $PackageSource
        }
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofReleaseContainer.ps1') `
            @releaseArguments
    }
}
