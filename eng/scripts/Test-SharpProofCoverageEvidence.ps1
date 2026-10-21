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
    $expected = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@([pscustomobject]@{ Name = 'fixture'; Target = 'fixture.csproj' }))
    $completed = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(([ordered]@{ Test = $expected[0]; ExitCode = 0 }).AsReadOnly()))
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

    # Arcade project filenames can differ from retained assembly identities.
    # Both direct project and solution-filter receipts must use AssemblyName.
    [IO.File]::WriteAllText($report, '<coverage />')
    $aliasProject = Join-Path $temporary 'Alias.Tests.csproj'
    $filterDirectory = Join-Path $temporary 'filters'
    [void][IO.Directory]::CreateDirectory($filterDirectory)
    $filterPath = Join-Path $filterDirectory 'Fixture.slnf'
    [IO.File]::WriteAllText($filterPath, '{"solution":{"path":"../Fixture.slnx","projects":["Alias.Tests.csproj"]}}')
    foreach ($coverageTarget in @($aliasProject, $filterPath)) {
        $expected[0].Target = $coverageTarget
        [IO.File]::WriteAllText($aliasProject, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><AssemblyName>fixture</AssemblyName></PropertyGroup></Project>')
        Save-SharpProofCoverageCohort -Root $temporary -Expected $expected -Completed $completed
        [IO.File]::WriteAllText($aliasProject, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><AssemblyName>other</AssemblyName></PropertyGroup></Project>')
        Assert-EvidenceRejected -Action {
            Save-SharpProofCoverageCohort -Root $temporary -Expected $expected -Completed $completed
        } -Message "Coverage task 'fixture' has no test receipt for expected assembly 'other.dll'."
    }
    $expected[0].Target = 'fixture.csproj'

    # Same-second VSTest runs may reuse deployment and attachment names. Each
    # project's receipt must resolve its own attachment under its task root.
    $isolatedRoot = Join-Path $temporary 'isolated-projects'
    $isolatedExpected = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@('first', 'second' | ForEach-Object {
                ([ordered]@{ Name = $_; Target = $_ + '.csproj' }).AsReadOnly()
            }))
    $isolatedCompleted = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($isolatedExpected | ForEach-Object {
                ([ordered]@{ Test = $_; ExitCode = 0 }).AsReadOnly()
            }))
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
    $architectureExpected = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@([pscustomobject]@{
                Name         = 'architecture'
                Target       = 'src/SharpProof.Architecture.Tests/SharpProof.Architecture.Tests.csproj'
                EvidenceKind = 'ArchitectureOnly'
            }))
    $architectureCompleted = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(([ordered]@{ Test = $architectureExpected[0]; ExitCode = 0 }).AsReadOnly()))
    $saveArchitecture = {
        Save-SharpProofCoverageCohort -Root $temporary -Expected $architectureExpected -Completed $architectureCompleted
    }
    & $saveArchitecture

    $architectureExpected[0].Target = 'src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj'
    Assert-EvidenceRejected -Action $saveArchitecture -Message 'Only the exact noninstrumented architecture task*'
    $architectureExpected[0].Target = 'src/SharpProof.Architecture.Tests/SharpProof.Architecture.Tests.csproj'
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

& (Join-Path $PSScriptRoot 'Test-SharpProofSemanticCoveragePlan.ps1')

& (Join-Path $PSScriptRoot 'Test-SharpProofCoverageMergeRecords.ps1')
