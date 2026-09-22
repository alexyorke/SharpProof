[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
. (Join-Path $PSScriptRoot 'SharpProof.FuzzEvidenceLifecycle.ps1')

$seedToday = Get-SharpProofRotatingSeed -UtcDate ([DateTime]::new(2026, 8, 31))
$seedAfterStride = Get-SharpProofRotatingSeed -UtcDate ([DateTime]::new(2027, 10, 2))
if ((($seedAfterStride - $seedToday) % 397) -eq 0) {
    throw 'Rotating seeds repeat the FuzzRunner case stride after 397 days.'
}

$campaignScript = Get-Content -Raw (Join-Path $PSScriptRoot 'Invoke-SharpProofFuzzCampaign.ps1')
if ($campaignScript -notmatch '\[int\]\$_ -ne \$RotatingSeed') {
    throw 'Campaign must not replay a retained seed used by the rotating run.'
}

$root = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-fuzz-evidence-' + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $campaign = Join-Path $root 'campaign.json'
    $unrelated = Join-Path $root 'notes.txt'
    [IO.File]::WriteAllText($campaign, '{"passed":true}')
    [IO.File]::WriteAllText((Join-Path $root 'rotating-1.stdout.json'), 'old')
    [IO.File]::WriteAllText((Join-Path $root 'rotating-1.stderr.txt'), 'old')
    [IO.File]::WriteAllText((Join-Path $root 'retained-2.stdout.json'), 'old')
    [IO.File]::WriteAllText((Join-Path $root 'retained-2.stderr.txt'), 'old')
    [IO.File]::WriteAllText((Join-Path $root 'rotating--1.stdout.json'), 'old')
    [IO.File]::WriteAllText((Join-Path $root 'retained--2.stderr.txt'), 'old')
    $noncanonical = @(
        'rotating-2147483648.stdout.json',
        'retained--2147483649.stderr.txt',
        'retained-0007.stdout.json')
    foreach ($name in $noncanonical) {
        [IO.File]::WriteAllText((Join-Path $root $name), 'keep')
    }
    [IO.File]::WriteAllText($unrelated, 'keep')

    $gitRoot = Join-Path $root 'source'
    [IO.Directory]::CreateDirectory($gitRoot) | Out-Null
    [IO.File]::WriteAllText((Join-Path $gitRoot 'tracked.txt'), 'clean')
    Initialize-SharpProofFixtureRepository `
        -RepositoryRoot $gitRoot `
        -UserEmail 'fixture@sharpproof.invalid' `
        -Paths 'tracked.txt' `
        -CommitMessage baseline
    $expectedCommit = (& git -C $gitRoot rev-parse HEAD).Trim()
    $actualCommit = Get-SharpProofCleanFuzzSourceCommit `
        -RepositoryRoot $gitRoot
    if ($actualCommit -cne $expectedCommit) {
        throw 'Clean fuzz source state did not retain the exact commit.'
    }
    $untrackedSource = Join-Path $gitRoot 'untracked.cs'
    [IO.File]::WriteAllText($untrackedSource, 'class Untracked {}')
    $untrackedRejected = $false
    try { [void](Get-SharpProofCleanFuzzSourceCommit -RepositoryRoot $gitRoot) }
    catch { $untrackedRejected = $true }
    [IO.File]::Delete($untrackedSource)
    if (-not $untrackedRejected -or
        (Get-SharpProofCleanFuzzSourceCommit -RepositoryRoot $gitRoot) -cne $expectedCommit) {
        throw 'Untracked fuzz source was certified as HEAD.'
    }
    [IO.File]::AppendAllText((Join-Path $gitRoot 'tracked.txt'), 'dirty')
    $dirtyRejected = $false
    try {
        [void](Get-SharpProofCleanFuzzSourceCommit -RepositoryRoot $gitRoot)
    }
    catch {
        $dirtyRejected = $_.Exception.Message -ceq
            'Fuzz evidence requires a clean repository source tree.'
    }
    if (-not $dirtyRejected) {
        throw 'Dirty tracked fuzz source state was accepted.'
    }

    Initialize-SharpProofFuzzEvidence -OutputDirectory $root
    if ([IO.File]::Exists($campaign) -or
        [IO.File]::Exists((Join-Path $root 'rotating-1.stdout.json')) -or
        [IO.File]::Exists((Join-Path $root 'rotating-1.stderr.txt')) -or
        [IO.File]::Exists((Join-Path $root 'retained-2.stdout.json')) -or
        [IO.File]::Exists((Join-Path $root 'retained-2.stderr.txt')) -or
        [IO.File]::Exists((Join-Path $root 'rotating--1.stdout.json')) -or
        [IO.File]::Exists((Join-Path $root 'retained--2.stderr.txt'))) {
        throw 'Owned stale fuzz evidence survived initialization.'
    }
    foreach ($name in $noncanonical) {
        if ([IO.File]::ReadAllText((Join-Path $root $name)) -cne 'keep') {
            throw "Noncanonical fuzz-like output was changed: $name"
        }
    }
    if ([IO.File]::ReadAllText($unrelated) -cne 'keep') {
        throw 'Unrelated fuzz output was changed.'
    }

    $lease = Enter-SharpProofFuzzEvidenceLease `
        -OutputDirectory $root -TimeoutSeconds 0
    $overlapRejected = $false
    try {
        [void](Enter-SharpProofFuzzEvidenceLease `
            -OutputDirectory $root -TimeoutSeconds 0)
    }
    catch {
        $overlapRejected = $_.Exception.Message -like
            'Timed out acquiring fuzz evidence lease:*'
    }
    finally {
        Exit-SharpProofFuzzEvidenceLease -Lease $lease
    }
    if (-not $overlapRejected) {
        throw 'Overlapping fuzz evidence leases were accepted.'
    }

    $manifest = Join-Path $root 'retained-seeds.json'
    [IO.File]::WriteAllText(
        $manifest,
        '{"schemaVersion":1,"casesPerSeed":5,"seeds":[-1,2]}')
    $parsed = Read-SharpProofRetainedFuzzSeedManifest -Path $manifest
    if ($parsed.CasesPerSeed -ne 5 -or
        @($parsed.Seeds).Count -ne 2 -or $parsed.Seeds[0] -ne -1) {
        throw 'A strict retained fuzz seed manifest was not preserved.'
    }
    $raced = Read-SharpProofRetainedFuzzSeedManifest `
        -Path $manifest `
        -AfterValidation {
            param($validatedPath)
            [IO.File]::WriteAllText(
                $validatedPath,
                '{"schemaVersion":1,"casesPerSeed":9,"seeds":[7]}')
        }
    if ($raced.CasesPerSeed -ne 5 -or
        @($raced.Seeds).Count -ne 2 -or
        $raced.Seeds[0] -ne -1) {
        throw 'Retained fuzz seed parsing changed after its validated read.'
    }
    foreach ($invalid in @(
            '{"schemaVersion":1,"casesPerSeed":"5","seeds":[1]}',
            '{"schemaVersion":1,"casesPerSeed":5,"seeds":[null]}',
            '{"schemaVersion":1,"casesPerSeed":5,"seeds":[true]}',
            '{"schemaVersion":1,"casesPerSeed":5,"seeds":[1.6]}',
            '{"schemaVersion":1,"casesPerSeed":1000001,"seeds":[1]}',
            '{"schemaVersion":1,"casesPerSeed":5,"seeds":["7"]}')) {
        [IO.File]::WriteAllText($manifest, $invalid)
        $rejected = $false
        try { [void](Read-SharpProofRetainedFuzzSeedManifest -Path $manifest) }
        catch { $rejected = $true }
        if (-not $rejected) {
            throw "Malformed retained seed manifest was accepted: $invalid"
        }
    }
    foreach ($invalidBudget in @(0, 1000001, '5')) {
        $rejected = $false
        try {
            [void](Assert-SharpProofFuzzCaseBudget `
                -Value $invalidBudget -Name 'fixture budget')
        }
        catch { $rejected = $true }
        if (-not $rejected) {
            throw "Invalid fuzz case budget was accepted: $invalidBudget"
        }
    }
    $boundedManifest =
        '{"schemaVersion":1,"casesPerSeed":1,"seeds":[1]}'
    $encoding = [Text.UTF8Encoding]::new($false)
    $exactManifest = $boundedManifest +
        (' ' * (1048576 - $encoding.GetByteCount($boundedManifest)))
    [IO.File]::WriteAllText($manifest, $exactManifest, $encoding)
    $exactParsed = Read-SharpProofRetainedFuzzSeedManifest -Path $manifest
    if ($exactParsed.CasesPerSeed -ne 1 -or
        @($exactParsed.Seeds).Count -ne 1) {
        throw 'The exact-limit retained manifest was not accepted.'
    }
    [IO.File]::AppendAllText($manifest, ' ', $encoding)
    $rejected = $false
    try { [void](Read-SharpProofRetainedFuzzSeedManifest -Path $manifest) }
    catch { $rejected = $true }
    if (-not $rejected) {
        throw 'An oversized retained fuzz seed manifest was accepted.'
    }
    [void](Assert-SharpProofFuzzCampaignBudget `
        -RotatingCases 10000 -RetainedCases 1000 `
        -RetainedRunCount 1 -MaximumCases 1000000)
    $rejected = $false
    try {
        [void](Assert-SharpProofFuzzCampaignBudget `
            -RotatingCases 1000000 -RetainedCases 1 `
            -RetainedRunCount 1 -MaximumCases 1000000)
    }
    catch { $rejected = $true }
    if (-not $rejected) {
        throw 'An aggregate fuzz campaign above the maximum was accepted.'
    }
    $tooManySeeds = '{"schemaVersion":1,"casesPerSeed":1,"seeds":[' +
        ((1..1025) -join ',') + ']}'
    [IO.File]::WriteAllText($manifest, $tooManySeeds)
    $rejected = $false
    try { [void](Read-SharpProofRetainedFuzzSeedManifest -Path $manifest) }
    catch { $rejected = $true }
    if (-not $rejected) {
        throw 'A retained manifest above the seed-count limit was accepted.'
    }

    # A prerequisite or launcher failure after initialization publishes nothing.
    if ([IO.File]::Exists($campaign)) {
        throw 'A failed run retained stable campaign evidence.'
    }

    $failedSummary = [pscustomobject][ordered]@{
        schemaVersion = 4
        status = 'failed'
        commit = ('0' * 40)
        runs = @([pscustomobject][ordered]@{
                name = 'rotating-1'
                exitCode = 1
                validationPassed = $false
                validationError = 'runner exited with code 1'
            })
        passed = $false
    }
    $failureMessage = $null
    try {
        $null = Complete-SharpProofFuzzEvidence `
            -OutputDirectory $root `
            -Summary $failedSummary
    }
    catch {
        $failureMessage = $_.Exception.Message
    }
    if ($failureMessage -cne
            "SharpProof fuzz campaign failed. Evidence: $campaign" -or
        -not [IO.File]::Exists($campaign)) {
        throw 'Failed campaign evidence was not published before failure.'
    }
    $failedEvidence = Get-Content -LiteralPath $campaign -Raw |
        ConvertFrom-Json
    if ([string]$failedEvidence.status -cne 'failed' -or
        [bool]$failedEvidence.passed -or
        @($failedEvidence.runs).Count -ne 1) {
        throw 'Published failed campaign evidence lost its failure details.'
    }

    Initialize-SharpProofFuzzEvidence -OutputDirectory $root
    $first = "{`"schemaVersion`":3,`"passed`":true}`n"
    Publish-SharpProofFuzzEvidence -OutputDirectory $root -Json $first
    if ([IO.File]::ReadAllText($campaign) -cne $first -or
        [IO.File]::Exists((Join-Path $root '.campaign.json.tmp'))) {
        throw 'Successful campaign evidence was not atomically completed.'
    }

    # Retry invalidates the prior pass and can publish a new complete generation.
    Initialize-SharpProofFuzzEvidence -OutputDirectory $root
    $second = "{`"schemaVersion`":3,`"passed`":true,`"retry`":true}`n"
    Publish-SharpProofFuzzEvidence -OutputDirectory $root -Json $second
    if ([IO.File]::ReadAllText($campaign) -cne $second -or
        [IO.File]::ReadAllText($unrelated) -cne 'keep') {
        throw 'Retry did not replace only the owned stable evidence.'
    }

    # Execute the real campaign in a tiny committed fixture. Only external
    # process launching is stubbed; evidence publication and leases are real.
    $fixture = Join-Path $root 'lease-source'
    $fixtureScripts = Join-Path $fixture 'scripts'
    [IO.Directory]::CreateDirectory($fixtureScripts) | Out-Null
    foreach ($name in @('Invoke-SharpProofFuzzCampaign.ps1',
            'Resolve-SharpProofContainedPath.ps1', 'Assert-SharpProofFuzzRunnerResult.ps1',
            'Assert-SharpProofJsonProperties.ps1', 'SharpProof.FuzzEvidenceLifecycle.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $fixtureScripts
    }
    $module = @'
function Get-SharpProofDotnetWrapperPath { return 'fixture' }
function Start-SharpProofEncodedPowerShell {
    param($WrapperPath, $TimeoutSeconds, $WorkingDirectory, $Arguments, $StandardOutput, $StandardError)
    if ($StandardOutput) {
        [IO.File]::WriteAllText($StandardOutput, 'retained failed run')
        [IO.File]::WriteAllText($StandardError, 'injected runner failure')
        return [pscustomobject]@{ ExitCode = 1 }
    }
    return [pscustomobject]@{ ExitCode = 0 }
}
Export-ModuleMember -Function *
'@
    [IO.File]::WriteAllText((Join-Path $fixtureScripts 'SharpProof.ContainerExecution.psm1'), $module)
    foreach ($directory in @('eng/acceptance', 'eng/fuzz', 'Tools/SharpProof.Fuzz/bin/Release/net9.0')) {
        [IO.Directory]::CreateDirectory((Join-Path $fixture $directory)) | Out-Null
    }
    [IO.File]::WriteAllText((Join-Path $fixture '.gitignore'), "artifacts/`n")
    [IO.File]::WriteAllText((Join-Path $fixture 'eng/acceptance/contract.json'),
        '{"fuzz":{"nightlyCases":1,"maximumCampaignCases":1,"maximumParallelism":1},"worker":{"maximumProjectWallSeconds":1}}')
    [IO.File]::WriteAllText((Join-Path $fixture 'eng/fuzz/retained-seeds.json'),
        '{"schemaVersion":1,"casesPerSeed":1,"seeds":[7]}')
    [IO.File]::WriteAllText((Join-Path $fixture 'Tools/SharpProof.Fuzz/bin/Release/net9.0/SharpProof.Fuzz.dll'), '')
    Initialize-SharpProofFixtureRepository -RepositoryRoot $fixture `
        -UserEmail 'fixture@sharpproof.invalid' -Paths '.' -CommitMessage baseline
    foreach ($failure in @('dirty', 'summary')) {
        $extra = Join-Path $fixture 'untracked.cs'
        if ($failure -eq 'dirty') { [IO.File]::WriteAllText($extra, 'class Extra {}') }
        $failureMessage = ''
        try {
            & (Join-Path $fixtureScripts 'Invoke-SharpProofFuzzCampaign.ps1') `
                -OutputDirectory 'artifacts/fuzz' -RotatingSeed 7 -RotatingCases 1 -RetainedCases 1
        }
        catch { $failureMessage = $_.Exception.Message }
        [IO.File]::Delete($extra)
        if ($failureMessage -notlike $(if ($failure -eq 'dirty') { 'Fuzz evidence requires*' } else { 'SharpProof fuzz campaign failed*' })) {
            throw "The campaign did not reach the expected $failure failure: $failureMessage"
        }
        $fixtureOutput = Join-Path $fixture 'artifacts/fuzz'
        $retryLease = Enter-SharpProofFuzzEvidenceLease -OutputDirectory $fixtureOutput -TimeoutSeconds 0
        Exit-SharpProofFuzzEvidenceLease -Lease $retryLease
        if ($failure -eq 'summary' -and
            (-not [IO.File]::Exists((Join-Path $fixtureOutput 'campaign.json')) -or
             -not [IO.File]::Exists((Join-Path $fixtureOutput 'rotating-7.stderr.txt')))) {
            throw 'Failed campaign evidence was lost during lease cleanup.'
        }
    }

    Write-Host 'Fuzz evidence lifecycle fixtures: 28'
}
finally {
    if ([IO.Directory]::Exists($root)) {
        [IO.Directory]::Delete($root, $true)
    }
}
