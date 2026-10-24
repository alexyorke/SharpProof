[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$PromotionDirectory,
    [Parameter(Mandatory)][ValidateSet('stage-registry', 'finalize')][string]$Phase,
    [Parameter(Mandatory)][ValidatePattern('\Asha256:[a-f0-9]{64}\z')][string]$ExpectedImageDigest
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PrivatePreviewReleaseAssets.psm1') -Force
& (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1')
$root = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
& (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewEvidence.ps1') -EvidenceDirectory $root -ExpectedSourceCommit $env:SHARPPROOF_SOURCE_COMMIT -ExpectedWorkflowCommit $env:SHARPPROOF_CANDIDATE_WORKFLOW_COMMIT -ExpectedImageDigest $ExpectedImageDigest
& (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPromotion.ps1') -CandidateDirectory $root -PromotionDirectory $PromotionDirectory -Phase $Phase -RequireSignature
$manifest = Get-Content -LiteralPath (Join-Path $root 'release-manifest.json') -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN) -or [string]::IsNullOrWhiteSpace($env:GITHUB_ACTOR)) { throw 'Private package publication credentials are missing.' }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-private-publish-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromMinutes(5)
function Send-PrivateRequest([Net.Http.HttpMethod]$Method, [uri]$Uri, [Net.Http.HttpContent]$Content = $null) {
    if ($Uri.Scheme -cne 'https' -or $Uri.Port -ne 443) { throw 'Private registry transport must use HTTPS.' }
    $request = [Net.Http.HttpRequestMessage]::new($Method, $Uri)
    try {
        [void]$request.Headers.TryAddWithoutValidation('User-Agent', 'SharpProof-private-release')
        if ($Uri.Host -ceq 'api.github.com') {
            $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $env:GH_TOKEN)
            $accept = if ($Uri.AbsolutePath -cmatch '/releases/assets/[0-9]+\z') { 'application/octet-stream' } else { 'application/vnd.github+json' }
            [void]$request.Headers.TryAddWithoutValidation('Accept', $accept)
            [void]$request.Headers.TryAddWithoutValidation('X-GitHub-Api-Version', '2022-11-28')
        }
        elseif ($Uri.Host -ceq 'nuget.pkg.github.com' -and ($Uri.AbsolutePath -ceq '/alexyorke' -or $Uri.AbsolutePath.StartsWith('/alexyorke/', [StringComparison]::Ordinal))) {
            $credential = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$($env:GITHUB_ACTOR):$($env:GH_TOKEN)"))
            $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Basic', $credential)
            if ($Method -eq [Net.Http.HttpMethod]::Put) { [void]$request.Headers.TryAddWithoutValidation('X-NuGet-ApiKey', $env:GH_TOKEN) }
        }
        elseif ($Method -ne [Net.Http.HttpMethod]::Get -or $Uri.Host -cnotmatch '\A[a-z0-9.-]+\.githubusercontent\.com\z') { throw 'Private registry request escaped the fixed GitHub service origins.' }
        if ($null -ne $Content) { $request.Content = $Content }
        return $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    }
    finally { $request.Dispose() }
}
function Read-PrivateJson([uri]$Uri, [switch]$AllowMissing) {
    $response = Send-PrivateRequest ([Net.Http.HttpMethod]::Get) $Uri
    try {
        if ($AllowMissing -and [int]$response.StatusCode -eq 404) { return $null }
        if (-not $response.IsSuccessStatusCode) { throw "Private registry metadata request failed with HTTP $([int]$response.StatusCode)." }
        $json = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ($json.Length -gt 4MB) { throw 'Private registry metadata exceeds its bound.' }
        return ($json | ConvertFrom-Json)
    }
    finally { $response.Dispose() }
}
function Assert-PrivatePackage([string]$Type, [string]$Name, [switch]$AllowMissing) {
    $escaped = [uri]::EscapeDataString($Name)
    $package = Read-PrivateJson "https://api.github.com/users/alexyorke/packages/$Type/$escaped" -AllowMissing:$AllowMissing
    if ($null -eq $package) { return $false }
    if ($package.visibility -cne 'private' -or $null -eq $package.PSObject.Properties['repository'] -or
        $package.repository.full_name -cne 'alexyorke/SharpProof-preview') { throw 'An existing package is public or linked to a different repository; publication is rejected.' }
    return $true
}
function Compare-RemoteArchive([uri]$Uri, [string]$ExpectedHash, [switch]$AllowMissing, [string]$OutputPath = '') {
    $response = Send-PrivateRequest ([Net.Http.HttpMethod]::Get) $Uri
    try {
        for ($redirect = 0; [int]$response.StatusCode -cin @(301, 302, 303, 307, 308); $redirect++) {
            if ($redirect -ge 3 -or $null -eq $response.Headers.Location) { throw 'Invalid private archive redirect.' }
            $target = [uri]::new($Uri, $response.Headers.Location)
            $response.Dispose()
            $Uri = $target
            $response = Send-PrivateRequest ([Net.Http.HttpMethod]::Get) $Uri
        }
        if ($AllowMissing -and [int]$response.StatusCode -eq 404) { return $false }
        if (-not $response.IsSuccessStatusCode) { throw "Private archive retrieval failed with HTTP $([int]$response.StatusCode)." }
        $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        try {
            if ($OutputPath) {
                $destination = [IO.File]::Create($OutputPath)
                try { $stream.CopyTo($destination) }
                finally { $destination.Dispose() }
                $hash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
            }
            else { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        }
        finally { $stream.Dispose() }
        if ($hash -cne $ExpectedHash) { throw 'An existing private package version has conflicting archive bytes.' }
        return $true
    }
    finally { $response.Dispose() }
}
try {
    $index = Read-PrivateJson 'https://nuget.pkg.github.com/alexyorke/index.json'
    $downloadResources = @($index.resources | Where-Object { $_.'@type' -ceq 'PackageBaseAddress/3.0.0' })
    $publishResources = @($index.resources | Where-Object { $_.'@type' -ceq 'PackagePublish/2.0.0' })
    if ($downloadResources.Count -ne 1 -or $publishResources.Count -ne 1) { throw 'Private NuGet service index does not expose the required exact-byte download and publish protocols.' }
    $downloadBase = [uri](([string]$downloadResources[0].'@id').TrimEnd('/') + '/')
    $publishEndpoint = [uri]$publishResources[0].'@id'
    foreach ($endpoint in @($downloadBase, $publishEndpoint)) {
        if ($endpoint.Scheme -cne 'https' -or $endpoint.Host -cne 'nuget.pkg.github.com' -or -not $endpoint.AbsolutePath.StartsWith('/alexyorke', [StringComparison]::Ordinal)) { throw 'Private feed resource escaped the fixed owner feed.' }
    }
    $states = @{}
    # Authenticate and compare every duplicate before the first registry mutation.
    foreach ($package in $manifest.packages) {
        $metadataExists = Assert-PrivatePackage nuget $package.id -AllowMissing
        $lower = $package.id.ToLowerInvariant()
        $archiveUri = [uri]::new($downloadBase, "$lower/1.0.0-preview.1/$lower.1.0.0-preview.1.nupkg")
        $states[$package.id] = [ordered]@{ exists = Compare-RemoteArchive $archiveUri $package.sha256 -AllowMissing; uri = $archiveUri }
        if ($states[$package.id].exists -and -not $metadataExists) { throw 'An existing package archive has no authenticated private repository metadata.' }
    }
    [void](Assert-PrivatePackage container sharpproof-preview -AllowMissing)
    $bundle = Join-Path $scratch 'private-preview-evidence.tar'
    & tar --sort=name --mtime=@0 --owner=0 --group=0 --numeric-owner -C $root -cf $bundle .
    if ($LASTEXITCODE -ne 0) { throw 'Cannot retain the complete private release evidence bundle.' }
    $promotionBundle = Join-Path $scratch 'private-preview-promotion.tar'
    & tar --sort=name --mtime=@0 --owner=0 --group=0 --numeric-owner -C $PromotionDirectory -cf $promotionBundle .
    if ($LASTEXITCODE -ne 0) { throw 'Cannot retain the authenticated promotion receipts.' }
    $assets = @($bundle, $promotionBundle, (Join-Path $root 'release-manifest.json'), (Join-Path $root 'release-manifest.sig'), (Join-Path $root 'SHA256SUMS'), (Join-Path $root 'packages.spdx.json')) + @($manifest.archives | ForEach-Object { Join-Path $root $_.fileName })
    Assert-SharpProofPrivatePreviewReleaseAssetSizes -Paths $assets
    $existingTag = Read-PrivateJson 'https://api.github.com/repos/alexyorke/SharpProof-preview/git/ref/tags/v1.0.0-preview.1' -AllowMissing
    if ($null -ne $existingTag) {
        $target = $existingTag.object
        for ($depth = 0; $target.type -ceq 'tag'; $depth++) {
            if ($depth -ge 3) { throw 'Private release tag nesting exceeds the approved bound.' }
            $tagObject = Read-PrivateJson "https://api.github.com/repos/alexyorke/SharpProof-preview/git/tags/$($target.sha)"
            $target = $tagObject.object
        }
        if ($target.type -cne 'commit' -or $target.sha -cne $env:SHARPPROOF_SOURCE_COMMIT) { throw 'Existing private release tag identifies a different source commit.' }
    }
    $release = Read-PrivateJson 'https://api.github.com/repos/alexyorke/SharpProof-preview/releases/tags/v1.0.0-preview.1' -AllowMissing
    if ($null -ne $release) {
        if ($Phase -ceq 'stage-registry') { throw 'A finalized release already exists; use finalize to reverify immutable release evidence.' }
        if (@($release.assets).Count -ne ($assets.Count + 1)) { throw 'Existing private release is incomplete or contains unexpected assets.' }
        foreach ($localAsset in $assets) {
            $name = [IO.Path]::GetFileName($localAsset)
            $remoteAsset = @($release.assets | Where-Object name -CEQ $name)
            if ($remoteAsset.Count -ne 1) { throw 'Existing private release is missing an immutable asset.' }
            $hash = (Get-FileHash -LiteralPath $localAsset -Algorithm SHA256).Hash.ToLowerInvariant()
            [void](Compare-RemoteArchive "https://api.github.com/repos/alexyorke/SharpProof-preview/releases/assets/$($remoteAsset[0].id)" $hash)
        }
        $existingFresh = @($release.assets | Where-Object name -CEQ 'fresh-consumer.json')
        if ($existingFresh.Count -ne 1 -or $existingFresh[0].digest -cnotmatch '\Asha256:[a-f0-9]{64}\z') { throw 'Existing release is missing its authenticated immutable fresh consumer receipt.' }
        $originalFreshPath = Join-Path $scratch 'fresh-consumer.json'
        [void](Compare-RemoteArchive "https://api.github.com/repos/alexyorke/SharpProof-preview/releases/assets/$($existingFresh[0].id)" $existingFresh[0].digest.Substring(7) -OutputPath $originalFreshPath)
        $originalFresh = Get-Content -LiteralPath $originalFreshPath -Raw | ConvertFrom-Json
        if ($originalFresh.status -cne 'passed' -or $originalFresh.sourceCommit -cne $manifest.sourceCommit -or
            $originalFresh.candidateWorkflowCommit -cne $manifest.workflowCommit -or $originalFresh.consumerImage -cne $manifest.consumerImage -or
            $originalFresh.candidateManifestSha256 -cne (Get-FileHash -LiteralPath (Join-Path $root 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() -or
            $originalFresh.sourceCheckoutMounted -isnot [bool] -or $originalFresh.sourceCheckoutMounted -or
            ($originalFresh.packages | ConvertTo-Json -Depth 5 -Compress) -cne ($manifest.packages | ConvertTo-Json -Depth 5 -Compress)) { throw 'Existing fresh registry consumer receipt does not qualify this exact candidate.' }
    }
    & bash (Join-Path $PSScriptRoot 'Publish-SharpProofPrivatePreviewImage.sh') $env:SHARPPROOF_SOURCE_COMMIT v1.0.0-preview.1 $ExpectedImageDigest
    if ($LASTEXITCODE -ne 0) { throw 'Private immutable candidate image publication failed.' }
    [void](Assert-PrivatePackage container sharpproof-preview)
    foreach ($package in $manifest.packages) {
        if (-not $states[$package.id].exists) {
            # Recheck signed originals immediately before giving their streams to the registry.
            & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewEvidence.ps1') -EvidenceDirectory $root -ExpectedSourceCommit $env:SHARPPROOF_SOURCE_COMMIT -ExpectedWorkflowCommit $env:SHARPPROOF_CANDIDATE_WORKFLOW_COMMIT -ExpectedImageDigest $ExpectedImageDigest
            $multipart = [Net.Http.MultipartFormDataContent]::new()
            $stream = [IO.File]::OpenRead((Join-Path $root $package.fileName))
            try {
                $content = [Net.Http.StreamContent]::new($stream)
                $content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')
                $multipart.Add($content, 'package', $package.fileName)
                $response = Send-PrivateRequest ([Net.Http.HttpMethod]::Put) $publishEndpoint $multipart
                try { if (-not $response.IsSuccessStatusCode -and [int]$response.StatusCode -ne 409) { throw "Private package publication failed with HTTP $([int]$response.StatusCode)." } }
                finally { $response.Dispose() }
            }
            finally { $multipart.Dispose(); $stream.Dispose() }
        }
        [void](Compare-RemoteArchive $states[$package.id].uri $package.sha256)
        [void](Assert-PrivatePackage nuget $package.id)
    }
    if ($Phase -ceq 'stage-registry') {
        $staged = [ordered]@{ schemaVersion = 1; status = 'registry-candidates-published-no-tag'; releaseRepository = 'alexyorke/SharpProof-preview'; sourceCommit = $manifest.sourceCommit; candidateWorkflowCommit = $manifest.workflowCommit; promotionWorkflowCommit = $env:SHARPPROOF_WORKFLOW_COMMIT; consumerImage = $manifest.consumerImage; packages = $manifest.packages; manifestSha256 = (Get-FileHash -LiteralPath (Join-Path $root 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() }
        [IO.File]::WriteAllText((Join-Path (Get-Location).Path 'private-preview-publication.json'), ($staged | ConvertTo-Json -Depth 10) + "`n", [Text.UTF8Encoding]::new($false))
        Write-Host 'Exact private registry candidates are available for actual access review; no release or tag was created.'
        return
    }
    # Finalization cannot use locally packed archives or a prepublication image smoke.
    & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewFreshConsumer.ps1') -EvidenceDirectory $root -OutputDirectory artifacts/private-fresh-consumer
    $freshReceipt = Join-Path (Get-Location).Path 'artifacts/private-fresh-consumer/fresh-consumer.json'
    # A rerun retains the original immutable release receipt. The new actual
    # execution audit remains in this run's private Actions artifact.
    $assets += $(if ($null -ne $release) { $originalFreshPath } else { $freshReceipt })
    Assert-SharpProofPrivatePreviewReleaseAssetSizes -Paths $assets
    if ($null -eq $release) {
        & gh release create v1.0.0-preview.1 --repo alexyorke/SharpProof-preview --target $env:SHARPPROOF_SOURCE_COMMIT --prerelease --draft --title 'SharpProof 1.0.0-preview.1 private preview' --notes 'Private exact-source candidate; verify the pinned manifest signature before use.'
        if ($LASTEXITCODE -ne 0) { throw 'Cannot create private release receipt.' }
        & gh release upload v1.0.0-preview.1 --repo alexyorke/SharpProof-preview @assets
        if ($LASTEXITCODE -ne 0) { throw 'Cannot retain exact private archives, symbols, SBOM, signature, and evidence.' }
        & gh release edit v1.0.0-preview.1 --repo alexyorke/SharpProof-preview --draft=false
        if ($LASTEXITCODE -ne 0) { throw 'Cannot finalize private release receipt.' }
    }
    $publishedRelease = Read-PrivateJson 'https://api.github.com/repos/alexyorke/SharpProof-preview/releases/tags/v1.0.0-preview.1'
    if ($publishedRelease.draft -or @($publishedRelease.assets).Count -ne $assets.Count) { throw 'Private release assets were not finalized exactly.' }
    foreach ($localAsset in $assets) {
        $name = [IO.Path]::GetFileName($localAsset)
        $remoteAsset = @($publishedRelease.assets | Where-Object name -CEQ $name)
        if ($remoteAsset.Count -ne 1) { throw 'Published private release is missing an immutable asset.' }
        [void](Compare-RemoteArchive "https://api.github.com/repos/alexyorke/SharpProof-preview/releases/assets/$($remoteAsset[0].id)" (Get-FileHash -LiteralPath $localAsset -Algorithm SHA256).Hash.ToLowerInvariant())
    }
    $receipt = [ordered]@{ schemaVersion = 1; status = 'published-and-reverified'; releaseRepository = 'alexyorke/SharpProof-preview'; sourceCommit = $manifest.sourceCommit; workflowCommit = $manifest.workflowCommit; consumerImage = $manifest.consumerImage; packageFeed = 'https://nuget.pkg.github.com/alexyorke/index.json'; packages = $manifest.packages; manifestSha256 = (Get-FileHash -LiteralPath (Join-Path $root 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant(); releaseId = $publishedRelease.id }
    [IO.File]::WriteAllText((Join-Path (Get-Location).Path 'private-preview-publication.json'), ($receipt | ConvertTo-Json -Depth 10) + "`n", [Text.UTF8Encoding]::new($false))
    Write-Host 'Published and reverified six private package versions and immutable candidate image; exact archives, symbols, SBOM, checksums, and offline signature retained in the private release.'
}
finally {
    $client.Dispose(); $handler.Dispose()
    $resolved = [IO.Path]::GetFullPath($scratch)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd([IO.Path]::DirectorySeparatorChar) -cne [IO.Path]::GetTempPath().TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::GetFileName($resolved) -cnotmatch '\Asharpproof-private-publish-[a-f0-9]{32}\z') { throw 'Refusing unsafe private publication scratch cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
