[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Package manifests require the canonical Linux container.'
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packages = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.nupkg' -File | Sort-Object Name)
if ($packages.Count -eq 0) { throw 'No release packages were found.' }
$rows = [Collections.Generic.List[string]]::new()
$inventory = [ordered]@{}
foreach ($package in $packages) {
    $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        # Release archives are unsigned; assembly strong names are separate.
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName
            if (-not $name -or $name.StartsWith('/') -or $name.Contains('\') -or $name.Contains(':') -or
                @($name.Split('/') | Where-Object { $_ -in @('.', '..') }).Count -or -not $names.Add($name)) {
                throw 'The original package has an ambiguous, duplicate, or unsafe ZIP member.'
            }
            if ([IO.Path]::GetFileName($name).Equals('.signature.p7s', [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Original release archives must be unsigned; author-signed archives are not supported by this publication workflow.'
            }
            $content = $entry.Open()
            try { $content.CopyTo([IO.Stream]::Null) }
            finally { $content.Dispose() }
        }
        $specs = @($archive.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' })
        if ($specs.Count -ne 1) { throw 'A package must contain exactly one root nuspec.' }
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $stream = $specs[0].Open()
        $reader = [Xml.XmlReader]::Create($stream, $settings)
        try {
            $document = [Xml.XmlDocument]::new()
            $document.XmlResolver = $null
            $document.Load($reader)
            $metadata = $document.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
            $id = $metadata.SelectSingleNode('*[local-name()="id"]').InnerText.ToLowerInvariant()
            $version = $metadata.SelectSingleNode('*[local-name()="version"]').InnerText.ToLowerInvariant()
            if ($id -notmatch '^[a-z0-9][a-z0-9_.-]*$' -or
                $version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[a-z0-9.-]+)?$' -or
                $package.Name -notmatch '^[a-zA-Z0-9_.-]+\.nupkg$') {
                throw 'Package identity must use a normalized NuGet version and a safe filename.'
            }
            $hash = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA512).Hash.ToLowerInvariant()
            # For unsigned archives NuGet's content hash equals the raw SHA-512.
            $contentHash = [Convert]::ToBase64String([Convert]::FromHexString($hash))
            $rows.Add("$id`t$version`t$($package.Name)`t$hash`t$contentHash")
            if ($inventory.Contains($id)) { throw 'Duplicate package identity in release inventory.' }
            $inventory[$id] = @{ type = 'Direct'; resolved = $version; contentHash = $contentHash }
        }
        finally {
            $reader.Dispose()
            $stream.Dispose()
        }
    }
    finally { $archive.Dispose() }
}
[IO.File]::WriteAllText((Join-Path $packageRoot 'packages.tsv'), ($rows -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$lock = @{ version = 1; dependencies = @{ release = $inventory } } | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText((Join-Path $packageRoot 'packages.lock.json'), $lock + "`n", [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-SharpProofPublishedPackages.sh') -Destination $packageRoot
Write-Output "Recorded identities, original archive SHA-512, and NuGet content hashes for $($packages.Count) unsigned release packages."
