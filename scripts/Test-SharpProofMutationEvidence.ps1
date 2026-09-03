[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationEvidence.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationBaselines.psm1') -Force

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-mutation-evidence-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null

function Write-Fixture {
    param(
        [string]$Name,
        [string]$Summary,
        [string]$Counters,
        [string]$Definitions,
        [string]$Entries,
        [string]$Results
    )

    $path = Join-Path $fixtureRoot ($Name + '.trx')
    $xml = @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <TestDefinitions>$Definitions</TestDefinitions>
  <TestEntries>$Entries</TestEntries>
  <Results>$Results</Results>
  <ResultSummary outcome="$Summary"><Counters $Counters /></ResultSummary>
</TestRun>
"@
    [IO.File]::WriteAllText($path, $xml, [Text.UTF8Encoding]::new($false))
    return $path
}

function Write-RawFixture {
    param(
        [string]$Name,
        [string]$Xml
    )

    $path = Join-Path $fixtureRoot ($Name + '.trx')
    [IO.File]::WriteAllText(
        $path,
        $Xml,
        [Text.UTF8Encoding]::new($false))
    return $path
}

function New-TestParts {
    param(
        [string]$Outcome,
        [string]$Message,
        [string]$Method = 'ExpectedTest',
        [string]$DisplayName,
        [string]$Class = 'SharpProof.Test.EvidenceTests',
        [string]$TestId = 'test-1',
        [string]$ExecutionId = 'execution-1',
        [string]$StackTrace = 'at SharpProof.Test.EvidenceTests.ExpectedTest() in /workspace/SharpProof/Test.cs:line 1'
    )

    if ([string]::IsNullOrEmpty($DisplayName)) {
        $DisplayName = $Method
    }
    $escaped = [Security.SecurityElement]::Escape($Message)
    $output = if ($Outcome -eq 'Failed') {
        $escapedStack = [Security.SecurityElement]::Escape($StackTrace)
        "<Output><ErrorInfo><Message>$escaped</Message><StackTrace>$escapedStack</StackTrace></ErrorInfo></Output>"
    }
    else { '' }
    return [pscustomobject]@{
        Definition = "<UnitTest id='$TestId' name='$DisplayName'><Execution id='$ExecutionId'/><TestMethod className='$Class' name='$Method'/></UnitTest>"
        Entry = "<TestEntry testId='$TestId' executionId='$ExecutionId'/>"
        Result = "<UnitTestResult testId='$TestId' executionId='$ExecutionId' testName='$DisplayName' outcome='$Outcome'>$output</UnitTestResult>"
        Identity = "$Class.$Method|$DisplayName"
    }
}

function New-TrxFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [object[]]$Parts,
        [int]$Failed = 0,
        [string]$Summary
    )

    $parts = @($Parts)
    $total = $parts.Count
    if ($total -eq 0 -or $Failed -lt 0 -or $Failed -gt $total) {
        throw 'TRX fixtures require a valid part and failure count.'
    }
    if ([string]::IsNullOrEmpty($Summary)) {
        $Summary = if ($Failed -gt 0) { 'Failed' } else { 'Completed' }
    }
    $counters = 'total="{0}" executed="{0}" passed="{1}" failed="{2}" {3}' -f `
        $total, ($total - $Failed), $Failed, $zeroInfrastructure
    return Write-Fixture `
        -Name $Name `
        -Summary $Summary `
        -Counters $counters `
        -Definitions (($parts | ForEach-Object Definition) -join '') `
        -Entries (($parts | ForEach-Object Entry) -join '') `
        -Results (($parts | ForEach-Object Result) -join '')
}

function Assert-Throws {
    param(
        [scriptblock]$Action,
        [string]$Because,
        [string]$ExpectedMessage
    )

    $failure = $null
    try {
        & $Action
    }
    catch {
        $failure = $_
    }
    if ($null -eq $failure) {
        throw "Expected rejection: $Because"
    }
    if ($failure.Exception.Message -notlike "*$ExpectedMessage*") {
        throw (
            "Unexpected rejection for '$Because': " +
            $failure.Exception.Message)
    }
}

function Test-MutationReuseValidation {
    $repository = Join-Path $fixtureRoot 'reuse-repository'
    $scripts = Join-Path $repository 'scripts'
    $contractDirectory = Join-Path $repository 'eng/acceptance'
    $evidenceDirectory = Join-Path $repository 'artifacts/mutation'
    $receiptDirectory = Join-Path $evidenceDirectory 'receipts'
    New-Item -ItemType Directory -Path `
        $scripts, $contractDirectory, $receiptDirectory, `
        (Join-Path $repository 'Project') -Force | Out-Null
    foreach ($name in @(
            'Invoke-SharpProofTrustedMutationsParallel.ps1',
            'Test-SharpProofMutationCatalog.ps1',
            'SharpProof.MutationEvidence.psm1',
            'SharpProof.MutationBaselines.psm1',
            'SharpProof.ContainerExecution.psm1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) `
            -Destination (Join-Path $scripts $name)
    }
    [IO.File]::WriteAllText(
        (Join-Path $scripts 'Test-SharpProofTrustedMutations.ps1'),
        "Set-Content -LiteralPath (Join-Path `$PSScriptRoot '../campaign-launched') -Value launched`nthrow 'campaign launched'`n",
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $repository '.gitignore'),
        "/artifacts/`n",
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $repository 'Project/Source.cs'),
        "internal static class Source { }`n",
        [Text.UTF8Encoding]::new($false))

    $catalog = @(
        [pscustomobject][ordered]@{
            Name = 'first-mutation'
            File = 'Project/Source.cs'
            Project = 'Project.Test/Project.Test.csproj'
            Filter = 'FullyQualifiedName~FirstTest'
            Original = 'before-one'
            Mutated = 'after-one'
        },
        [pscustomobject][ordered]@{
            Name = 'second-mutation'
            File = 'Project/Source.cs'
            Project = 'Project.Test/Project.Test.csproj'
            Filter = 'FullyQualifiedName~SecondTest'
            Original = 'before-two'
            Mutated = 'after-two'
        })
    [pscustomobject]@{
        mutationEvidence = [ordered]@{
            expectedCatalogCount = $catalog.Count
        }
        automation = [ordered]@{
            mutationParallelism = 1
            mutationShardWallSeconds = 30
        }
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (
        Join-Path $contractDirectory 'contract.json') -Encoding utf8NoBOM

    $zeroInfrastructure = 'error="0" timeout="0" aborted="0" inconclusive="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" passedButRunAborted="0"'
    $results = @()
    for ($index = 0; $index -lt $catalog.Count; $index++) {
        $entry = $catalog[$index]
        $method = ([string]$entry.Filter).Substring(
            'FullyQualifiedName~'.Length)
        $identity = "Fixture.Tests.$method|$method"
        $parts = New-TestParts `
            -Outcome Failed `
            -Message "Assert.That(actual, Is.EqualTo(expected))`nExpected: 1`nBut was: 2" `
            -Method $method `
            -DisplayName $method `
            -Class 'Fixture.Tests' `
            -TestId "test-$index" `
            -ExecutionId "execution-$index"
        $trx = Join-Path $receiptDirectory ($entry.Name + '.trx')
        $log = Join-Path $receiptDirectory ($entry.Name + '.log')
        $xml = @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <TestDefinitions>$($parts.Definition)</TestDefinitions>
  <TestEntries>$($parts.Entry)</TestEntries>
  <Results>$($parts.Result)</Results>
  <ResultSummary outcome="Failed"><Counters total="1" executed="1" passed="0" failed="1" $zeroInfrastructure /></ResultSummary>
</TestRun>
"@
        [IO.File]::WriteAllText(
            $trx, $xml, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText(
            $log, "assertion-backed failure`n", [Text.UTF8Encoding]::new($false))
        $baselineParts = New-TestParts `
            -Outcome Passed `
            -Message '' `
            -Method $method `
            -DisplayName $method `
            -Class 'Fixture.Tests' `
            -TestId "baseline-test-$index" `
            -ExecutionId "baseline-execution-$index"
        $baselineTrx = Join-Path $receiptDirectory (
            $entry.Name + '-baseline.trx')
        $baselineXml = @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <TestDefinitions>$($baselineParts.Definition)</TestDefinitions>
  <TestEntries>$($baselineParts.Entry)</TestEntries>
  <Results>$($baselineParts.Result)</Results>
  <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" $zeroInfrastructure /></ResultSummary>
</TestRun>
"@
        [IO.File]::WriteAllText(
            $baselineTrx,
            $baselineXml,
            [Text.UTF8Encoding]::new($false))
        $invocation = Get-SharpProofMutationBaselineInvocation `
            -Project $entry.Project `
            -Filter $entry.Filter `
            -Configuration Release
        $results += [pscustomobject][ordered]@{
            name = $entry.Name
            file = $entry.File
            test = $entry.Filter
            project = $entry.Project
            original = $entry.Original
            mutated = $entry.Mutated
            killed = $true
            exitCode = 1
            executedCount = 1
            failedCount = 1
            assertionFailureCount = 1
            selectedTests = @($identity)
            baselineInvocation = $invocation.Identity
            baselineSelectedTests = @($identity)
            baselineTrx = "receipts/$($entry.Name)-baseline.trx"
            log = "receipts/$($entry.Name).log"
            trx = "receipts/$($entry.Name).trx"
        }
    }

    & git -C $repository init --quiet
    & git -C $repository config user.email fixture@sharpproof.test
    & git -C $repository config user.name 'SharpProof Fixture'
    & git -C $repository add -- .
    & git -C $repository commit --quiet -m fixture
    $commit = (& git -C $repository rev-parse HEAD).Trim()
    $evidencePath = Join-Path $evidenceDirectory 'trusted-mutations.json'
    $campaignSentinel = Join-Path $repository 'campaign-launched'

    function New-CompleteEvidence {
        return [pscustomobject][ordered]@{
            schemaVersion = 2
            commit = $commit
            configuration = 'Release'
            selection = 'full'
            catalogCount = $catalog.Count
            mutationCount = $catalog.Count
            killedCount = $catalog.Count
            mutations = @($results | ForEach-Object {
                    $_ | ConvertTo-Json -Depth 5 | ConvertFrom-Json
                })
        }
    }

    function Invoke-ReuseCase {
        param(
            [Parameter(Mandatory = $true)][string]$Name,
            [Parameter(Mandatory = $true)][object]$Evidence,
            [bool]$ExpectSuccess = $false,
            [bool]$Dirty = $false
        )
        $Evidence | ConvertTo-Json -Depth 6 | Set-Content `
            -LiteralPath $evidencePath -Encoding utf8NoBOM
        Remove-Item -LiteralPath $campaignSentinel `
            -Force -ErrorAction SilentlyContinue
        if ($Dirty) {
            Add-Content -LiteralPath (Join-Path $repository 'Project/Source.cs') `
                -Value '// dirty'
        }
        try {
            $caseOutput = & pwsh -NoLogo -NoProfile -File (
                Join-Path $scripts 'Invoke-SharpProofTrustedMutationsParallel.ps1') `
                -Configuration Release `
                -OutputPath 'artifacts/mutation/trusted-mutations.json' `
                -ExpectedCommit $commit `
                -Parallelism 1 2>&1
            $exitCode = $LASTEXITCODE
        }
        finally {
            if ($Dirty) {
                & git -C $repository checkout -- Project/Source.cs
            }
        }
        if ($ExpectSuccess) {
            if ($exitCode -ne 0 -or
                [string]::Join("`n", @($caseOutput)) -notlike `
                    '*Mutation evidence is already complete*') {
                throw "Valid mutation reuse failed for '$Name': $caseOutput"
            }
        }
        elseif ($exitCode -eq 0) {
            throw "Forged mutation reuse was accepted for '$Name'."
        }
        if (Test-Path -LiteralPath $campaignSentinel) {
            throw "Mutation campaign launched while validating '$Name'."
        }
    }

    $empty = New-CompleteEvidence
    $empty.mutations = @()
    Invoke-ReuseCase -Name empty-mutations -Evidence $empty

    $duplicate = New-CompleteEvidence
    $duplicate.mutations[1] = $duplicate.mutations[0]
    Invoke-ReuseCase -Name duplicate-row -Evidence $duplicate

    $missingName = New-CompleteEvidence
    $missingName.mutations[0].PSObject.Properties.Remove('name')
    Invoke-ReuseCase -Name missing-name -Evidence $missingName

    foreach ($missing in @(
            @{ Name = 'missing-log'; Property = 'log'; Delete = $false },
            @{ Name = 'missing-trx'; Property = 'trx'; Delete = $false },
            @{ Name = 'missing-baseline-invocation'; Property = 'baselineInvocation'; Delete = $false },
            @{ Name = 'missing-baseline-ledger'; Property = 'baselineSelectedTests'; Delete = $false },
            @{ Name = 'missing-baseline-trx'; Property = 'baselineTrx'; Delete = $false })) {
        $candidate = New-CompleteEvidence
        $candidate.mutations[0].PSObject.Properties.Remove($missing.Property)
        Invoke-ReuseCase -Name $missing.Name -Evidence $candidate
    }

    $wrongBaselineInvocation = New-CompleteEvidence
    $wrongBaselineInvocation.mutations[0].baselineInvocation = 'wrong-baseline-invocation'
    Invoke-ReuseCase -Name wrong-baseline-invocation `
        -Evidence $wrongBaselineInvocation
    $wrongBaselineLedger = New-CompleteEvidence
    $wrongBaselineLedger.mutations[0].baselineSelectedTests = @(
        'Fixture.Tests.Other|Other')
    Invoke-ReuseCase -Name wrong-baseline-ledger -Evidence $wrongBaselineLedger

    Invoke-ReuseCase `
        -Name dirty-tree `
        -Evidence (New-CompleteEvidence) `
        -Dirty $true
    Invoke-ReuseCase `
        -Name valid-complete `
        -Evidence (New-CompleteEvidence) `
        -ExpectSuccess $true

    Remove-Item -LiteralPath $evidencePath -Force
    $shardRoot = Join-Path $evidenceDirectory (
        "shards/$commit/release-weighted-v3-focused-baseline-1")
    New-Item -ItemType Directory -Path $shardRoot -Force | Out-Null

    $baselineTrx = Join-Path $shardRoot 'baseline.trx'
    Copy-Item -LiteralPath (
        Join-Path $receiptDirectory 'first-mutation-baseline.trx') `
        -Destination $baselineTrx
    $baselineInvocation = Get-SharpProofMutationBaselineInvocation `
        -Project $catalog[0].Project `
        -Filter $catalog[0].Filter `
        -Configuration Release
    [pscustomobject][ordered]@{
        schemaVersion = 2
        commit = $commit
        configuration = 'Release'
        selection = 'full'
        catalogCount = $catalog.Count
        testCount = 1
        tests = @([pscustomobject][ordered]@{
                project = $catalog[0].Project
                filter = $catalog[0].Filter
                configuration = 'Release'
                invocation = $baselineInvocation.Identity
                ledger = @($results[0].baselineSelectedTests)
                trx = 'baseline.trx'
            })
        timing = [ordered]@{
            restoreElapsedMilliseconds = 1
            baselineElapsedMilliseconds = 1
            baselineInvocationCount = 1
        }
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (
        Join-Path $shardRoot 'baseline.json') -Encoding utf8NoBOM

    $cachedRows = @($results | ForEach-Object {
            $_ | ConvertTo-Json -Depth 5 | ConvertFrom-Json
        })
    for ($index = 0; $index -lt $cachedRows.Count; $index++) {
        foreach ($property in @('log', 'trx', 'baselineTrx')) {
            $receipt = Join-Path `
                $evidenceDirectory ([string]$cachedRows[$index].$property)
            $cachedRows[$index].$property = [IO.Path]::GetRelativePath(
                $shardRoot,
                $receipt).Replace('\', '/')
        }
        $cachedRows[$index] | Add-Member `
            -NotePropertyName catalogOrdinal `
            -NotePropertyValue $index
    }
    $shardPath = Join-Path $shardRoot 'shard-01.json'
    [pscustomobject][ordered]@{
        schemaVersion = 2
        commit = $commit
        configuration = 'Release'
        selection = 'selected'
        catalogCount = $catalog.Count
        mutationCount = $cachedRows.Count
        killedCount = $cachedRows.Count
        mutations = $cachedRows
    } | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath $shardPath -Encoding utf8NoBOM

    Remove-Item -LiteralPath $campaignSentinel `
        -Force -ErrorAction SilentlyContinue
    $caseOutput = & pwsh -NoLogo -NoProfile -File (
        Join-Path $scripts 'Invoke-SharpProofTrustedMutationsParallel.ps1') `
        -Configuration Release `
        -OutputPath 'artifacts/mutation/trusted-mutations.json' `
        -ExpectedCommit $commit `
        -Parallelism 1 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -or
        -not (Test-Path -LiteralPath $evidencePath) -or
        (Test-Path -LiteralPath $campaignSentinel)) {
        throw "Valid cached mutation receipts were not reused: $caseOutput"
    }
    Remove-Item -LiteralPath $evidencePath -Force

    $syntheticRows = @($results | ForEach-Object {
            $_ | ConvertTo-Json -Depth 5 | ConvertFrom-Json
        })
    for ($index = 0; $index -lt $syntheticRows.Count; $index++) {
        $syntheticRows[$index].name = "synthetic-mutation-$index"
        $syntheticRows[$index].file = "Synthetic/Source-$index.cs"
        $syntheticRows[$index].project = "Synthetic.Test/Project-$index.csproj"
        $syntheticRows[$index].test = "FullyQualifiedName~SyntheticTest$index"
        $syntheticRows[$index].original = "synthetic-before-$index"
        $syntheticRows[$index].mutated = "synthetic-after-$index"
        $syntheticRows[$index] | Add-Member `
            -NotePropertyName catalogOrdinal `
            -NotePropertyValue $index
    }
    [pscustomobject][ordered]@{
        schemaVersion = 2
        commit = $commit
        configuration = 'Release'
        selection = 'selected'
        catalogCount = $catalog.Count
        mutationCount = $syntheticRows.Count
        killedCount = $syntheticRows.Count
        mutations = $syntheticRows
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (
        $shardPath) -Encoding utf8NoBOM

    Remove-Item -LiteralPath $campaignSentinel `
        -Force -ErrorAction SilentlyContinue
    $caseOutput = & pwsh -NoLogo -NoProfile -File (
        Join-Path $scripts 'Invoke-SharpProofTrustedMutationsParallel.ps1') `
        -Configuration Release `
        -OutputPath 'artifacts/mutation/trusted-mutations.json' `
        -ExpectedCommit $commit `
        -Parallelism 1 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0) {
        throw 'Synthetic cached mutation shard rows were accepted as full evidence.'
    }
    if (Test-Path -LiteralPath $evidencePath) {
        throw 'Rejected cached mutation shards published full evidence.'
    }
    if (-not (Test-Path -LiteralPath $campaignSentinel)) {
        throw 'Rejected cached mutation shards were not scheduled to rerun.'
    }
}

$zeroInfrastructure = 'error="0" timeout="0" aborted="0" inconclusive="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" passedButRunAborted="0"'

try {
    $passing = New-TestParts -Outcome Passed -Message ''
    $passingPath = New-TrxFixture -Name passing -Parts $passing
    $baseline = Read-SharpProofMutationTestEvidence `
        -TrxPath $passingPath `
        -EvidenceName baseline `
        -Mode Baseline `
        -ProcessExitCode 0 `
        -ExpectedMethodName ExpectedTest
    if ($baseline.executedCount -ne 1 -or $baseline.failedCount -ne 0) {
        throw 'Passing baseline evidence was not projected correctly.'
    }

    $mutationArguments = @{
        Mode = 'Mutation'
        ProcessExitCode = 1
        ExpectedMethodName = 'ExpectedTest'
        ExpectedLedger = $baseline.testLedger
    }

    $batchFirst = New-TestParts `
        -Outcome Passed `
        -Message '' `
        -Method FirstExpected `
        -TestId test-batch-1 `
        -ExecutionId execution-batch-1
    $batchSecond = New-TestParts `
        -Outcome Passed `
        -Message '' `
        -Method 'SecondExpected(CaseOne)' `
        -TestId test-batch-2 `
        -ExecutionId execution-batch-2
    $batchPath = New-TrxFixture `
        -Name passing-batch `
        -Parts @($batchFirst, $batchSecond)
    $batch = Read-SharpProofMutationTestEvidence `
        -TrxPath $batchPath `
        -EvidenceName passing-batch `
        -Mode Baseline `
        -ProcessExitCode 0 `
        -ExpectedMethodName @('FirstExpected', 'SecondExpected')
    if ($batch.executedCount -ne 2 -or
        @($batch.testLedgers['FirstExpected']).Count -ne 1 -or
        @($batch.testLedgers['SecondExpected']).Count -ne 1) {
        throw 'Batched baseline evidence was not partitioned by method.'
    }

    $bomPath = Join-Path $fixtureRoot 'passing-bom.trx'
    [IO.File]::WriteAllText(
        $bomPath,
        [IO.File]::ReadAllText($passingPath),
        [Text.UTF8Encoding]::new($true))
    $bomBaseline = Read-SharpProofMutationTestEvidence `
        -TrxPath $bomPath `
        -EvidenceName passing-bom `
        -Mode Baseline `
        -ProcessExitCode 0 `
        -ExpectedMethodName ExpectedTest
    if ($bomBaseline.executedCount -ne 1) {
        throw 'UTF-8 BOM evidence was not projected correctly.'
    }

    $parameterized = New-TestParts `
        -Outcome Passed `
        -Message '' `
        -Method 'ExpectedTest(CaseOne)'
    $parameterizedPath = New-TrxFixture -Name parameterized -Parts $parameterized
    $parameterizedBaseline = Read-SharpProofMutationTestEvidence `
        -TrxPath $parameterizedPath `
        -EvidenceName parameterized `
        -Mode Baseline `
        -ProcessExitCode 0 `
        -ExpectedMethodName ExpectedTest
    if ($parameterizedBaseline.executedCount -ne 1) {
        throw 'Parameterized method evidence was not projected correctly.'
    }

    $caseAssertionMessage =
        "Assert.That(actual, Is.EqualTo(expected))`n Expected: 1`n But was: 2"
    $caseBaselineParts = New-TestParts `
        -Outcome Passed `
        -Message '' `
        -Method ExpectedTest `
        -DisplayName 'ExpectedTest(Case("A"))'
    $caseBaselinePath = New-TrxFixture `
        -Name case-ledger-baseline `
        -Parts $caseBaselineParts
    $caseBaseline = Read-SharpProofMutationTestEvidence `
        -TrxPath $caseBaselinePath `
        -EvidenceName case-ledger-baseline `
        -Mode Baseline `
        -ProcessExitCode 0 `
        -ExpectedMethodName ExpectedTest

    $exactCaseParts = New-TestParts `
        -Outcome Failed `
        -Message $caseAssertionMessage `
        -Method ExpectedTest `
        -DisplayName 'ExpectedTest(Case("A"))'
    $exactCasePath = New-TrxFixture `
        -Name exact-case-ledger `
        -Parts $exactCaseParts `
        -Failed 1
    $exactCase = Read-SharpProofMutationTestEvidence `
        -TrxPath $exactCasePath `
        -EvidenceName exact-case-ledger `
        -Mode Mutation `
        -ProcessExitCode 1 `
        -ExpectedMethodName ExpectedTest `
        -ExpectedLedger $caseBaseline.testLedger
    if ($exactCase.assertionFailureCount -ne 1) {
        throw 'Exact-case ledger identity did not pass unchanged.'
    }

    $caseOnlyDrifts = @(
        [pscustomobject]@{
            Name = 'parameter-case-ledger'
            Because = 'case-only parameter identity drift'
            ExpectedMethod = 'ExpectedTest'
            Parts = New-TestParts `
                -Outcome Failed `
                -Message $caseAssertionMessage `
                -Method ExpectedTest `
                -DisplayName 'ExpectedTest(Case("a"))'
        },
        [pscustomobject]@{
            Name = 'display-case-ledger'
            Because = 'case-only display identity drift'
            ExpectedMethod = 'ExpectedTest'
            Parts = New-TestParts `
                -Outcome Failed `
                -Message $caseAssertionMessage `
                -Method ExpectedTest `
                -DisplayName 'expectedTest(Case("A"))'
        },
        [pscustomobject]@{
            Name = 'class-case-ledger'
            Because = 'case-only class identity drift'
            ExpectedMethod = 'ExpectedTest'
            Parts = New-TestParts `
                -Outcome Failed `
                -Message $caseAssertionMessage `
                -Method ExpectedTest `
                -DisplayName 'ExpectedTest(Case("A"))' `
                -Class 'SharpProof.Test.evidenceTests'
        },
        [pscustomobject]@{
            Name = 'method-case-ledger'
            Because = 'case-only method identity drift'
            ExpectedMethod = 'expectedTest'
            Parts = New-TestParts `
                -Outcome Failed `
                -Message $caseAssertionMessage `
                -Method expectedTest `
                -DisplayName 'ExpectedTest(Case("A"))'
        })
    foreach ($drift in $caseOnlyDrifts) {
        $driftPath = New-TrxFixture `
            -Name $drift.Name `
            -Parts $drift.Parts `
            -Failed 1
        Assert-Throws `
            -Because $drift.Because `
            -ExpectedMessage 'test ledger changed' `
            -Action {
            Read-SharpProofMutationTestEvidence `
                -TrxPath $driftPath `
                -EvidenceName $drift.Name `
                -Mode Mutation `
                -ProcessExitCode 1 `
                -ExpectedMethodName $drift.ExpectedMethod `
                -ExpectedLedger $caseBaseline.testLedger
        }
    }

    $upperParameter = New-TestParts `
        -Outcome Passed `
        -Message '' `
        -Method ExpectedTest `
        -DisplayName 'ExpectedTest(Case("A"))' `
        -TestId test-case-row-1 `
        -ExecutionId execution-case-row-1
    $lowerParameter = New-TestParts `
        -Outcome Passed `
        -Message '' `
        -Method ExpectedTest `
        -DisplayName 'ExpectedTest(Case("a"))' `
        -TestId test-case-row-2 `
        -ExecutionId execution-case-row-2
    $caseRowsPath = New-TrxFixture `
        -Name case-distinct-rows `
        -Parts @($upperParameter, $lowerParameter)
    $caseRows = Read-SharpProofMutationTestEvidence `
        -TrxPath $caseRowsPath `
        -EvidenceName case-distinct-rows `
        -Mode Baseline `
        -ProcessExitCode 0 `
        -ExpectedMethodName ExpectedTest
    if ($caseRows.testLedger.Count -ne 2 -or
        -not [StringComparer]::Ordinal.Equals(
            $caseRows.testLedger[0], $upperParameter.Identity) -or
        -not [StringComparer]::Ordinal.Equals(
            $caseRows.testLedger[1], $lowerParameter.Identity)) {
        throw 'Case-distinct parameter rows were collapsed or misordered.'
    }

    $upperMethod = New-TestParts `
        -Outcome Passed `
        -Message '' `
        -Method ExpectedTest `
        -TestId test-case-method-1 `
        -ExecutionId execution-case-method-1
    $lowerMethod = New-TestParts `
        -Outcome Passed `
        -Message '' `
        -Method expectedTest `
        -TestId test-case-method-2 `
        -ExecutionId execution-case-method-2
    $caseMethodsPath = New-TrxFixture `
        -Name case-distinct-methods `
        -Parts @($upperMethod, $lowerMethod)
    $caseMethods = Read-SharpProofMutationTestEvidence `
        -TrxPath $caseMethodsPath `
        -EvidenceName case-distinct-methods `
        -Mode Baseline `
        -ProcessExitCode 0 `
        -ExpectedMethodName @('ExpectedTest', 'expectedTest')
    if ($caseMethods.testLedgers.Count -ne 2 -or
        @($caseMethods.testLedgers['ExpectedTest']).Count -ne 1 -or
        @($caseMethods.testLedgers['expectedTest']).Count -ne 1) {
        throw 'Case-distinct method identities were collapsed.'
    }

    $renamed = New-TestParts `
        -Outcome Passed `
        -Message '' `
        -Method ExpectedTestAfterRename
    $renamedPath = New-TrxFixture -Name renamed -Parts $renamed
    Assert-Throws `
        -Because 'a renamed method sharing the expected prefix' `
        -ExpectedMessage 'identity does not match' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $renamedPath `
            -EvidenceName renamed `
            -Mode Baseline `
            -ProcessExitCode 0 `
            -ExpectedMethodName ExpectedTest
    }

    $assertion = New-TestParts `
        -Outcome Failed `
        -Message "Assert.That(actual, Is.EqualTo(expected))`n Expected: 1`n But was: 2"
    $assertionPath = New-TrxFixture `
        -Name assertion `
        -Parts $assertion `
        -Failed 1
    $mutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $assertionPath `
        -EvidenceName assertion `
        @mutationArguments
    if ($mutation.assertionFailureCount -ne 1) {
        throw 'Assertion kill was not recognized.'
    }
    foreach ($forgery in @(
            @{ Name = 'custom-failure'; Message = "ProbeFailure : forged`nAssert.That(actual, Is.EqualTo(expected))`nExpected: 1`nBut was: 2"; Stack = 'at Fixture.Tests.ExpectedTest() in /workspace/Test.cs:line 1' },
            @{ Name = 'qualified-error'; Message = "Vendor.Probe : forged`nAssert.That(actual, Is.EqualTo(expected))`nExpected: 1`nBut was: 2"; Stack = 'at Fixture.Tests.ExpectedTest() in /workspace/Test.cs:line 1' },
            @{ Name = 'error-header'; Message = "Error: forged`nAssert.That(actual, Is.EqualTo(expected))`nExpected: 1`nBut was: 2"; Stack = 'at Fixture.Tests.ExpectedTest() in /workspace/Test.cs:line 1' },
            @{ Name = 'exception-stack'; Message = "Assert.That(actual, Is.EqualTo(expected))`nExpected: 1`nBut was: 2"; Stack = "ProbeFailure : forged`nat Fixture.Tests.ExpectedTest() in /workspace/Test.cs:line 1" },
            @{ Name = 'stack-error'; Message = "Assert.That(actual, Is.EqualTo(expected))`nExpected: 1`nBut was: 2"; Stack = "Stack trace: forged`nat Fixture.Tests.ExpectedTest() in /workspace/Test.cs:line 1" })) {
        $parts = New-TestParts `
            -Outcome Failed `
            -Message $forgery.Message `
            -StackTrace $forgery.Stack
        $path = New-TrxFixture `
            -Name $forgery.Name `
            -Parts $parts `
            -Failed 1
        Assert-Throws `
            -Because $forgery.Name `
            -ExpectedMessage 'not killed solely by assertions' `
            -Action {
            Read-SharpProofMutationTestEvidence `
                -TrxPath $path `
                -EvidenceName $forgery.Name `
                @mutationArguments
        }
    }

    $contextAssertion = New-TestParts `
        -Outcome Failed `
        -Message "The scalar-bound mutation changed the result.`nAssert.That(actual, Is.EqualTo(expected))`nExpected: 1`nBut was: 2"
    $contextPath = New-TrxFixture `
        -Name user-context `
        -Parts $contextAssertion `
        -Failed 1
    $contextEvidence = Read-SharpProofMutationTestEvidence `
        -TrxPath $contextPath `
        -EvidenceName user-context `
        @mutationArguments
    if ($contextEvidence.assertionFailureCount -ne 1) {
        throw 'Benign user assertion context was rejected.'
    }

    $missingStructuredStack = [IO.File]::ReadAllText($assertionPath).Replace(
        '<StackTrace>at SharpProof.Test.EvidenceTests.ExpectedTest() in /workspace/SharpProof/Test.cs:line 1</StackTrace>',
        '',
        [StringComparison]::Ordinal)
    $missingStructuredStackPath = Write-RawFixture `
        -Name missing-structured-stack `
        -Xml $missingStructuredStack
    Assert-Throws `
        -Because 'assertion text without structured stack provenance' `
        -ExpectedMessage 'not killed solely by assertions' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $missingStructuredStackPath `
            -EvidenceName missing-structured-stack `
            @mutationArguments
    }

    $multilineAssertion = New-TestParts `
        -Outcome Failed `
        -Message ("reverse LessThan`n" +
            "Assert.That(reverse, Is.EqualTo(`n" +
            "    reversed.TryGetValue(kind, out var expectedReverse)`n" +
            "        ? expectedReverse`n" +
            "        : kind)`n" +
            " Expected: GreaterThan`n" +
            " But was: GreaterThanOrEqual")
    $multilinePath = New-TrxFixture `
        -Name multiline-assertion `
        -Parts $multilineAssertion `
        -Failed 1
    $multilineMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $multilinePath `
        -EvidenceName multiline-assertion `
        @mutationArguments
    if ($multilineMutation.assertionFailureCount -ne 1) {
        throw 'Multiline assertion kill was not recognized.'
    }

    $identifierContinuation = New-TestParts `
        -Outcome Failed `
        -Message ("Assert.That(WorkerProtocolJson.Validate(response).Errors`n" +
            "                .Select(static error => error.Code), Does.Contain(""response.claim_set""))`n" +
            " Expected: some item equal to ""response.claim_set""`n" +
            " But was:  < ""summary.totals"" >")
    $identifierContinuationPath = New-TrxFixture `
        -Name identifier-continuation `
        -Parts $identifierContinuation `
        -Failed 1
    $identifierContinuationMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $identifierContinuationPath `
        -EvidenceName identifier-continuation `
        @mutationArguments
    if ($identifierContinuationMutation.assertionFailureCount -ne 1) {
        throw 'Assertion code identifier continuation was not recognized.'
    }

    $prefixedAssertion = New-TestParts `
        -Outcome Failed `
        -Message "Incomplete-reason flags 4 changed projection precedence.`n Assert.That(actual, Is.EqualTo(expected))`n Expected: 1`n But was: 2"
    $prefixedPath = New-TrxFixture `
        -Name prefixed-assertion `
        -Parts $prefixedAssertion `
        -Failed 1
    $prefixedMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $prefixedPath `
        -EvidenceName prefixed-assertion `
        @mutationArguments
    if ($prefixedMutation.assertionFailureCount -ne 1) {
        throw 'Prefixed assertion kill was not recognized.'
    }

    $collectionAssertion = New-TestParts `
        -Outcome Failed `
        -Message "Assert.That(actual, Is.EqualTo(expected))`n Expected is <System.Int32[1]>, actual is <System.Int32[0]>`n Values differ at index [0]"
    $collectionPath = New-TrxFixture `
        -Name collection-assertion `
        -Parts $collectionAssertion `
        -Failed 1
    $collectionMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $collectionPath `
        -EvidenceName collection `
        @mutationArguments
    if ($collectionMutation.assertionFailureCount -ne 1) {
        throw 'Collection assertion kill was not recognized.'
    }

    $nunitCollectionAssertion = New-TestParts `
        -Outcome Failed `
        -Message ("Assert.That(actual, Is.EqualTo(expected))`n" +
            " Expected and actual are both <System.Linq.Enumerable+SelectArrayIterator>`n" +
            " Values differ at index [0]`n" +
            " Expected string length 24 but was 20. Strings differ at index 0.`n" +
             " Expected: ConsoleApplication`n" +
             " But was: WindowsApplication`n" +
             " First non-matching item at index [0]: item`n" +
             " Missing (1): expected-item`n" +
             " Extra (2): actual-item`n" +
             " -----------^")
    $nunitCollectionPath = New-TrxFixture `
        -Name nunit-collection-assertion `
        -Parts $nunitCollectionAssertion `
        -Failed 1
    $nunitCollectionMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $nunitCollectionPath `
        -EvidenceName nunit-collection `
        @mutationArguments
    if ($nunitCollectionMutation.assertionFailureCount -ne 1) {
        throw 'NUnit collection assertion kill was not recognized.'
    }

    $multipleAssertion = New-TestParts `
        -Outcome Failed `
        -Message ("Multiple failures or warnings in test:`n" +
            " 1) Assert.That(first, Is.EqualTo(expected))`n" +
            " Expected: 1`n" +
            " But was: 2`n" +
            " at test.cs:10`n" +
            " 2) Assert.That(second, Is.True)`n" +
            " Expected: True`n" +
            " But was: False`n" +
            " at test.cs:11")
    $multiplePath = New-TrxFixture `
        -Name multiple-assertion `
        -Parts $multipleAssertion `
        -Failed 1
    $multipleMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $multiplePath `
        -EvidenceName multiple-assertion `
        @mutationArguments
    if ($multipleMutation.assertionFailureCount -ne 1) {
        throw 'Multiple assertion kill was not recognized.'
    }

    $multipleCollectionAssertion = New-TestParts `
        -Outcome Failed `
        -Message ("Multiple failures or warnings in test:`n" +
            " 1) Assert.That(first, Is.EqualTo(expected))`n" +
            " Expected is <System.String[1]>, actual is <System.Linq.EmptyPartition`1[System.String]>`n" +
            " Values differ at index [0]`n" +
            " 2) Assert.That(second, Is.EqualTo(expected))`n" +
            " Expected: 1`n" +
            " But was: 2")
    $multipleCollectionPath = New-TrxFixture `
        -Name multiple-collection-assertion `
        -Parts $multipleCollectionAssertion `
        -Failed 1
    $multipleCollectionMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $multipleCollectionPath `
        -EvidenceName multiple-collection-assertion `
        @mutationArguments
    if ($multipleCollectionMutation.assertionFailureCount -ne 1) {
        throw 'Multiple collection assertion kill was not recognized.'
    }

    $multipleMixed = New-TestParts `
        -Outcome Failed `
        -Message ("Multiple failures or warnings in test:`n" +
            " 1) Assert.That(first, Is.EqualTo(expected))`n" +
            " Expected: 1`n" +
            " But was: 2`n" +
            " 2) System.InvalidOperationException : crash")
    $multipleMixedPath = New-TrxFixture `
        -Name multiple-mixed `
        -Parts $multipleMixed `
        -Failed 1
    Assert-Throws `
        -Because 'a mixed Assert.Multiple failure and exception' `
        -ExpectedMessage 'not killed solely by assertions' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $multipleMixedPath `
            -EvidenceName multiple-mixed `
            @mutationArguments
    }

    $crash = New-TestParts `
        -Outcome Failed `
        -Message 'System.NullReferenceException: crash'
    $crashPath = New-TrxFixture `
        -Name crash `
        -Parts $crash `
        -Failed 1
    Assert-Throws `
        -Because 'a crash stack mentions Assert.That' `
        -ExpectedMessage 'not killed solely by assertions' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $crashPath `
            -EvidenceName crash `
            @mutationArguments
    }

    $other = New-TestParts -Outcome Passed -Message '' -Method OtherTest
    $wrongIdentityPath = New-TrxFixture -Name wrong-identity -Parts $other
    Assert-Throws `
        -Because 'an unexpected selected test' `
        -ExpectedMessage 'identity does not match' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $wrongIdentityPath `
            -EvidenceName wrong `
            -Mode Baseline `
            -ProcessExitCode 0 `
            -ExpectedMethodName ExpectedTest
    }

    $partialPath = Write-Fixture `
        -Name partial `
        -Summary Completed `
        -Counters ('total="2" executed="1" passed="1" failed="0" ' +
            $zeroInfrastructure) `
        -Definitions $passing.Definition `
        -Entries $passing.Entry `
        -Results $passing.Result
    Assert-Throws `
        -Because 'partial execution counters' `
        -ExpectedMessage 'counters disagree' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $partialPath `
            -EvidenceName partial `
            -Mode Baseline `
            -ProcessExitCode 0 `
            -ExpectedMethodName ExpectedTest
    }

    $timeoutPath = Write-Fixture `
        -Name timeout `
        -Summary Failed `
        -Counters 'total="1" executed="1" passed="0" failed="0" error="0" timeout="1" aborted="0" inconclusive="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" passedButRunAborted="0"' `
        -Definitions $passing.Definition `
        -Entries $passing.Entry `
        -Results $passing.Result
    Assert-Throws `
        -Because 'timeout infrastructure evidence' `
        -ExpectedMessage "non-test outcome 'timeout'" `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $timeoutPath `
            -EvidenceName timeout `
            @mutationArguments
    }

    Assert-Throws `
        -Because 'an abnormal mutation process exit' `
        -ExpectedMessage 'process exit code' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $assertionPath `
            -EvidenceName abnormal-exit `
            -Mode Mutation `
            -ProcessExitCode 2 `
            -ExpectedMethodName ExpectedTest `
            -ExpectedLedger $baseline.testLedger
    }

    Assert-Throws `
        -Because 'mutation evidence without a baseline ledger' `
        -ExpectedMessage 'requires a nonempty baseline ledger' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $assertionPath `
            -EvidenceName missing-ledger `
            -Mode Mutation `
            -ProcessExitCode 1 `
            -ExpectedMethodName ExpectedTest
    }

    $mixed = New-TestParts `
        -Outcome Failed `
        -Message "Assert.That(actual, Is.EqualTo(expected))`n Expected: 1`n But was: 2`n System.NullReferenceException: teardown crash"
    $mixedPath = New-TrxFixture `
        -Name mixed `
        -Parts $mixed `
        -Failed 1
    Assert-Throws `
        -Because 'assertion evidence with a trailing crash' `
        -ExpectedMessage 'not killed solely by assertions' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $mixedPath `
            -EvidenceName mixed `
            @mutationArguments
    }

    $foreignXml = [IO.File]::ReadAllText($passingPath).Replace(
        '<Results>',
        '<Results xmlns="urn:foreign">',
        [StringComparison]::Ordinal)
    $foreignPath = Write-RawFixture -Name foreign-results -Xml $foreignXml
    Assert-Throws `
        -Because 'a foreign-namespace result container' `
        -ExpectedMessage "misplaced or duplicate 'Results'" `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $foreignPath `
            -EvidenceName foreign `
            -Mode Baseline `
            -ProcessExitCode 0 `
            -ExpectedMethodName ExpectedTest
    }

    $duplicateExecutionXml = [IO.File]::ReadAllText($passingPath).Replace(
        '<Execution id=''execution-1''/>',
        '<Execution id=''execution-1''/><Execution id=''execution-2''/>',
        [StringComparison]::Ordinal)
    $duplicateExecutionPath = Write-RawFixture `
        -Name duplicate-execution `
        -Xml $duplicateExecutionXml
    Assert-Throws `
        -Because 'duplicate execution identities' `
        -ExpectedMessage 'malformed test definitions' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $duplicateExecutionPath `
            -EvidenceName duplicate-execution `
            -Mode Baseline `
            -ProcessExitCode 0 `
            -ExpectedMethodName ExpectedTest
    }

    $nestedSummaryXml = [IO.File]::ReadAllText($passingPath).Replace(
        '<ResultSummary outcome="Completed">',
        '<Wrapper><ResultSummary outcome="Completed">',
        [StringComparison]::Ordinal).Replace(
        '</ResultSummary>',
        '</ResultSummary></Wrapper>',
        [StringComparison]::Ordinal)
    $nestedSummaryPath = Write-RawFixture `
        -Name nested-summary `
        -Xml $nestedSummaryXml
    Assert-Throws `
        -Because 'a nested result summary' `
        -ExpectedMessage 'exactly one result summary' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $nestedSummaryPath `
            -EvidenceName nested-summary `
            -Mode Baseline `
            -ProcessExitCode 0 `
            -ExpectedMethodName ExpectedTest
    }

    Test-MutationReuseValidation
    Write-Host 'Mutation evidence behavioral fixtures passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
