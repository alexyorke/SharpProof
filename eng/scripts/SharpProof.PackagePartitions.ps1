Set-StrictMode -Version Latest

function Get-SharpProofPackagePartitionFilters {
    param([object[]]$Cases, [int]$Partition, [int]$Count)
    # Give each filter, largest first, to the partition with the fewest cases
    # of its class, so every partition gets an even share of each class,
    # including the heavy package-layout cases.
    $classLoad = @{}
    $load = [int[]]::new($Count)
    $selected = [Collections.Generic.List[string]]::new()
    $units = @($Cases | Group-Object { $_.filter } -CaseSensitive | ForEach-Object {
            [pscustomobject]@{ filter = $_.Name; size = $_.Count; class = $_.Group[0].method -replace '\.[^.]+$', '' }
        } | Sort-Object class, @{ Expression = 'size'; Descending = $true }, filter -CaseSensitive)
    foreach ($unit in $units) {
        if (-not $classLoad.ContainsKey($unit.class)) { $classLoad[$unit.class] = [int[]]::new($Count) }
        $target = 0
        for ($index = 1; $index -lt $Count; $index++) {
            $better = $classLoad[$unit.class][$index] -lt $classLoad[$unit.class][$target] -or
            ($classLoad[$unit.class][$index] -eq $classLoad[$unit.class][$target] -and $load[$index] -lt $load[$target])
            if ($better) { $target = $index }
        }
        $classLoad[$unit.class][$target] += $unit.size
        $load[$target] += $unit.size
        if ($target -eq $Partition) { $selected.Add($unit.filter) }
    }
    return $selected.ToArray()
}

function Read-SharpProofPackageDiscovery {
    param([string]$Dump, [string]$Filter = '')
    [xml]$document = Get-Content -LiteralPath $Dump -Raw
    $found = [Collections.Generic.List[object]]::new()
    foreach ($test in $document.SelectNodes('//*[local-name()="test-case"]')) {
        $categories = @($test.SelectNodes('ancestor-or-self::*/*[local-name()="properties"]/*[local-name()="property"][@name="Category"]') | ForEach-Object { [string]$_.value })
        if ($Filter -ne '' -and @($categories | Where-Object { $_ -in @('Performance', 'Coverage', 'Corpus') }).Count -gt 0) { continue }
        $method = [string]$test.classname + '.' + [string]$test.methodname
        if ($method -notmatch '^SharpProof\.[A-Za-z_][A-Za-z0-9_.+`]*\.[A-Za-z_][A-Za-z0-9_]*$' -or [string]::IsNullOrWhiteSpace([string]$test.fullname)) {
            throw 'Package discovery has an invalid method or case name.'
        }
        $name = [string]$test.fullname
        $found.Add([pscustomobject]@{ method = $method; name = $name; renamed = $name -ne $method -and $name -match '^SharpProof\.[A-Za-z_][A-Za-z0-9_.+`]*$' })
    }
    # Cases renamed with TestName= are partitioned one by one. NUnit matches
    # FullyQualifiedName~Method against the method's suite and runs all of its
    # cases, renamed ones included, so the other cases of such a method get
    # exact filters too; every other method's cases stay together.
    $split = [Collections.Generic.HashSet[string]]::new([string[]]@($found | Where-Object renamed | ForEach-Object method), [StringComparer]::Ordinal)
    $cases = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($case in $found) {
        $filter = if ($case.renamed) {
            'FullyQualifiedName=' + $case.name
        }
        elseif ($split.Contains($case.method)) {
            if ($case.name -notmatch '^SharpProof\.[A-Za-z0-9_.+`]*\([A-Za-z0-9_.,\- ]*\)$') {
                throw "Package case $($case.name) needs a TestName to be partitioned with its renamed siblings."
            }
            'FullyQualifiedName=' + ($case.name -replace '([()])', '\$1')
        }
        else {
            'FullyQualifiedName~' + $case.method
        }
        $cases.Add(([ordered]@{ method = $case.method; name = $case.name; filter = $filter }).AsReadOnly())
    }
    if ($cases.Count -eq 0) { throw 'Package partition discovery is empty.' }
    return $cases.ToImmutable()
}
