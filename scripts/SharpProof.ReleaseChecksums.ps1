Set-StrictMode -Version Latest

function Get-SharpProofReleaseChecksumBytes {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Artifacts
    )

    if ($Artifacts.Count -eq 0) {
        throw 'Release checksums require at least one artifact.'
    }
    $rows = [Collections.Generic.List[string]]::new()
    $names = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($artifact in $Artifacts) {
        $fileName = [string]$artifact.fileName
        $sha256 = [string]$artifact.sha256
        if ([string]::IsNullOrWhiteSpace($fileName) -or
            $fileName -match '[\r\n]' -or
            -not $names.Add($fileName) -or
            $sha256 -cnotmatch '^[0-9a-f]{64}$') {
            throw "Release checksum metadata is invalid: '$fileName'."
        }
        $rows.Add($sha256 + '  ' + $fileName)
    }
    $orderedNames = [string[]]@($names)
    [Array]::Sort($orderedNames, [StringComparer]::Ordinal)
    $actualNames = [string[]]@($Artifacts | ForEach-Object {
        [string]$_.fileName
    })
    $canonicalOrder = $actualNames.Length -eq $orderedNames.Length
    for ($index = 0; $canonicalOrder -and
        $index -lt $actualNames.Length; $index++) {
        $canonicalOrder = $actualNames[$index] -ceq $orderedNames[$index]
    }
    if (-not $canonicalOrder) {
        throw 'Release checksum artifacts are not in canonical ordinal order.'
    }
    $text = ($rows -join "`n") + "`n"
    return [Text.UTF8Encoding]::new($false, $true).GetBytes($text)
}

function Test-SharpProofReleaseChecksumFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Artifacts,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    if (-not [IO.File]::Exists($Path)) {
        throw "$Owner is missing: $Path"
    }
    $expected = Get-SharpProofReleaseChecksumBytes -Artifacts $Artifacts
    $actual = [IO.File]::ReadAllBytes($Path)
    if ($actual.Length -ne $expected.Length -or
        [Convert]::ToBase64String($actual) -cne
            [Convert]::ToBase64String($expected)) {
        throw "$Owner bytes are not canonical or do not match the release manifest."
    }
}

function Write-SharpProofReleaseChecksumFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Artifacts
    )

    $bytes = Get-SharpProofReleaseChecksumBytes -Artifacts $Artifacts
    $directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path))
    $leaf = [IO.Path]::GetFileName($Path)
    $temporaryPath = Join-Path $directory (
        '.' + $leaf + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllBytes($temporaryPath, $bytes)
        [IO.File]::Move($temporaryPath, $Path, $true)
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
}
