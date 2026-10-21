Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SharpProof.CanonicalCohortEvidence.ps1')

function Get-SharpProofPackagePartition {
    param([string]$Method, [ValidateRange(1, 4)][int]$Count = 4)
    # Interpret the entire SHA-256 digest as an unsigned big-endian integer.
    $remainder = 0
    foreach ($octet in [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Method))) {
        $remainder = ($remainder * 256 + [int]$octet) % $Count
    }
    return $remainder
}

function Read-SharpProofPackageDiscovery {
    param([string]$Dump, [string]$Filter = '')
    [xml]$document = Get-Content -LiteralPath $Dump -Raw
    $cases = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($test in $document.SelectNodes('//*[local-name()="test-case"]')) {
        $categories = @($test.SelectNodes('ancestor-or-self::*/*[local-name()="properties"]/*[local-name()="property"][@name="Category"]') | ForEach-Object { [string]$_.value })
        if ($Filter -ne '' -and @($categories | Where-Object { $_ -in @('Performance', 'Coverage', 'Corpus') }).Count -gt 0) { continue }
        $method = [string]$test.classname + '.' + [string]$test.methodname
        if ($method -notmatch '^SharpProof\.[A-Za-z_][A-Za-z0-9_.+`]*\.[A-Za-z_][A-Za-z0-9_]*$' -or [string]::IsNullOrWhiteSpace([string]$test.fullname)) {
            throw 'Package discovery has an invalid method or case name.'
        }
        $cases.Add(([ordered]@{ method = $method; name = [string]$test.fullname }).AsReadOnly())
    }
    if ($cases.Count -eq 0) { throw 'Package partition discovery is empty.' }
    return $cases.ToImmutable()
}

function Assert-SharpProofPackagePartitionReceipt {
    param([string]$Root, [object[]]$Expected)
    foreach ($receipt in Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.trx' -File) {
        [xml]$document = Get-Content -LiteralPath $receipt.FullName -Raw
        $counters = $document.SelectSingleNode('//*[local-name()="Counters"]')
        if ($null -eq $counters -or [int]$counters.failed -ne 0 -or [int]$counters.error -ne 0) {
            throw 'Package partition has failed or invalid TRX counters.'
        }
    }
    $actual = @(Read-SharpProofCanonicalCases -Root $Root)
    foreach ($case in $actual | Where-Object { $_.outcome -ceq 'NotExecuted' }) {
        if ($case.fullyQualifiedName -cne 'SharpProof.Package.Test.WorkerMsBuildIntegrationTests.VerificationFailsExplicitlyOnUnsupportedHosts' -or
            $case.reason -cne 'The packaged worker is supported in this container.') {
            throw 'Package partition has an unexpected skipped test.'
        }
    }
    $expectedNames = @($Expected | ForEach-Object { ConvertTo-SharpProofTrxName -Name $_.name } | Sort-Object -CaseSensitive)
    $actualNames = @($actual | ForEach-Object { $_.fullyQualifiedName } | Sort-Object -CaseSensitive)
    if (($expectedNames -join "`n") -cne ($actualNames -join "`n") -or
        @($actual | Where-Object { $_.outcome -cnotin @('Passed', 'NotExecuted') }).Count -ne 0) {
        throw 'Package partition TRX cases differ from actual NUnit discovery or contain unsuccessful results.'
    }
}

function Assert-SharpProofPackagePartitions {
    param([string[]]$Roots, [string]$Commit, [string]$Filter, [string]$RunId = $env:GITHUB_RUN_ID)
    if ($Roots.Count -ne 4) { throw 'Exactly four package partitions are required.' }
    $universe = $null
    $indices = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[int]()
    $assigned = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($root in $Roots) {
        $receipt = Get-Content -LiteralPath (Join-Path $root 'package-partition.json') -Raw | ConvertFrom-Json
        if ($receipt.schemaVersion -ne 1 -or $receipt.count -ne 4 -or $receipt.index -notin 0..3 -or
            -not $indices.Add([int]$receipt.index) -or $receipt.commit -cne $Commit -or
            $receipt.runId -cne [string]$RunId -or $receipt.filter -cne $Filter) {
            throw 'Package partitions have duplicated, missing, or mismatched identities.'
        }
        $cases = @(Read-SharpProofPackageDiscovery -Dump (Join-Path $root 'discovery.dump') -Filter $Filter)
        $names = @($cases | ForEach-Object { $_.method + '|' + $_.name } | Sort-Object -CaseSensitive)
        if ($null -ne $universe -and ($universe -join "`n") -cne ($names -join "`n")) { throw 'Package partitions discovered different test universes.' }
        $universe = $names
        $expected = @($cases | Where-Object { (Get-SharpProofPackagePartition -Method $_.method) -eq $receipt.index })
        $expectedNames = @($expected | ForEach-Object { $_.name } | Sort-Object -CaseSensitive)
        if (($expectedNames -join "`n") -cne (@($receipt.selected | Sort-Object -CaseSensitive) -join "`n")) { throw 'Package partition assignments differ from SHA-256 method ownership.' }
        Assert-SharpProofPackagePartitionReceipt -Root $root -Expected $expected
        foreach ($case in $expected) { $assigned.Add($case.method + '|' + $case.name) }
    }
    if (($universe -join "`n") -cne (@($assigned.ToImmutable() | Sort-Object -CaseSensitive) -join "`n")) { throw 'Package partition union differs from unpartitioned discovery.' }
    Write-Host "Package partitions cover all $($universe.Count) discovered cases exactly once."
}
