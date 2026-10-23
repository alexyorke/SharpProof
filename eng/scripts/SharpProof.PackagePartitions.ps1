Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SharpProof.CanonicalCohortEvidence.ps1')

$script:sharpProofPackagePartitionPlan = $null

function Read-SharpProofPackagePartitionPlan {
    param([string]$Path)
    $catalog = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($catalog.schemaVersion -ne 1 -or
        $catalog.algorithm -cne 'duration-lpt-sha256-v1' -or
        $catalog.partitionCount -ne 4 -or @($catalog.methods).Count -eq 0) {
        throw 'Unsupported package partition duration catalog.'
    }
    $weights = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, long]([StringComparer]::Ordinal)
    foreach ($entry in $catalog.methods) {
        $name = [string]$entry.name
        if ($name -notmatch '^SharpProof\.[A-Za-z_][A-Za-z0-9_.+`]*\.[A-Za-z_][A-Za-z0-9_]*$' -or
            ($entry.elapsedMilliseconds -isnot [long] -and $entry.elapsedMilliseconds -isnot [int]) -or
            $entry.elapsedMilliseconds -le 0 -or $weights.ContainsKey($name)) {
            throw 'Package partition durations have invalid or duplicated method weights.'
        }
        $weights.Add($name, [long]$entry.elapsedMilliseconds)
    }
    # LPT uses measured sums of every parameterized case. Sort ordinally first
    # so equal weights and equal bucket loads have culture-independent ties.
    [string[]]$names = @($weights.Keys)
    [Array]::Sort($names, [StringComparer]::Ordinal)
    $assignments = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, int]([StringComparer]::Ordinal)
    $loads = [long[]]@(0, 0, 0, 0)
    foreach ($name in @($names | Sort-Object -Stable -Descending { $weights[$_] })) {
        $partition = 0
        foreach ($index in 1..3) {
            if ($loads[$index] -lt $loads[$partition]) { $partition = $index }
        }
        if ($weights[$name] -gt [long]::MaxValue - $loads[$partition]) {
            throw 'Package partition duration total exceeds Int64.'
        }
        $assignments.Add($name, $partition)
        $loads[$partition] += $weights[$name]
    }
    return ([ordered]@{
            algorithm     = [string]$catalog.algorithm
            catalogSha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
            assignments   = $assignments.ToImmutable()
            milliseconds  = [Collections.Immutable.ImmutableArray]::CreateRange[long]($loads)
        }).AsReadOnly()
}

function Get-SharpProofPackagePartitionPlan {
    if ($null -eq $script:sharpProofPackagePartitionPlan) {
        $script:sharpProofPackagePartitionPlan = Read-SharpProofPackagePartitionPlan -Path (
            Join-Path $PSScriptRoot 'package-partition-durations.json')
    }
    return $script:sharpProofPackagePartitionPlan
}

function Get-SharpProofPackagePartition {
    param([string]$Method, [ValidateRange(1, 4)][int]$Count = 4)
    if ($Count -eq 4) {
        $plan = Get-SharpProofPackagePartitionPlan
        if ($plan.assignments.ContainsKey($Method)) { return $plan.assignments[$Method] }
    }
    # Unmeasured/new methods keep their previous stable ownership, without
    # changing the measured plan. Timing history is never read from a runner.
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
    $plan = Get-SharpProofPackagePartitionPlan
    $universe = $null
    $indices = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[int]()
    $assigned = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($root in $Roots) {
        $receipt = Get-Content -LiteralPath (Join-Path $root 'package-partition.json') -Raw | ConvertFrom-Json
        if ($receipt.schemaVersion -ne 2 -or $receipt.count -ne 4 -or $receipt.index -notin 0..3 -or
            -not $indices.Add([int]$receipt.index) -or $receipt.commit -cne $Commit -or
            $receipt.runId -cne [string]$RunId -or $receipt.filter -cne $Filter -or
            $receipt.assignmentVersion -cne $plan.algorithm -or
            $receipt.durationCatalogSha256 -cne $plan.catalogSha256) {
            throw 'Package partitions have duplicated, missing, or mismatched identities.'
        }
        $cases = @(Read-SharpProofPackageDiscovery -Dump (Join-Path $root 'discovery.dump') -Filter $Filter)
        $names = @($cases | ForEach-Object { $_.method + '|' + $_.name } | Sort-Object -CaseSensitive)
        if ($null -ne $universe -and ($universe -join "`n") -cne ($names -join "`n")) { throw 'Package partitions discovered different test universes.' }
        $universe = $names
        $expected = @($cases | Where-Object { (Get-SharpProofPackagePartition -Method $_.method) -eq $receipt.index })
        $expectedNames = @($expected | ForEach-Object { $_.name } | Sort-Object -CaseSensitive)
        if (($expectedNames -join "`n") -cne (@($receipt.selected | Sort-Object -CaseSensitive) -join "`n")) { throw 'Package partition assignments differ from the versioned duration plan and stable fallback.' }
        Assert-SharpProofPackagePartitionReceipt -Root $root -Expected $expected
        foreach ($case in $expected) { $assigned.Add($case.method + '|' + $case.name) }
    }
    if (($universe -join "`n") -cne (@($assigned.ToImmutable() | Sort-Object -CaseSensitive) -join "`n")) { throw 'Package partition union differs from unpartitioned discovery.' }
    Write-Host "Package partitions cover all $($universe.Count) discovered cases exactly once."
}
