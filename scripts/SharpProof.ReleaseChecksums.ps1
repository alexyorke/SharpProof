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

function Test-SharpProofExactRegularFileSet {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string[]]$ExpectedFileNames,
        [Parameter(Mandatory = $true)][string]$Owner
    )

    $expected = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $expectedCaseInsensitive = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $ExpectedFileNames) {
        if ([string]::IsNullOrWhiteSpace($name) -or
            $name -in @('.', '..') -or
            $name.Contains('/') -or $name.Contains('\') -or
            -not $expected.Add($name) -or
            -not $expectedCaseInsensitive.Add($name)) {
            throw "$Owner contains a duplicate or invalid expected file name."
        }
    }
    $entries = @(Get-ChildItem -LiteralPath $Directory -Force -Recurse)
    if ($entries.Count -ne $expected.Count) {
        throw "$Owner has an unexpected file or directory count."
    }
    $actual = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $identities = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($entry in $entries) {
        if ($entry.PSIsContainer -or
            (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) -or
            $entry.DirectoryName -cne [IO.Path]::GetFullPath($Directory)) {
            throw "$Owner contains a directory, nested entry, or filesystem alias."
        }
        if (-not $actual.Add($entry.Name)) {
            throw "$Owner contains a duplicate file name."
        }
        $modeText = (& stat -Lc '%f' -- $entry.FullName).Trim()
        $mode = 0
        $isRegular = $LASTEXITCODE -eq 0 -and
            $modeText -match '^[0-9a-fA-F]+$'
        if ($isRegular) {
            try {
                $mode = [Convert]::ToInt32($modeText, 16)
            }
            catch {
                $isRegular = $false
            }
        }
        if (-not $isRegular -or ($mode -band 0xF000) -ne 0x8000) {
            throw "$Owner contains a non-regular file: '$($entry.Name)'."
        }
        $identity = (& stat -Lc '%d:%i' -- $entry.FullName).Trim()
        if ($LASTEXITCODE -ne 0 -or
            $identity -notmatch '^[0-9]+:[0-9]+$' -or
            -not $identities.Add($identity)) {
            throw "$Owner contains an unstable or aliased file identity."
        }
    }
    if (-not $actual.SetEquals($expected)) {
        throw "$Owner does not contain the exact file set."
    }
}

function Test-SharpProofReleasePackageInput {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string[]]$PackageNames,
        [switch]$AllowGeneratedEvidence
    )

    $expectedNames = @($PackageNames)
    if ($AllowGeneratedEvidence) {
        $expectedNames += @(
            'SharpProof.release.json',
            'SharpProof.spdx.json',
            'SHA256SUMS')
    }
    Test-SharpProofExactRegularFileSet `
        -Directory $Directory `
        -ExpectedFileNames $expectedNames `
        -Owner 'Release package input'
}

function Copy-SharpProofReleaseSbom {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$StagingDirectory
    )

    if (-not [IO.File]::Exists($SourcePath)) {
        throw "SBOM source is not a file: $SourcePath"
    }
    $destination = Join-Path $StagingDirectory 'SharpProof.spdx.json'
    [IO.File]::Copy($SourcePath, $destination, $false)
    return $destination
}

function Test-SharpProofReleaseBundleTopology {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()][object[]]$Artifacts,
        [Parameter(Mandatory = $true)][string]$Owner
    )

    if (-not [IO.Directory]::Exists($Directory) -or
        $Artifacts.Count -ne 7 -or
        @($Artifacts | Where-Object { [string]$_.kind -ceq 'package' }).Count -ne 3 -or
        @($Artifacts | Where-Object { [string]$_.kind -ceq 'symbols' }).Count -ne 3 -or
        @($Artifacts | Where-Object {
            [string]$_.kind -ceq 'sbom' -and
            [string]$_.fileName -ceq 'SharpProof.spdx.json'
        }).Count -ne 1) {
        throw "$Owner must contain one exact seven-artifact release bundle."
    }
    $expected = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $expectedCaseInsensitive = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @('SharpProof.release.json', 'SHA256SUMS')) {
        $null = $expected.Add($name)
        $null = $expectedCaseInsensitive.Add($name)
    }
    foreach ($artifact in $Artifacts) {
        $name = [string]$artifact.fileName
        if ([string]::IsNullOrWhiteSpace($name) -or
            $name -in @('.', '..') -or
            $name.Contains('/') -or $name.Contains('\') -or
            -not $expected.Add($name) -or
            -not $expectedCaseInsensitive.Add($name)) {
            throw "$Owner contains a duplicate or invalid artifact name."
        }
    }
    if ($expected.Count -ne 9) {
        throw "$Owner does not describe exactly nine unique files."
    }

    Test-SharpProofExactRegularFileSet `
        -Directory $Directory `
        -ExpectedFileNames @($expected) `
        -Owner $Owner
}

function Publish-SharpProofReleaseBundleAtomically {
    param(
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory,
        [Parameter(Mandatory = $true)][object[]]$Artifacts,
        [Parameter(Mandatory = $true)][string]$Owner
    )

    $staging = [IO.Path]::GetFullPath($StagingDirectory)
    $destination = [IO.Path]::GetFullPath($DestinationDirectory)
    if ([IO.Path]::GetDirectoryName($staging) -cne
            [IO.Path]::GetDirectoryName($destination) -or
        $staging -ceq $destination -or
        -not [IO.Directory]::Exists($destination)) {
        throw "$Owner staging and destination topology is invalid."
    }
    try {
        Test-SharpProofReleaseBundleTopology `
            -Directory $staging -Artifacts $Artifacts -Owner "$Owner staging"
    }
    catch {
        if ([IO.Directory]::Exists($staging)) {
            [IO.Directory]::Delete($staging, $true)
        }
        throw
    }
    $backup = Join-Path ([IO.Path]::GetDirectoryName($destination)) (
        '.' + [IO.Path]::GetFileName($destination) + '.' +
        [Guid]::NewGuid().ToString('N') + '.backup')
    [IO.Directory]::Move($destination, $backup)
    try {
        [IO.Directory]::Move($staging, $destination)
        Test-SharpProofReleaseBundleTopology `
            -Directory $destination -Artifacts $Artifacts -Owner $Owner
        [IO.Directory]::Delete($backup, $true)
    }
    catch {
        if ([IO.Directory]::Exists($destination)) {
            [IO.Directory]::Delete($destination, $true)
        }
        if ([IO.Directory]::Exists($backup)) {
            [IO.Directory]::Move($backup, $destination)
        }
        throw
    }
}
