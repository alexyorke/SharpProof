Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-NativeToolCondition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Native Tool coverage rejected: $Message" }
}
function Assert-NativeToolJsonNames([Text.Json.JsonElement]$Element) {
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            Assert-NativeToolCondition ($names.Add($property.Name)) 'duplicate JSON property.'
            Assert-NativeToolJsonNames $property.Value
        }
    }
    elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($value in $Element.EnumerateArray()) { Assert-NativeToolJsonNames $value }
    }
}
function Read-NativeToolJson([string]$Path) {
    $file = Get-Item -LiteralPath $Path -ErrorAction Stop
    Assert-NativeToolCondition ($file.Length -gt 0 -and $file.Length -le 32 * 1024 * 1024) 'JSON evidence byte limit.'
    $text = [IO.File]::ReadAllText($file.FullName, [Text.UTF8Encoding]::new($false, $true))
    $json = [Text.Json.JsonDocument]::Parse($text)
    try { Assert-NativeToolJsonNames $json.RootElement }
    finally { $json.Dispose() }
    return , (ConvertFrom-Json -InputObject $text -AsHashtable -Depth 64 -NoEnumerate)
}
function Assert-NativeToolKeys($Value, [string[]]$Names, [string]$Description) {
    Assert-NativeToolCondition ($Value -is [Collections.IDictionary]) "$Description must be an object."
    $actual = @($Value.Keys | Sort-Object -CaseSensitive)
    Assert-NativeToolCondition (($actual -join '|') -ceq (($Names | Sort-Object -CaseSensitive) -join '|')) "$Description schema differs."
}
function Get-NativeToolHash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Resolve-NativeToolFile([string]$Root, [string]$Relative) {
    Assert-NativeToolCondition (-not [string]::IsNullOrWhiteSpace($Relative) -and
        -not $Relative.Contains('\') -and -not $Relative.Contains(':') -and
        -not $Relative.StartsWith('/', [StringComparison]::Ordinal) -and
        @($Relative.Split('/') | Where-Object { $_ -cin @('', '.', '..') }).Count -eq 0) 'noncanonical evidence path.'
    $base = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $current = $base
    foreach ($part in $Relative.Split('/')) {
        $current = Join-Path $current $part
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        Assert-NativeToolCondition (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'linked evidence path.'
    }
    Assert-NativeToolCondition ((Get-Item -LiteralPath $current).PSIsContainer -eq $false) 'evidence is not a file.'
    return $current
}
function Assert-NativeToolInventory([string]$Root, [string[]]$Expected) {
    $directory = Get-Item -LiteralPath $Root -Force -ErrorAction Stop
    Assert-NativeToolCondition ($directory.PSIsContainer -and
        ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'linked or missing evidence root.'
    $items = @(Get-ChildItem -LiteralPath $Root -Recurse -Force)
    Assert-NativeToolCondition (@($items | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count -eq 0) 'linked evidence inventory.'
    $actual = @($items | Where-Object { -not $_.PSIsContainer } | ForEach-Object {
            [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
        } | Sort-Object -CaseSensitive)
    Assert-NativeToolCondition (($actual -join '|') -ceq (($Expected | Sort-Object -CaseSensitive) -join '|')) 'evidence file inventory differs.'
}
function Assert-NativeToolFile($Description, [string]$Root, [string]$ExpectedPath) {
    Assert-NativeToolKeys $Description @('path', 'sha256') 'file descriptor'
    Assert-NativeToolCondition ($Description['path'] -ceq $ExpectedPath -and
        $Description['sha256'] -cmatch '\A[a-f0-9]{64}\z') 'file descriptor identity differs.'
    $path = Resolve-NativeToolFile $Root $ExpectedPath
    Assert-NativeToolCondition ((Get-NativeToolHash $path) -ceq $Description['sha256']) "file hash differs: $ExpectedPath"
    return $path
}
function Send-NativeToolArtifactRequest([Net.Http.HttpClient]$Client, [uri]$Uri) {
    Assert-NativeToolCondition ($Uri.Scheme -ceq 'https' -and $Uri.Port -eq 443 -and [string]::IsNullOrEmpty($Uri.UserInfo)) 'artifact transport must use HTTPS.'
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $Uri)
    try {
        $request.Headers.UserAgent.ParseAdd('SharpProof-native-coverage')
        if ($Uri.Host -ceq 'api.github.com' -and $Uri.Query -ceq '' -and
            $Uri.AbsolutePath -cmatch '\A/repos/alexyorke/SharpProof/actions/artifacts/[1-9][0-9]*(?:/zip)?\z') {
            Assert-NativeToolCondition (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) 'authenticated GitHub artifact access is required.'
            $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $env:GITHUB_TOKEN)
            $request.Headers.Accept.ParseAdd('application/vnd.github+json')
            $request.Headers.Add('X-GitHub-Api-Version', '2022-11-28')
        }
        else {
            # Same service-origin policy as Get-SharpProofPrivatePreviewArtifact.
            # Signed storage redirects never receive the GitHub credential.
            Assert-NativeToolCondition ($Uri.Host -cmatch '\A[a-z0-9.-]+\.(?:githubusercontent\.com|blob\.core\.windows\.net)\z') 'artifact redirect escaped the GitHub artifact service.'
        }
        return $Client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    }
    finally { $request.Dispose() }
}
function Get-NativeToolArtifact([string]$ArtifactId) {
    $handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    try {
        $response = Send-NativeToolArtifactRequest $client ([uri]"https://api.github.com/repos/alexyorke/SharpProof/actions/artifacts/$ArtifactId")
        try {
            Assert-NativeToolCondition $response.IsSuccessStatusCode 'GitHub artifact metadata request failed.'
            $stream = $response.Content.ReadAsStream()
            $buffer = [byte[]]::new(1024 * 1024 + 1)
            $length = 0
            while ($length -lt $buffer.Length -and ($read = $stream.Read($buffer, $length, $buffer.Length - $length)) -gt 0) { $length += $read }
            Assert-NativeToolCondition ($length -gt 0 -and $length -le 1024 * 1024) 'GitHub artifact metadata byte limit.'
            $text = [Text.UTF8Encoding]::new($false, $true).GetString($buffer, 0, $length)
            $json = [Text.Json.JsonDocument]::Parse($text)
            try { Assert-NativeToolJsonNames $json.RootElement } finally { $json.Dispose() }
            return , (ConvertFrom-Json -InputObject $text -AsHashtable -Depth 32)
        }
        finally { $response.Dispose() }
    }
    finally { $client.Dispose(); $handler.Dispose() }
}
function Save-NativeToolArtifactArchive([string]$ArtifactId, [string]$Destination) {
    $handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromMinutes(10)
    $response = $null
    try {
        $uri = [uri]"https://api.github.com/repos/alexyorke/SharpProof/actions/artifacts/$ArtifactId/zip"
        $response = Send-NativeToolArtifactRequest $client $uri
        for ($redirect = 0; [int]$response.StatusCode -cin @(301, 302, 303, 307, 308); $redirect++) {
            Assert-NativeToolCondition ($redirect -lt 3 -and $null -ne $response.Headers.Location) 'invalid artifact redirect.'
            $uri = [uri]::new($uri, $response.Headers.Location)
            $response.Dispose(); $response = $null
            $response = Send-NativeToolArtifactRequest $client $uri
        }
        Assert-NativeToolCondition $response.IsSuccessStatusCode 'GitHub artifact archive request failed.'
        Assert-NativeToolCondition ($null -eq $response.Content.Headers.ContentLength -or $response.Content.Headers.ContentLength -le 512MB) 'artifact archive byte limit.'
        $inputStream = $response.Content.ReadAsStream(); $output = [IO.File]::Create($Destination)
        try {
            $buffer = [byte[]]::new(65536); $total = [long]0
            while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $total += $read
                Assert-NativeToolCondition ($total -le 512MB) 'artifact archive byte limit.'
                $output.Write($buffer, 0, $read)
            }
        }
        finally { $output.Dispose(); $inputStream.Dispose() }
    }
    finally { if ($null -ne $response) { $response.Dispose() }; $client.Dispose(); $handler.Dispose() }
}
function Assert-NativeToolArtifactContent($Metadata, [string]$Id, [string]$Root, [string[]]$Expected, [scriptblock]$ArchiveLookup) {
    $owned = $null -eq $ArchiveLookup
    $scratch = if ($owned) { Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-native-artifact-' + [Guid]::NewGuid().ToString('N') + '.zip') } else { & $ArchiveLookup $Id }
    try {
        if ($owned) { Save-NativeToolArtifactArchive $Id $scratch }
        Assert-NativeToolCondition ((Get-Item -LiteralPath $scratch).Length -le 512MB -and
            $Metadata['digest'] -ceq ('sha256:' + (Get-NativeToolHash $scratch))) 'artifact archive differs from authenticated Actions digest.'
        $archive = [IO.Compression.ZipFile]::OpenRead($scratch)
        try {
            Assert-NativeToolCondition ($archive.Entries.Count -gt 0 -and $archive.Entries.Count -le 64) 'artifact entry count limit.'
            $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            $files = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $total = [long]0
            foreach ($entry in $archive.Entries) {
                $name = $entry.FullName.TrimEnd('/')
                Assert-NativeToolCondition ($name -cmatch '\A[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*\z' -and
                    @($name.Split('/') | Where-Object { $_ -cin @('.', '..') }).Count -eq 0 -and $names.Add($name) -and
                    (($entry.ExternalAttributes -shr 16) -band 0xF000) -ne 0xA000) 'unsafe, linked, or duplicate artifact member.'
                if ($entry.FullName.EndsWith('/', [StringComparison]::Ordinal)) {
                    Assert-NativeToolCondition (@($Expected | Where-Object { $_.StartsWith($name + '/', [StringComparison]::Ordinal) }).Count -gt 0) 'artifact directory is outside expected inventory.'
                    continue
                }
                $total += $entry.Length
                Assert-NativeToolCondition ($entry.Length -ge 0 -and $entry.Length -le 512MB -and $total -le 1GB -and
                    $name -cin $Expected -and $files.Add($name)) 'artifact member inventory/byte limit differs.'
                $local = Resolve-NativeToolFile $Root $name
                Assert-NativeToolCondition ((Get-Item -LiteralPath $local).Length -eq $entry.Length) 'extracted artifact member differs from authenticated ZIP.'
                $stream = $entry.Open(); $hasher = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
                try {
                    $buffer = [byte[]]::new(65536); $observed = [long]0
                    while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                        $observed += $read
                        Assert-NativeToolCondition ($observed -le $entry.Length) 'artifact expanded member byte limit.'
                        $hasher.AppendData($buffer, 0, $read)
                    }
                    Assert-NativeToolCondition ($observed -eq $entry.Length) 'artifact expanded member byte limit.'
                    $hash = [Convert]::ToHexString($hasher.GetHashAndReset()).ToLowerInvariant()
                }
                finally { $stream.Dispose(); $hasher.Dispose() }
                Assert-NativeToolCondition ($hash -ceq (Get-NativeToolHash $local)) 'extracted artifact member differs from authenticated ZIP.'
            }
            Assert-NativeToolCondition ($files.Count -eq $Expected.Count) 'artifact member inventory differs.'
        }
        finally { $archive.Dispose() }
    }
    finally {
        if ($owned -and (Test-Path -LiteralPath $scratch)) { [IO.File]::Delete($scratch) }
    }
}
function Assert-NativeToolArtifact($Metadata, [string]$Id, [string]$Name, [string]$RunId, [string]$HeadSha) {
    Assert-NativeToolCondition ($Metadata -is [Collections.IDictionary] -and
        [string]$Metadata['id'] -ceq $Id -and $Metadata['name'] -ceq $Name -and
        $Metadata['digest'] -cmatch '\Asha256:[a-f0-9]{64}\z' -and
        $Metadata['expired'] -is [bool] -and -not $Metadata['expired'] -and
        $Metadata['workflow_run'] -is [Collections.IDictionary] -and
        [string]$Metadata['workflow_run']['id'] -ceq $RunId -and
        $Metadata['workflow_run']['head_sha'] -ceq $HeadSha) 'immutable artifact origin differs from this run/head.'
}
function Get-SharpProofNativeToolModuleIdentity([string]$AssemblyPath, [string]$PdbPath) {
    $stream = [IO.File]::OpenRead($AssemblyPath)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    $pdbStream = $null
    $provider = $null
    try {
        Assert-NativeToolCondition $pe.HasMetadata 'Tool PE metadata is missing.'
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $entries = @($pe.ReadDebugDirectory() | Where-Object { $_.Type -eq [Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView })
        Assert-NativeToolCondition ($entries.Count -eq 1 -and $entries[0].IsPortableCodeView) 'Tool PE has no unique portable CodeView identity.'
        $codeView = $pe.ReadCodeViewDebugDirectoryData($entries[0])
        $pdbStream = [IO.File]::OpenRead($PdbPath)
        $provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
        $pdb = $provider.GetMetadataReader()
        $pdbId = [Reflection.Metadata.BlobContentId]::new($pdb.DebugMetadataHeader.Id)
        Assert-NativeToolCondition ($pdbId.Guid -eq $codeView.Guid) 'Tool PDB does not belong to its PE CodeView identity.'
        return ([ordered]@{ project = 'SharpProof.Tool'; assemblyName = $metadata.GetString($metadata.GetAssemblyDefinition().Name)
                moduleMvid      = $metadata.GetGuid($metadata.GetModuleDefinition().Mvid).ToString('D')
                pdbCodeViewGuid = $codeView.Guid.ToString('D')
            }).AsReadOnly()
    }
    finally {
        if ($null -ne $provider) { $provider.Dispose() }
        if ($null -ne $pdbStream) { $pdbStream.Dispose() }
        $pe.Dispose()
    }
}
function Assert-NativeToolModule($Actual, $Expected) {
    Assert-NativeToolKeys $Actual @('project', 'assemblyName', 'moduleMvid', 'pdbCodeViewGuid') 'module'
    foreach ($name in @('project', 'assemblyName', 'moduleMvid', 'pdbCodeViewGuid')) {
        Assert-NativeToolCondition ([string]$Actual[$name] -ceq [string]$Expected.$name) "module $name differs from independent authority."
    }
    Assert-NativeToolCondition ($Actual['project'] -ceq 'SharpProof.Tool' -and $Actual['assemblyName'] -ceq 'SharpProof.Tool') 'supplement must belong only to Tool.'
}
function Assert-NativeToolPackage([string]$Path, [string]$Id, [string]$Version, [string]$AssemblyHash = '', [string]$PdbHash = '') {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName
            Assert-NativeToolCondition ($names.Add($name) -and -not $name.Contains('\') -and -not $name.Contains(':') -and
                -not $name.StartsWith('/', [StringComparison]::Ordinal) -and @($name.Split('/') | Where-Object { $_ -cin @('.', '..') }).Count -eq 0) 'unsafe or duplicate package member.'
        }
        $nuspecs = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) })
        Assert-NativeToolCondition ($nuspecs.Count -eq 1 -and $nuspecs[0].Length -le 1024 * 1024) 'package nuspec identity is missing or duplicated.'
        $settings = [Xml.XmlReaderSettings]::new(); $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit; $settings.XmlResolver = $null
        $reader = [Xml.XmlReader]::Create($nuspecs[0].Open(), $settings)
        $xml = [Xml.XmlDocument]::new()
        try { $xml.Load($reader) } finally { $reader.Dispose() }
        $metadata = @($xml.SelectNodes('/*[local-name()="package"]/*[local-name()="metadata"]'))
        Assert-NativeToolCondition ($metadata.Count -eq 1) 'package metadata is missing.'
        $ids = @($metadata[0].SelectNodes('*[local-name()="id"]')); $versions = @($metadata[0].SelectNodes('*[local-name()="version"]'))
        Assert-NativeToolCondition ($ids.Count -eq 1 -and $versions.Count -eq 1 -and
            $ids[0].InnerText -ceq $Id -and $versions[0].InnerText -ceq $Version) 'package id/version differs.'
        if ($Id -ceq 'SharpProof.Tool') {
            foreach ($item in @(@{ Name = 'SharpProof.Tool.dll'; Hash = $AssemblyHash; Required = $true }, @{ Name = 'SharpProof.Tool.pdb'; Hash = $PdbHash; Required = $false })) {
                $entries = @($archive.Entries | Where-Object { [IO.Path]::GetFileName($_.FullName) -ceq $item.Name })
                Assert-NativeToolCondition (($item.Required -and $entries.Count -eq 1) -or (-not $item.Required -and $entries.Count -le 1)) 'Tool executable/symbol package member differs.'
                if ($entries.Count -eq 1) {
                    Assert-NativeToolCondition ($entries[0].FullName -ceq ('tools/net10.0/any/' + $item.Name)) 'Tool executable/symbol package path differs.'
                    $entryStream = $entries[0].Open()
                    try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entryStream)).ToLowerInvariant() }
                    finally { $entryStream.Dispose() }
                    Assert-NativeToolCondition ($hash -ceq $item.Hash) 'installed package executable/symbol differs from producer bytes.'
                }
            }
        }
    }
    finally { $archive.Dispose() }
}
function Import-SharpProofNativeToolCoverage {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][string]$ProducerRoot,
        [Parameter(Mandatory)][string]$ConsumerRoot, [Parameter(Mandatory)]$Authority,
        [Parameter(Mandatory)][string]$DestinationRoot, [Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)][string]$WorkflowHeadSha, [Parameter(Mandatory)][string]$ProducerArtifactId,
        [Parameter(Mandatory)][string]$ProducerAttempt, [Parameter(Mandatory)][string]$ConsumerArtifactId,
        [Parameter(Mandatory)][string]$ConsumerAttempt, [scriptblock]$ArtifactLookup, [scriptblock]$ArtifactArchiveLookup
    )
    foreach ($value in @($RunId, $ProducerArtifactId, $ProducerAttempt, $ConsumerArtifactId, $ConsumerAttempt)) {
        Assert-NativeToolCondition ($value -cmatch '\A[1-9][0-9]*\z') 'run/artifact/attempt must be a positive identity.'
    }
    Assert-NativeToolCondition ($WorkflowHeadSha -cmatch '\A[a-f0-9]{40}\z' -and
        $Authority.commit -cmatch '\A[a-f0-9]{40}\z' -and $Authority.configuration -ceq 'Release' -and
        $ProducerArtifactId -cne $ConsumerArtifactId) 'source or distinct artifact identity differs.'
    if ($null -eq $ArtifactLookup) { $ArtifactLookup = { param([string]$ArtifactId) Get-NativeToolArtifact $ArtifactId } }
    $producerArtifact = & $ArtifactLookup $ProducerArtifactId
    $consumerArtifact = & $ArtifactLookup $ConsumerArtifactId
    Assert-NativeToolArtifact $producerArtifact $ProducerArtifactId "coverage-windows-tool-input-$($Authority.commit)-$ProducerAttempt" $RunId $WorkflowHeadSha
    Assert-NativeToolArtifact $consumerArtifact $ConsumerArtifactId "coverage-windows-tool-output-$($Authority.commit)-$ConsumerAttempt" $RunId $WorkflowHeadSha
    $producer = Read-NativeToolJson (Resolve-NativeToolFile $ProducerRoot 'producer.json')
    $consumer = Read-NativeToolJson (Resolve-NativeToolFile $ConsumerRoot 'consumer.json')
    Assert-NativeToolKeys $producer @('schemaVersion', 'kind', 'sourceCommit', 'runId', 'producerAttempt', 'packageVersion', 'phase', 'module', 'files', 'collector') 'producer receipt'
    Assert-NativeToolKeys $consumer @('schemaVersion', 'kind', 'sourceCommit', 'runId', 'runAttempt', 'producerAttempt', 'producerArtifactId', 'packageVersion', 'module', 'producerReceiptSha256', 'files', 'checks', 'command') 'consumer receipt'
    $version = "0.0.0-ci.$RunId.$ProducerAttempt"
    Assert-NativeToolCondition ($producer['schemaVersion'] -eq 1 -and $producer['kind'] -ceq 'sharpproof-windows-tool-coverage-input' -and
        $producer['sourceCommit'] -ceq $Authority.commit -and $producer['runId'] -ceq $RunId -and
        $producer['producerAttempt'] -ceq $ProducerAttempt -and $producer['packageVersion'] -ceq $version -and $producer['phase'] -ceq 'semantic-extras') 'producer run/source/version/phase binding differs.'
    Assert-NativeToolCondition ($consumer['schemaVersion'] -eq 1 -and $consumer['kind'] -ceq 'sharpproof-windows-tool-coverage' -and
        $consumer['sourceCommit'] -ceq $Authority.commit -and $consumer['runId'] -ceq $RunId -and
        $consumer['runAttempt'] -ceq $ConsumerAttempt -and $consumer['producerAttempt'] -ceq $ProducerAttempt -and
        $consumer['producerArtifactId'] -ceq $ProducerArtifactId -and $consumer['packageVersion'] -ceq $version -and
        $consumer['producerReceiptSha256'] -ceq (Get-NativeToolHash (Join-Path $ProducerRoot 'producer.json'))) 'consumer run/source/producer binding differs.'
    $toolModules = @($Authority.modules | Where-Object { $_.project -ceq 'SharpProof.Tool' -and $_.assemblyName -ceq 'SharpProof.Tool' })
    Assert-NativeToolCondition ($toolModules.Count -eq 1) 'independent authority has no unique Tool module.'
    $tool = $toolModules[0]
    Assert-NativeToolModule $producer['module'] $tool
    Assert-NativeToolModule $consumer['module'] $tool
    Assert-NativeToolKeys $producer['files'] @('authority', 'package', 'assembly', 'pdb', 'collector') 'producer files'
    Assert-NativeToolKeys $producer['collector'] @('id', 'version') 'collector'
    [xml]$versions = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'eng/Versions.props') -Raw
    $collectorPin = $versions.SelectSingleNode('/Project/PropertyGroup/DotNetCoverageToolVersion').InnerText
    Assert-NativeToolCondition ($collectorPin -cmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z' -and
        $producer['collector']['id'] -ceq 'dotnet-coverage' -and $producer['collector']['version'] -ceq $collectorPin) 'collector pin differs from repository authority.'
    $inputFiles = @('coverage-authority.json', 'SharpProof.Tool.dll', 'SharpProof.Tool.pdb', "feed/SharpProof.Tool.$version.nupkg", "feed/dotnet-coverage.$collectorPin.nupkg", 'producer.json')
    Assert-NativeToolInventory $ProducerRoot $inputFiles
    $producerAuthorityPath = Assert-NativeToolFile $producer['files']['authority'] $ProducerRoot 'coverage-authority.json'
    $assemblyPath = Assert-NativeToolFile $producer['files']['assembly'] $ProducerRoot 'SharpProof.Tool.dll'
    $pdbPath = Assert-NativeToolFile $producer['files']['pdb'] $ProducerRoot 'SharpProof.Tool.pdb'
    $packagePath = Assert-NativeToolFile $producer['files']['package'] $ProducerRoot "feed/SharpProof.Tool.$version.nupkg"
    $collectorPath = Assert-NativeToolFile $producer['files']['collector'] $ProducerRoot "feed/dotnet-coverage.$collectorPin.nupkg"
    $producerAuthority = Read-NativeToolJson $producerAuthorityPath
    Assert-NativeToolCondition ($producerAuthority['schemaVersion'] -eq 1 -and $producerAuthority['commit'] -ceq $Authority.commit -and
        $producerAuthority['configuration'] -ceq 'Release') 'producer authority commit/configuration differs.'
    $producerModules = @($producerAuthority['modules'] | Where-Object { $_['project'] -ceq 'SharpProof.Tool' })
    Assert-NativeToolCondition ($producerModules.Count -eq 1) 'producer authority has no unique Tool module.'
    Assert-NativeToolModule $producer['module'] $producerModules[0]
    $assemblyHash = $producer['files']['assembly']['sha256']; $pdbHash = $producer['files']['pdb']['sha256']
    $independentAssembly = Resolve-NativeToolFile $RepositoryRoot $tool.assemblyPath
    $independentPdb = Resolve-NativeToolFile $RepositoryRoot $tool.pdbPath
    Assert-NativeToolCondition ($assemblyHash -ceq (Get-NativeToolHash $independentAssembly) -and
        $pdbHash -ceq (Get-NativeToolHash $independentPdb)) 'producer DLL/PDB differs from independently built authority bytes.'
    Assert-NativeToolModule (Get-SharpProofNativeToolModuleIdentity $assemblyPath $pdbPath) $tool
    Assert-NativeToolPackage $packagePath 'SharpProof.Tool' $version $assemblyHash $pdbHash
    Assert-NativeToolPackage $collectorPath 'dotnet-coverage' $collectorPin
    Assert-NativeToolKeys $consumer['files'] @('report', 'binary') 'consumer files'
    Assert-NativeToolInventory $ConsumerRoot @('consumer.json', 'windows-tool.cobertura.xml', 'windows-tool.coverage')
    $reportPath = Assert-NativeToolFile $consumer['files']['report'] $ConsumerRoot 'windows-tool.cobertura.xml'
    $binaryPath = Assert-NativeToolFile $consumer['files']['binary'] $ConsumerRoot 'windows-tool.coverage'
    Assert-NativeToolCondition ((Get-Item -LiteralPath $binaryPath).Length -gt 0) 'native collector binary is empty.'
    $checkNames = @('nativeWindows', 'architecture', 'outsideCheckout', 'installedAssemblySha256', 'installedPdbSha256', 'assemblyUnmodified',
        'existingProject', 'protectedDacl', 'daclPreserved', 'ownerPreserved', 'projectChanged', 'initializerWritten', 'noStagingFiles')
    Assert-NativeToolKeys $consumer['checks'] $checkNames 'native checks'
    foreach ($name in $checkNames | Where-Object { $_ -cnotin @('architecture', 'installedAssemblySha256', 'installedPdbSha256') }) {
        Assert-NativeToolCondition ($consumer['checks'][$name] -is [bool] -and $consumer['checks'][$name]) "native behavior did not pass: $name"
    }
    Assert-NativeToolCondition ($consumer['checks']['architecture'] -ceq 'X64' -and $consumer['checks']['installedAssemblySha256'] -ceq $assemblyHash -and
        $consumer['checks']['installedPdbSha256'] -ceq $pdbHash) 'installed native process bytes/architecture differ.'
    Assert-NativeToolKeys $consumer['command'] @('tool', 'arguments') 'native command'
    Assert-NativeToolCondition ($consumer['command']['tool'] -ceq 'dotnet-sharpproof' -and
        $consumer['command']['arguments'] -is [array] -and @($consumer['command']['arguments']).Count -eq 8 -and
        ($consumer['command']['arguments'] -join '|') -ceq 'init|--project|Consumer.csproj|--verification|portable|--apply|--format|json') 'native installed command differs.'
    Assert-NativeToolCondition ((Get-Item -LiteralPath $reportPath).Length -le 16 * 1024 * 1024) 'native coverage XML byte limit.'
    $settings = [Xml.XmlReaderSettings]::new(); $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit; $settings.XmlResolver = $null; $settings.MaxCharactersInDocument = 16 * 1024 * 1024
    $reader = [Xml.XmlReader]::Create($reportPath, $settings); $document = [Xml.XmlDocument]::new(); $document.XmlResolver = $null
    try { $document.Load($reader) } finally { $reader.Dispose() }
    Assert-NativeToolCondition ($document.DocumentElement.Name -ceq 'coverage' -and $document.SelectNodes('/coverage/sharpProofAuthority').Count -eq 0) 'native report has an invalid or pre-stamped authority envelope.'
    $packages = @($document.SelectNodes('/coverage/packages/package'))
    Assert-NativeToolCondition ($packages.Count -gt 0 -and @($packages | Where-Object { $_.GetAttribute('name') -cne 'SharpProof.Tool' }).Count -eq 0) 'native report contains another module.'
    $classes = @($document.SelectNodes('/coverage/packages/package/classes/class'))
    Assert-NativeToolCondition ($classes.Count -gt 0) 'native report has no Tool classes.'
    $documents = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($source in $tool.documents) { $documents.Add([string]$source.path, $source) }
    $positive = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($class in $classes) {
        $path = $class.GetAttribute('filename')
        Assert-NativeToolCondition ($documents.ContainsKey($path)) 'native report source is outside Tool PDB authority.'
        if (-not $positive.ContainsKey($path)) { $positive.Add($path, [Collections.Generic.HashSet[int]]::new()) }
        $source = $documents[$path]
        foreach ($line in $class.SelectNodes('.//line')) {
            $numberToken = $line.GetAttribute('number'); $hitToken = $line.GetAttribute('hits')
            $number = 0; $hits = 0
            Assert-NativeToolCondition ($numberToken -cmatch '\A[1-9][0-9]*\z' -and $hitToken -cmatch '\A(?:0|[1-9][0-9]*)\z' -and
                [int]::TryParse($numberToken, [ref]$number) -and [int]::TryParse($hitToken, [ref]$hits)) 'native line/hit count is invalid.'
            Assert-NativeToolCondition (@($source.sequencePointRanges | Where-Object { [int]$_.startLine -le $number -and [int]$_.endLine -ge $number }).Count -gt 0) 'native report line is outside Tool PDB ranges.'
            if ($hits -gt 0) { $null = $positive[$path].Add($number) }
        }
    }
    $daclPath = 'src/SharpProof.Tool/SetupInitializerFiles.cs'
    Assert-NativeToolCondition ($documents.ContainsKey($daclPath) -and $positive.ContainsKey($daclPath)) 'native report did not execute initializer DACL source.'
    $sourceLines = [IO.File]::ReadAllLines((Resolve-NativeToolFile $RepositoryRoot $daclPath))
    foreach ($pattern in @('var security = new FileInfo(target).GetAccessControl(', 'return new FileInfo(temporary).Create(')) {
        $positions = @(for ($index = 0; $index -lt $sourceLines.Length; $index++) { if ($sourceLines[$index].Contains($pattern, [StringComparison]::Ordinal)) { $index + 1 } })
        Assert-NativeToolCondition ($positions.Count -eq 1) 'native DACL source contract is ambiguous or missing.'
        $statement = [int]$positions[0]
        $ranges = @($documents[$daclPath].sequencePointRanges | Where-Object { [int]$_.startLine -eq $statement })
        Assert-NativeToolCondition (@($documents[$daclPath].sequencePoints) -contains $statement -and $ranges.Count -gt 0 -and
            @($ranges | Where-Object { $range = $_; @($positive[$daclPath] | Where-Object { $_ -ge [int]$range.startLine -and $_ -le [int]$range.endLine }).Count -gt 0 }).Count -gt 0) 'native report did not cover both authenticated DACL sequence points.'
    }
    Assert-NativeToolArtifactContent $producerArtifact $ProducerArtifactId $ProducerRoot $inputFiles $ArtifactArchiveLookup
    Assert-NativeToolArtifactContent $consumerArtifact $ConsumerArtifactId $ConsumerRoot @('consumer.json', 'windows-tool.cobertura.xml', 'windows-tool.coverage') $ArtifactArchiveLookup
    Assert-NativeToolCondition (-not (Test-Path -LiteralPath $DestinationRoot)) 'native staging directory already exists.'
    [IO.Directory]::CreateDirectory($DestinationRoot) | Out-Null
    $envelope = $document.CreateElement('sharpProofAuthority')
    $envelope.SetAttribute('schemaVersion', '1'); $envelope.SetAttribute('commit', [string]$Authority.commit)
    $moduleText = @($Authority.modules | ForEach-Object { [string]$_.project + ':' + [string]$_.assemblyName + ':' + [string]$_.moduleMvid + ':' + [string]$_.pdbCodeViewGuid } | Sort-Object) -join ','
    $envelope.SetAttribute('modules', $moduleText); $null = $document.DocumentElement.AppendChild($envelope)
    $destination = Join-Path $DestinationRoot 'windows-tool.cobertura.xml'
    $writerSettings = [Xml.XmlWriterSettings]::new(); $writerSettings.Encoding = [Text.UTF8Encoding]::new($false); $writerSettings.Indent = $true
    $writer = [Xml.XmlWriter]::Create($destination, $writerSettings)
    try { $document.Save($writer) } finally { $writer.Dispose() }
    $receipt = ([ordered]@{ schemaVersion = 1; commit = $Authority.commit; workflowHeadSha = $WorkflowHeadSha; runId = $RunId
            producerArtifactId = $ProducerArtifactId; producerAttempt = $ProducerAttempt; consumerArtifactId = $ConsumerArtifactId; consumerAttempt = $ConsumerAttempt
            producerArtifactDigest = $producerArtifact['digest']; consumerArtifactDigest = $consumerArtifact['digest']
            moduleMvid = $tool.moduleMvid; pdbCodeViewGuid = $tool.pdbCodeViewGuid
            producerReceiptSha256 = $consumer['producerReceiptSha256']; originalReportSha256 = $consumer['files']['report']['sha256']
            normalizedReportSha256 = Get-NativeToolHash $destination; binarySha256 = $consumer['files']['binary']['sha256']
        }).AsReadOnly()
    [IO.File]::WriteAllText((Join-Path $DestinationRoot 'native-validation.json'), (($receipt | ConvertTo-Json) + "`n"), [Text.UTF8Encoding]::new($false))
    return $destination
}
Export-ModuleMember -Function Import-SharpProofNativeToolCoverage, Get-SharpProofNativeToolModuleIdentity
