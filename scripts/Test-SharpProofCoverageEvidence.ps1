[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.CoverageShardEvidence.ps1')

foreach ($name in @('report.cobertura.xml', 'report.cobertura[1].xml', 'report.cobertura[12].xml')) {
    if (-not (Test-SharpProofCoverageReportName -Path $name)) {
        throw "Collector report name was rejected: '$name'."
    }
}
foreach ($name in @('report.cobertura-other.xml', 'report.cobertura[0].xml', 'report.cobertura[1].xml.tmp')) {
    if (Test-SharpProofCoverageReportName -Path $name) {
        throw "Non-collector report name was accepted: '$name'."
    }
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('SharpProofCoverageEvidence.' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    $taskRoot = Join-Path $temporary 'fixture'
    $attachmentRoot = Join-Path $taskRoot 'attachments'
    [IO.Directory]::CreateDirectory($attachmentRoot) | Out-Null
    $report = Join-Path $attachmentRoot 'report.cobertura[1].xml'
    [IO.File]::WriteAllText($report, '<coverage />')
    $receipt = @'
<TestRun>
  <ResultSummary><Counters total="1" failed="0" error="0" /></ResultSummary>
  <TestDefinitions><UnitTest id="test-1"><TestMethod codeBase="fixture.dll" /></UnitTest></TestDefinitions>
  <Results><UnitTestResult testId="test-1" outcome="Passed" /></Results>
  <CollectorDataEntries><Collector><UriAttachments><UriAttachment><A href="attachments/report.cobertura[1].xml" /></UriAttachment></UriAttachments></Collector></CollectorDataEntries>
</TestRun>
'@
    [IO.File]::WriteAllText((Join-Path $taskRoot 'fixture.trx'), $receipt)
    $expected = @([pscustomobject]@{ Name = 'fixture'; Target = 'fixture.csproj' })
    $completed = @([pscustomobject]@{ Test = $expected[0]; ExitCode = 0 })
    Save-SharpProofCoverageCohort -Root $temporary -Expected $expected -Completed $completed
    if (-not (Test-Path -LiteralPath (Join-Path $temporary 'coverage-cohort.json') -PathType Leaf)) {
        throw 'The collision-suffixed collector attachment did not complete its cohort.'
    }
    [IO.File]::Delete($report)
    $rejectedMissingAttachment = $false
    try {
        Save-SharpProofCoverageCohort -Root $temporary -Expected $expected -Completed $completed
    }
    catch {
        if ($_.Exception.Message -notlike '*missing a collector attachment*') { throw }
        $rejectedMissingAttachment = $true
    }
    if (-not $rejectedMissingAttachment) {
        throw 'A missing collision-suffixed collector attachment was accepted.'
    }

    function Assert-EvidenceRejected {
        param([scriptblock]$Action, [string]$Message)
        try { & $Action }
        catch {
            if ($_.Exception.Message -notlike $Message) { throw }
            return
        }
        throw "Invalid evidence was accepted: '$Message'."
    }

    # Same-second VSTest runs may reuse deployment and attachment names. Each
    # project's receipt must resolve its own attachment under its task root.
    $isolatedRoot = Join-Path $temporary 'isolated-projects'
    $isolatedExpected = @('first', 'second' | ForEach-Object {
            [pscustomobject]@{ Name = $_; Target = $_ + '.csproj' }
        })
    $isolatedCompleted = @($isolatedExpected | ForEach-Object {
            [pscustomobject]@{ Test = $_; ExitCode = 0 }
        })
    foreach ($task in $isolatedExpected) {
        $projectRoot = Join-Path $isolatedRoot $task.Name
        $deploymentRoot = Join-Path $projectRoot 'same-second/In/attachments'
        [IO.Directory]::CreateDirectory($deploymentRoot) | Out-Null
        [IO.File]::WriteAllText((Join-Path $deploymentRoot 'report.cobertura[1].xml'), '<coverage />')
        $projectReceipt = $receipt.Replace('fixture.dll', $task.Name + '.dll').Replace(
            '<TestRun>', '<TestRun><Deployment runDeploymentRoot="same-second" />')
        [IO.File]::WriteAllText((Join-Path $projectRoot 'same-name.trx'), $projectReceipt)
    }
    Save-SharpProofCoverageCohort -Root $isolatedRoot -Expected $isolatedExpected -Completed $isolatedCompleted
    [IO.File]::Delete((Join-Path $isolatedRoot 'second/same-second/In/attachments/report.cobertura[1].xml'))
    Assert-EvidenceRejected -Action {
        Save-SharpProofCoverageCohort -Root $isolatedRoot -Expected $isolatedExpected -Completed $isolatedCompleted
    } -Message "Coverage task 'second' is missing a collector attachment."

    $architectureRoot = Join-Path $temporary 'architecture'
    [IO.Directory]::CreateDirectory($architectureRoot) | Out-Null
    $definitions = (1..5 | ForEach-Object {
            '<UnitTest id="architecture-' + $_ + '"><TestMethod codeBase="SharpProof.ArchitectureTest.dll" /></UnitTest>'
        }) -join ''
    $results = (1..5 | ForEach-Object {
            '<UnitTestResult testId="architecture-' + $_ + '" outcome="Passed" />'
        }) -join ''
    $architectureReceipt = '<TestRun><ResultSummary><Counters total="5" failed="0" error="0" />' +
    '<CollectorDataEntries><Collector collectorDisplayName="Code Coverage" /></CollectorDataEntries></ResultSummary>' +
    '<TestDefinitions>' + $definitions + '</TestDefinitions><Results>' + $results + '</Results></TestRun>'
    $architectureReceiptPath = Join-Path $architectureRoot 'architecture.trx'
    [IO.File]::WriteAllText($architectureReceiptPath, $architectureReceipt)
    $architectureExpected = @([pscustomobject]@{
            Name         = 'architecture'
            Target       = 'SharpProof.ArchitectureTest/SharpProof.ArchitectureTest.csproj'
            EvidenceKind = 'ArchitectureOnly'
        })
    $architectureCompleted = @([pscustomobject]@{ Test = $architectureExpected[0]; ExitCode = 0 })
    $saveArchitecture = {
        Save-SharpProofCoverageCohort -Root $temporary -Expected $architectureExpected -Completed $architectureCompleted
    }
    & $saveArchitecture

    $architectureExpected[0].Target = 'SharpProof.Worker.Test/SharpProof.Worker.Test.csproj'
    Assert-EvidenceRejected -Action $saveArchitecture -Message 'Only the exact noninstrumented architecture task*'
    $architectureExpected[0].Target = 'SharpProof.ArchitectureTest/SharpProof.ArchitectureTest.csproj'
    [IO.File]::WriteAllText($architectureReceiptPath, $architectureReceipt.Replace('SharpProof.ArchitectureTest.dll', 'SharpProof.Worker.Test.dll'))
    Assert-EvidenceRejected -Action $saveArchitecture -Message 'Architecture-only receipt evidence cannot contain*'
    [IO.File]::WriteAllText($architectureReceiptPath, $architectureReceipt.Replace('outcome="Passed"', 'outcome="NotExecuted"'))
    Assert-EvidenceRejected -Action $saveArchitecture -Message '*dangling or unsuccessful test result*'
    [IO.File]::WriteAllText($architectureReceiptPath, $architectureReceipt.Replace('total="5"', 'total="4"'))
    Assert-EvidenceRejected -Action $saveArchitecture -Message 'Architecture-only evidence must retain all five*'
    [IO.File]::WriteAllText($architectureReceiptPath, $architectureReceipt)

    $receiptWithoutCollector = [regex]::Replace($receipt, '(?s)<CollectorDataEntries>.*?</CollectorDataEntries>', '')
    [IO.File]::WriteAllText((Join-Path $taskRoot 'fixture.trx'), $receiptWithoutCollector)
    Assert-EvidenceRejected -Action {
        Save-SharpProofCoverageCohort -Root $temporary -Expected $expected -Completed $completed
    } -Message '*has no collector attachment*'
}
finally {
    [IO.Directory]::Delete($temporary, $true)
}
Write-Host 'Coverage evidence regressions passed: isolated project roots, collector collisions, missing attachments, and exact architecture-only receipts.'
