[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('contract', 'restore', 'build', 'test', 'portable-tests', 'worker-tests', 'package-tests', 'package-consumers', 'samples', 'performance', 'coverage', 'mutation', 'dependency-audit', 'acceptance', 'pack', 'pilots', 'release-tag', 'release-baseline', 'release-plan', 'release-qualification', 'release-publish')]
    [string]$Command,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$Target = 'SharpProof.sln',

    [string]$PackageSource = '',

    [string]$TestFilter = ''
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
if ($env:SHARPPROOF_CONTAINER -cne '1' -or
    -not (Test-Path -LiteralPath '/etc/sharpproof/container-contract.json' -PathType Leaf)) {
    throw 'SharpProof container commands require the canonical container contract.'
}

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$testProjectParallelism = Get-SharpProofTestProjectParallelism `
    -RepositoryRoot $repositoryRoot

switch ($Command) {
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
    'test' {
        Invoke-DotNet @('restore', $Target, '--locked-mode')
        $arguments = @(
            'test', $Target, '--configuration', $Configuration, '--no-restore')
        if ($Target.EndsWith('.sln', [StringComparison]::OrdinalIgnoreCase) -or
            $Target.EndsWith('.slnf', [StringComparison]::OrdinalIgnoreCase)) {
            $arguments += "/m:$testProjectParallelism"
        }
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $arguments += @('--filter', $TestFilter)
        }
        Invoke-DotNet $arguments
    }
    'portable-tests' {
        $target = 'SharpProof.Portable.Tests.slnf'
        Invoke-DotNet @('restore', $target, '--locked-mode')
        $arguments = @(
            'test', $target, '--configuration', $Configuration,
            '--no-restore', "/m:$testProjectParallelism")
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $arguments += @('--filter', $TestFilter)
        }
        Invoke-DotNet $arguments
    }
    'worker-tests' {
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        $arguments = @(
            'test',
            'SharpProof.Worker.Test/SharpProof.Worker.Test.csproj',
            '--configuration', $Configuration, '--no-restore')
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $arguments += @('--filter', $TestFilter)
        }
        Invoke-DotNet $arguments
    }
    'package-tests' {
        & (Join-Path `
            $repositoryRoot 'scripts/Invoke-SharpProofPackageTests.ps1') `
            -Configuration $Configuration `
            -TestFilter $TestFilter `
            -PackageSource $PackageSource
        if ($LASTEXITCODE -ne 0) { throw 'Package tests failed.' }
    }
    'package-consumers' {
        if ([string]::IsNullOrWhiteSpace($PackageSource)) {
            throw 'package-consumers requires -PackageSource.'
        }
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofPackageConsumers.ps1') `
            -Configuration $Configuration `
            -ExpectedSmt Required `
            -PackageSource $PackageSource
        if ($LASTEXITCODE -ne 0) { throw 'Package consumer validation failed.' }
        $toolchain = Get-Content -LiteralPath (Join-Path `
            $repositoryRoot 'eng/container/toolchain.json') -Raw |
            ConvertFrom-Json
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofPackageConsumers.ps1') `
            -Configuration $Configuration `
            -ExpectedSmt Required `
            -PackageSource $PackageSource `
            -ConsumerSdkVersion ([string]$toolchain.dotnet.minimumSdkVersion) `
            -FrameworkConsumersOnly
        if ($LASTEXITCODE -ne 0) {
            throw 'Minimum-SDK package consumer validation failed.'
        }
    }
    'samples' {
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofSamples.ps1') `
            -Configuration $Configuration `
            -ExpectedSmt Required `
            -PackageSource $PackageSource
        if ($LASTEXITCODE -ne 0) { throw 'Sample validation failed.' }
    }
    'performance' {
        $output = Join-Path $repositoryRoot 'artifacts/ci/performance.json'
        & (Join-Path $repositoryRoot 'scripts/Invoke-SharpProofGateEvidence.ps1') `
            -Gate performance `
            -OutputPath $output
        if ($LASTEXITCODE -ne 0) { throw 'Performance validation failed.' }
    }
    'coverage' {
        Invoke-DotNet @(
            'restore', 'SharpProof.sln', '--locked-mode')
        Invoke-DotNet @(
            'build', 'SharpProof.sln', '--configuration', 'Release',
            '--no-restore')
        $coverageRoot = Join-Path $repositoryRoot (
            'artifacts/coverage/container-' + [Guid]::NewGuid().ToString('N'))
        $coverageCollectionArguments = @{
            ResultsDirectory = $coverageRoot
        }
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $coverageCollectionArguments.TestFilter = $TestFilter
        }
        & (Join-Path $repositoryRoot 'scripts/Invoke-SharpProofCoverage.ps1') `
            @coverageCollectionArguments
        if ($LASTEXITCODE -ne 0) { throw 'Coverage collection failed.' }
        $comparisonRef = if (-not [string]::IsNullOrWhiteSpace(
                $env:SHARPPROOF_COVERAGE_COMPARISON_REF)) {
            $env:SHARPPROOF_COVERAGE_COMPARISON_REF
        }
        else {
            'HEAD^'
        }
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
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofCoverage.ps1') `
            @coverageArguments
        if ($LASTEXITCODE -ne 0) { throw 'Coverage validation failed.' }
    }
    'mutation' {
        $mutationOutput = 'artifacts/mutation/trusted-mutations.json'
        [IO.Directory]::CreateDirectory((Join-Path $repositoryRoot (
                    Split-Path -Parent $mutationOutput))) | Out-Null
        $commit = (& git rev-parse HEAD).Trim()
        & (Join-Path `
            $repositoryRoot `
            'scripts/Invoke-SharpProofTrustedMutationsParallel.ps1') `
            -Configuration $Configuration `
            -OutputPath $mutationOutput `
            -ExpectedCommit $commit
        if ($LASTEXITCODE -ne 0) { throw 'Trusted mutation validation failed.' }
    }
    'dependency-audit' {
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        $output = Join-Path $repositoryRoot (
            'artifacts/dependency-audit/dependency-audit.json')
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofDependencyAudit.ps1') `
            -SolutionPath (Join-Path $repositoryRoot 'SharpProof.sln') `
            -NuGetConfigurationPath (Join-Path $repositoryRoot 'NuGet.Config') `
            -OutputPath $output
        if ($LASTEXITCODE -ne 0) { throw 'Dependency audit failed.' }
    }
    'acceptance' {
        $restoreTimer = [Diagnostics.Stopwatch]::StartNew()
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        $restoreTimer.Stop()
        $env:SHARPPROOF_ACCEPTANCE_RESTORE_MILLISECONDS =
            [string][long]$restoreTimer.Elapsed.TotalMilliseconds
        try {
            & (Join-Path $repositoryRoot 'eng/acceptance/Verify.ps1') `
                -Configuration $Configuration
            if ($LASTEXITCODE -ne 0) {
                throw 'Acceptance validation failed.'
            }
        }
        finally {
            Remove-Item Env:SHARPPROOF_ACCEPTANCE_RESTORE_MILLISECONDS `
                -ErrorAction SilentlyContinue
        }
    }
    'pack' {
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
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
        Invoke-DotNet @(
            'build', 'SharpProof.sln', '--configuration', 'Release',
            '--no-restore', '/p:GeneratePackageOnBuild=false',
            $repositoryCommitProperty)
        foreach ($project in @($manifest.projects)) {
            Invoke-DotNet @(
                'pack', [string]$project, '--configuration', 'Release',
                '--output', $output, '--no-build', '--no-restore',
                '/p:GeneratePackageOnBuild=false',
                $repositoryCommitProperty)
        }
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofPackageConsumers.ps1') `
            -PackageSource $output `
            -ValidatePackageSourceOnly
        if ($LASTEXITCODE -ne 0) { throw 'Package graph validation failed.' }
        & (Join-Path $repositoryRoot 'scripts/New-SharpProofReleaseEvidence.ps1') `
            -PackageSource $output
        if ($LASTEXITCODE -ne 0) { throw 'Release evidence generation failed.' }
        [xml]$release = Get-Content (Join-Path $repositoryRoot 'SharpProof.Release.props') -Raw
        $prefix = [string]$release.Project.PropertyGroup.SharpProofVersionPrefix
        $version = ([string]$release.Project.PropertyGroup.SharpProofPackageVersion).Replace(
            '$(SharpProofVersionPrefix)', $prefix)
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofReleaseArtifacts.ps1') `
            -PackageSource $output `
            -ExpectedTag ('v' + $version)
        if ($LASTEXITCODE -ne 0) { throw 'Release artifact validation failed.' }
    }
    'pilots' {
        if ([string]::IsNullOrWhiteSpace($PackageSource)) {
            $PackageSource = Join-Path $repositoryRoot 'artifacts/container-packages'
        }
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofPilots.ps1') -PackageSource $PackageSource
        if ($LASTEXITCODE -ne 0) { throw 'Pilot validation failed.' }
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
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofReleaseContainer.ps1') `
            -Mode WriteQualificationEvidence `
            -PackageSource $PackageSource
    }
    'release-publish' {
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofReleaseContainer.ps1') `
            -Mode Publish `
            -PackageSource $PackageSource
    }
}
