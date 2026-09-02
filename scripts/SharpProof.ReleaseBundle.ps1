Set-StrictMode -Version Latest

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
    foreach ($name in @('SharpProof.release.json')) {
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
    if ($expected.Count -ne 8) {
        throw "$Owner does not describe exactly eight unique files."
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
