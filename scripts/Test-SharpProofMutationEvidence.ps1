[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationEvidence.psm1') -Force

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

function New-TestParts {
    param(
        [string]$Outcome,
        [string]$Message,
        [string]$Method = 'ExpectedTest',
        [string]$Class = 'SharpProof.Test.EvidenceTests',
        [string]$TestId = 'test-1',
        [string]$ExecutionId = 'execution-1'
    )

    $escaped = [Security.SecurityElement]::Escape($Message)
    $output = if ($Outcome -eq 'Failed') {
        "<Output><ErrorInfo><Message>$escaped</Message><StackTrace>irrelevant Assert.That(text)</StackTrace></ErrorInfo></Output>"
    }
    else { '' }
    return [pscustomobject]@{
        Definition = "<UnitTest id='$TestId' name='$Method'><Execution id='$ExecutionId'/><TestMethod className='$Class' name='$Method'/></UnitTest>"
        Entry = "<TestEntry testId='$TestId' executionId='$ExecutionId'/>"
        Result = "<UnitTestResult testId='$TestId' executionId='$ExecutionId' testName='$Method' outcome='$Outcome'>$output</UnitTestResult>"
        Identity = "$Class.$Method|$Method"
    }
}

function Assert-Throws {
    param(
        [scriptblock]$Action,
        [string]$Because
    )

    try {
        & $Action
    }
    catch {
        return
    }
    throw "Expected rejection: $Because"
}

$zeroInfrastructure = 'error="0" timeout="0" aborted="0" inconclusive="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" passedButRunAborted="0"'

try {
    $passing = New-TestParts -Outcome Passed -Message ''
    $passingPath = Write-Fixture `
        -Name passing `
        -Summary Completed `
        -Counters ('total="1" executed="1" passed="1" failed="0" ' +
            $zeroInfrastructure) `
        -Definitions $passing.Definition `
        -Entries $passing.Entry `
        -Results $passing.Result
    $baseline = Read-SharpProofMutationTestEvidence `
        -TrxPath $passingPath `
        -EvidenceName baseline `
        -Mode Baseline `
        -ExpectedMethodName ExpectedTest
    if ($baseline.executedCount -ne 1 -or $baseline.failedCount -ne 0) {
        throw 'Passing baseline evidence was not projected correctly.'
    }

    $assertion = New-TestParts `
        -Outcome Failed `
        -Message "Assert.That(actual, Is.EqualTo(expected))`n Expected: 1`n But was: 2"
    $assertionPath = Write-Fixture `
        -Name assertion `
        -Summary Failed `
        -Counters ('total="1" executed="1" passed="0" failed="1" ' +
            $zeroInfrastructure) `
        -Definitions $assertion.Definition `
        -Entries $assertion.Entry `
        -Results $assertion.Result
    $mutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $assertionPath `
        -EvidenceName assertion `
        -Mode Mutation `
        -ExpectedMethodName ExpectedTest `
        -ExpectedLedger $baseline.testLedger
    if ($mutation.assertionFailureCount -ne 1) {
        throw 'Assertion kill was not recognized.'
    }

    $crash = New-TestParts `
        -Outcome Failed `
        -Message 'System.NullReferenceException: crash'
    $crashPath = Write-Fixture `
        -Name crash `
        -Summary Failed `
        -Counters ('total="1" executed="1" passed="0" failed="1" ' +
            $zeroInfrastructure) `
        -Definitions $crash.Definition `
        -Entries $crash.Entry `
        -Results $crash.Result
    Assert-Throws -Because 'a crash stack mentions Assert.That' -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $crashPath `
            -EvidenceName crash `
            -Mode Mutation `
            -ExpectedMethodName ExpectedTest `
            -ExpectedLedger $baseline.testLedger
    }

    $other = New-TestParts -Outcome Passed -Message '' -Method OtherTest
    $wrongIdentityPath = Write-Fixture `
        -Name wrong-identity `
        -Summary Completed `
        -Counters ('total="1" executed="1" passed="1" failed="0" ' +
            $zeroInfrastructure) `
        -Definitions $other.Definition `
        -Entries $other.Entry `
        -Results $other.Result
    Assert-Throws -Because 'an unexpected selected test' -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $wrongIdentityPath `
            -EvidenceName wrong `
            -Mode Baseline `
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
    Assert-Throws -Because 'partial execution counters' -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $partialPath `
            -EvidenceName partial `
            -Mode Baseline `
            -ExpectedMethodName ExpectedTest
    }

    $timeoutPath = Write-Fixture `
        -Name timeout `
        -Summary Failed `
        -Counters 'total="1" executed="1" passed="0" failed="0" error="0" timeout="1" aborted="0" inconclusive="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" passedButRunAborted="0"' `
        -Definitions $passing.Definition `
        -Entries $passing.Entry `
        -Results $passing.Result
    Assert-Throws -Because 'timeout infrastructure evidence' -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $timeoutPath `
            -EvidenceName timeout `
            -Mode Mutation `
            -ExpectedMethodName ExpectedTest
    }

    Write-Host 'Mutation evidence behavioral fixtures passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
