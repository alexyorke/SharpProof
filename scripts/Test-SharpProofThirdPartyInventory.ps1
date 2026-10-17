[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,

    [string]$InventoryPath = (Join-Path $PSScriptRoot '../eng/release/third-party-components.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force

function Assert-SafeInventoryPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '(^/|\\|:|[\x00-\x1f])' -or
        @($Path.Split('/') | Where-Object { $_ -cin @('', '.', '..') }).Count -gt 0) {
        throw "Unsafe third-party inventory path: '$Path'."
    }
}

$inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json -AsHashtable
if ($inventory.schemaVersion -ne 1 -or $inventory.packages -isnot [System.Collections.IDictionary]) {
    throw 'Unsupported third-party inventory schema.'
}
$expectedIds = @($SharpProofPackageIds | Sort-Object -CaseSensitive)
$actualIds = @($inventory.packages.Keys | Sort-Object -CaseSensitive)
if (($actualIds -join '|') -cne ($expectedIds -join '|')) {
    throw 'Third-party inventory must declare exactly the three SharpProof package IDs.'
}
if (@($inventory.packages['SharpProof']).Count -ne 0) {
    throw 'The metadata-only SharpProof facade must have an empty third-party inventory.'
}
$packages = @(Get-ChildItem -LiteralPath $PackageSource -File -Filter '*.nupkg')
if ($packages.Count -ne 3) { throw 'Third-party validation requires exactly three nupkg files.' }
$identities = @($packages | ForEach-Object {
    Get-SharpProofPackageIdentity -Path $_.FullName -RequireSingleIdentity
})
$packageIds = @($identities.Id | Sort-Object -CaseSensitive)
if (($packageIds -join '|') -cne ($expectedIds -join '|') -or
    @($identities.Version | Sort-Object -Unique -CaseSensitive).Count -ne 1) {
    throw 'Third-party validation requires the exact three-package identity set at one version.'
}

$componentCount = 0
foreach ($identity in $identities) {
    $archive = [IO.Compression.ZipFile]::OpenRead($identity.Path)
    try {
        $archiveEntries = [Collections.Generic.Dictionary[string, IO.Compression.ZipArchiveEntry]]::new([StringComparer]::Ordinal)
        $binaries = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $archive.Entries) {
            if (-not $archiveEntries.TryAdd($entry.FullName, $entry)) {
                throw "Package '$($identity.Id)' has duplicate ZIP entry '$($entry.FullName)'."
            }
            $leaf = ($entry.FullName -split '/')[-1]
            if ([IO.Path]::GetExtension($leaf) -in @('.dll', '.so', '.dylib') -and
                -not $leaf.StartsWith('SharpProof.', [StringComparison]::OrdinalIgnoreCase)) {
                $null = $binaries.Add($entry.FullName)
            }
        }
        $declared = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $pinned = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($component in @($inventory.packages[$identity.Id])) {
            foreach ($field in @('id', 'version', 'license')) {
                if (-not $component.Contains($field) -or [string]::IsNullOrWhiteSpace([string]$component[$field])) {
                    throw "Package '$($identity.Id)' has a component without $field."
                }
            }
            $componentCount++
            $entries = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            if (-not $component.Contains('entries') -or @($component.entries).Count -eq 0) {
                throw "Component '$($component.id)' must declare nonempty entries."
            }
            foreach ($path in @($component.entries)) {
                Assert-SafeInventoryPath $path
                if (-not $entries.Add($path)) { throw "Component '$($component.id)' repeats entry '$path'." }
                if (-not $archiveEntries.ContainsKey($path)) {
                    throw "Component '$($component.id)' declares missing package entry '$path'."
                }
                # Multiple components may be statically incorporated in one DLL.
                $null = $declared.Add($path)
            }
            if ($component.Contains('entrySha256')) {
                $componentPins = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($pin in @($component.entrySha256)) {
                    $path = [string]$pin.path
                    if (-not $entries.Contains($path) -or -not $componentPins.Add($path) -or
                        [string]$pin.sha256 -cnotmatch '^[0-9a-f]{64}$') {
                        throw "Component '$($component.id)' has an invalid or foreign entry digest '$path'."
                    }
                    $stream = $archiveEntries[$path].Open()
                    try {
                        $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
                    }
                    finally { $stream.Dispose() }
                    if ($actual -cne [string]$pin.sha256) { throw "Third-party entry SHA-256 mismatch: '$path'." }
                    $null = $pinned.Add($path)
                }
            }
        }
        if (-not $declared.SetEquals($binaries)) {
            $unlisted = @($binaries | Where-Object { -not $declared.Contains($_) }) -join ', '
            $extra = @($declared | Where-Object { -not $binaries.Contains($_) }) -join ', '
            throw "Third-party binary inventory mismatch for '$($identity.Id)'; unlisted: [$unlisted]; non-binary declarations: [$extra]."
        }
        foreach ($path in $binaries) {
            $leaf = ($path -split '/')[-1]
            if ($leaf -cin @('libz3.dll', 'libz3.so', 'libz3.dylib') -and -not $pinned.Contains($path)) {
                throw "Native Z3 payload lacks a declared SHA-256 pin: '$path'."
            }
        }
    }
    finally { $archive.Dispose() }
}
Write-Host "Third-party inventory verified: three packages, $componentCount components, exact binary coverage and declared digests."
