[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$acceptanceRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $acceptanceRoot '..\..')).Path
$contractPath = Join-Path $acceptanceRoot 'contract.json'
$wrapperPath = Join-Path $repositoryRoot 'scripts\Invoke-SharpProofDotnet.ps1'
$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
$packagePropsPath = Join-Path `
    $repositoryRoot `
    'SharpProof.Package\buildTransitive\SharpProof.props'
[xml]$packageProps = Get-Content -LiteralPath $packagePropsPath -Raw

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        $Actual,

        [Parameter(Mandatory = $true)]
        $Expected,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($Actual -ne $Expected) {
        throw "$Name must be '$Expected'; found '$Actual'."
    }
}

function Get-MsBuildProperty {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $node = $packageProps.SelectSingleNode(
        "/Project/PropertyGroup/$Name")
    if ($null -eq $node) {
        throw "Required package property '$Name' is missing."
    }
    return $node.InnerText
}

function Invoke-SharpProofDotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [int]$TimeoutSeconds = 300
    )

    & $wrapperPath `
        -MemoryLimitMb ([int]$contract.worker.maximumMemoryMiB) `
        -TimeoutSeconds $TimeoutSeconds `
        @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Assert-Equal $contract.schemaVersion 2 'schemaVersion'
Assert-Equal $contract.releaseLine '0.2.0-preview' 'releaseLine'
Assert-Equal $contract.flagship 'effects' 'flagship'
Assert-Equal $contract.analyzer.defaultMode 'off' 'analyzer.defaultMode'
Assert-Equal $contract.analyzer.defaultDiagnosticSeverity 'Info' 'analyzer.defaultDiagnosticSeverity'
Assert-Equal $contract.analyzer.diagnosticsEnabledByDefault $false 'analyzer.diagnosticsEnabledByDefault'
Assert-Equal $contract.analyzer.unsupportedCallableBehavior 'silent' 'analyzer.unsupportedCallableBehavior'
Assert-Equal ($contract.supportedTargetFrameworks -join ',') 'netstandard2.0,net8.0,net472' 'supportedTargetFrameworks'
Assert-Equal $contract.worker.protocolVersion 2 'worker.protocolVersion'
Assert-Equal $contract.worker.maximumParallelism 4 'worker.maximumParallelism'
Assert-Equal $contract.worker.maximumMemoryMiB 2048 'worker.maximumMemoryMiB'
Assert-Equal $contract.worker.queryRlimit 3000000 'worker.queryRlimit'
Assert-Equal $contract.worker.methodRlimit 20000000 'worker.methodRlimit'
Assert-Equal $contract.worker.maximumMethodWallSeconds 10 'worker.maximumMethodWallSeconds'
Assert-Equal $contract.worker.maximumProjectWallSeconds 300 'worker.maximumProjectWallSeconds'
Assert-Equal $contract.worker.forcedTerminationMilliseconds 1000 'worker.forcedTerminationMilliseconds'
Assert-Equal $contract.cache.schemaVersion 2 'cache.schemaVersion'
Assert-Equal $contract.cache.maximumMiB 512 'cache.maximumMiB'
Assert-Equal ($contract.cache.cacheableOutcomes -join ',') 'Proven,Refuted' 'cache.cacheableOutcomes'
Assert-Equal `
    (Get-MsBuildProperty 'SharpProofVerifyQueryRlimit') `
    ([string]$contract.worker.queryRlimit) `
    'SharpProofVerifyQueryRlimit'
Assert-Equal `
    (Get-MsBuildProperty 'SharpProofVerifyMethodRlimit') `
    ([string]$contract.worker.methodRlimit) `
    'SharpProofVerifyMethodRlimit'
Assert-Equal `
    (Get-MsBuildProperty 'SharpProofVerifyMethodWallTimeMilliseconds') `
    ([string]([int]$contract.worker.maximumMethodWallSeconds * 1000)) `
    'SharpProofVerifyMethodWallTimeMilliseconds'
Assert-Equal `
    (Get-MsBuildProperty 'SharpProofVerifyProjectWallTimeMilliseconds') `
    ([string]([int]$contract.worker.maximumProjectWallSeconds * 1000)) `
    'SharpProofVerifyProjectWallTimeMilliseconds'
Assert-Equal `
    (Get-MsBuildProperty 'SharpProofVerifyMaxParallelism') `
    ([string]$contract.worker.maximumParallelism) `
    'SharpProofVerifyMaxParallelism'
Assert-Equal `
    (Get-MsBuildProperty 'SharpProofVerifyProcessMemoryLimitBytes') `
    ([string]([int64]$contract.worker.maximumMemoryMiB * 1024 * 1024)) `
    'SharpProofVerifyProcessMemoryLimitBytes'
Assert-Equal `
    (Get-MsBuildProperty 'SharpProofVerifyTerminationGraceMilliseconds') `
    ([string]$contract.worker.forcedTerminationMilliseconds) `
    'SharpProofVerifyTerminationGraceMilliseconds'
Assert-Equal `
    (Get-MsBuildProperty 'SharpProofVerifyCacheMaximumBytes') `
    ([string]([int64]$contract.cache.maximumMiB * 1024 * 1024)) `
    'SharpProofVerifyCacheMaximumBytes'

Push-Location $repositoryRoot
try {
    $kernelPaths = @($contract.trustedKernel.paths)
    $kernelMaximum = [int]$contract.trustedKernel.maximumNonblankLines
    if ($kernelPaths.Count -eq 0 -or $kernelMaximum -le 0) {
        throw 'The trusted-kernel LOC contract must declare paths and a positive limit.'
    }
    $kernelNonblankLines = 0
    foreach ($relativeKernelPath in $kernelPaths) {
        $kernelPath = [IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot ([string]$relativeKernelPath)))
        if (-not $kernelPath.StartsWith(
                $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $kernelPath -PathType Leaf)) {
            throw "Invalid trusted-kernel path: $relativeKernelPath"
        }
        $kernelNonblankLines += @(
            Get-Content -LiteralPath $kernelPath |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        ).Count
    }
    if ($kernelNonblankLines -gt $kernelMaximum) {
        throw "Trusted-kernel nonblank LOC $kernelNonblankLines exceeds " +
            "the contract limit $kernelMaximum."
    }
    Write-Host (
        "Trusted-kernel nonblank lines: $kernelNonblankLines " +
        "(maximum $kernelMaximum)")

    & (Join-Path $repositoryRoot 'scripts\Test-ProductionCSharpSize.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw 'The production C# size ratchet failed.'
    }

    if (-not $SkipBuild) {
        Invoke-SharpProofDotnet `
            -Arguments @('build', 'SharpProof.sln', '-c', $Configuration, '--no-restore') `
            -TimeoutSeconds ([int]$contract.worker.maximumProjectWallSeconds)
    }

    $testProjects = @(
        'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj',
        'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj',
        'SharpProof.Attributes.Test\SharpProof.Attributes.Test.csproj',
        'SharpProof.ContractForGenerator.Test\SharpProof.ContractForGenerator.Test.csproj',
        'SharpProof.Contracts.Test\SharpProof.Contracts.Test.csproj',
        'SharpProof.Dataflow.Test\SharpProof.Dataflow.Test.csproj',
        'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj',
        'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj',
        'SharpProof.Ir.Test\SharpProof.Ir.Test.csproj',
        'SharpProof.Meta.Analyzers.Test\SharpProof.Meta.Analyzers.Test.csproj',
        'SharpProof.Package.Test\SharpProof.Package.Test.csproj',
        'SharpProof.Smt.Test\SharpProof.Smt.Test.csproj',
        'SharpProof.Specs.Test\SharpProof.Specs.Test.csproj',
        'SharpProof.Testing.Test\SharpProof.Testing.Test.csproj',
        'SharpProof.Gates.Test\SharpProof.Gates.Test.csproj',
        'SharpProof.Fuzz.Test\SharpProof.Fuzz.Test.csproj',
        'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj',
        'SharpProof.Verify.Test\SharpProof.Verify.Test.csproj'
    )

    foreach ($testProject in $testProjects) {
        $resolvedTestProject = Join-Path $repositoryRoot $testProject
        if (-not (Test-Path -LiteralPath $resolvedTestProject -PathType Leaf)) {
            throw "Required test project is missing: $testProject"
        }

        Invoke-SharpProofDotnet -Arguments @(
            'test',
            $testProject,
            '-c',
            $Configuration,
            '--no-build',
            '--logger',
            'console;verbosity=minimal'
        )
    }

    Invoke-SharpProofDotnet -Arguments @(
        'run',
        '--project',
        'Tools\SharpProof.Fuzz\SharpProof.Fuzz.csproj',
        '-c',
        $Configuration,
        '--no-build',
        '--',
        '--cases',
        [string]$contract.fuzz.pullRequestCases,
        '--seed',
        '23063',
        '--max-parallelism',
        [string]$contract.fuzz.maximumParallelism
    )

    Invoke-SharpProofDotnet -Arguments @(
        'run',
        '--project',
        'SharpProof.Gates\SharpProof.Gates.csproj',
        '-c',
        $Configuration,
        '--no-build',
        '--',
        'all'
    )
}
finally {
    Pop-Location
}

Write-Host 'SharpProof acceptance checks passed.'
