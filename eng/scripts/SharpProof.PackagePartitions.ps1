Set-StrictMode -Version Latest

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
