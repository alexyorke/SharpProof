[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.PackagePartitions.ps1')

function Assert-Rejection {
    param([scriptblock]$Action)
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid package partition evidence was accepted.' }
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('package-partition-regression-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
try {
    # Independent known SHA-256 vectors: ASCII, case-sensitive identifiers,
    # and unsigned arithmetic all have stable ownership.
    if ((Get-SharpProofPackagePartition -Method 'abc') -ne 1 -or
        (Get-SharpProofPackagePartition -Method '') -ne 1) { throw 'SHA-256 modulo vector mismatch.' }
    $nodes = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($index in 0..31) {
        $nodes.Add('<test-case classname="SharpProof.Package.Test.Fixture" methodname="Method' + $index + '" fullname="SharpProof.Package.Test.Fixture.Method' + $index + '(1)" />')
        $nodes.Add('<test-case classname="SharpProof.Package.Test.Fixture" methodname="Method' + $index + '" fullname="SharpProof.Package.Test.Fixture.Method' + $index + '(2)" />')
    }
    $nodes.Add('<test-suite><properties><property name="Category" value="Corpus" /></properties><test-case classname="SharpProof.Package.Test.Fixture" methodname="Corpus" fullname="SharpProof.Package.Test.Fixture.Corpus" /></test-suite>')
    $dump = '<test-suite>' + ($nodes.ToImmutable() -join '') + '</test-suite>'
    $filter = 'TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus'
    $roots = @(0..3 | ForEach-Object { Join-Path $root $_ })
    foreach ($index in 0..3) {
        $part = $roots[$index]
        [IO.Directory]::CreateDirectory($part) | Out-Null
        $dump | Set-Content -LiteralPath (Join-Path $part 'discovery.dump') -Encoding utf8NoBOM
        $cases = @(Read-SharpProofPackageDiscovery -Dump (Join-Path $part 'discovery.dump') -Filter $filter)
        if ($cases.Count -ne 64) { throw 'Category exclusion lost parameterized cases.' }
        $all = @(Read-SharpProofPackageDiscovery -Dump (Join-Path $part 'discovery.dump'))
        if ($all.Count -ne 65) { throw 'Unfiltered package coverage changed its category universe.' }
        $selected = @($cases | Where-Object { (Get-SharpProofPackagePartition -Method $_.method) -eq $index })
        $definitions = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $results = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        foreach ($case in $selected) {
            $identity = [Guid]::NewGuid().ToString()
            $method = $case.name.Substring('SharpProof.Package.Test.Fixture.'.Length)
            $definitions.Add('<UnitTest id="' + $identity + '"><TestMethod className="SharpProof.Package.Test.Fixture" name="' + $method + '" codeBase="/bin/net10.0/SharpProof.Package.Test.dll" /></UnitTest>')
            $results.Add('<UnitTestResult testId="' + $identity + '" executionId="' + [Guid]::NewGuid().ToString() + '" testName="' + $case.name + '" outcome="Passed" />')
        }
        ('<TestRun><TestDefinitions>' + ($definitions.ToImmutable() -join '') + '</TestDefinitions><Results>' + ($results.ToImmutable() -join '') + '</Results><Counters total="' + $selected.Count + '" failed="0" error="0" /></TestRun>') | Set-Content -LiteralPath (Join-Path $part 'receipt.trx') -Encoding utf8NoBOM
        ([ordered]@{ schemaVersion = 1; count = 4; index = $index; commit = 'commit'; runId = 'run'; filter = $filter; selected = @($selected | ForEach-Object { $_.name }) }).AsReadOnly() | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $part 'package-partition.json') -Encoding utf8NoBOM
    }
    Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots[0..2] -Commit 'commit' -RunId 'run' -Filter $filter }
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots @($roots[0], $roots[0], $roots[2], $roots[3]) -Commit 'commit' -RunId 'run' -Filter $filter }
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'other' -RunId 'run' -Filter $filter }
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'other' -Filter $filter }
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter '' }
    $receiptPath = Join-Path $roots[0] 'receipt.trx'
    $saved = Get-Content -LiteralPath $receiptPath -Raw
    $saved.Replace('outcome="Passed"', 'outcome="Failed"') | Set-Content -LiteralPath $receiptPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    $saved.Replace('outcome="Passed"', 'outcome="NotExecuted"') | Set-Content -LiteralPath $receiptPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    $saved.Replace('failed="0"', 'failed="1"') | Set-Content -LiteralPath $receiptPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    $saved.Replace('Method', 'Missing') | Set-Content -LiteralPath $receiptPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    $saved | Set-Content -LiteralPath $receiptPath
    Copy-Item -LiteralPath $receiptPath -Destination (Join-Path $roots[0] 'duplicate.trx')
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    [IO.File]::Delete((Join-Path $roots[0] 'duplicate.trx'))
    $dumpPath = Join-Path $roots[0] 'discovery.dump'
    $dump.Replace('Method31', 'Changed31') | Set-Content -LiteralPath $dumpPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    $dump | Set-Content -LiteralPath $dumpPath
    $manifestPath = Join-Path $roots[0] 'package-partition.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.selected = @($manifest.selected) + 'extra'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    Write-Host 'Package partition regressions passed: parameter cases, categories, exact union, missing/duplicate/mismatched identities, failures, and tampering.'
}
finally { [IO.Directory]::Delete($root, $true) }
