[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageSource,
    [ValidateRange(1, 10)]
    [int]$Iterations = 3,
    [string]$OutputDirectory = 'artifacts/consumer-performance',
    [ValidateRange(0, 3600000)]
    [int]$MaximumBuildMilliseconds = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
Assert-SharpProofContainer 'Run consumer performance measurements in the canonical Linux amd64 container.'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
$feed = (Resolve-Path -LiteralPath $PackageSource).Path
$packageFiles = @(Get-ChildItem -LiteralPath $feed -File | Where-Object { $_.Extension -in @('.nupkg', '.snupkg') })
$identity = Get-SharpProofPackageIdentitySet -Files $packageFiles -RequireRepository
$sourceCommit = (& git -C $repositoryRoot rev-parse --verify HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not identify the benchmark repository commit.' }
$trackedChanges = @(& git -C $repositoryRoot status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0) { throw 'Could not identify benchmark working tree changes.' }
$harnessHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
$output = [IO.Path]::GetFullPath($OutputDirectory, $repositoryRoot)
[IO.Directory]::CreateDirectory($output) | Out-Null
$runDirectory = Join-Path $output ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($runDirectory) | Out-Null
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$workspace = Join-Path $temporaryParent ('SharpProof.ConsumerPerformance.' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($workspace) | Out-Null
$measurements = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
$ordinal = 0
$originalLocation = Get-Location
$completed = $false

function Invoke-MeasuredDotnet {
    param([string]$Label, [string[]]$Arguments)
    $script:ordinal++
    $log = Join-Path $runDirectory ($script:ordinal.ToString('D3') + '-' + $Label + '.log')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & (Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1') -TimeoutSeconds 600 -OutputPath $log @Arguments
    $exitCode = $LASTEXITCODE
    $timer.Stop()
    if ($exitCode -ne 0) {
        Get-Content -LiteralPath $log -Tail 25 | Write-Host
        throw "Consumer performance invocation '$Label' failed ($exitCode). See $log"
    }
    return [long]$timer.ElapsedMilliseconds
}

function New-SubjectSource {
    param([int]$Increment)
    $methods = foreach ($index in 1..8) {
        "    public static int Increment$index(int value) { Contract.Requires(value >= 0 && value <= 1000); Contract.Ensures(Contract.Result<int>() == Contract.Old(value) + $Increment); return value + $Increment; }"
    }
    return "using SharpProof.Attributes;`npublic static class Subject {`n" + ($methods -join "`n") + "`n}`n"
}

try {
    $sdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not identify the measured SDK.' }
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    $version = [Security.SecurityElement]::Escape([string]$identity.Version)
    # One isolated package cache shared by the three variants makes restore
    # dependencies comparable. Restore is measured separately, never hidden in build.
    $cache = Join-Path $workspace 'packages'
    foreach ($iteration in 1..$Iterations) {
        # Rotate the order to reduce systematic first-run/compiler warmup bias.
        $variants = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('baseline', 'portable', 'verified'))
        for ($offset = 0; $offset -lt 3; $offset++) {
            $variant = $variants[($offset + $iteration - 1) % 3]
            $consumer = Join-Path $workspace "$iteration-$variant"
            [IO.Directory]::CreateDirectory($consumer) | Out-Null
            $resultFile = Join-Path $consumer 'result.json'
            $references = '<PackageReference Include="SharpProof.Attributes" Version="' + $version + '" />'
            if ($variant -ne 'baseline') {
                $references = '<PackageReference Include="SharpProof" Version="' + $version + '" />'
            }
            $verificationProfile = if ($variant -eq 'verified') { 'strict' } else { 'advisory' }
            $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <NuGetAudit>false</NuGetAudit>
    <SharpProofProfile>$verificationProfile</SharpProofProfile>
    <SharpProofVerifyResultFile>$resultFile</SharpProofVerifyResultFile>
    <WarningsAsErrors>AD0001;CS8032;CS8034;CS8785</WarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>$references</ItemGroup>
</Project>
"@
            [IO.File]::WriteAllText((Join-Path $consumer 'Consumer.csproj'), $project, [Text.UTF8Encoding]::new($false))
            $config = @"
<configuration><packageSources><clear/><add key="product" value="$escapedFeed"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="product"><package pattern="SharpProof*"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>
"@
            [IO.File]::WriteAllText((Join-Path $consumer 'NuGet.config'), $config, [Text.UTF8Encoding]::new($false))
            $sourcePath = Join-Path $consumer 'Subject.cs'
            [IO.File]::WriteAllText($sourcePath, (New-SubjectSource 1), [Text.UTF8Encoding]::new($false))
            Set-Location $consumer
            $restoreMilliseconds = Invoke-MeasuredDotnet "$iteration-$variant-restore" @('restore', 'Consumer.csproj', '--packages', $cache)
            $measurements.Add([ordered]@{ iteration = $iteration; variant = $variant; scenario = 'restore'; elapsedMilliseconds = $restoreMilliseconds; claims = 0; proven = 0; incomplete = 0; cacheStatus = $null })
            foreach ($scenario in @('first-build', 'no-op-build', 'warm-rebuild', 'small-edit')) {
                if ($scenario -eq 'small-edit') {
                    [IO.File]::WriteAllText($sourcePath, (New-SubjectSource 2), [Text.UTF8Encoding]::new($false))
                }
                $arguments = @('build', 'Consumer.csproj', '--configuration', 'Release', '--no-restore', '--nologo')
                if ($scenario -eq 'warm-rebuild') { $arguments += '-t:Rebuild' }
                $elapsed = Invoke-MeasuredDotnet "$iteration-$variant-$scenario" $arguments
                $claims = 0
                $proven = 0
                $incomplete = 0
                $cacheStatus = $null
                if ($variant -eq 'verified') {
                    if (-not (Test-Path -LiteralPath $resultFile -PathType Leaf)) { throw "No verification evidence for $scenario." }
                    $result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
                    $claims = @($result.claimResults).Count
                    $proven = @($result.claimResults | Where-Object { $_.outcome -ceq 'Proven' -and $_.vacuity -ceq 'None' }).Count
                    $incomplete = @($result.callableResults | Where-Object coverage -CNE 'Complete').Count
                    if ($result.runStatus -cne 'Complete' -or $result.failureReason -cne 'None' -or $claims -ne 8 -or $proven -ne 8 -or $incomplete -ne 0) {
                        throw "The $scenario measurement changed proof quality; expected eight nonvacuous Proven claims with complete coverage."
                    }
                    $cacheStatus = [string]$result.summary.cacheStatus
                }
                $measurements.Add([ordered]@{ iteration = $iteration; variant = $variant; scenario = $scenario; elapsedMilliseconds = $elapsed; claims = $claims; proven = $proven; incomplete = $incomplete; cacheStatus = $cacheStatus })
            }
        }
    }
    $completed = $true
}
finally {
    Set-Location $originalLocation
    $measurementSnapshot = $measurements.ToImmutable()
    $report = [ordered]@{
        schemaVersion            = 2
        completed                = $completed
        measuredAtUtc            = [DateTime]::UtcNow.ToString('O')
        source                   = [ordered]@{
            commit            = $sourceCommit
            hasTrackedChanges = $trackedChanges.Count -gt 0
            harnessSha256     = $harnessHash
        }
        packageVersion           = [string]$identity.Version
        packageRepositoryCommits = @($identity.RepositoryCommits)
        sdk                      = if (Get-Variable sdk -ErrorAction SilentlyContinue) { $sdk } else { $null }
        platform                 = [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
        processorCount           = [Environment]::ProcessorCount
        iterations               = $Iterations
        scope                    = 'Synthetic eight-method consumer; restore separated; baseline has Attributes only; portable uses one SharpProof reference with explicit advisory; verified uses the same single reference with strict verification. First build has clean outputs, not a cold OS cache. Warm rebuild reuses package/compiler caches. No-op evidence can be reused and is not a fresh proof.'
        packages                 = @($packageFiles | Sort-Object Name | ForEach-Object { [ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
        measurements             = @($measurementSnapshot)
    }
    $reportPath = Join-Path $runDirectory 'measurements.json'
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    Write-Host "Consumer performance report: $reportPath"
    $resolvedWorkspace = Resolve-SharpProofContainedPath -Root $temporaryParent -Path $workspace -ParameterName 'Consumer performance workspace'
    Remove-Item -LiteralPath $resolvedWorkspace -Recurse -Force
}
if ($MaximumBuildMilliseconds -gt 0) {
    $overBudget = @($measurementSnapshot | Where-Object { $_.scenario -ne 'restore' -and $_.elapsedMilliseconds -gt $MaximumBuildMilliseconds })
    if ($overBudget.Count -gt 0) { throw "$($overBudget.Count) consumer measurements exceeded the explicit $MaximumBuildMilliseconds ms budget. See $reportPath" }
}
foreach ($scenario in @('first-build', 'no-op-build', 'warm-rebuild', 'small-edit')) {
    $medianBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, double]([StringComparer]::OrdinalIgnoreCase)
    foreach ($variant in @('baseline', 'portable', 'verified')) {
        $times = @($measurementSnapshot | Where-Object { $_.scenario -eq $scenario -and $_.variant -eq $variant } | ForEach-Object elapsedMilliseconds | Sort-Object)
        $middle = [int][Math]::Floor($times.Count / 2)
        $median = if ($times.Count % 2 -eq 1) { $times[$middle] } else { ($times[$middle - 1] + $times[$middle]) / 2 }
        $medianBuilder[$variant] = $median
    }
    $medians = $medianBuilder.ToImmutable()
    Write-Host ("{0}: baseline={1}ms portable={2}ms ({3:+0.##;-0.##;0}ms) verified={4}ms ({5:+0.##;-0.##;0}ms)" -f $scenario, $medians['baseline'], $medians['portable'], ($medians['portable'] - $medians['baseline']), $medians['verified'], ($medians['verified'] - $medians['baseline']))
}
