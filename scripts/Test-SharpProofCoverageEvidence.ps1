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
}
finally {
    [IO.Directory]::Delete($temporary, $true)
}
Write-Host 'Coverage evidence regressions passed: collector collisions and missing attachments.'
