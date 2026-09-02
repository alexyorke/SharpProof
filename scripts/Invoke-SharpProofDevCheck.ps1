[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 1800,

    [switch]$PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
    throw 'The developer check requires the canonical Linux container.'
}
$planScript = Join-Path $PSScriptRoot 'Get-SharpProofDevCheckPlan.ps1'
Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
$dotnetWrapper = Get-SharpProofDotnetWrapperPath
$buildParallelism = Get-SharpProofBuildParallelism `
    -RepositoryRoot $repositoryRoot
$commandPlanJson = & $planScript -Configuration $Configuration
if ($PlanOnly) {
    $commandPlanJson
    return
}
$commandPlan = $commandPlanJson | ConvertFrom-Json
if ([int]$commandPlan.schemaVersion -ne 1 -or
    [string]$commandPlan.configuration -cne $Configuration) {
    throw 'Developer-check command plan is invalid.'
}
$plannedCommands = @($commandPlan.commands)
function Get-RequiredPlanCommand {
    param([Parameter(Mandatory = $true)][string]$Id)

    $matches = @($plannedCommands | Where-Object {
            [string]$_.id -ceq $Id
        })
    if ($matches.Count -ne 1) {
        throw "Developer-check command plan must contain exactly one '$Id' row."
    }
    return $matches[0]
}

$restoreCommand = Get-RequiredPlanCommand 'restore'
$solutionBuildCommand = Get-RequiredPlanCommand 'solution-build'
$semanticTestsCommand = Get-RequiredPlanCommand 'semantic-tests'
$performanceSmokeCommand = Get-RequiredPlanCommand 'performance-smoke'
$packageProductBuildCommands = @($plannedCommands | Where-Object {
        [string]$_.id -ceq 'package-product-build'
    })
if ($packageProductBuildCommands.Count -gt 1) {
    throw 'Developer-check command plan contains duplicate package product builds.'
}
$packagePackCommands = @($plannedCommands | Where-Object {
        [string]$_.id -like 'package-pack:*'
    })
if ($packagePackCommands.Count -ne 3 -or
    @($packagePackCommands | Where-Object {
            [string]$_.configuration -cne 'Release' -or
            -not [bool]$_.noBuild
        }).Count -ne 0) {
    throw 'Developer-check package-pack rows are invalid.'
}
if ([string]$restoreCommand.configuration -cne $Configuration -or
    [string]$solutionBuildCommand.configuration -cne $Configuration -or
    [string]$semanticTestsCommand.configuration -cne $Configuration -or
    [string]$performanceSmokeCommand.configuration -cne $Configuration) {
    throw 'Developer-check phase configurations do not match the requested configuration.'
}
$packageProductBuild = $packageProductBuildCommands.Count -eq 1
$timings = [Collections.Generic.List[object]]::new()
$campaign = [Diagnostics.Stopwatch]::StartNew()

Invoke-SharpProofTimedPhase -Name 'restore' -Timings $timings -Action {
    & $dotnetWrapper -TimeoutSeconds $TimeoutSeconds `
        restore SharpProof.sln --locked-mode /nodeReuse:false
    if ($LASTEXITCODE -ne 0) {
        throw 'Developer-check restore failed.'
    }
}
Invoke-SharpProofTimedPhase -Name 'build' -Timings $timings -Action {
    $builds = [Collections.Generic.List[object]]::new()
    $builds.Add([pscustomobject]@{
        Name = 'solution-' +
            ([string]$solutionBuildCommand.configuration).ToLowerInvariant()
        Arguments = @(
            'build', 'SharpProof.sln', '-c',
            [string]$solutionBuildCommand.configuration,
            '--no-restore')
    })
    if ($packageProductBuild) {
        $builds.Add([pscustomobject]@{
            Name = 'package-products-release'
            Arguments = @(
                'build',
                'SharpProof.Verifier/SharpProof.Verifier.csproj',
                '-c', [string]$packageProductBuildCommands[0].configuration,
                '--no-restore',
                '-p:GeneratePackageOnBuild=false')
        })
    }
    Invoke-SharpProofParallelDotnetBuilds `
        -Builds @($builds) `
        -RepositoryRoot $repositoryRoot `
        -Parallelism $buildParallelism `
        -TimeoutSeconds $TimeoutSeconds
}
Invoke-SharpProofTimedPhase -Name 'semantic-tests' -Timings $timings -Action {
    & (Join-Path $PSScriptRoot 'Invoke-SharpProofSemanticTests.ps1') `
        -Configuration ([string]$semanticTestsCommand.configuration) `
        -NoBuild:([bool]$semanticTestsCommand.noBuild) `
        -TimeoutSeconds $TimeoutSeconds
}
Invoke-SharpProofTimedPhase -Name 'package-tests' -Timings $timings -Action {
    $packageArguments = @{
        Configuration = $Configuration
        TimeoutSeconds = $TimeoutSeconds
    }
    $packageArguments.NoBuild = $true
    if (-not [bool]$packagePackCommands[0].noBuild) {
        $packageArguments.Remove('NoBuild')
    }
    & (Join-Path $PSScriptRoot 'Invoke-SharpProofPackageTests.ps1') `
        @packageArguments
}
Invoke-SharpProofTimedPhase -Name 'performance-smoke' -Timings $timings -Action {
    & $dotnetWrapper -TimeoutSeconds $TimeoutSeconds `
        run --project SharpProof.Gates/SharpProof.Gates.csproj `
        -c ([string]$performanceSmokeCommand.configuration) `
        --no-build --no-restore -- performance-smoke
    if ($LASTEXITCODE -ne 0) {
        throw 'Developer performance smoke failed.'
    }
}

$campaign.Stop()
$timingDirectory = Join-Path $repositoryRoot 'artifacts/timings'
[IO.Directory]::CreateDirectory($timingDirectory) | Out-Null
$timingOutput = Join-Path $timingDirectory (
    'dev-check-' + $Configuration.ToLowerInvariant() + '.json')
$temporaryTiming =
    $timingOutput + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
[pscustomobject]@{
    schemaVersion = 1
    command = 'check'
    configuration = $Configuration
    totalElapsedMilliseconds = [long]$campaign.Elapsed.TotalMilliseconds
    phases = @($timings)
} | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $temporaryTiming -Encoding utf8NoBOM
Move-Item -LiteralPath $temporaryTiming -Destination $timingOutput -Force

Write-Host 'SharpProof developer check passed.'
Write-Host "Timing evidence: $timingOutput"
