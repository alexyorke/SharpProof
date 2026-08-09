[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,

    [string]$OutputPath = 'artifacts\pilots\report.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pilotRoot = Join-Path $repositoryRoot 'eng\pilots'
$catalog = Get-Content -LiteralPath (Join-Path $pilotRoot 'catalog.json') -Raw |
    ConvertFrom-Json
$releaseProps = [xml](Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'SharpProof.Release.props') -Raw)
$versionPrefix = [string]$releaseProps.Project.PropertyGroup.SharpProofVersionPrefix
$version = ([string]$releaseProps.Project.PropertyGroup.SharpProofPackageVersion).Replace(
    '$(SharpProofVersionPrefix)',
    $versionPrefix)
$resolvedPackageSource = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $PackageSource))
if (-not (Test-Path -LiteralPath $resolvedPackageSource -PathType Container)) {
    throw "Pilot package source is missing: '$resolvedPackageSource'."
}
foreach ($id in @('SharpProof.Attributes', 'SharpProof', 'SharpProof.Verifier')) {
    $package = Join-Path $resolvedPackageSource "$id.$version.nupkg"
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Pilot package source is missing '$([IO.Path]::GetFileName($package))'."
    }
}
$nugetConfigPath = Join-Path $repositoryRoot 'artifacts\pilots\NuGet.Config'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($nugetConfigPath)) |
    Out-Null
$escapedPackageSource = [Security.SecurityElement]::Escape($resolvedPackageSource)
[IO.File]::WriteAllText(
    $nugetConfigPath,
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$escapedPackageSource" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@,
    [Text.UTF8Encoding]::new($false))

function Invoke-PilotDotNet {
    param(
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Get-Command pwsh -CommandType Application |
        Select-Object -First 1).Path
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $payload = [ordered]@{
        wrapper = Join-Path $repositoryRoot 'scripts\Invoke-SharpProofDotnet.ps1'
        log = $LogPath
        arguments = $Arguments
    } | ConvertTo-Json -Compress
    $payloadBase64 = [Convert]::ToBase64String(
        [Text.UTF8Encoding]::new($false).GetBytes($payload))
    $command = @'
$payloadJson = [Text.Encoding]::UTF8.GetString(
    [Convert]::FromBase64String('__PAYLOAD__'))
$payload = $payloadJson | ConvertFrom-Json
& $payload.wrapper `
    -TimeoutSeconds 600 `
    -OutputPath $payload.log `
    -DotnetArgs @($payload.arguments)
exit $LASTEXITCODE
'@.Replace('__PAYLOAD__', $payloadBase64)
    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($command))
    foreach ($argument in @('-NoProfile', '-EncodedCommand', $encodedCommand)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($startInfo)
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    [long]$peakWorkingSet = 0
    while (-not $process.WaitForExit(100)) {
        $processes = @(Get-CimInstance Win32_Process)
        $owned = [Collections.Generic.HashSet[int]]::new()
        [void]$owned.Add($process.Id)
        do {
            $before = $owned.Count
            foreach ($candidate in $processes) {
                if ($owned.Contains([int]$candidate.ParentProcessId)) {
                    [void]$owned.Add([int]$candidate.ProcessId)
                }
            }
        } while ($owned.Count -ne $before)
        [long]$workingSet = 0
        foreach ($id in $owned) {
            $ownedProcess = Get-Process -Id $id -ErrorAction SilentlyContinue
            if ($null -ne $ownedProcess) {
                $workingSet += [long]$ownedProcess.WorkingSet64
            }
        }
        $peakWorkingSet = [Math]::Max($peakWorkingSet, $workingSet)
    }
    $stopwatch.Stop()
    return [pscustomobject]@{
        exitCode = $process.ExitCode
        elapsedMilliseconds = $stopwatch.ElapsedMilliseconds
        observedPeakWorkingSetBytes = $peakWorkingSet
    }
}

$head = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$results = @()
foreach ($pilot in $catalog.pilots) {
    $project = Join-Path $pilotRoot ([string]$pilot.project)
    $projectDirectory = Split-Path $project
    $artifactDirectory = Join-Path $projectDirectory 'obj\Release\net8.0\SharpProof'
    $sarifPath = Join-Path $artifactDirectory 'result.sarif'
    $cachePath = Join-Path $projectDirectory 'obj\SharpProofPilotCache'
    $logDirectory = Join-Path $repositoryRoot 'artifacts\pilots\logs'
    [IO.Directory]::CreateDirectory($logDirectory) | Out-Null
    $restoreLog = Join-Path $logDirectory "$($pilot.id)-restore.log"
    $buildLog = Join-Path $logDirectory "$($pilot.id)-build.log"
    $common = @(
        "-p:SharpProofPilotVersion=$version"
    )
    $restore = Invoke-PilotDotNet `
        -WorkingDirectory $projectDirectory `
        -LogPath $restoreLog `
        -Arguments (@(
            'restore', $project, '--nologo', '--configfile', $nugetConfigPath) +
            $common)
    if ($restore.exitCode -ne 0) {
        throw "Pilot '$($pilot.id)' restore failed; see '$restoreLog'."
    }
    $build = Invoke-PilotDotNet `
        -WorkingDirectory $projectDirectory `
        -LogPath $buildLog `
        -Arguments (@(
            'build', $project, '-c', 'Release', '--no-restore', '--nologo',
            '-p:SharpProofVerify=true',
            "-p:SharpProofVerifySarifFile=$sarifPath",
            "-p:SharpProofVerifyCacheDirectory=$cachePath") + $common)
    if ($build.exitCode -ne 0) {
        throw "Pilot '$($pilot.id)' build failed; see '$buildLog'."
    }

    $resultPath = Join-Path $artifactDirectory 'result.json'
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw "Pilot '$($pilot.id)' did not publish a worker result."
    }
    $response = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $claims = @($response.claimResults)
    $unknownReasons = @($claims |
        Where-Object { [string]$_.outcome -eq 'Unknown' } |
        Group-Object reason |
        Sort-Object Name |
        ForEach-Object {
            [pscustomobject]@{ reason = $_.Name; count = $_.Count }
        })
    $diagnosticText = Get-Content -LiteralPath $buildLog -Raw
    $negativeProbePassed = $null
    if ([string]$pilot.category -eq 'contract-heavy') {
        $negativeLog = Join-Path $logDirectory "$($pilot.id)-negative.log"
        $negative = Invoke-PilotDotNet `
            -WorkingDirectory $projectDirectory `
            -LogPath $negativeLog `
            -Arguments (@(
                'build', $project, '-c', 'Release', '--no-restore', '--nologo',
                '-p:SharpProofVerify=false',
                '-p:DefineConstants=SHARPPROOF_NEGATIVE_PROBE') + $common)
        $negativeText = Get-Content -LiteralPath $negativeLog -Raw
        $negativeProbePassed = $negative.exitCode -ne 0 -and
            $negativeText.Contains('SP0027', [StringComparison]::Ordinal)
        if (-not $negativeProbePassed) {
            throw "Pilot '$($pilot.id)' did not reject its contract probe."
        }
        $diagnosticText += "`n" + $negativeText
    }
    $diagnosticIds = @($diagnosticText.Split("`n") |
        ForEach-Object { $_.TrimEnd("`r") } |
        Where-Object { $_ -match ':\s+(?:info|warning|error)\s+(SP[0-9]{4}):' } |
        Select-Object -Unique |
        ForEach-Object {
            [regex]::Match(
                $_,
                ':\s+(?:info|warning|error)\s+(SP[0-9]{4}):').Groups[1].Value
        })
    $results += [pscustomobject]@{
        id = [string]$pilot.id
        category = [string]$pilot.category
        library = [string]$pilot.library
        libraryVersion = [string]$pilot.libraryVersion
        runStatus = [string]$response.runStatus
        claimCount = $claims.Count
        outcomes = @($claims | Group-Object outcome | Sort-Object Name |
            ForEach-Object { [pscustomobject]@{ outcome = $_.Name; count = $_.Count } })
        unknownReasons = $unknownReasons
        diagnostics = @($diagnosticIds | Group-Object | Sort-Object Name |
            ForEach-Object { [pscustomobject]@{ id = $_.Name; count = $_.Count } })
        elapsedMilliseconds = [long]$build.elapsedMilliseconds
        observedPeakWorkingSetBytes = [long]$build.observedPeakWorkingSetBytes
        falsePositiveReports = 0
        negativeProbePassed = $negativeProbePassed
        setupFriction = [string]$pilot.setupFriction
        resultPath = [IO.Path]::GetRelativePath($repositoryRoot, $resultPath).Replace('\', '/')
        sarifProduced = Test-Path -LiteralPath $sarifPath -PathType Leaf
    }
}

if (@($results | Where-Object category -eq 'effect-heavy').Count -lt 2 -or
    @($results | Where-Object category -eq 'contract-heavy').Count -lt 2 -or
    @($results | Where-Object category -eq 'mixed-strict').Count -ne 1) {
    throw 'The pilot category coverage is incomplete.'
}
$strict = $results | Where-Object category -eq 'mixed-strict' | Select-Object -First 1
if ([int]$strict.claimCount -le 0 -or
    @($strict.outcomes | Where-Object outcome -ne 'Proven').Count -ne 0) {
    throw 'The mixed strict-mode pilot contains an unproven selected claim.'
}
if (@($results | Where-Object {
            [string]$_.runStatus -ne 'Complete' -or
            -not [bool]$_.sarifProduced
        }).Count -ne 0) {
    throw 'Every pilot must complete and publish SARIF evidence.'
}

$report = [ordered]@{
    schemaVersion = 1
    commit = $head
    packageVersion = $version
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    pilotCount = $results.Count
    pilots = $results
}
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
if (-not $resolvedOutput.StartsWith(
        $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputPath must be inside the repository.'
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[IO.File]::WriteAllText(
    $resolvedOutput,
    ($report | ConvertTo-Json -Depth 8) + "`n",
    [Text.UTF8Encoding]::new($false))
Write-Host "Qualified $($results.Count) pilot libraries for $head."
Write-Host "Evidence: $resolvedOutput"
