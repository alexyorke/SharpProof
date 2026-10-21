[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.CanonicalCohortEvidence.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-canonical-evidence-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
try {
    $taskRoot = Join-Path $fixture 'task'
    $discoveryRoot = Join-Path $taskRoot 'discovery'
    [IO.Directory]::CreateDirectory($discoveryRoot) | Out-Null
    # Reverse assembly/file order so enumeration cannot stand in for sorting.
    foreach ($entry in @(([ordered]@{ File = 'a'; Assembly = 'Z'; Id = '2' }).AsReadOnly(), ([ordered]@{ File = 'z'; Assembly = 'A'; Id = '1' }).AsReadOnly())) {
        $assembly = $entry.Assembly
        $id = $entry.Id
        $trx = @"
<TestRun><TestDefinitions><UnitTest id="$id"><TestMethod codeBase="/tmp/net8.0/$assembly.Test.dll" className="Fixture" name="Case$assembly" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="$id" executionId="$id" testName="Case$assembly" outcome="Passed" /></Results><ResultSummary><Counters total="1" /></ResultSummary></TestRun>
"@
        [IO.File]::WriteAllText((Join-Path $taskRoot ($entry.File + '.trx')), $trx)
        [IO.File]::WriteAllText((Join-Path $discoveryRoot ($assembly + '.Test.dll.discovery.dump')), "<test-run><test-case id='$id' fullname='Fixture.Case$assembly' /></test-run>")
    }
    [IO.File]::WriteAllLines((Join-Path $taskRoot 'discovered-tests.txt'), [string[]]@('Fixture.CaseA', 'Fixture.CaseZ'))
    $cases = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(Read-SharpProofCanonicalCases -Root $fixture))
    if ($cases.Count -ne 2 -or $cases[0].assembly -cne 'A.Test.dll' -or $cases[1].assembly -cne 'Z.Test.dll') {
        throw 'Canonical TRX case ordering depends on file enumeration.'
    }
    $discovery = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(Read-SharpProofCanonicalDiscovery -Root $fixture))
    if ($discovery.Count -ne 2 -or $discovery[0].assembly -cne 'A.Test.dll' -or $discovery[1].assembly -cne 'Z.Test.dll') {
        throw 'Canonical NUnit case ordering depends on file enumeration.'
    }
    function Assert-CanonicalFailure([scriptblock]$Action, [string]$Expected) {
        try { & $Action | Out-Null }
        catch {
            if ($_.Exception.Message -cne $Expected) { throw }
            return
        }
        throw "Canonical evidence accepted an invalid fixture: $Expected"
    }
    $encodedCollision = [Collections.Immutable.ImmutableArray]::Create[object](
        ([ordered]@{ assembly = 'A'; fullyQualifiedName = [string][char]1 }).AsReadOnly(),
        ([ordered]@{ assembly = 'A'; fullyQualifiedName = '\u0001' }).AsReadOnly())
    Assert-CanonicalFailure { Get-SharpProofTrxDiscoveryNames -Discovery @($encodedCollision) } 'Distinct NUnit case names collide under the actual TRX serializer encoding.'
    $repeatedNames = [Collections.Immutable.ImmutableArray]::Create[object](
        ([ordered]@{ assembly = 'A'; fullyQualifiedName = 'Same' }).AsReadOnly(),
        ([ordered]@{ assembly = 'A'; fullyQualifiedName = 'Same' }).AsReadOnly())
    if (@(Get-SharpProofTrxDiscoveryNames -Discovery @($repeatedNames)).Count -ne 2) {
        throw 'Canonical discovery lost parameterized name multiplicity.'
    }
    $trxPath = Join-Path $taskRoot 'a.trx'
    $validTrx = [IO.File]::ReadAllText($trxPath)
    try {
        [IO.File]::WriteAllText($trxPath, $validTrx.Replace('testId="2"', 'testId="missing"'))
        Assert-CanonicalFailure { Read-SharpProofCanonicalCases -Root $fixture } 'Canonical TRX result has no matching execution definition.'
        [IO.File]::WriteAllText($trxPath, $validTrx.Replace('</Results>', '<UnitTestResult testId="2" executionId="2" testName="CaseZ" outcome="Passed" /></Results>').Replace('total="1"', 'total="2"'))
        Assert-CanonicalFailure { Read-SharpProofCanonicalCases -Root $fixture } 'Canonical TRX execution identity is absent or duplicated.'
    }
    finally { [IO.File]::WriteAllText($trxPath, $validTrx) }
    $dumpPath = Join-Path $discoveryRoot 'Z.Test.dll.discovery.dump'
    $validDump = [IO.File]::ReadAllText($dumpPath)
    try {
        [IO.File]::WriteAllText($dumpPath, $validDump.Replace('</test-run>', "<test-case id='2' fullname='Fixture.CaseZ' /></test-run>"))
        Assert-CanonicalFailure { Read-SharpProofCanonicalDiscovery -Root $fixture } 'Actual NUnit discovery has an absent or duplicated per-case identity.'
    }
    finally { [IO.File]::WriteAllText($dumpPath, $validDump) }
    $partsRoot = Join-Path $fixture 'parts'
    [IO.Directory]::CreateDirectory($partsRoot) | Out-Null
    foreach ($partName in @('worker-a', 'worker-b', 'projects')) {
        ([ordered]@{
            schemaVersion = 1; commit = 'commit'; tree = 'tree'; runId = 'run'; runAttempt = '1'
            cohort = 'Remaining'; configuration = 'Release'; filter = 'filter'; projects = @('project')
            expectedTasks = @($partName); completedTasks = @($partName); completed = @()
            discovered = @($partName); discoveredCases = @(); cases = @()
        }).AsReadOnly() | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $partsRoot "canonical-cohort.$partName.json") -Encoding utf8NoBOM
    }
    $merged = Read-SharpProofCanonicalReceipt -CohortRoot $partsRoot
    if (($merged.expectedTasks -join ',') -cne 'projects,worker-a,worker-b' -or $merged.discovered.Count -ne 3) {
        throw 'Three-part canonical receipt merging lost a Worker half or the Projects cohort.'
    }
    $partPath = Join-Path $partsRoot 'canonical-cohort.worker-b.json'
    $validPart = Get-Content -LiteralPath $partPath -Raw
    $attempts = [ordered]@{
        'canonical-cohort.projects.json' = '1'
        'canonical-cohort.worker-a.json' = '1'
        'canonical-cohort.worker-b.json' = '2'
    }
    $validPart.Replace('"runAttempt": "1"', '"runAttempt": "2"') | Set-Content -LiteralPath $partPath
    Assert-CanonicalFailure { Read-SharpProofCanonicalReceipt -CohortRoot $partsRoot } 'Canonical cohort parts differ in their run, source, or selection.'
    $retained = Read-SharpProofCanonicalReceipt -CohortRoot $partsRoot -ExpectedAttempts $attempts -CurrentAttempt '2'
    if ($retained.discovered.Count -ne 3) { throw 'Retained successful attempts lost canonical evidence.' }
    Assert-CanonicalFailure { Read-SharpProofCanonicalReceipt -CohortRoot $partsRoot -ExpectedAttempts $attempts -CurrentAttempt '1' } 'Canonical receipt differs from its successful producer attempt.'
    $attempts['canonical-cohort.worker-b.json'] = '1'
    Assert-CanonicalFailure { Read-SharpProofCanonicalReceipt -CohortRoot $partsRoot -ExpectedAttempts $attempts -CurrentAttempt '2' } 'Canonical receipt differs from its successful producer attempt.'
    $attempts.Remove('canonical-cohort.projects.json')
    Assert-CanonicalFailure { Read-SharpProofCanonicalReceipt -CohortRoot $partsRoot -ExpectedAttempts $attempts -CurrentAttempt '2' } 'Canonical artifact attempts do not identify every receipt.'
    $validPart | Set-Content -LiteralPath $partPath
    $validPart.Replace('"commit": "commit"', '"commit": "other"') | Set-Content -LiteralPath $partPath
    Assert-CanonicalFailure { Read-SharpProofCanonicalReceipt -CohortRoot $partsRoot } 'Canonical cohort parts differ in their run, source, or selection.'
    Write-Host 'Canonical evidence regressions passed: deterministic ordering, encoded collisions, name multiplicity, missing definitions and duplicate execution/discovery identities.'
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
