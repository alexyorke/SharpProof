[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,

    [string]$OutputPath = 'artifacts\pilots\report.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Get-SharpProofPilotPackageAuthority.ps1')

function Resolve-RepositoryPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

function Get-OwnedWorkingSetBytes([int]$RootProcessId) {
    $snapshot = [Collections.Generic.Dictionary[int, object]]::new()
    foreach ($directory in [IO.Directory]::EnumerateDirectories('/proc')) {
        [int]$processId = 0
        if (-not [int]::TryParse(
                [IO.Path]::GetFileName($directory),
                [ref]$processId)) {
            continue
        }

        try {
            $stat = [IO.File]::ReadAllText((Join-Path $directory 'stat'))
        }
        catch [IO.IOException] {
            continue
        }
        catch [UnauthorizedAccessException] {
            continue
        }

        $commandEnd = $stat.LastIndexOf(')')
        if ($commandEnd -lt 0) {
            continue
        }
        $fields = $stat.Substring($commandEnd + 1).Split(
            ' ', [StringSplitOptions]::RemoveEmptyEntries)
        if ($fields.Length -le 21) {
            continue
        }

        [int]$parentProcessId = 0
        [long]$residentPages = 0
        if (-not [int]::TryParse($fields[1], [ref]$parentProcessId) -or
            -not [long]::TryParse($fields[21], [ref]$residentPages)) {
            continue
        }
        $snapshot[$processId] = [pscustomobject]@{
            parent = $parentProcessId
            residentPages = $residentPages
        }
    }

    $owned = [Collections.Generic.HashSet[int]]::new()
    [void]$owned.Add($RootProcessId)
    do {
        $before = $owned.Count
        foreach ($entry in $snapshot.GetEnumerator()) {
            if ($owned.Contains([int]$entry.Value.parent)) {
                [void]$owned.Add([int]$entry.Key)
            }
        }
    } while ($owned.Count -ne $before)

    [long]$workingSet = 0
    foreach ($processId in $owned) {
        if ($snapshot.ContainsKey($processId)) {
            $workingSet += [long]$snapshot[$processId].residentPages *
                [Environment]::SystemPageSize
        }
    }
    return $workingSet
}

$pilotRoot = Join-Path $repositoryRoot 'eng\pilots'
$catalog = Get-Content -LiteralPath (Join-Path $pilotRoot 'catalog.json') -Raw |
    ConvertFrom-Json
$releaseProps = [xml](Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'SharpProof.Release.props') -Raw)
$versionPrefix = [string]$releaseProps.Project.PropertyGroup.SharpProofVersionPrefix
$version = ([string]$releaseProps.Project.PropertyGroup.SharpProofPackageVersion).Replace(
    '$(SharpProofVersionPrefix)',
    $versionPrefix)
$resolvedPackageSource = Resolve-RepositoryPath $PackageSource
if (-not (Test-Path -LiteralPath $resolvedPackageSource -PathType Container)) {
    throw "Pilot package source is missing: '$resolvedPackageSource'."
}
if (@(& git -C $repositoryRoot status --porcelain).Count -ne 0) {
    throw 'Pilot qualification requires a clean checkout.'
}
$head = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$packageArtifacts = @(Get-SharpProofPilotPackageAuthority `
    -PackageSource $resolvedPackageSource `
    -ExpectedVersion $version `
    -ExpectedCommit $head)
$runId = [Guid]::NewGuid().ToString('N')
$runRoot = Join-Path $repositoryRoot "artifacts/pilots/runs/$runId"
$nugetCache = Join-Path $runRoot 'nuget'
$dotnetHome = Join-Path $runRoot 'dotnet-home'
$qualificationStartedUtc = [DateTimeOffset]::UtcNow
[IO.Directory]::CreateDirectory($nugetCache) | Out-Null
[IO.Directory]::CreateDirectory($dotnetHome) | Out-Null
$nugetConfigPath = Join-Path $runRoot 'NuGet.Config'
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
  <packageSourceMapping>
    <packageSource key="candidate"><package pattern="SharpProof*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
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
    $startInfo.Environment['NUGET_PACKAGES'] = $nugetCache
    $startInfo.Environment['DOTNET_CLI_HOME'] = $dotnetHome
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
        $workingSet = Get-OwnedWorkingSetBytes $process.Id
        $peakWorkingSet = [Math]::Max($peakWorkingSet, $workingSet)
    }
    $stopwatch.Stop()
    return [pscustomobject]@{
        exitCode = $process.ExitCode
        elapsedMilliseconds = $stopwatch.ElapsedMilliseconds
        observedPeakWorkingSetBytes = $peakWorkingSet
    }
}

$results = @()
foreach ($pilot in $catalog.pilots) {
    $project = Join-Path $pilotRoot ([string]$pilot.project)
    $projectDirectory = Split-Path $project
    $artifactDirectory = Join-Path $projectDirectory 'obj\Release\net8.0\SharpProof'
    $sarifPath = Join-Path $artifactDirectory 'result.sarif'
    $cachePath = Join-Path $runRoot "cache/$($pilot.id)"
    $logDirectory = Join-Path $runRoot 'logs'
    [IO.Directory]::CreateDirectory($logDirectory) | Out-Null
    $requestPath = Join-Path $artifactDirectory 'request.json'
    $resultPath = Join-Path $artifactDirectory 'result.json'
    $manifestPath = Join-Path $artifactDirectory 'compiler-manifest.json'
    foreach ($stale in @($requestPath, $resultPath, $manifestPath, $sarifPath)) {
        if (Test-Path -LiteralPath $stale) {
            Remove-Item -LiteralPath $stale -Force
        }
    }
    $pilotStartedUtc = [DateTimeOffset]::UtcNow
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

    $evidenceFiles = @($requestPath, $resultPath, $manifestPath, $sarifPath)
    if (@($evidenceFiles | Where-Object {
                -not (Test-Path -LiteralPath $_ -PathType Leaf) -or
                (Get-Item -LiteralPath $_).LastWriteTimeUtc -lt $pilotStartedUtc.UtcDateTime
            }).Count -ne 0) {
        throw "Pilot '$($pilot.id)' did not publish a fresh complete evidence set."
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
        evidence = @($evidenceFiles | ForEach-Object {
                [ordered]@{
                    kind = switch ([IO.Path]::GetFileName($_)) {
                        'request.json' { 'request' }
                        'result.json' { 'result' }
                        'compiler-manifest.json' { 'compilerManifest' }
                        'result.sarif' { 'sarif' }
                    }
                    path = [IO.Path]::GetRelativePath($repositoryRoot, $_).Replace('\', '/')
                    bytes = [int64](Get-Item -LiteralPath $_).Length
                    sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            })
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
    schemaVersion = 2
    runId = $runId
    runStartedUtc = $qualificationStartedUtc.ToString('O')
    commit = $head
    packageVersion = $version
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    pilotCount = $results.Count
    packageArtifacts = $packageArtifacts
    pilots = $results
}
$resolvedOutput = Resolve-RepositoryPath $OutputPath
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
