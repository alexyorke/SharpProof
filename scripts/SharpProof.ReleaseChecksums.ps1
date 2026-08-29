Set-StrictMode -Version Latest

function Convert-SharpProofPackageArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not [IO.File]::Exists($fullPath)) {
        throw "Package archive is not a file: $fullPath"
    }
    $directory = [IO.Path]::GetDirectoryName($fullPath)
    $temporaryPath = Join-Path $directory (
        '.' + [IO.Path]::GetFileName($fullPath) + '.' +
        [Guid]::NewGuid().ToString('N') + '.tmp')
    $fixedTimestamp = [DateTimeOffset]::new(
        1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    $canonicalCoreName =
        'package/services/metadata/core-properties/core-properties.psmdcp'
    try {
        $source = [IO.Compression.ZipFile]::OpenRead($fullPath)
        try {
            $entries = @($source.Entries)
            $coreEntries = @($entries | Where-Object {
                $_.FullName -imatch
                    '^package/services/metadata/core-properties/[^/]+\.psmdcp$'
            })
            if ($coreEntries.Count -gt 1) {
                throw "Package archive contains multiple core-properties entries: $fullPath"
            }
            $coreName = if ($coreEntries.Count -eq 1) {
                [string]$coreEntries[0].FullName
            }
            else {
                $null
            }
            $mappedEntries = @($entries | ForEach-Object {
                [pscustomobject]@{
                    Input = $_
                    OutputName = if ($null -ne $coreName -and
                        [string]$_.FullName -ceq $coreName) {
                        $canonicalCoreName
                    }
                    else {
                        [string]$_.FullName
                    }
                }
            })
            $duplicateNames = @(
                $mappedEntries |
                    Group-Object OutputName |
                    Where-Object Count -ne 1
            )
            if ($duplicateNames.Count -ne 0) {
                throw "Package archive has duplicate canonical entry '$($duplicateNames[0].Name)': $fullPath"
            }
            $outputNames = [string[]]@(
                $mappedEntries | ForEach-Object { [string]$_.OutputName })
            [Array]::Sort($outputNames, [StringComparer]::Ordinal)
            $destination = [IO.Compression.ZipFile]::Open(
                $temporaryPath,
                [IO.Compression.ZipArchiveMode]::Create)
            try {
                foreach ($outputName in $outputNames) {
                    $mapped = @($mappedEntries | Where-Object {
                        [string]$_.OutputName -ceq $outputName
                    })[0]
                    $entry = $destination.CreateEntry(
                        $outputName,
                        [IO.Compression.CompressionLevel]::Optimal)
                    $entry.LastWriteTime = $fixedTimestamp
                    $inputStream = $mapped.Input.Open()
                    try {
                        $memory = [IO.MemoryStream]::new()
                        try {
                            $inputStream.CopyTo($memory)
                            $bytes = $memory.ToArray()
                        }
                        finally {
                            $memory.Dispose()
                        }
                    }
                    finally {
                        $inputStream.Dispose()
                    }
                    $isCore = $null -ne $coreName -and
                        $mapped.Input.FullName -ceq $coreName
                    $isRelationship = $mapped.Input.FullName -imatch (
                        '(^|/)_rels/.*\.rels$') -or
                        $mapped.Input.FullName -ceq '[Content_Types].xml'
                    if ($isCore -or ($isRelationship -and $null -ne $coreName)) {
                        $text = [Text.UTF8Encoding]::new($false, $true).
                            GetString($bytes)
                        $text = $text.Replace(
                            '/' + $coreName,
                            '/' + $canonicalCoreName)
                        $text = $text.Replace($coreName, $canonicalCoreName)
                        if ($isRelationship) {
                            $text = [Text.RegularExpressions.Regex]::Replace(
                                $text,
                                '(?is)(<Relationship\b(?=[^>]*\bType="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties")[^>]*\bId=")[^"]+(")',
                                '${1}rIdCoreProperties${2}')
                        }
                        if ($isCore) {
                            $text = [Text.RegularExpressions.Regex]::Replace(
                                $text,
                                '(<(?:[A-Za-z_][A-Za-z0-9_.-]*:)?(?:created|modified)\b[^>]*>)[^<]*(</(?:[A-Za-z_][A-Za-z0-9_.-]*:)?(?:created|modified)\s*>)',
                                '${1}1980-01-01T00:00:00Z${2}')
                        }
                        $bytes = [Text.UTF8Encoding]::new($false, $true).
                            GetBytes($text)
                    }
                    $outputStream = $entry.Open()
                    try {
                        if ($bytes.Length -ne 0) {
                            $outputStream.Write($bytes, 0, $bytes.Length)
                        }
                    }
                    finally {
                        $outputStream.Dispose()
                    }
                }
            }
            finally {
                $destination.Dispose()
            }
        }
        finally {
            $source.Dispose()
        }
        [IO.File]::Move($temporaryPath, $fullPath, $true)
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
}

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

function Get-SharpProofReleaseBundleBackupCandidates {
    param(
        [Parameter(Mandatory = $true)][string]$DestinationDirectory
    )

    $destination = [IO.Path]::GetFullPath($DestinationDirectory)
    $parent = [IO.Path]::GetDirectoryName($destination)
    if (-not [IO.Directory]::Exists($parent)) {
        return @()
    }
    $leaf = [IO.Path]::GetFileName($destination)
    $prefix = '.' + $leaf + '.'
    $suffix = '.backup'
    return @(
        Get-ChildItem -LiteralPath $parent -Force -Directory |
            Where-Object {
                $_.Name.StartsWith($prefix, [StringComparison]::Ordinal) -and
                $_.Name.EndsWith($suffix, [StringComparison]::Ordinal) -and
                (($null -eq $_.Attributes) -or
                    (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0))
            } |
            Sort-Object Name
    )
}

function Restore-SharpProofReleaseBundleBackup {
    param(
        [Parameter(Mandatory = $true)][string]$DestinationDirectory,
        [Parameter()][AllowNull()][object[]]$Artifacts,
        [Parameter(Mandatory = $true)][string]$Owner
    )

    $destination = [IO.Path]::GetFullPath($DestinationDirectory)
    if ([IO.Directory]::Exists($destination)) {
        return $false
    }
    $candidates = @(Get-SharpProofReleaseBundleBackupCandidates `
        -DestinationDirectory $destination)
    if ($candidates.Count -eq 0) {
        return $false
    }
    if ($candidates.Count -ne 1) {
        throw "$Owner found multiple recoverable release bundle backups."
    }
    $backup = [string]$candidates[0].FullName
    $effectiveArtifacts = @($Artifacts)
    if ($effectiveArtifacts.Count -eq 0) {
        $manifestReader = Get-Command `
            -Name Read-SharpProofCanonicalReleaseJson `
            -CommandType Function `
            -ErrorAction SilentlyContinue
        if ($null -eq $manifestReader) {
            throw "$Owner cannot validate a backup without release manifest support."
        }
        $manifest = Read-SharpProofCanonicalReleaseJson `
            -Path (Join-Path $backup 'SharpProof.release.json') `
            -DocumentType ReleaseManifest
        $effectiveArtifacts = @($manifest.artifacts)
    }
    Test-SharpProofReleaseBundleTopology `
        -Directory $backup `
        -Artifacts $effectiveArtifacts `
        -Owner "$Owner backup"
    [IO.Directory]::Move($backup, $destination)
    return $true
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
        $staging -ceq $destination) {
        throw "$Owner staging and destination topology is invalid."
    }
    $null = Restore-SharpProofReleaseBundleBackup `
        -DestinationDirectory $destination `
        -Artifacts $Artifacts `
        -Owner $Owner
    if (-not [IO.Directory]::Exists($destination)) {
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
