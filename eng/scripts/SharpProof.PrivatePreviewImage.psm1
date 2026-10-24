Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Formats.Tar
$script:ImageJsonLimit = 4 * 1024 * 1024

function Assert-ImageCondition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-ImageJsonProperties([Text.Json.JsonElement]$Element) {
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            Assert-ImageCondition ($names.Add($property.Name)) 'Duplicate JSON property in image evidence.'
            Assert-ImageJsonProperties $property.Value
        }
    }
    elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-ImageJsonProperties $item }
    }
}

function ConvertFrom-ImageJson([AllowNull()][AllowEmptyCollection()][byte[]]$Bytes) {
    Assert-ImageCondition ($null -ne $Bytes -and $Bytes.Length -le $script:ImageJsonLimit) 'Image JSON evidence exceeds its byte limit.'
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
    $document = [Text.Json.JsonDocument]::Parse($text)
    try { Assert-ImageJsonProperties $document.RootElement }
    finally { $document.Dispose() }
    return , (ConvertFrom-Json -InputObject $text -AsHashtable -Depth 64 -NoEnumerate)
}

function Read-ImageJsonFile([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        Assert-ImageCondition ($stream.Length -le $script:ImageJsonLimit) 'Image JSON evidence exceeds its byte limit.'
        $bytes = [byte[]]::new([int]$stream.Length)
        $stream.ReadExactly($bytes, 0, $bytes.Length)
        return , (ConvertFrom-ImageJson $bytes)
    }
    finally { $stream.Dispose() }
}

function Assert-ImageDigest([AllowNull()][object]$Digest) {
    Assert-ImageCondition ($Digest -is [string] -and $Digest -cmatch '\Asha256:[a-f0-9]{64}\z') 'Image digest must be an exact lowercase SHA256 identity.'
}

function Read-ImageStreamHash([IO.Stream]$Stream, [bool]$Retain) {
    $hasher = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $memory = if ($Retain) { [IO.MemoryStream]::new() } else { $null }
    $buffer = [byte[]]::new(1024 * 1024)
    [long]$length = 0
    try {
        while (($count = $Stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $hasher.AppendData($buffer, 0, $count)
            $length += $count
            if ($Retain) {
                Assert-ImageCondition ($length -le $script:ImageJsonLimit) 'Image JSON evidence exceeds its byte limit.'
                $memory.Write($buffer, 0, $count)
            }
        }
        return ([ordered]@{
                Digest = 'sha256:' + [Convert]::ToHexString($hasher.GetHashAndReset()).ToLowerInvariant()
                Size   = $length
                Data   = if ($Retain) { $memory.ToArray() } else { $null }
            }).AsReadOnly()
    }
    finally {
        $hasher.Dispose()
        if ($null -ne $memory) { $memory.Dispose() }
    }
}

function Read-ImageArchive([string]$Path) {
    $blobBuilder = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $metadataBuilder = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $stream = [IO.File]::OpenRead($Path)
    $reader = [Formats.Tar.TarReader]::new($stream, $false)
    [long]$retainedBytes = 0
    try {
        while ($null -ne ($entry = $reader.GetNextEntry($false))) {
            $rawName = $entry.Name
            $parts = [string[]]@($rawName.Split('/') | Where-Object { $_ -cne '' -and $_ -cne '.' })
            Assert-ImageCondition (-not $rawName.StartsWith('/', [StringComparison]::Ordinal) -and
                -not $rawName.Contains('\') -and $parts -cnotcontains '..') 'Unsafe image archive member.'
            $name = $parts -join '/'
            Assert-ImageCondition ($seen.Count -lt 8192 -and $seen.Add($name)) 'Duplicate image archive member or archive entry limit exceeded.'
            if ($entry.EntryType -eq [Formats.Tar.TarEntryType]::Directory) { continue }
            Assert-ImageCondition ($entry.EntryType -in @([Formats.Tar.TarEntryType]::RegularFile,
                    [Formats.Tar.TarEntryType]::V7RegularFile)) 'Image archive links and special members are forbidden.'
            $isBlob = $name.StartsWith('blobs/', [StringComparison]::Ordinal)
            if ($isBlob) {
                Assert-ImageCondition ($name -cmatch '\Ablobs/sha256/[a-f0-9]{64}\z') 'Unsupported image blob identity.'
            }
            $isMetadata = $name -cin @('index.json', 'oci-layout', 'manifest.json')
            $retain = $isMetadata -or ($isBlob -and $entry.Length -le $script:ImageJsonLimit)
            Assert-ImageCondition ($null -ne $entry.DataStream) 'Image archive member is unreadable.'
            $hash = Read-ImageStreamHash $entry.DataStream $retain
            Assert-ImageCondition ($hash.Size -eq $entry.Length) 'Image archive member was truncated.'
            if ($isBlob) {
                Assert-ImageCondition ($hash.Digest -ceq ('sha256:' + $name.Substring('blobs/sha256/'.Length))) 'Image blob bytes do not match their original digest.'
                # Only JSON candidates need retained bytes; compressed layers stay streamed.
                $data = $hash.Data
                if ($null -ne $data -and $data.Length -gt 0 -and $data[0] -notin @(123, 91, 32, 9, 10, 13)) { $data = $null }
                if ($null -ne $data) { $retainedBytes += $data.Length }
                Assert-ImageCondition ($retainedBytes -le 64 * 1024 * 1024) 'Retained image JSON exceeds its aggregate byte limit.'
                $blobBuilder.Add($hash.Digest, ([ordered]@{ Size = $hash.Size; Data = $data }).AsReadOnly())
            }
            elseif ($isMetadata) { $metadataBuilder.Add($name, (ConvertFrom-ImageJson $hash.Data)) }
        }
    }
    finally { $reader.Dispose() }
    Assert-ImageCondition ($metadataBuilder.ContainsKey('oci-layout') -and
        $metadataBuilder['oci-layout'] -is [Collections.IDictionary] -and
        $metadataBuilder['oci-layout']['imageLayoutVersion'] -ceq '1.0.0') 'Docker archive does not retain an original OCI image layout.'
    return ([ordered]@{
            Blobs    = [Collections.ObjectModel.ReadOnlyDictionary[string, object]]::new($blobBuilder)
            Metadata = [Collections.ObjectModel.ReadOnlyDictionary[string, object]]::new($metadataBuilder)
        }).AsReadOnly()
}

function Get-ImageDescriptorBlob($Descriptor, $Blobs, [string[]]$Allowed) {
    Assert-ImageCondition ($Descriptor -is [Collections.IDictionary] -and
        $Descriptor['mediaType'] -cin $Allowed) 'Unsupported OCI descriptor media type.'
    $digest = $Descriptor['digest']
    Assert-ImageDigest $digest
    Assert-ImageCondition ($Blobs.ContainsKey($digest)) 'Original manifest/config/layer blob is missing from the archive.'
    $blob = $Blobs[$digest]
    Assert-ImageCondition (($Descriptor['size'] -is [int] -or $Descriptor['size'] -is [long]) -and
        $Descriptor['size'] -eq $blob.Size) 'OCI descriptor size does not match its original blob.'
    Assert-ImageCondition (-not $Descriptor['urls']) 'External OCI blob references are forbidden.'
    return ([ordered]@{ Digest = $digest; Data = $blob.Data }).AsReadOnly()
}

function Assert-ImageLabels($Config, [string]$SourceCommit) {
    Assert-ImageCondition ($Config -is [Collections.IDictionary] -and $Config['os'] -ceq 'linux' -and
        $Config['architecture'] -ceq 'amd64') 'Consumer image must target linux/amd64.'
    $settings = $Config['config']
    Assert-ImageCondition ($settings -is [Collections.IDictionary] -and
        $settings['Labels'] -is [Collections.IDictionary]) 'Consumer image labels are missing.'
    $labels = $settings['Labels']
    Assert-ImageCondition ($labels['org.opencontainers.image.source'] -ceq 'https://github.com/alexyorke/SharpProof-preview' -and
        $labels['org.opencontainers.image.revision'] -ceq $SourceCommit -and
        $labels['io.sharpproof.consumer.version'] -ceq '1.0.0-preview.1' -and
        $labels['io.sharpproof.platform'] -ceq 'linux/amd64') 'Consumer image source/version/platform labels differ from the private candidate.'
}

function Get-ImageArchiveIdentity([string]$ArchivePath, [string]$BuildMetadataPath,
    [string]$ExpectedDigest, [string]$ExpectedSourceCommit, [string]$ImageReference) {
    Assert-ImageDigest $ExpectedDigest
    Assert-ImageCondition ($ExpectedSourceCommit -cmatch '\A[a-f0-9]{40}\z') 'Expected source commit must be exact.'
    Assert-ImageCondition ($ImageReference -ceq ('ghcr.io/alexyorke/sharpproof-preview:sha-' + $ExpectedSourceCommit)) 'Consumer image reference must be the exact private source alias.'
    $build = Read-ImageJsonFile $BuildMetadataPath
    Assert-ImageCondition ($build -is [Collections.IDictionary] -and
        $build['containerimage.digest'] -ceq $ExpectedDigest) 'Build metadata digest differs from the qualified registry digest.'
    $archive = Read-ImageArchive $ArchivePath
    $index = if ($archive.Metadata.ContainsKey('index.json')) { $archive.Metadata['index.json'] } else { $null }
    Assert-ImageCondition ($index -is [Collections.IDictionary] -and $index['schemaVersion'] -eq 2 -and
        $index['manifests'] -is [array] -and $index['manifests'].Count -eq 1) 'Archive must contain exactly one original OCI image descriptor.'
    $manifestTypes = @('application/vnd.oci.image.manifest.v1+json', 'application/vnd.docker.distribution.manifest.v2+json')
    $indexTypes = @('application/vnd.oci.image.index.v1+json', 'application/vnd.docker.distribution.manifest.list.v2+json')
    $rootBlob = Get-ImageDescriptorBlob $index['manifests'][0] $archive.Blobs ($manifestTypes + $indexTypes)
    Assert-ImageCondition ($rootBlob.Digest -ceq $ExpectedDigest) 'Archive original manifest digest differs from build metadata.'
    $root = ConvertFrom-ImageJson $rootBlob.Data
    Assert-ImageCondition ($root -is [Collections.IDictionary]) 'Original OCI root must be a JSON object.'
    if ($root['mediaType'] -cin $indexTypes) {
        Assert-ImageCondition ($root['schemaVersion'] -eq 2 -and $root['manifests'] -is [array] -and
            $root['manifests'].Count -eq 1) 'Ambiguous or multi-platform image indexes are forbidden.'
        $child = $root['manifests'][0]
        $platform = $child['platform']
        Assert-ImageCondition ($null -eq $platform -or ($platform -is [Collections.IDictionary] -and
                $platform['os'] -ceq 'linux' -and $platform['architecture'] -ceq 'amd64' -and
                -not $platform['variant'])) 'Image index selects an unsupported platform.'
        $rootBlob = Get-ImageDescriptorBlob $child $archive.Blobs $manifestTypes
        $root = ConvertFrom-ImageJson $rootBlob.Data
    }
    Assert-ImageCondition ($root -is [Collections.IDictionary] -and $root['schemaVersion'] -eq 2 -and
        $root['mediaType'] -cin $manifestTypes) 'Original image manifest is unsupported.'
    $configBlob = Get-ImageDescriptorBlob $root['config'] $archive.Blobs @('application/vnd.oci.image.config.v1+json', 'application/vnd.docker.container.image.v1+json')
    if ($build.Contains('containerimage.config.digest')) {
        Assert-ImageCondition ($build['containerimage.config.digest'] -ceq $configBlob.Digest) 'Build metadata config identity differs from the original image config.'
    }
    $config = ConvertFrom-ImageJson $configBlob.Data
    Assert-ImageLabels $config $ExpectedSourceCommit
    $layers = $root['layers']
    Assert-ImageCondition ($layers -is [array] -and $layers.Count -gt 0) 'Original image layers are missing.'
    foreach ($layer in $layers) {
        $null = Get-ImageDescriptorBlob $layer $archive.Blobs @('application/vnd.oci.image.layer.v1.tar',
            'application/vnd.oci.image.layer.v1.tar+gzip', 'application/vnd.oci.image.layer.v1.tar+zstd',
            'application/vnd.docker.image.rootfs.diff.tar.gzip')
    }
    $rootfs = $config['rootfs']
    Assert-ImageCondition ($rootfs -is [Collections.IDictionary] -and $rootfs['type'] -ceq 'layers' -and
        $rootfs['diff_ids'] -is [array] -and $rootfs['diff_ids'].Count -eq $layers.Count) 'Original config does not bind every image layer.'
    foreach ($diffId in $rootfs['diff_ids']) { Assert-ImageDigest $diffId }
    if ($archive.Metadata.ContainsKey('manifest.json')) {
        $compatibility = $archive.Metadata['manifest.json']
        Assert-ImageCondition ($compatibility -is [array] -and $compatibility.Count -eq 1 -and
            $compatibility[0]['RepoTags'] -is [array] -and $compatibility[0]['RepoTags'].Count -eq 1 -and
            $compatibility[0]['RepoTags'][0] -ceq $ImageReference -and
            $compatibility[0]['Config'] -ceq ('blobs/sha256/' + $configBlob.Digest.Substring(7))) 'Docker compatibility manifest selects a different image or source alias.'
    }
    return ([ordered]@{ ConfigDigest = $configBlob.Digest; DiffIds = $rootfs['diff_ids'] }).AsReadOnly()
}

function Invoke-ImageDocker([string[]]$Arguments) {
    $outputPath = [IO.Path]::GetTempFileName()
    $errorPath = [IO.Path]::GetTempFileName()
    $output = [IO.File]::Create($outputPath)
    $errors = [IO.File]::Create($errorPath)
    $process = [Diagnostics.Process]::new()
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'docker'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    $process.StartInfo = $startInfo
    $started = $false
    try {
        $started = $process.Start()
        Assert-ImageCondition $started 'Docker image process could not be started.'
        $outputTask = $process.StandardOutput.BaseStream.CopyToAsync($output)
        $errorTask = $process.StandardError.BaseStream.CopyToAsync($errors)
        $timeout = if ($Arguments[0] -ceq 'image' -and $Arguments[1] -ceq 'load') { 1800000 } else { 60000 }
        if (-not $process.WaitForExit($timeout)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw 'Docker image load/inspection timed out; candidate was not qualified.'
        }
        $outputTask.GetAwaiter().GetResult()
        $errorTask.GetAwaiter().GetResult()
        $output.Flush()
        $errors.Flush()
        Assert-ImageCondition ($process.ExitCode -eq 0) 'Docker image load/inspection failed; candidate was not qualified.'
        Assert-ImageCondition ($output.Length -le $script:ImageJsonLimit) 'Docker image inspection output exceeds its byte limit.'
        $output.Dispose()
        return , [IO.File]::ReadAllBytes($outputPath)
    }
    finally {
        if ($started -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
        $output.Dispose()
        $errors.Dispose()
        [IO.File]::Delete($outputPath)
        [IO.File]::Delete($errorPath)
    }
}

function Get-SharpProofPrivatePreviewImageReceipt {
    param(
        [Parameter(Mandatory)][string]$ArchivePath, [Parameter(Mandatory)][string]$BuildMetadataPath,
        [Parameter(Mandatory)][string]$ExpectedDigest, [Parameter(Mandatory)][string]$ExpectedSourceCommit,
        [Parameter(Mandatory)][string]$ImageReference, [switch]$Load, [scriptblock]$DockerRunner
    )
    $identity = Get-ImageArchiveIdentity $ArchivePath $BuildMetadataPath $ExpectedDigest $ExpectedSourceCommit $ImageReference
    $stream = [IO.File]::OpenRead($ArchivePath)
    try { $archiveHash = Read-ImageStreamHash $stream $false }
    finally { $stream.Dispose() }
    if ($null -eq $DockerRunner) { $DockerRunner = { param([string[]]$CommandArguments) Invoke-ImageDocker $CommandArguments } }
    if ($Load) { $null = & $DockerRunner ([string[]]@('image', 'load', '--input', $ArchivePath)) }
    $inspectionBytes = [byte[]](& $DockerRunner ([string[]]@('image', 'inspect', $ImageReference)))
    $inspected = ConvertFrom-ImageJson $inspectionBytes
    Assert-ImageCondition ($inspected -is [array] -and $inspected.Count -eq 1 -and
        $inspected[0] -is [Collections.IDictionary]) 'Docker inspection must identify exactly one candidate image.'
    $image = $inspected[0]
    Assert-ImageCondition ($image['Id'] -ceq $identity.ConfigDigest) 'Loaded image ID differs from the original OCI config digest.'
    Assert-ImageLabels @{ os = $image['Os']; architecture = $image['Architecture']; config = $image['Config'] } $ExpectedSourceCommit
    Assert-ImageCondition ($image['RootFS'] -is [Collections.IDictionary] -and
        ($image['RootFS']['Layers'] -join '|') -ceq ($identity.DiffIds -join '|')) 'Loaded image layers differ from the verified original OCI image.'
    return ([ordered]@{
            schemaVersion = 1; archiveSha256 = $archiveHash.Digest.Substring(7); bytes = $archiveHash.Size
            registryDigest = $ExpectedDigest; imageConfigDigest = $identity.ConfigDigest; imageId = $image['Id']
            sourceCommit = $ExpectedSourceCommit; imageReference = $ImageReference
        }).AsReadOnly()
}

Export-ModuleMember -Function Get-SharpProofPrivatePreviewImageReceipt
