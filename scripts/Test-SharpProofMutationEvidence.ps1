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
        -ProcessExitCode 0 `
        -ExpectedMethodName ExpectedTest
    if ($baseline.executedCount -ne 1 -or $baseline.failedCount -ne 0) {
        throw 'Passing baseline evidence was not projected correctly.'
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
    $parameterizedPath = Write-Fixture `
        -Name parameterized `
        -Summary Completed `
        -Counters ('total="1" executed="1" passed="1" failed="0" ' +
            $zeroInfrastructure) `
        -Definitions $parameterized.Definition `
        -Entries $parameterized.Entry `
        -Results $parameterized.Result
    $parameterizedBaseline = Read-SharpProofMutationTestEvidence `
        -TrxPath $parameterizedPath `
        -EvidenceName parameterized `
        -Mode Baseline `
        -ProcessExitCode 0 `
        -ExpectedMethodName ExpectedTest
    if ($parameterizedBaseline.executedCount -ne 1) {
        throw 'Parameterized method evidence was not projected correctly.'
    }

    $renamed = New-TestParts `
        -Outcome Passed `
        -Message '' `
        -Method ExpectedTestAfterRename
    $renamedPath = Write-Fixture `
        -Name renamed `
        -Summary Completed `
        -Counters ('total="1" executed="1" passed="1" failed="0" ' +
            $zeroInfrastructure) `
        -Definitions $renamed.Definition `
        -Entries $renamed.Entry `
        -Results $renamed.Result
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
        -ProcessExitCode 1 `
        -ExpectedMethodName ExpectedTest `
        -ExpectedLedger $baseline.testLedger
    if ($mutation.assertionFailureCount -ne 1) {
        throw 'Assertion kill was not recognized.'
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
    $multilinePath = Write-Fixture `
        -Name multiline-assertion `
        -Summary Failed `
        -Counters ('total="1" executed="1" passed="0" failed="1" ' +
            $zeroInfrastructure) `
        -Definitions $multilineAssertion.Definition `
        -Entries $multilineAssertion.Entry `
        -Results $multilineAssertion.Result
    $multilineMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $multilinePath `
        -EvidenceName multiline-assertion `
        -Mode Mutation `
        -ProcessExitCode 1 `
        -ExpectedMethodName ExpectedTest `
        -ExpectedLedger $baseline.testLedger
    if ($multilineMutation.assertionFailureCount -ne 1) {
        throw 'Multiline assertion kill was not recognized.'
    }

    $prefixedAssertion = New-TestParts `
        -Outcome Failed `
        -Message "Incomplete-reason flags 4 changed projection precedence.`n Assert.That(actual, Is.EqualTo(expected))`n Expected: 1`n But was: 2"
    $prefixedPath = Write-Fixture `
        -Name prefixed-assertion `
        -Summary Failed `
        -Counters ('total="1" executed="1" passed="0" failed="1" ' +
            $zeroInfrastructure) `
        -Definitions $prefixedAssertion.Definition `
        -Entries $prefixedAssertion.Entry `
        -Results $prefixedAssertion.Result
    $prefixedMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $prefixedPath `
        -EvidenceName prefixed-assertion `
        -Mode Mutation `
        -ProcessExitCode 1 `
        -ExpectedMethodName ExpectedTest `
        -ExpectedLedger $baseline.testLedger
    if ($prefixedMutation.assertionFailureCount -ne 1) {
        throw 'Prefixed assertion kill was not recognized.'
    }

    $collectionAssertion = New-TestParts `
        -Outcome Failed `
        -Message "Assert.That(actual, Is.EqualTo(expected))`n Expected is <System.Int32[1]>, actual is <System.Int32[0]>`n Values differ at index [0]"
    $collectionPath = Write-Fixture `
        -Name collection-assertion `
        -Summary Failed `
        -Counters ('total="1" executed="1" passed="0" failed="1" ' +
            $zeroInfrastructure) `
        -Definitions $collectionAssertion.Definition `
        -Entries $collectionAssertion.Entry `
        -Results $collectionAssertion.Result
    $collectionMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $collectionPath `
        -EvidenceName collection `
        -Mode Mutation `
        -ProcessExitCode 1 `
        -ExpectedMethodName ExpectedTest `
        -ExpectedLedger $baseline.testLedger
    if ($collectionMutation.assertionFailureCount -ne 1) {
        throw 'Collection assertion kill was not recognized.'
    }

    $nunitCollectionAssertion = New-TestParts `
        -Outcome Failed `
        -Message ("Assert.That(actual, Is.EqualTo(expected))`n" +
            " Expected and actual are both <System.Linq.Enumerable+SelectArrayIterator>`n" +
            " Values differ at index [0]`n" +
            " String lengths are both 18. Strings differ at index 0.`n" +
            " Expected: ConsoleApplication`n" +
            " But was: WindowsApplication`n" +
            " -----------^")
    $nunitCollectionPath = Write-Fixture `
        -Name nunit-collection-assertion `
        -Summary Failed `
        -Counters ('total="1" executed="1" passed="0" failed="1" ' +
            $zeroInfrastructure) `
        -Definitions $nunitCollectionAssertion.Definition `
        -Entries $nunitCollectionAssertion.Entry `
        -Results $nunitCollectionAssertion.Result
    $nunitCollectionMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $nunitCollectionPath `
        -EvidenceName nunit-collection `
        -Mode Mutation `
        -ProcessExitCode 1 `
        -ExpectedMethodName ExpectedTest `
        -ExpectedLedger $baseline.testLedger
    if ($nunitCollectionMutation.assertionFailureCount -ne 1) {
        throw 'NUnit collection assertion kill was not recognized.'
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
    Assert-Throws `
        -Because 'a crash stack mentions Assert.That' `
        -ExpectedMessage 'not killed solely by assertions' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $crashPath `
            -EvidenceName crash `
            -Mode Mutation `
            -ProcessExitCode 1 `
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
            -Mode Mutation `
            -ProcessExitCode 1 `
            -ExpectedMethodName ExpectedTest `
            -ExpectedLedger $baseline.testLedger
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
    $mixedPath = Write-Fixture `
        -Name mixed `
        -Summary Failed `
        -Counters ('total="1" executed="1" passed="0" failed="1" ' +
            $zeroInfrastructure) `
        -Definitions $mixed.Definition `
        -Entries $mixed.Entry `
        -Results $mixed.Result
    Assert-Throws `
        -Because 'assertion evidence with a trailing crash' `
        -ExpectedMessage 'not killed solely by assertions' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $mixedPath `
            -EvidenceName mixed `
            -Mode Mutation `
            -ProcessExitCode 1 `
            -ExpectedMethodName ExpectedTest `
            -ExpectedLedger $baseline.testLedger
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

    Write-Host 'Mutation evidence behavioral fixtures passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
