[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PrivatePreviewImage.psm1') -Force
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-image-regression-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$sourceCommit = 'a' * 40
$imageReference = 'ghcr.io/alexyorke/sharpproof-preview:sha-' + $sourceCommit
$manifestType = 'application/vnd.oci.image.manifest.v1+json'
$indexType = 'application/vnd.oci.image.index.v1+json'
$script:controls = 0

function Get-FixtureJsonBytes($Value) {
    return , [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $Value -Depth 20 -Compress))
}
function Get-FixtureDigest([byte[]]$Bytes) {
    return 'sha256:' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}
function Get-FixtureDescriptor([string]$Type, [byte[]]$Bytes) {
    return @{ mediaType = $Type; digest = Get-FixtureDigest $Bytes; size = $Bytes.Length }
}
function Write-FixtureArchive($Fixture) {
    $stream = [IO.File]::Create($Fixture.Archive)
    $writer = [Formats.Tar.TarWriter]::new($stream, $false)
    try {
        foreach ($entry in $Fixture.Entries.GetEnumerator()) {
            $tarEntry = [Formats.Tar.PaxTarEntry]::new([Formats.Tar.TarEntryType]::RegularFile, $entry.Key)
            $data = [IO.MemoryStream]::new([byte[]]$entry.Value, $false)
            try { $tarEntry.DataStream = $data; $writer.WriteEntry($tarEntry) }
            finally { $data.Dispose() }
        }
        foreach ($extra in $Fixture.Extra) {
            $tarEntry = [Formats.Tar.PaxTarEntry]::new($extra.Type, $extra.Name)
            if ($extra.Type -eq [Formats.Tar.TarEntryType]::SymbolicLink) {
                $tarEntry.LinkName = '../escape'
                $writer.WriteEntry($tarEntry)
            }
            else {
                $data = [IO.MemoryStream]::new([byte[]]$extra.Bytes, $false)
                try { $tarEntry.DataStream = $data; $writer.WriteEntry($tarEntry) }
                finally { $data.Dispose() }
            }
        }
    }
    finally { $writer.Dispose() }
    [IO.File]::WriteAllBytes($Fixture.Metadata, (Get-FixtureJsonBytes $Fixture.Build))
}
function New-ImageFixture([scriptblock]$ConfigChange, [scriptblock]$ManifestChange, [switch]$Indexed, [string]$InvalidLabel) {
    $directory = Join-Path $scratch ([Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $plainLayer = [Text.Encoding]::UTF8.GetBytes('fixture-original-layer')
    $compressed = [IO.MemoryStream]::new()
    $gzip = [IO.Compression.GZipStream]::new($compressed, [IO.Compression.CompressionMode]::Compress, $true)
    try { $gzip.Write($plainLayer, 0, $plainLayer.Length) }
    finally { $gzip.Dispose() }
    $layer = $compressed.ToArray()
    $compressed.Dispose()
    $config = @{ os = 'linux'; architecture = 'amd64'; config = @{ Labels = @{
                'org.opencontainers.image.source'   = 'https://github.com/alexyorke/SharpProof-preview'
                'org.opencontainers.image.revision' = $sourceCommit
                'io.sharpproof.consumer.version' = '1.0.0-preview.1'; 'io.sharpproof.platform' = 'linux/amd64'
            }
        }; rootfs = @{ type = 'layers'; diff_ids = @((Get-FixtureDigest $plainLayer)) }
    }
    if (-not [string]::IsNullOrEmpty($InvalidLabel)) { $config.config.Labels[$InvalidLabel] = 'invalid' }
    if ($null -ne $ConfigChange) { & $ConfigChange $config }
    $configBytes = Get-FixtureJsonBytes $config
    $configDigest = Get-FixtureDigest $configBytes
    $manifest = @{ schemaVersion = 2; mediaType = $manifestType
        config = Get-FixtureDescriptor 'application/vnd.oci.image.config.v1+json' $configBytes
        layers = @((Get-FixtureDescriptor 'application/vnd.oci.image.layer.v1.tar+gzip' $layer))
    }
    if ($null -ne $ManifestChange) { & $ManifestChange $manifest }
    $manifestBytes = Get-FixtureJsonBytes $manifest
    $rootBytes = $manifestBytes
    $rootType = $manifestType
    if ($Indexed) {
        $rootBytes = Get-FixtureJsonBytes @{ schemaVersion = 2; mediaType = $indexType
            manifests = @((Get-FixtureDescriptor $manifestType $manifestBytes))
        }
        $rootType = $indexType
    }
    $expected = Get-FixtureDigest $rootBytes
    $entries = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $entries.Add('oci-layout', (Get-FixtureJsonBytes @{ imageLayoutVersion = '1.0.0' }))
    $entries.Add('index.json', (Get-FixtureJsonBytes @{ schemaVersion = 2; manifests = @((Get-FixtureDescriptor $rootType $rootBytes)) }))
    $entries.Add('manifest.json', (Get-FixtureJsonBytes @(@{ Config = 'blobs/sha256/' + $configDigest.Substring(7)
                    RepoTags = @($imageReference); Layers = @('blobs/sha256/' + (Get-FixtureDigest $layer).Substring(7))
                })))
    foreach ($bytes in @($configBytes, $layer, $manifestBytes, $rootBytes)) {
        $entries['blobs/sha256/' + (Get-FixtureDigest $bytes).Substring(7)] = $bytes
    }
    $fixture = [pscustomobject]@{ Archive = Join-Path $directory 'consumer image.tar'; Metadata = Join-Path $directory 'build metadata.json'
        Expected = $expected; ConfigDigest = $configDigest; Entries = $entries; Reference = $imageReference
        Build     = @{ 'containerimage.digest' = $expected; 'containerimage.config.digest' = $configDigest }
        LayerName = 'blobs/sha256/' + (Get-FixtureDigest $layer).Substring(7)
        Inspect   = @(@{ Id = $configDigest; Os = 'linux'; Architecture = 'amd64'; Config = $config.config
                RootFS = @{ Layers = $config.rootfs.diff_ids }; RepoDigests = @()
            })
        Calls = [Collections.Immutable.ImmutableList[object]]::Empty; Extra = [Collections.Immutable.ImmutableList[object]]::Empty
    }
    Write-FixtureArchive $fixture
    return $fixture
}
function Invoke-ImageFixture($Fixture, [switch]$Load) {
    $inspectBytes = Get-FixtureJsonBytes $Fixture.Inspect
    $mock = {
        param([string[]]$CommandArguments)
        $Fixture.Calls = $Fixture.Calls.Add([Collections.Immutable.ImmutableArray]::CreateRange[string]($CommandArguments))
        if ($CommandArguments[1] -ceq 'load') { return , [Text.Encoding]::UTF8.GetBytes('Loaded fixture') }
        return , $inspectBytes
    }.GetNewClosure()
    return Get-SharpProofPrivatePreviewImageReceipt -ArchivePath $Fixture.Archive -BuildMetadataPath $Fixture.Metadata `
        -ExpectedDigest $Fixture.Expected -ExpectedSourceCommit $sourceCommit -ImageReference $Fixture.Reference -Load:$Load -DockerRunner $mock
}
function Assert-ImageRejection($Fixture, [string]$Expected, [switch]$AfterLoad) {
    $rejected = $false
    try { Invoke-ImageFixture $Fixture -Load | Out-Null }
    catch { if (-not $_.Exception.Message.Contains($Expected)) { throw }; $rejected = $true }
    if (-not $rejected) { throw "OCI fixture accepted invalid input: $Expected" }
    if (-not $AfterLoad -and $Fixture.Calls.Count -ne 0) { throw 'Invalid archive reached Docker mutation.' }
}
function Test-ImageControl([string]$ControlName, [scriptblock]$Action) {
    try { & $Action }
    catch { throw "Private OCI control failed ($ControlName): $($_.Exception.Message)" }
    $script:controls++
}

try {
    Test-ImageControl 'original identities and receipt' {
        $fixture = New-ImageFixture
        $before = [IO.File]::ReadAllBytes($fixture.Archive)
        $receipt = Invoke-ImageFixture $fixture
        if ($receipt.archiveSha256 -cne (Get-FixtureDigest $before).Substring(7) -or $receipt.bytes -ne $before.Length -or
            $receipt.registryDigest -cne $fixture.Expected -or $receipt.imageConfigDigest -cne $fixture.ConfigDigest -or
            $receipt.imageId -cne $fixture.ConfigDigest -or $receipt.sourceCommit -cne $sourceCommit -or
            $receipt.imageReference -cne $imageReference -or $fixture.Calls.Count -ne 1 -or
            $fixture.Calls[0][1] -cne 'inspect') { throw 'Image receipt lost original archive/config/source identities.' }
    }
    Test-ImageControl 'load preserves archive bytes and spaced argument' {
        $fixture = New-ImageFixture
        $before = Get-FileHash -LiteralPath $fixture.Archive -Algorithm SHA256
        $null = Invoke-ImageFixture $fixture -Load
        if ($fixture.Calls.Count -ne 2 -or $fixture.Calls[0][1] -cne 'load' -or
            $fixture.Calls[0][3] -cne $fixture.Archive -or $fixture.Calls[1][1] -cne 'inspect' -or
            (Get-FileHash -LiteralPath $fixture.Archive -Algorithm SHA256).Hash -cne $before.Hash) { throw 'Image load repacked or selected another archive.' }
    }
    Test-ImageControl 'single original image index' {
        $fixture = New-ImageFixture -Indexed
        if ((Invoke-ImageFixture $fixture).registryDigest -cne $fixture.Expected) { throw 'Original image index digest was lost.' }
    }
    foreach ($name in @('containerimage.digest', 'containerimage.config.digest')) {
        Test-ImageControl "metadata $name" {
            $fixture = New-ImageFixture
            $fixture.Build[$name] = 'sha256:' + ('f' * 64)
            Write-FixtureArchive $fixture
            Assert-ImageRejection $fixture 'metadata'
        }
    }
    foreach ($part in @('manifest', 'config', 'compressed layer')) {
        Test-ImageControl "changed $part blob bytes" {
            $fixture = New-ImageFixture
            $blobName = switch ($part) {
                'manifest' { 'blobs/sha256/' + $fixture.Expected.Substring(7) }
                'config' { 'blobs/sha256/' + $fixture.ConfigDigest.Substring(7) }
                'compressed layer' { $fixture.LayerName }
            }
            $fixture.Entries[$blobName] = [byte[]]@($fixture.Entries[$blobName]) + [byte[]]@(32)
            Write-FixtureArchive $fixture
            Assert-ImageRejection $fixture 'blob bytes'
        }
    }
    Test-ImageControl 'missing original layer' {
        $fixture = New-ImageFixture
        $null = $fixture.Entries.Remove($fixture.LayerName)
        Write-FixtureArchive $fixture
        Assert-ImageRejection $fixture 'blob is missing'
    }
    Test-ImageControl 'compatibility-only archive' {
        $fixture = New-ImageFixture
        foreach ($name in @($fixture.Entries.Keys)) { if ($name -cne 'manifest.json') { $null = $fixture.Entries.Remove($name) } }
        Write-FixtureArchive $fixture
        Assert-ImageRejection $fixture 'original OCI image layout'
    }
    Test-ImageControl 'ambiguous index' {
        $fixture = New-ImageFixture
        $index = [Text.Encoding]::UTF8.GetString($fixture.Entries['index.json']) | ConvertFrom-Json -AsHashtable
        $index.manifests = @($index.manifests[0], $index.manifests[0])
        $fixture.Entries['index.json'] = Get-FixtureJsonBytes $index
        Write-FixtureArchive $fixture
        Assert-ImageRejection $fixture 'exactly one original OCI'
    }
    Test-ImageControl 'external descriptor URLs' {
        $fixture = New-ImageFixture -ManifestChange { param($manifest) $manifest.layers[0]['urls'] = @('https://example.invalid/layer') }
        Assert-ImageRejection $fixture 'External OCI'
    }
    Test-ImageControl 'descriptor sizes' {
        $fixture = New-ImageFixture -ManifestChange { param($manifest) $manifest.layers[0].size = 1 }
        Assert-ImageRejection $fixture 'descriptor size'
    }
    foreach ($label in @('org.opencontainers.image.source', 'org.opencontainers.image.revision', 'io.sharpproof.consumer.version')) {
        Test-ImageControl "bound $label" {
            $fixture = New-ImageFixture -InvalidLabel $label
            Assert-ImageRejection $fixture 'source/version/platform labels'
        }
    }
    Test-ImageControl 'linux amd64' {
        $fixture = New-ImageFixture -ConfigChange { param($config) $config.architecture = 'arm64' }
        Assert-ImageRejection $fixture 'linux/amd64'
    }
    foreach ($kind in @('traversal', 'symlink', 'duplicate')) {
        Test-ImageControl "tar $kind" {
            $fixture = New-ImageFixture
            $extra = switch ($kind) {
                'traversal' { @{ Name = '../escape'; Type = [Formats.Tar.TarEntryType]::RegularFile; Bytes = [byte[]]@(1) } }
                'symlink' { @{ Name = 'unsafe-link'; Type = [Formats.Tar.TarEntryType]::SymbolicLink; Bytes = $null } }
                'duplicate' { $blobName = 'blobs/sha256/' + $fixture.ConfigDigest.Substring(7); @{ Name = $blobName; Type = [Formats.Tar.TarEntryType]::RegularFile; Bytes = $fixture.Entries[$blobName] } }
            }
            $fixture.Extra = $fixture.Extra.Add($extra)
            Write-FixtureArchive $fixture
            $expected = switch ($kind) { 'traversal' { 'Unsafe image' }; 'symlink' { 'links and special' }; 'duplicate' { 'Duplicate image' } }
            Assert-ImageRejection $fixture $expected
        }
    }
    foreach ($part in @('config ID', 'rootfs', 'labels')) {
        Test-ImageControl "loaded $part" {
            $fixture = New-ImageFixture
            switch ($part) {
                'config ID' { $fixture.Inspect[0].Id = 'sha256:' + ('f' * 64) }
                'rootfs' { $fixture.Inspect[0].RootFS.Layers = @('sha256:' + ('f' * 64)) }
                'labels' { $fixture.Inspect[0].Config.Labels['org.opencontainers.image.revision'] = 'b' * 40 }
            }
            $expected = switch ($part) { 'config ID' { 'image ID differs' }; 'rootfs' { 'image layers differ' }; 'labels' { 'source/version/platform labels' } }
            Assert-ImageRejection $fixture $expected -AfterLoad
        }
    }
    Test-ImageControl 'compatibility tag binding' {
        $fixture = New-ImageFixture
        $compatibility = [Text.Encoding]::UTF8.GetString($fixture.Entries['manifest.json']) | ConvertFrom-Json -AsHashtable -NoEnumerate
        $compatibility[0].RepoTags = @('ghcr.io/alexyorke/sharpproof-preview:latest')
        $fixture.Entries['manifest.json'] = Get-FixtureJsonBytes $compatibility
        Write-FixtureArchive $fixture
        Assert-ImageRejection $fixture 'compatibility manifest'
    }
    Test-ImageControl 'exact private source alias' {
        $fixture = New-ImageFixture
        $fixture.Reference = 'ghcr.io/alexyorke/sharpproof-preview:latest'
        Assert-ImageRejection $fixture 'exact private source alias'
    }
    Test-ImageControl 'duplicate JSON authority' {
        $fixture = New-ImageFixture
        [IO.File]::WriteAllText($fixture.Metadata, '{"containerimage.digest":"x","containerimage.digest":"y"}')
        Assert-ImageRejection $fixture 'Duplicate JSON'
    }
    Write-Host "Private OCI archive regressions passed: $script:controls controls. Original OCI byte/hash fixtures and injected Docker calls only; no real load, registry or publication."
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $resolved.StartsWith($temporaryParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($resolved) -cnotmatch '\Asharpproof-image-regression-[a-f0-9]{32}\z') { throw 'Refusing unsafe OCI fixture cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
