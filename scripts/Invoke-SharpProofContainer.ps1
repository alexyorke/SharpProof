[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('restore', 'build', 'test', 'portable-tests', 'worker-tests', 'package-tests', 'package-consumers', 'performance', 'coverage', 'mutation', 'dependency-audit', 'acceptance', 'pack', 'pilots')]
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

$portableTests = @(
    'SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj',
    'SharpProof.Attributes.Test/SharpProof.Attributes.Test.csproj',
    'SharpProof.ContractForGenerator.Test/SharpProof.ContractForGenerator.Test.csproj',
    'SharpProof.Contracts.Test/SharpProof.Contracts.Test.csproj',
    'SharpProof.Dataflow.Test/SharpProof.Dataflow.Test.csproj',
    'SharpProof.Effects.Test/SharpProof.Effects.Test.csproj',
    'SharpProof.Frontend.Test/SharpProof.Frontend.Test.csproj',
    'SharpProof.Ir.Test/SharpProof.Ir.Test.csproj',
    'SharpProof.Meta.Analyzers.Test/SharpProof.Meta.Analyzers.Test.csproj',
    'SharpProof.Smt.Test/SharpProof.Smt.Test.csproj',
    'SharpProof.Specs.Test/SharpProof.Specs.Test.csproj',
    'SharpProof.Summaries.Test/SharpProof.Summaries.Test.csproj',
    'SharpProof.Testing.Test/SharpProof.Testing.Test.csproj',
    'SharpProof.Verify.Test/SharpProof.Verify.Test.csproj'
)

switch ($Command) {
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
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $arguments += @('--filter', $TestFilter)
        }
        Invoke-DotNet $arguments
    }
    'portable-tests' {
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        foreach ($project in $portableTests) {
            $arguments = @(
                'test', $project, '--configuration', $Configuration, '--no-restore')
            if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
                $arguments += @('--filter', $TestFilter)
            }
            Invoke-DotNet $arguments
        }
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
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        $arguments = @(
            'test',
            'SharpProof.Package.Test/SharpProof.Package.Test.csproj',
            '--configuration', $Configuration, '--no-restore')
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $arguments += @('--filter', $TestFilter)
        }
        Invoke-DotNet $arguments
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
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofTrustedMutations.ps1') `
            -Configuration $Configuration `
            -OutputPath $mutationOutput `
            -ExpectedCommit $commit
        if ($LASTEXITCODE -ne 0) { throw 'Trusted mutation validation failed.' }
    }
    'dependency-audit' {
        $output = Join-Path $repositoryRoot (
            'artifacts/dependency-audit/dependency-audit.json')
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofDependencyAudit.ps1') `
            -SolutionPath (Join-Path $repositoryRoot 'SharpProof.sln') `
            -NuGetConfigurationPath (Join-Path $repositoryRoot 'NuGet.Config') `
            -OutputPath $output
        if ($LASTEXITCODE -ne 0) { throw 'Dependency audit failed.' }
    }
    'acceptance' {
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        & (Join-Path $repositoryRoot 'eng/acceptance/Verify.ps1') `
            -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Acceptance validation failed.' }
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
        foreach ($project in @($manifest.projects)) {
            Invoke-DotNet @(
                'pack', [string]$project, '--configuration', 'Release',
                '--output', $output,
                '/p:RepositoryCommit=' + (& git rev-parse HEAD).Trim())
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
}
