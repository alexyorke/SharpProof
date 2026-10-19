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
    foreach ($entry in @(@{ File = 'a'; Assembly = 'Z'; Id = '2' }, @{ File = 'z'; Assembly = 'A'; Id = '1' })) {
        $assembly = $entry.Assembly
        $id = $entry.Id
        $trx = @"
<TestRun><TestDefinitions><UnitTest id="$id"><TestMethod codeBase="/tmp/net8.0/$assembly.Test.dll" className="Fixture" name="Case$assembly" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="$id" executionId="$id" testName="Case$assembly" outcome="Passed" /></Results><ResultSummary><Counters total="1" /></ResultSummary></TestRun>
"@
        [IO.File]::WriteAllText((Join-Path $taskRoot ($entry.File + '.trx')), $trx)
        [IO.File]::WriteAllText((Join-Path $discoveryRoot ($assembly + '.Test.dll.discovery.dump')), "<test-run><test-case id='$id' fullname='Fixture.Case$assembly' /></test-run>")
    }
    [IO.File]::WriteAllLines((Join-Path $taskRoot 'discovered-tests.txt'), [string[]]@('Fixture.CaseA', 'Fixture.CaseZ'))
    $cases = @(Read-SharpProofCanonicalCases -Root $fixture)
    if ($cases.Count -ne 2 -or $cases[0].assembly -cne 'A.Test.dll' -or $cases[1].assembly -cne 'Z.Test.dll') {
        throw 'Canonical TRX case ordering depends on file enumeration.'
    }
    $discovery = @(Read-SharpProofCanonicalDiscovery -Root $fixture)
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
        [ordered]@{ assembly = 'A'; fullyQualifiedName = [string][char]1 },
        [ordered]@{ assembly = 'A'; fullyQualifiedName = '\u0001' })
    Assert-CanonicalFailure { Get-SharpProofTrxDiscoveryNames -Discovery @($encodedCollision) } 'Distinct NUnit case names collide under the actual TRX serializer encoding.'
    $repeatedNames = [Collections.Immutable.ImmutableArray]::Create[object](
        [ordered]@{ assembly = 'A'; fullyQualifiedName = 'Same' },
        [ordered]@{ assembly = 'A'; fullyQualifiedName = 'Same' })
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
    Write-Host 'Canonical evidence regressions passed: deterministic ordering, encoded collisions, name multiplicity, missing definitions and duplicate execution/discovery identities.'
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
