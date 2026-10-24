[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Fuzz campaign regressions require the canonical Linux container.'
. (Join-Path $PSScriptRoot 'SharpProof.FuzzEvidenceLifecycle.ps1')

# Execute the actual campaign body with process and clean-source boundary stubs.
# This keeps the build/run routing, schedule, failure publication and identity
# checks under test without compiling a runner or weakening its real contract.
$campaignPath = Join-Path $PSScriptRoot 'Invoke-SharpProofFuzzCampaign.ps1'
$parseTokens = $null
$parseErrors = $null
$tree = [Management.Automation.Language.Parser]::ParseFile(
    $campaignPath, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'Fuzz campaign does not parse.' }
$campaignTry = @($tree.EndBlock.Statements | Where-Object {
        $_ -is [Management.Automation.Language.TryStatementAst]
    })
if ($campaignTry.Count -ne 1) { throw 'Actual fuzz campaign body not found.' }
$campaignBody = [scriptblock]::Create(
    'param([int]$RotatingSeed, [int]$RotatingCases, [int]$RetainedCases, [string]$RepositoryRoot)' + "`n" +
    (($campaignTry[0].Body.Statements | ForEach-Object { $_.Extent.Text }) -join "`n"))
$sourceRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-fuzz-campaign-regression-' + [guid]::NewGuid().ToString('N'))
$contractPath = Join-Path $fixtureRoot 'eng/acceptance/contract.json'
$retainedPath = Join-Path $fixtureRoot 'eng/fuzz/retained-seeds.json'
$runnerPath = Join-Path $fixtureRoot 'artifacts/bin/SharpProof.Fuzz/Release/net10.0/SharpProof.Fuzz.dll'
foreach ($path in @($contractPath, $retainedPath, $runnerPath)) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
}
$contractJson = Get-Content -LiteralPath (
    Join-Path $sourceRepositoryRoot 'eng/acceptance/contract.json') -Raw
Copy-Item -LiteralPath (Join-Path $sourceRepositoryRoot 'eng/fuzz/retained-seeds.json') -Destination $retainedPath
[IO.File]::WriteAllText($runnerPath, '', [Text.UTF8Encoding]::new($false))
$retainedManifest = Read-SharpProofRetainedFuzzSeedManifest -Path $retainedPath
$script:processCalls = [Collections.Immutable.ImmutableList[object]]::Empty
$script:sourceChecks = 0
$script:scenario = ''

function Get-SharpProofDotnetWrapperPath {
    return '/fixture/unused-dotnet-wrapper.ps1'
}

function Get-SharpProofCleanFuzzSourceCommit {
    param([string]$RepositoryRoot)
    if ($RepositoryRoot -cne $fixtureRoot) { throw 'Unexpected clean-source root.' }
    $script:sourceChecks++
    if ($script:scenario -ceq 'dirty-source') {
        throw 'Fuzz evidence requires a clean repository source tree.'
    }
    if (($script:scenario -ceq 'rebuild-source-change' -and $script:sourceChecks -eq 2) -or
        ($script:scenario -ceq 'campaign-source-change' -and $script:sourceChecks -eq 3)) {
        return 'b' * 40
    }
    return 'a' * 40
}

function Start-SharpProofEncodedPowerShell {
    param(
        [string]$WrapperPath, [int]$TimeoutSeconds,
        [Collections.Immutable.ImmutableArray[string]]$ImmutableArguments,
        [string]$WorkingDirectory, [string]$StandardOutput, [string]$StandardError
    )
    if ($WrapperPath -cne '/fixture/unused-dotnet-wrapper.ps1' -or
        $WorkingDirectory -cne $fixtureRoot) { throw 'Unexpected process boundary.' }
    $script:processCalls = $script:processCalls.Add(([ordered]@{
                Arguments = $ImmutableArguments; Timeout = $TimeoutSeconds
                StandardOutput = $StandardOutput; StandardError = $StandardError
            }).AsReadOnly())
    $exitCode = 0
    if ($ImmutableArguments[0] -ceq 'build') {
        foreach ($log in @($StandardOutput, $StandardError)) {
            if ([string]::IsNullOrWhiteSpace($log)) { throw 'Rebuild log capture was lost.' }
            [IO.File]::WriteAllText($log,
                "first-log-marker`n" + (('build output ' + ('x' * 700) + "`n") * 50) +
                "last-log-marker`n", [Text.UTF8Encoding]::new($false))
        }
        if ($script:scenario -ceq 'build-failure') { $exitCode = 17 }
        if ($script:scenario -ceq 'build-timeout') { $exitCode = 124 }
    }
    else {
        [IO.File]::WriteAllText($StandardOutput, '{"fixture":true}', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($StandardError, '', [Text.UTF8Encoding]::new($false))
        if ($script:scenario -ceq 'rotating-failure' -and $script:processCalls.Count -eq 2) { $exitCode = 19 }
        if ($script:scenario -ceq 'retained-timeout' -and $script:processCalls.Count -eq 3) { $exitCode = 124 }
    }
    return ([ordered]@{ ExitCode = $exitCode }).AsReadOnly()
}

function Assert-SharpProofFuzzRunnerResult {
    param([string]$Path, [int]$ExpectedCases, [int]$ExpectedSeed, [int]$ExpectedMaximumParallelism)
    $arguments = $script:processCalls[$script:processCalls.Count - 1].Arguments
    if (-not [IO.File]::Exists($Path) -or $ExpectedCases -ne [int]$arguments[2] -or
        $ExpectedSeed -ne [int]$arguments[4] -or $ExpectedMaximumParallelism -ne [int]$arguments[6]) {
        throw 'Runner validation binding was lost.'
    }
    return ([ordered]@{
            SchemaVersion = 5; Cases = $ExpectedCases; Agreements = $ExpectedCases
            Abstentions = 0; Passed = $true
        }).AsReadOnly()
}

function Invoke-CampaignFixture {
    param([string]$Name, [switch]$AlternateBudgets)
    $script:scenario = $Name
    $script:sourceChecks = 0
    $script:processCalls = [Collections.Immutable.ImmutableList[object]]::Empty
    $fixtureContract = $contractJson | ConvertFrom-Json
    if ($AlternateBudgets) {
        $fixtureContract.automation.solutionBuildWallSeconds = 37
        $fixtureContract.worker.maximumProjectWallSeconds = 11
        $fixtureContract.fuzz.rotatingRunWallSeconds = 53
    }
    [IO.File]::WriteAllText($contractPath,
        ($fixtureContract | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    $logicalOutput = "artifacts/fuzz/$Name"
    $resolvedOutput = Join-Path $fixtureRoot $logicalOutput
    if ($Name -ceq 'dirty-source') {
        [IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
        [IO.File]::WriteAllText((Join-Path $resolvedOutput 'runner-build.json'), '{"stale":true}')
    }
    $failure = ''
    try { & $campaignBody -RotatingSeed 123457 -RepositoryRoot $fixtureRoot | Out-Null }
    catch { $failure = $_.Exception.Message }
    $buildReceiptPath = Join-Path $resolvedOutput 'runner-build.json'
    if ($script:processCalls.Count -gt 0) {
        if (-not [IO.File]::Exists($buildReceiptPath)) { throw 'Runner rebuild timing receipt is missing.' }
        $buildReceipt = Get-Content -LiteralPath $buildReceiptPath -Raw | ConvertFrom-Json
        if ($buildReceipt.schemaVersion -ne 1 -or $buildReceipt.commit -cne ('a' * 40) -or
            $buildReceipt.deadlineSeconds -ne $fixtureContract.automation.solutionBuildWallSeconds -or
            $buildReceipt.elapsedMilliseconds -lt 0 -or
            $buildReceipt.exitCode -ne $(if ($Name -ceq 'build-failure') { 17 } elseif ($Name -ceq 'build-timeout') { 124 } else { 0 }) -or
            $buildReceipt.timedOut -ne ($Name -ceq 'build-timeout')) {
            throw 'Runner rebuild timing/deadline/failure receipt changed.'
        }
    }
    elseif ([IO.File]::Exists($buildReceiptPath)) { throw 'Rejected source emitted a rebuild timing receipt.' }
    $summaryPath = Join-Path $resolvedOutput 'campaign.json'
    $summary = if ([IO.File]::Exists($summaryPath)) {
        Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    }
    else { $null }
    return ([ordered]@{
            Failure = $failure; Summary = $summary; Calls = $script:processCalls
            SourceChecks = $script:sourceChecks; Output = $resolvedOutput
            Contract = $fixtureContract
        }).AsReadOnly()
}

try {
    foreach ($alternate in @($false, $true)) {
        $name = if ($alternate) { 'alternate-budgets' } else { 'success' }
        $result = Invoke-CampaignFixture -Name $name -AlternateBudgets:$alternate
        if ($result.Failure -or -not $result.Summary.passed -or $result.SourceChecks -ne 3) {
            throw "Successful campaign or its three identity checks failed: $($result.Failure)"
        }
        $calls = $result.Calls
        if ($calls.Count -ne 2 + $retainedManifest.Seeds.Length -or
            $calls[0].Arguments[0] -cne 'build' -or
            $calls[0].Timeout -ne $result.Contract.automation.solutionBuildWallSeconds -or
            -not $calls[0].Arguments.Contains('--no-incremental') -or
            $calls[1].Timeout -ne $result.Contract.fuzz.rotatingRunWallSeconds) {
            throw 'Build and rotating execution budgets or rebuild arguments changed.'
        }
        for ($index = 2; $index -lt $calls.Count; $index++) {
            if ($calls[$index].Timeout -ne $result.Contract.worker.maximumProjectWallSeconds -or
                [int]$calls[$index].Arguments[2] -ne $retainedManifest.CasesPerSeed -or
                [int]$calls[$index].Arguments[4] -ne $retainedManifest.Seeds[$index - 2]) {
                throw 'Retained proof budget, cases or seed changed.'
            }
        }
        if ($result.Summary.rotatingSeed -ne 123457 -or
            $result.Summary.rotatingCases -ne $result.Contract.fuzz.nightlyCases -or
            $result.Summary.requestedCases -ne ($result.Contract.fuzz.nightlyCases +
                $retainedManifest.CasesPerSeed * $retainedManifest.Seeds.Length)) {
            throw 'Campaign seed or case schedule changed.'
        }
    }
    foreach ($name in @('build-failure', 'build-timeout')) {
        $result = Invoke-CampaignFixture -Name $name
        $expected = if ($name -ceq 'build-timeout') {
            "timed out after $($result.Contract.automation.solutionBuildWallSeconds) seconds (code 124)"
        }
        else { 'failed with code 17' }
        if (-not $result.Failure.Contains($expected) -or $null -ne $result.Summary -or
            $result.Calls.Count -ne 1 -or $result.SourceChecks -ne 1 -or
            $result.Failure.Length -gt 17000 -or $result.Failure.Contains('first-log-marker') -or
            -not $result.Failure.Contains('last-log-marker')) {
            throw "Rebuild failure or bounded diagnostics were lost: $name"
        }
        foreach ($file in @('runner-build.stdout.txt', 'runner-build.stderr.txt')) {
            if ((Get-Item -LiteralPath (Join-Path $result.Output $file)).Length -lt 30000 -or
                -not $result.Failure.Contains($file)) { throw 'Complete rebuild logs were not retained.' }
        }
    }
    foreach ($name in @('rotating-failure', 'retained-timeout')) {
        $result = Invoke-CampaignFixture -Name $name
        $runIndex = if ($name -ceq 'rotating-failure') { 0 } else { 1 }
        $expectedCode = if ($name -ceq 'rotating-failure') { 19 } else { 124 }
        if (-not $result.Failure.Contains('SharpProof fuzz campaign failed.') -or
            $result.Summary.passed -or $result.Summary.status -cne 'failed' -or
            $result.Summary.runs[$runIndex].exitCode -ne $expectedCode -or
            $result.Summary.runs[$runIndex].validationPassed -or $result.SourceChecks -ne 3) {
            throw "Runner failure was not retained in failed campaign evidence: $name"
        }
    }
    foreach ($name in @('dirty-source', 'rebuild-source-change', 'campaign-source-change')) {
        $result = Invoke-CampaignFixture -Name $name
        $expected = switch ($name) {
            'dirty-source' { 'Fuzz evidence requires a clean repository source tree.' }
            'rebuild-source-change' { 'Fuzz source changed while rebuilding the runner.' }
            'campaign-source-change' { 'Fuzz source changed during the campaign.' }
        }
        if ($result.Failure -cne $expected -or $null -ne $result.Summary) {
            throw "Clean exact-source certification was lost: $name"
        }
    }
    Write-Host 'Fuzz campaign regressions passed: separate build/proof budgets, unchanged schedules, bounded retained build diagnostics, failed runner evidence and exact-source checks.'
}
finally {
    $fullFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $fullFixture.StartsWith($temporaryParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($fullFixture) -notlike 'sharpproof-fuzz-campaign-regression-*') {
        throw 'Refusing to remove an unexpected fuzz regression fixture.'
    }
    Remove-Item -LiteralPath $fullFixture -Recurse -Force
}
