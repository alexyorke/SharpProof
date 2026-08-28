[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseChecksums.ps1')

function Write-SyntheticPackage {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$CoreId,
        [Parameter(Mandatory = $true)][int]$TimestampOffset,
        [Parameter(Mandatory = $true)][string]$Payload
    )

    $archive = [IO.Compression.ZipFile]::Open(
        $Path,
        [IO.Compression.ZipArchiveMode]::Create)
    try {
        $coreName = "package/services/metadata/core-properties/$CoreId.psmdcp"
        $coreXml = @"
<?xml version="1.0" encoding="utf-8"?>
<coreProperties xmlns="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dcterms:created xsi:type="dcterms:W3CDTF">2026-08-28T01:02:03Z</dcterms:created><dcterms:modified xsi:type="dcterms:W3CDTF">2026-08-28T04:05:06Z</dcterms:modified></coreProperties>
"@
        $contents = @{
            'z-last.txt' = $Payload
            'a-first.txt' = 'same-content'
            '[Content_Types].xml' = '<Types><Override PartName="/{0}" ContentType="application/vnd.openxmlformats-package.core-properties+xml" /></Types>' -f $coreName
            '_rels/.rels' = '<Relationships><Relationship Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="/{0}" Id="R{1}" /></Relationships>' -f $coreName, $CoreId.Replace('-', '')
            $coreName = $coreXml
        }
        $names = [string[]]@($contents.Keys)
        [Array]::Reverse($names)
        foreach ($name in $names) {
            $entry = $archive.CreateEntry(
                $name,
                [IO.Compression.CompressionLevel]::Fastest)
            $entry.LastWriteTime = [DateTimeOffset]::UtcNow.AddMinutes(
                $TimestampOffset)
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.Write($contents[$name]) }
            finally { $writer.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

$fixture = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-package-repro-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
try {
    $first = Join-Path $fixture 'first.nupkg'
    $second = Join-Path $fixture 'second.nupkg'
    $different = Join-Path $fixture 'different.nupkg'
    Write-SyntheticPackage -Path $first -CoreId '11111111-1111-1111-1111-111111111111' `
        -TimestampOffset 0 -Payload 'payload'
    Write-SyntheticPackage -Path $second -CoreId '22222222-2222-2222-2222-222222222222' `
        -TimestampOffset 7 -Payload 'payload'
    Write-SyntheticPackage -Path $different -CoreId '33333333-3333-3333-3333-333333333333' `
        -TimestampOffset 7 -Payload 'different-payload'

    Convert-SharpProofPackageArchive -Path $first
    Convert-SharpProofPackageArchive -Path $second
    Convert-SharpProofPackageArchive -Path $different
    $firstHash = (Get-FileHash -LiteralPath $first -Algorithm SHA256).Hash
    $secondHash = (Get-FileHash -LiteralPath $second -Algorithm SHA256).Hash
    $differentHash = (Get-FileHash -LiteralPath $different -Algorithm SHA256).Hash
    if ($firstHash -cne $secondHash -or $firstHash -ceq $differentHash) {
        throw 'Canonical package archives did not converge or lost payload sensitivity.'
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($first)
    try {
        $names = [string[]]@($archive.Entries | ForEach-Object FullName)
        $sortedNames = [string[]]$names.Clone()
        [Array]::Sort($sortedNames, [StringComparer]::Ordinal)
        if (($names -join '|') -cne ($sortedNames -join '|') -or
            $names -notcontains 'package/services/metadata/core-properties/core-properties.psmdcp' -or
            @($names | Where-Object { $_ -match '[0-9a-f]{8}-[0-9a-f]{4}-' }).Count -ne 0) {
            throw 'Canonical package archive metadata was not normalized.'
        }
        foreach ($entry in $archive.Entries) {
            if ($entry.LastWriteTime -ne [DateTimeOffset]::new(
                    1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)) {
                throw "Canonical archive entry has a noncanonical timestamp: $($entry.FullName)"
            }
        }
        $core = $archive.GetEntry(
            'package/services/metadata/core-properties/core-properties.psmdcp')
        $reader = [IO.StreamReader]::new($core.Open())
        try { $coreText = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
        if ($coreText -notmatch '1980-01-01T00:00:00Z') {
            throw 'Core-properties timestamps were not canonicalized.'
        }
    }
    finally { $archive.Dispose() }
    Write-Host 'Package reproducibility fixtures passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
