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
        [string]$DisplayName,
        [string]$Class = 'SharpProof.Test.EvidenceTests',
        [string]$TestId = 'test-1',
        [string]$ExecutionId = 'execution-1'
    )

    if ([string]::IsNullOrEmpty($DisplayName)) {
        $DisplayName = $Method
    }
    $escaped = [Security.SecurityElement]::Escape($Message)
    $output = if ($Outcome -eq 'Failed') {
        "<Output><ErrorInfo><Message>$escaped</Message><StackTrace>irrelevant Assert.That(text)</StackTrace></ErrorInfo></Output>"
    }
    else { '' }
    return [pscustomobject]@{
        Definition = "<UnitTest id='$TestId' name='$DisplayName'><Execution id='$ExecutionId'/><TestMethod className='$Class' name='$Method'/></UnitTest>"
        Entry = "<TestEntry testId='$TestId' executionId='$ExecutionId'/>"
        Result = "<UnitTestResult testId='$TestId' executionId='$ExecutionId' testName='$DisplayName' outcome='$Outcome'>$output</UnitTestResult>"
        Identity = "$Class.$Method|$DisplayName"
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
    $catalogAuthority = [pscustomobject][ordered]@{
        Name = 'authority'
        File = 'Project\Source.cs'
        Project = 'Project.Test\Project.Test.csproj'
        Filter = 'FullyQualifiedName~AuthorityTest'
        Original = "before`ntext"
        Mutated = "after`ntext"
    }
    $catalogDigest = Get-SharpProofMutationCatalogSha256 `
        -Mutations @($catalogAuthority)
    if ($catalogDigest -ne (
            Get-SharpProofMutationCatalogSha256 `
                -Mutations @($catalogAuthority))) {
        throw 'Mutation catalog authority digest is not deterministic.'
    }
    foreach ($change in @(
            @{ Name = 'authority-2' },
            @{ File = 'Project\Other.cs' },
            @{ Project = 'Other.Test\Other.Test.csproj' },
            @{ Filter = 'FullyQualifiedName~OtherTest' },
            @{ Original = "different`noriginal" },
            @{ Mutated = "different`nmutation" })) {
        $changed = [ordered]@{
            Name = $catalogAuthority.Name
            File = $catalogAuthority.File
            Project = $catalogAuthority.Project
            Filter = $catalogAuthority.Filter
            Original = $catalogAuthority.Original
            Mutated = $catalogAuthority.Mutated
        }
        foreach ($entry in $change.GetEnumerator()) {
            $changed[$entry.Key] = $entry.Value
        }
        if ((Get-SharpProofMutationCatalogSha256 `
                -Mutations @([pscustomobject]$changed)) -eq $catalogDigest) {
            throw (
                "Mutation catalog digest ignored authority field " +
                ($change.Keys -join ', ') + '.')
        }
    }

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
    $batchPath = Write-Fixture `
        -Name passing-batch `
        -Summary Completed `
        -Counters ('total="2" executed="2" passed="2" failed="0" ' +
            $zeroInfrastructure) `
        -Definitions ($batchFirst.Definition + $batchSecond.Definition) `
        -Entries ($batchFirst.Entry + $batchSecond.Entry) `
        -Results ($batchFirst.Result + $batchSecond.Result)
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

    $caseAssertionMessage =
        "Assert.That(actual, Is.EqualTo(expected))`n Expected: 1`n But was: 2"
    $caseBaselineParts = New-TestParts `
        -Outcome Passed `
        -Message '' `
        -Method ExpectedTest `
        -DisplayName 'ExpectedTest(Case("A"))'
    $caseBaselinePath = Write-Fixture `
        -Name case-ledger-baseline `
        -Summary Completed `
        -Counters ('total="1" executed="1" passed="1" failed="0" ' +
            $zeroInfrastructure) `
        -Definitions $caseBaselineParts.Definition `
        -Entries $caseBaselineParts.Entry `
        -Results $caseBaselineParts.Result
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
    $exactCasePath = Write-Fixture `
        -Name exact-case-ledger `
        -Summary Failed `
        -Counters ('total="1" executed="1" passed="0" failed="1" ' +
            $zeroInfrastructure) `
        -Definitions $exactCaseParts.Definition `
        -Entries $exactCaseParts.Entry `
        -Results $exactCaseParts.Result
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
        $driftPath = Write-Fixture `
            -Name $drift.Name `
            -Summary Failed `
            -Counters ('total="1" executed="1" passed="0" failed="1" ' +
                $zeroInfrastructure) `
            -Definitions $drift.Parts.Definition `
            -Entries $drift.Parts.Entry `
            -Results $drift.Parts.Result
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
    $caseRowsPath = Write-Fixture `
        -Name case-distinct-rows `
        -Summary Completed `
        -Counters ('total="2" executed="2" passed="2" failed="0" ' +
            $zeroInfrastructure) `
        -Definitions ($upperParameter.Definition + $lowerParameter.Definition) `
        -Entries ($upperParameter.Entry + $lowerParameter.Entry) `
        -Results ($upperParameter.Result + $lowerParameter.Result)
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
    $caseMethodsPath = Write-Fixture `
        -Name case-distinct-methods `
        -Summary Completed `
        -Counters ('total="2" executed="2" passed="2" failed="0" ' +
            $zeroInfrastructure) `
        -Definitions ($upperMethod.Definition + $lowerMethod.Definition) `
        -Entries ($upperMethod.Entry + $lowerMethod.Entry) `
        -Results ($upperMethod.Result + $lowerMethod.Result)
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

    $identifierContinuation = New-TestParts `
        -Outcome Failed `
        -Message ("Assert.That(WorkerProtocolJson.Validate(response).Errors`n" +
            "                .Select(static error => error.Code), Does.Contain(""response.claim_set""))`n" +
            " Expected: some item equal to ""response.claim_set""`n" +
            " But was:  < ""summary.totals"" >")
    $identifierContinuationPath = Write-Fixture `
        -Name identifier-continuation `
        -Summary Failed `
        -Counters ('total="1" executed="1" passed="0" failed="1" ' +
            $zeroInfrastructure) `
        -Definitions $identifierContinuation.Definition `
        -Entries $identifierContinuation.Entry `
        -Results $identifierContinuation.Result
    $identifierContinuationMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $identifierContinuationPath `
        -EvidenceName identifier-continuation `
        -Mode Mutation `
        -ProcessExitCode 1 `
        -ExpectedMethodName ExpectedTest `
        -ExpectedLedger $baseline.testLedger
    if ($identifierContinuationMutation.assertionFailureCount -ne 1) {
        throw 'Assertion code identifier continuation was not recognized.'
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
            " Expected string length 24 but was 20. Strings differ at index 0.`n" +
             " Expected: ConsoleApplication`n" +
             " But was: WindowsApplication`n" +
             " First non-matching item at index [0]: item`n" +
             " Missing (1): expected-item`n" +
             " Extra (2): actual-item`n" +
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
    $multiplePath = Write-Fixture `
        -Name multiple-assertion `
        -Summary Failed `
        -Counters ('total="1" executed="1" passed="0" failed="1" ' +
            $zeroInfrastructure) `
        -Definitions $multipleAssertion.Definition `
        -Entries $multipleAssertion.Entry `
        -Results $multipleAssertion.Result
    $multipleMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $multiplePath `
        -EvidenceName multiple-assertion `
        -Mode Mutation `
        -ProcessExitCode 1 `
        -ExpectedMethodName ExpectedTest `
        -ExpectedLedger $baseline.testLedger
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
    $multipleCollectionPath = Write-Fixture `
        -Name multiple-collection-assertion `
        -Summary Failed `
        -Counters ('total="1" executed="1" passed="0" failed="1" ' +
            $zeroInfrastructure) `
        -Definitions $multipleCollectionAssertion.Definition `
        -Entries $multipleCollectionAssertion.Entry `
        -Results $multipleCollectionAssertion.Result
    $multipleCollectionMutation = Read-SharpProofMutationTestEvidence `
        -TrxPath $multipleCollectionPath `
        -EvidenceName multiple-collection-assertion `
        -Mode Mutation `
        -ProcessExitCode 1 `
        -ExpectedMethodName ExpectedTest `
        -ExpectedLedger $baseline.testLedger
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
    $multipleMixedPath = Write-Fixture `
        -Name multiple-mixed `
        -Summary Failed `
        -Counters ('total="1" executed="1" passed="0" failed="1" ' +
            $zeroInfrastructure) `
        -Definitions $multipleMixed.Definition `
        -Entries $multipleMixed.Entry `
        -Results $multipleMixed.Result
    Assert-Throws `
        -Because 'a mixed Assert.Multiple failure and exception' `
        -ExpectedMessage 'not killed solely by assertions' `
        -Action {
        Read-SharpProofMutationTestEvidence `
            -TrxPath $multipleMixedPath `
            -EvidenceName multiple-mixed `
            -Mode Mutation `
            -ProcessExitCode 1 `
            -ExpectedMethodName ExpectedTest `
            -ExpectedLedger $baseline.testLedger
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
