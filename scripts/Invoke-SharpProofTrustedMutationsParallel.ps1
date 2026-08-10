[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputPath = 'artifacts/mutation/trusted-mutations.json',

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$ExpectedCommit,

    [ValidateRange(0, 16)]
    [int]$Parallelism = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
$contract = Get-Content -LiteralPath (Join-Path `
    $repositoryRoot 'eng/acceptance/contract.json') -Raw |
    ConvertFrom-Json
if ($Parallelism -eq 0) {
    $Parallelism = [int]$contract.automation.mutationParallelism
}
$visibleProcessors = [Environment]::ProcessorCount
if ($Parallelism -lt 1 -or $Parallelism -gt $visibleProcessors) {
    throw (
        'Mutation parallelism must be between 1 and the container-visible ' +
        "CPU count ($visibleProcessors).")
}
$parallelism = $Parallelism
$catalogCount = [int]$contract.mutationEvidence.expectedCatalogCount
$catalogSha256 = [string]$contract.mutationEvidence.expectedCatalogSha256
$shardWallSeconds = [int]$contract.automation.mutationShardWallSeconds
if ($shardWallSeconds -lt 1) {
    throw 'The mutation shard wall deadline must be positive.'
}
$output = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
$repositoryPrefix = $repositoryRoot + [IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($repositoryPrefix, [StringComparison]::Ordinal)) {
    throw "OutputPath must be inside the repository: $output"
}

if (Test-Path -LiteralPath $output -PathType Leaf) {
    $existing = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    if ([int]$existing.schemaVersion -eq 2 -and
        [string]$existing.commit -eq $ExpectedCommit -and
        [string]$existing.configuration -eq $Configuration -and
        [string]$existing.selection -eq 'full' -and
        [int]$existing.catalogCount -eq $catalogCount -and
        [string]$existing.catalogSha256 -eq $catalogSha256 -and
        [int]$existing.mutationCount -eq $catalogCount -and
        [int]$existing.killedCount -eq $catalogCount) {
        Write-Host "Mutation evidence is already complete: $output"
        return
    }
}

$shardRoot = Join-Path (Split-Path -Parent $output) (
    'shards/' + $ExpectedCommit + '/' + $Configuration.ToLowerInvariant() +
    '-weighted-v1-' + $parallelism)
[IO.Directory]::CreateDirectory($shardRoot) | Out-Null
$shards = @()
for ($index = 0; $index -lt $parallelism; $index++) {
    $fileName = 'shard-' + ($index + 1).ToString(
        'D2', [Globalization.CultureInfo]::InvariantCulture) + '.json'
    $shards += [pscustomobject]@{
        Index = $index
        Path = Join-Path $shardRoot $fileName
        RelativePath = [IO.Path]::GetRelativePath(
            $repositoryRoot, (Join-Path $shardRoot $fileName))
    }
}
$campaignTimer = [Diagnostics.Stopwatch]::StartNew()
$shardTimings = [Collections.Generic.List[object]]::new()

function Test-CompleteShard([object]$Shard) {
    if (-not (Test-Path -LiteralPath $Shard.Path -PathType Leaf)) {
        return $false
    }
    try {
        $evidence = Get-Content -LiteralPath $Shard.Path -Raw |
            ConvertFrom-Json
        return [int]$evidence.schemaVersion -eq 2 -and
            [string]$evidence.commit -eq $ExpectedCommit -and
            [string]$evidence.configuration -eq $Configuration -and
            [string]$evidence.selection -eq 'selected' -and
            [int]$evidence.catalogCount -eq $catalogCount -and
            [string]$evidence.catalogSha256 -eq $catalogSha256 -and
            [int]$evidence.mutationCount -gt 0 -and
            [int]$evidence.mutationCount -eq [int]$evidence.killedCount
    }
    catch {
        return $false
    }
}

$running = [Collections.Generic.List[object]]::new()
try {
    foreach ($shard in $shards) {
        if (Test-CompleteShard $shard) {
            Write-Host "Reusing completed mutation shard $($shard.Index + 1)."
            $evidence = Get-Content -LiteralPath $shard.Path -Raw |
                ConvertFrom-Json
            $shardTimings.Add([pscustomobject]@{
                index = $shard.Index
                mutationCount = [int]$evidence.mutationCount
                elapsedMilliseconds = 0
                reused = $true
            })
            continue
        }
        Remove-Item -LiteralPath $shard.Path -Force -ErrorAction SilentlyContinue
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = 'pwsh'
        $startInfo.WorkingDirectory = $repositoryRoot
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        foreach ($argument in @(
                '-NoLogo', '-NoProfile', '-File',
                (Join-Path $PSScriptRoot 'Test-SharpProofTrustedMutations.ps1'),
                '-Configuration', $Configuration,
                '-OutputPath', $shard.RelativePath,
                '-ExpectedCommit', $ExpectedCommit,
                '-MutationShardIndex', [string]$shard.Index,
                '-MutationShardCount', [string]$parallelism)) {
            [void]$startInfo.ArgumentList.Add($argument)
        }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        if (-not $process.Start()) {
            $process.Dispose()
            throw "Could not start mutation shard $($shard.Index + 1)."
        }
        $running.Add([pscustomobject]@{
            Shard = $shard
            Process = $process
            StartedUtc = $process.StartTime.ToUniversalTime()
            StandardOutput = $process.StandardOutput.ReadToEndAsync()
            StandardError = $process.StandardError.ReadToEndAsync()
        })
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($shardWallSeconds)
    while (@($running | Where-Object { -not $_.Process.HasExited }).Count -gt 0) {
        if ([DateTime]::UtcNow -ge $deadline) {
            throw (
                "Parallel mutation shards exceeded $shardWallSeconds seconds.")
        }
        Start-Sleep -Milliseconds 250
    }

    foreach ($active in @($running)) {
        $active.Process.WaitForExit()
        $stdout = $active.StandardOutput.GetAwaiter().GetResult()
        $stderr = $active.StandardError.GetAwaiter().GetResult()
        Write-Host "--- Mutation shard $($active.Shard.Index + 1) ---"
        if (-not [string]::IsNullOrWhiteSpace($stdout)) {
            Write-Host $stdout.TrimEnd()
        }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Host $stderr.TrimEnd()
        }
        if ($active.Process.ExitCode -ne 0 -or
            -not (Test-CompleteShard $active.Shard)) {
            throw (
                "Mutation shard $($active.Shard.Index + 1) failed with " +
                "exit code $($active.Process.ExitCode).")
        }
        $evidence = Get-Content -LiteralPath $active.Shard.Path -Raw |
            ConvertFrom-Json
        $shardTimings.Add([pscustomobject]@{
            index = $active.Shard.Index
            mutationCount = [int]$evidence.mutationCount
            elapsedMilliseconds = [long](
                ($active.Process.ExitTime.ToUniversalTime() -
                    $active.StartedUtc).TotalMilliseconds)
            reused = $false
        })
    }
}
finally {
    foreach ($active in @($running)) {
        if (-not $active.Process.HasExited) {
            $active.Process.Kill($true)
            $active.Process.WaitForExit()
        }
        $active.Process.Dispose()
    }
}

$orderedResults = [Collections.Generic.List[object]]::new()
$finalParent = Split-Path -Parent $output
foreach ($shard in $shards) {
    if (-not (Test-CompleteShard $shard)) {
        throw "Mutation shard evidence is incomplete: $($shard.Path)"
    }
    $evidence = Get-Content -LiteralPath $shard.Path -Raw | ConvertFrom-Json
    foreach ($result in @($evidence.mutations)) {
        foreach ($property in @('log', 'trx')) {
            $source = Join-Path (Split-Path -Parent $shard.Path) `
                ([string]$result.$property)
            $result.$property = [IO.Path]::GetRelativePath(
                $finalParent, $source).Replace('\', '/')
        }
        $orderedResults.Add($result)
    }
}
$orderedResults = @($orderedResults | Sort-Object catalogOrdinal)
if ($orderedResults.Count -ne $catalogCount -or
    @($orderedResults.name | Sort-Object -Unique).Count -ne $catalogCount) {
    throw 'Parallel mutation shards do not cover the exact mutation catalog.'
}
foreach ($result in $orderedResults) {
    $result.PSObject.Properties.Remove('catalogOrdinal')
}

$temporaryOutput = $output + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
[pscustomobject]@{
    schemaVersion = 2
    commit = $ExpectedCommit
    configuration = $Configuration
    selection = 'full'
    catalogCount = $catalogCount
    catalogSha256 = $catalogSha256
    mutationCount = $orderedResults.Count
    killedCount = @($orderedResults | Where-Object killed).Count
    mutations = @($orderedResults)
} | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $temporaryOutput -Encoding utf8NoBOM
Move-Item -LiteralPath $temporaryOutput -Destination $output -Force
$campaignTimer.Stop()
$timingDirectory = Join-Path $repositoryRoot 'artifacts/timings'
[IO.Directory]::CreateDirectory($timingDirectory) | Out-Null
$timingOutput = Join-Path $timingDirectory (
    'mutation-' + $Configuration.ToLowerInvariant() + '.json')
$temporaryTiming = $timingOutput + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
[pscustomobject]@{
    schemaVersion = 1
    command = 'mutation'
    commit = $ExpectedCommit
    configuration = $Configuration
    strategy = 'weighted-longest-processing-time-first'
    parallelism = $parallelism
    totalElapsedMilliseconds = [long]$campaignTimer.Elapsed.TotalMilliseconds
    shards = @($shardTimings | Sort-Object index)
} | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $temporaryTiming -Encoding utf8NoBOM
Move-Item -LiteralPath $temporaryTiming -Destination $timingOutput -Force
Write-Host "Killed $catalogCount trusted-boundary mutations in $parallelism shards."
Write-Host "Evidence: $output"
Write-Host "Timing evidence: $timingOutput"
