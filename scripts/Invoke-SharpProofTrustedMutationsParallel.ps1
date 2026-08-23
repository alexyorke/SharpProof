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
Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.MutationBaselines.psm1') -Force
Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.MutationEvidence.psm1') -Force
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
        [string]$existing.selection -eq 'full') {
        & (Join-Path $PSScriptRoot 'Test-SharpProofMutationCatalog.ps1') `
            -EvidencePath $output `
            -ExpectedCommit $ExpectedCommit
        Write-Host "Mutation evidence is already complete: $output"
        return
    }
}

$shardRoot = Join-Path (Split-Path -Parent $output) (
    'shards/' + $ExpectedCommit + '/' + $Configuration.ToLowerInvariant() +
    '-weighted-v3-focused-baseline-' + $parallelism)
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
            [int]$evidence.mutationCount -eq [int]$evidence.killedCount -and
            @($evidence.mutations | Where-Object {
                    [string]$_.baselineInvocationSha256 -notmatch
                        '^[0-9a-f]{64}$' -or
                    [string]$_.assertionProvenanceSha256 -notmatch
                        '^[0-9a-f]{64}$' -or
                    @($_.baselineSelectedTests).Count -eq 0 -or
                    [string]$_.baselineTrxSha256 -notmatch '^[0-9a-f]{64}$'
                }).Count -eq 0
    }
    catch {
        return $false
    }
}

$baselinePath = Join-Path $shardRoot 'baseline.json'
$baselineRelativePath = [IO.Path]::GetRelativePath(
    $repositoryRoot, $baselinePath)
function Test-CompleteBaseline {
    if (-not (Test-Path -LiteralPath $baselinePath -PathType Leaf)) {
        return $false
    }
    try {
        $baseline = Get-Content -LiteralPath $baselinePath -Raw |
            ConvertFrom-Json
        $rows = @($baseline.tests)
        if ([int]$baseline.schemaVersion -ne 2 -or
            [string]$baseline.commit -ne $ExpectedCommit -or
            [string]$baseline.configuration -ne $Configuration -or
            [string]$baseline.selection -ne 'full' -or
            [int]$baseline.catalogCount -ne $catalogCount -or
            [string]$baseline.catalogSha256 -ne $catalogSha256 -or
            [int]$baseline.testCount -le 0 -or
            [int]$baseline.testCount -ne $rows.Count) {
            return $false
        }
        foreach ($row in $rows) {
            $invocation = Get-SharpProofMutationBaselineInvocation `
                -Project ([string]$row.project) `
                -Filter ([string]$row.filter) `
                -Configuration $Configuration
            $trx = [IO.Path]::GetFullPath((Join-Path `
                    $shardRoot ([string]$row.trx)))
            if ([string]$row.configuration -ne $Configuration -or
                [string]$row.invocationSha256 -ne $invocation.Sha256 -or
                @($row.ledger).Count -eq 0 -or
                [string]$row.trxSha256 -notmatch '^[0-9a-f]{64}$' -or
                -not $trx.StartsWith(
                    $shardRoot + [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::Ordinal) -or
                -not [IO.File]::Exists($trx) -or
                (Get-FileHash -LiteralPath $trx -Algorithm SHA256).
                    Hash.ToLowerInvariant() -ne [string]$row.trxSha256) {
                return $false
            }
        }
        return $true
    }
    catch {
        return $false
    }
}

$baselineTimer = [Diagnostics.Stopwatch]::StartNew()
$baselineReused = Test-CompleteBaseline
if (-not $baselineReused) {
    Remove-Item -LiteralPath $baselinePath `
        -Force -ErrorAction SilentlyContinue
    $baselineRunOutput = [IO.Path]::GetRelativePath(
        $repositoryRoot, (Join-Path $shardRoot 'baseline-run.json'))
    & pwsh -NoLogo -NoProfile -File (
        Join-Path $PSScriptRoot 'Test-SharpProofTrustedMutations.ps1') `
        -Configuration $Configuration `
        -OutputPath $baselineRunOutput `
        -ExpectedCommit $ExpectedCommit `
        -BaselineEvidencePath $baselineRelativePath `
        -BaselineOnly
    if ($LASTEXITCODE -ne 0 -or -not (Test-CompleteBaseline)) {
        throw 'The shared mutation baseline campaign failed.'
    }
}
else {
    Write-Host "Reusing exact mutation baselines: $baselinePath"
}
$baselineTimer.Stop()
$baselineEvidence = Get-Content -LiteralPath $baselinePath -Raw |
    ConvertFrom-Json

function New-ShardTiming {
    param(
        [Parameter(Mandatory = $true)][object]$Shard,
        [Parameter(Mandatory = $true)][object]$Evidence,
        [Parameter(Mandatory = $true)][long]$ElapsedMilliseconds,
        [Parameter(Mandatory = $true)][bool]$Reused
    )

    $timing = if ($Evidence.PSObject.Properties.Name -contains 'timing') {
        $Evidence.timing
    }
    else {
        $null
    }
    return [pscustomobject]@{
        index = $Shard.Index
        mutationCount = [int]$Evidence.mutationCount
        elapsedMilliseconds = $ElapsedMilliseconds
        reused = $Reused
        restoreElapsedMilliseconds =
            $(if ($null -ne $timing) {
                [long]$timing.restoreElapsedMilliseconds
            }
            else { 0L })
        baselineElapsedMilliseconds =
            $(if ($null -ne $timing) {
                [long]$timing.baselineElapsedMilliseconds
            }
            else { 0L })
        mutationElapsedMilliseconds =
            $(if ($null -ne $timing) {
                [long]$timing.mutationElapsedMilliseconds
            }
            else { 0L })
        baselineInvocationCount =
            $(if ($null -ne $timing) {
                [int]$timing.baselineInvocationCount
            }
            else { 0 })
        mutationInvocationCount =
            $(if ($null -ne $timing) {
                [int]$timing.mutationInvocationCount
            }
            else { 0 })
    }
}

$running = [Collections.Generic.List[object]]::new()
try {
    foreach ($shard in $shards) {
        if (Test-CompleteShard $shard) {
            Write-Host "Reusing completed mutation shard $($shard.Index + 1)."
            $evidence = Get-Content -LiteralPath $shard.Path -Raw |
                ConvertFrom-Json
            $shardTimings.Add((New-ShardTiming `
                    -Shard $shard `
                    -Evidence $evidence `
                    -ElapsedMilliseconds 0 `
                    -Reused $true))
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
                '-BaselineEvidencePath', $baselineRelativePath,
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
        $shardTimings.Add((New-ShardTiming `
                -Shard $active.Shard `
                -Evidence $evidence `
                -ElapsedMilliseconds ([long](
                    ($active.Process.ExitTime.ToUniversalTime() -
                        $active.StartedUtc).TotalMilliseconds)) `
                -Reused $false))
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
        foreach ($property in @('log', 'trx', 'baselineTrx')) {
            $source = Join-Path (Split-Path -Parent $shard.Path) `
                ([string]$result.$property)
            $result.$property = [IO.Path]::GetRelativePath(
                $finalParent, $source).Replace('\', '/')
        }
        $orderedResults.Add($result)
    }
}
$orderedResults = @($orderedResults | Sort-Object catalogOrdinal)
$actualCatalogSha256 = Get-SharpProofMutationCatalogSha256 `
    -Mutations $orderedResults
if ($orderedResults.Count -ne $catalogCount -or
    @($orderedResults.name | Sort-Object -Unique).Count -ne $catalogCount -or
    $actualCatalogSha256 -ne $catalogSha256) {
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
    strategy = 'weighted-longest-processing-time-first-focused-baseline-v3'
    parallelism = $parallelism
    totalElapsedMilliseconds = [long]$campaignTimer.Elapsed.TotalMilliseconds
    baseline = [ordered]@{
        reused = $baselineReused
        elapsedMilliseconds = [long]$baselineTimer.Elapsed.TotalMilliseconds
        restoreElapsedMilliseconds =
            [long]$baselineEvidence.timing.restoreElapsedMilliseconds
        testElapsedMilliseconds =
            [long]$baselineEvidence.timing.baselineElapsedMilliseconds
        invocationCount =
            [int]$baselineEvidence.timing.baselineInvocationCount
        testCount = [int]$baselineEvidence.testCount
    }
    shards = @($shardTimings | Sort-Object index)
} | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $temporaryTiming -Encoding utf8NoBOM
Move-Item -LiteralPath $temporaryTiming -Destination $timingOutput -Force
Write-Host "Killed $catalogCount trusted-boundary mutations in $parallelism shards."
Write-Host "Evidence: $output"
Write-Host "Timing evidence: $timingOutput"
