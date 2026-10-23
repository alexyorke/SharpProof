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

function Test-SharpProofPackageCasePartitionedMethod {
    param([string]$Method)
    # These independently isolated cases dominate package-0 when their entire
    # parameterized method is assigned to one runner. All other methods retain
    # their original ownership, including their parameterized cases.
    return $Method -ceq 'SharpProof.Package.Test.PackageLayoutSmokeTests.MainPackageChecksLayoutBoundBytesWithExplicitAssumptionProvenance'
}

function Get-SharpProofPackageCasePartition {
    param([string]$Method, [string]$Name, [ValidateRange(1, 4)][int]$Count = 4)
    if (Test-SharpProofPackageCasePartitionedMethod -Method $Method) {
        if ([string]::IsNullOrWhiteSpace($Name)) { throw 'Package case ownership requires a discovered case name.' }
        return Get-SharpProofPackagePartition -Method ($Method + '|' + $Name) -Count $Count
    }
    return Get-SharpProofPackagePartition -Method $Method -Count $Count
}

function ConvertTo-SharpProofPackageFilterValue {
    param([string]$Value)
    # VSTest filter operators and backslashes must be escaped in exact NUnit
    # case names. Pass this value as one argument, without a shell evaluation.
    return [regex]::Replace($Value, '[\\()&|=!~]', '\$0')
}

function Get-SharpProofPackagePartitionFilter {
    param([object[]]$Cases)
    if ($Cases.Count -eq 0) { throw 'Package partition selected no cases.' }
    return @($Cases | ForEach-Object {
            $defaultName = $_.name -ceq $_.method -or $_.name.StartsWith($_.method + '(', [StringComparison]::Ordinal)
            # A custom TestName is a different discovery leaf from its source
            # method. Select that leaf explicitly so adapter discovery and
            # execution agree even when a parent method also matches.
            if ((Test-SharpProofPackageCasePartitionedMethod -Method $_.method) -or -not $defaultName) {
                'FullyQualifiedName=' + (ConvertTo-SharpProofPackageFilterValue -Value $_.name)
            }
            else {
                'FullyQualifiedName~' + $_.method
            }
        } | Sort-Object -Unique -CaseSensitive) -join '|'
}

function ConvertTo-SharpProofPackageCaseShardFilter {
    param([string]$Filter, [object[]]$Cases)
    foreach ($method in @($Cases | Where-Object { Test-SharpProofPackageCasePartitionedMethod -Method $_.method } | ForEach-Object { $_.method } | Sort-Object -Unique -CaseSensitive)) {
        $parentSelector = 'FullyQualifiedName~' + $method
        if ($Filter.Contains($parentSelector, [StringComparison]::Ordinal)) {
            # Wider local waves can put renamed children and their source
            # method in different inner buckets. Match only the source
            # method's default-name leaves here; aliases keep their own bucket.
            $defaultCases = @($Cases | Where-Object { $_.method -ceq $method -and $_.name.StartsWith($method + '(', [StringComparison]::Ordinal) })
            $leaves = Get-SharpProofPackagePartitionFilter -Cases $defaultCases
            $Filter = $Filter.Replace($parentSelector, '(' + $leaves + ')', [StringComparison]::Ordinal)
        }
    }
    return $Filter
}

function Assert-SharpProofPackagePartitionSelection {
    param([object[]]$Expected, [string[]]$Discovered)
    $expectedNames = @($Expected | ForEach-Object { $_.name } | Sort-Object -CaseSensitive)
    $actualNames = @($Discovered | Sort-Object -CaseSensitive)
    if ($expectedNames.Count -eq 0 -or ($expectedNames -join "`n") -cne ($actualNames -join "`n")) {
        throw 'Package partition filtered discovery differs from its assigned cases.'
    }
}

function Read-SharpProofPackageDiscovery {
    param([string]$Dump, [string]$Filter = '')
    [xml]$document = Get-Content -LiteralPath $Dump -Raw
    $cases = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    $names = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    foreach ($test in $document.SelectNodes('//*[local-name()="test-case"]')) {
        $categories = @($test.SelectNodes('ancestor-or-self::*/*[local-name()="properties"]/*[local-name()="property"][@name="Category"]') | ForEach-Object { [string]$_.value })
        if ($Filter -ne '' -and @($categories | Where-Object { $_ -in @('Performance', 'Coverage', 'Corpus') }).Count -gt 0) { continue }
        $method = [string]$test.classname + '.' + [string]$test.methodname
        if ($method -notmatch '^SharpProof\.[A-Za-z_][A-Za-z0-9_.+`]*\.[A-Za-z_][A-Za-z0-9_]*$' -or [string]::IsNullOrWhiteSpace([string]$test.fullname) -or
            -not $names.Add([string]$test.fullname)) {
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
        if ($receipt.schemaVersion -ne 2 -or $receipt.count -ne 4 -or $receipt.index -notin 0..3 -or
            -not $indices.Add([int]$receipt.index) -or $receipt.commit -cne $Commit -or
            $receipt.runId -cne [string]$RunId -or $receipt.filter -cne $Filter) {
            throw 'Package partitions have duplicated, missing, or mismatched identities.'
        }
        $cases = @(Read-SharpProofPackageDiscovery -Dump (Join-Path $root 'discovery.dump') -Filter $Filter)
        $names = @($cases | ForEach-Object { $_.method + '|' + $_.name } | Sort-Object -CaseSensitive)
        if ($null -ne $universe -and ($universe -join "`n") -cne ($names -join "`n")) { throw 'Package partitions discovered different test universes.' }
        $universe = $names
        $expected = @($cases | Where-Object { (Get-SharpProofPackageCasePartition -Method $_.method -Name $_.name) -eq $receipt.index })
        $expectedNames = @($expected | ForEach-Object { $_.name } | Sort-Object -CaseSensitive)
        if (($expectedNames -join "`n") -cne (@($receipt.selected | Sort-Object -CaseSensitive) -join "`n") -or
            $receipt.selectionFilter -cne (Get-SharpProofPackagePartitionFilter -Cases $expected)) { throw 'Package partition assignments differ from SHA-256 case ownership.' }
        $discovered = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $shards = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
        foreach ($shard in @($receipt.shards)) {
            if ($shard.name -cnotmatch '^[a-z][a-z0-9-]+$' -or -not $shards.Add([string]$shard.name)) { throw 'Package partition has invalid or duplicated discovery shards.' }
            $listing = Join-Path $root ($shard.name + '.txt')
            if ($shard.sha256 -cnotmatch '^[A-F0-9]{64}$' -or (Get-FileHash -LiteralPath $listing -Algorithm SHA256).Hash -cne $shard.sha256) { throw 'Package partition discovery hash differs from its receipt.' }
            $discovered.AddRange([string[]]@(Get-Content -LiteralPath $listing))
        }
        Assert-SharpProofPackagePartitionSelection -Expected $expected -Discovered @($discovered.ToImmutable())
        Assert-SharpProofPackagePartitionReceipt -Root $root -Expected $expected
        foreach ($case in $expected) { $assigned.Add($case.method + '|' + $case.name) }
    }
    if (($universe -join "`n") -cne (@($assigned.ToImmutable() | Sort-Object -CaseSensitive) -join "`n")) { throw 'Package partition union differs from unpartitioned discovery.' }
    Write-Host "Package partitions cover all $($universe.Count) discovered cases exactly once."
}
