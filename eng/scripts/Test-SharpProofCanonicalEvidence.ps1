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
    Write-Host 'Canonical evidence regressions passed: deterministic TRX and NUnit ordering.'
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
