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
    $resourceMethod = 'SharpProof.Package.Test.PackageLayoutSmokeTests.MainPackageChecksLayoutBoundBytesWithExplicitAssumptionProvenance'
    $resourceClass = 'SharpProof.Package.Test.PackageLayoutSmokeTests'
    $resourceName = 'MainPackageChecksLayoutBoundBytesWithExplicitAssumptionProvenance'
    $resourceCases = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($index in 0..31) {
        $resourceCases.Add(([ordered]@{ method = $resourceMethod; name = $resourceMethod + '(False,' + $index + ')' }).AsReadOnly())
    }
    foreach ($name in @('MainPackageClosureSharedGraphProof', 'MainPackageClosureSharedGraphViolation')) {
        $resourceCases.Add(([ordered]@{ method = $resourceMethod; name = $resourceClass + '.' + $name }).AsReadOnly())
    }
    $resourceOwners = @($resourceCases.ToImmutable() | ForEach-Object { Get-SharpProofPackageCasePartition -Method $_.method -Name $_.name } | Sort-Object -Unique)
    if (($resourceOwners -join ',') -cne '0,1,2,3') { throw 'Independent resource cases were not spread across the existing four runners.' }
    foreach ($name in @('First(1)', 'First(2)', 'first(1)')) {
        if ((Get-SharpProofPackageCasePartition -Method 'SharpProof.Package.Test.Fixture.First' -Name $name) -ne
            (Get-SharpProofPackagePartition -Method 'SharpProof.Package.Test.Fixture.First')) { throw 'An unrelated method changed partition ownership.' }
    }
    Assert-Rejection { Get-SharpProofPackageCasePartition -Method $resourceMethod -Name '' }
    Assert-Rejection { Get-SharpProofPackagePartitionFilter -Cases @() }
    if ((ConvertTo-SharpProofPackageFilterValue -Value 'a\(b)&c|d=e!f~g') -cne 'a\\\(b\)\&c\|d\=e\!f\~g') { throw 'Exact case filter escaping changed.' }
    $exactFilter = Get-SharpProofPackagePartitionFilter -Cases @($resourceCases[0], $resourceCases[32])
    if ($exactFilter -cne ('FullyQualifiedName=' + $resourceMethod + '\(False,0\)|FullyQualifiedName=' + $resourceClass + '.MainPackageClosureSharedGraphProof')) { throw 'Parameterized or renamed resource case selection is not exact.' }
    $renamedCase = ([ordered]@{ method = 'SharpProof.Package.Test.Fixture.Source'; name = 'SharpProof.Package.Test.Fixture.Renamed' }).AsReadOnly()
    if ((Get-SharpProofPackageCasePartition -Method $renamedCase.method -Name $renamedCase.name) -ne
        (Get-SharpProofPackagePartition -Method $renamedCase.method) -or
        (Get-SharpProofPackagePartitionFilter -Cases @($renamedCase)) -cne 'FullyQualifiedName=SharpProof.Package.Test.Fixture.Renamed') { throw 'A renamed ordinary case lost exact selection or changed its original method ownership.' }
    $parentSelector = 'FullyQualifiedName~' + $resourceMethod
    $innerFilter = ConvertTo-SharpProofPackageCaseShardFilter -Filter $parentSelector -Cases @($resourceCases[0], $resourceCases[32])
    if ($innerFilter -cne ('(FullyQualifiedName=' + $resourceMethod + '\(False,0\))')) { throw 'An inner resource bucket still matches a parameterized parent and its renamed children.' }
    $aliasSelector = 'FullyQualifiedName~' + $resourceCases[32].name
    if ((ConvertTo-SharpProofPackageCaseShardFilter -Filter $aliasSelector -Cases @($resourceCases[0], $resourceCases[32])) -cne $aliasSelector) { throw 'A renamed resource child changed inner bucket ownership.' }
    Assert-SharpProofPackagePartitionSelection -Expected @($resourceCases[0]) -Discovered @($resourceCases[0].name)
    Assert-Rejection { Assert-SharpProofPackagePartitionSelection -Expected @($resourceCases[0]) -Discovered @() }
    Assert-Rejection { Assert-SharpProofPackagePartitionSelection -Expected @($resourceCases[0]) -Discovered @($resourceCases[0].name, $resourceCases[0].name) }
    Assert-Rejection { Assert-SharpProofPackagePartitionSelection -Expected @($resourceCases[0]) -Discovered @($resourceCases[1].name) }
    $nodes = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($index in 0..31) {
        $nodes.Add('<test-case classname="SharpProof.Package.Test.Fixture" methodname="Method' + $index + '" fullname="SharpProof.Package.Test.Fixture.Method' + $index + '(1)" />')
        $nodes.Add('<test-case classname="SharpProof.Package.Test.Fixture" methodname="Method' + $index + '" fullname="SharpProof.Package.Test.Fixture.Method' + $index + '(2)" />')
    }
    foreach ($case in $resourceCases.ToImmutable()) {
        $nodes.Add('<test-case classname="' + $resourceClass + '" methodname="' + $resourceName + '" fullname="' + $case.name + '" />')
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
        if ($cases.Count -ne 98) { throw 'Category exclusion lost parameterized cases.' }
        $all = @(Read-SharpProofPackageDiscovery -Dump (Join-Path $part 'discovery.dump'))
        if ($all.Count -ne 99) { throw 'Unfiltered package coverage changed its category universe.' }
        $selected = @($cases | Where-Object { (Get-SharpProofPackageCasePartition -Method $_.method -Name $_.name) -eq $index })
        @($selected | ForEach-Object { $_.name }) | Set-Content -LiteralPath (Join-Path $part 'fixture-core.txt') -Encoding utf8NoBOM
        $definitions = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $results = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        foreach ($case in $selected) {
            $identity = [Guid]::NewGuid().ToString()
            $className = $case.method.Substring(0, $case.method.LastIndexOf('.'))
            $method = $case.name.Substring($className.Length + 1)
            $definitions.Add('<UnitTest id="' + $identity + '"><TestMethod className="' + $className + '" name="' + $method + '" codeBase="/bin/net10.0/SharpProof.Package.Test.dll" /></UnitTest>')
            $results.Add('<UnitTestResult testId="' + $identity + '" executionId="' + [Guid]::NewGuid().ToString() + '" testName="' + $case.name + '" outcome="Passed" />')
        }
        ('<TestRun><TestDefinitions>' + ($definitions.ToImmutable() -join '') + '</TestDefinitions><Results>' + ($results.ToImmutable() -join '') + '</Results><Counters total="' + $selected.Count + '" failed="0" error="0" /></TestRun>') | Set-Content -LiteralPath (Join-Path $part 'receipt.trx') -Encoding utf8NoBOM
        $discoveryShard = ([ordered]@{ name = 'fixture-core'; sha256 = (Get-FileHash -LiteralPath (Join-Path $part 'fixture-core.txt') -Algorithm SHA256).Hash }).AsReadOnly()
        ([ordered]@{ schemaVersion = 2; count = 4; index = $index; commit = 'commit'; runId = 'run'; filter = $filter; selected = @($selected | ForEach-Object { $_.name }); selectionFilter = Get-SharpProofPackagePartitionFilter -Cases $selected; shards = @($discoveryShard) }).AsReadOnly() | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $part 'package-partition.json') -Encoding utf8NoBOM
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
    $dump.Replace('</test-suite>', $nodes[0] + '</test-suite>') | Set-Content -LiteralPath $dumpPath
    Assert-Rejection { Read-SharpProofPackageDiscovery -Dump $dumpPath }
    $dump.Replace('fullname="SharpProof.Package.Test.Fixture.Method0(1)"', 'fullname=""') | Set-Content -LiteralPath $dumpPath
    Assert-Rejection { Read-SharpProofPackageDiscovery -Dump $dumpPath }
    $dump.Replace('Method31', 'Changed31') | Set-Content -LiteralPath $dumpPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    $dump | Set-Content -LiteralPath $dumpPath
    $manifestPath = Join-Path $roots[0] 'package-partition.json'
    $savedManifest = Get-Content -LiteralPath $manifestPath -Raw
    $manifest = $savedManifest | ConvertFrom-Json
    $manifest.schemaVersion = 1
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    $manifest = $savedManifest | ConvertFrom-Json
    $manifest.selectionFilter = 'FullyQualifiedName~SharpProof.Package.Test'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    $manifest = $savedManifest | ConvertFrom-Json
    $manifest.shards = @($manifest.shards[0], $manifest.shards[0])
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    $manifest = $savedManifest | ConvertFrom-Json
    $manifest.shards[0].name = '../fixture-core'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    $manifest = $savedManifest | ConvertFrom-Json
    $manifest.shards[0].sha256 = '0' * 64
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    $savedManifest | Set-Content -LiteralPath $manifestPath
    $selectionPath = Join-Path $roots[0] 'fixture-core.txt'
    $savedSelection = Get-Content -LiteralPath $selectionPath -Raw
    $savedSelection + $savedSelection | Set-Content -LiteralPath $selectionPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    $savedSelection | Set-Content -LiteralPath $selectionPath -NoNewline
    $manifest = $savedManifest | ConvertFrom-Json
    $manifest.selected = @($manifest.selected) + 'extra'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    Assert-Rejection { Assert-SharpProofPackagePartitions -Roots $roots -Commit 'commit' -RunId 'run' -Filter $filter }
    Write-Host 'Package partition regressions passed: independent resource cases, exact filters, unchanged method ownership, categories, discovery/TRX union, identity binding, failures, and tampering.'
}
finally { [IO.Directory]::Delete($root, $true) }
