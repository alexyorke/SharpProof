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
$dotnetWrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
$planScript = Join-Path $PSScriptRoot 'Get-SharpProofDevCheckPlan.ps1'
Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
$parallelism = Get-SharpProofTestProjectParallelism `
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
$packageProductBuild = @($commandPlan.commands | Where-Object {
        [string]$_.id -ceq 'package-product-build'
    }).Count -eq 1
$timings = [Collections.Generic.List[object]]::new()
$campaign = [Diagnostics.Stopwatch]::StartNew()

function Invoke-TimedPhase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    $timer = [Diagnostics.Stopwatch]::StartNew()
    & $Action
    $timer.Stop()
    $timings.Add([pscustomobject]@{
        name = $Name
        elapsedMilliseconds = [long]$timer.Elapsed.TotalMilliseconds
    })
}

Invoke-TimedPhase -Name 'restore' -Action {
    & $dotnetWrapper -TimeoutSeconds $TimeoutSeconds `
        restore SharpProof.sln --locked-mode /nodeReuse:false
    if ($LASTEXITCODE -ne 0) {
        throw 'Developer-check restore failed.'
    }
}
Invoke-TimedPhase -Name 'build' -Action {
    $builds = [Collections.Generic.List[object]]::new()
    $builds.Add([pscustomobject]@{
        Name = 'solution-' + $Configuration.ToLowerInvariant()
        Arguments = @(
            'build', 'SharpProof.sln', '-c', $Configuration,
            '--no-restore')
    })
    if ($packageProductBuild) {
        $builds.Add([pscustomobject]@{
            Name = 'package-products-release'
            Arguments = @(
                'build',
                'SharpProof.Verifier/SharpProof.Verifier.csproj',
                '-c', 'Release', '--no-restore',
                '-p:GeneratePackageOnBuild=false')
        })
    }
    Invoke-SharpProofParallelDotnetBuilds `
        -Builds @($builds) `
        -RepositoryRoot $repositoryRoot `
        -Parallelism $parallelism `
        -TimeoutSeconds $TimeoutSeconds
}
Invoke-TimedPhase -Name 'semantic-tests' -Action {
    & (Join-Path $PSScriptRoot 'Invoke-SharpProofSemanticTests.ps1') `
        -Configuration $Configuration `
        -NoBuild `
        -TimeoutSeconds $TimeoutSeconds
}
Invoke-TimedPhase -Name 'package-tests' -Action {
    $packageArguments = @{
        Configuration = $Configuration
        TimeoutSeconds = $TimeoutSeconds
    }
    $packageArguments.NoBuild = $true
    & (Join-Path $PSScriptRoot 'Invoke-SharpProofPackageTests.ps1') `
        @packageArguments
}
Invoke-TimedPhase -Name 'performance-smoke' -Action {
    & $dotnetWrapper -TimeoutSeconds $TimeoutSeconds `
        run --project SharpProof.Gates/SharpProof.Gates.csproj `
        -c $Configuration --no-build --no-restore -- performance-smoke
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
