[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('\A[1-9][0-9]*\z')][string]$ArtifactId,
    [Parameter(Mandatory)][ValidateSet('Candidate', 'Acceptance')][string]$Kind,
    [Parameter(Mandatory)][ValidatePattern('\A[a-f0-9]{40}\z')][string]$ExpectedWorkflowCommit,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ReceiptPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PrivatePreviewArtifact.psm1') -Force
if ($env:GITHUB_REPOSITORY -cne 'alexyorke/SharpProof-preview' -or $env:SHARPPROOF_PRIVATE_PREVIEW -cne 'true' -or
    [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { throw 'Private artifact authentication requires the fixed private repository.' }
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromMinutes(30)
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-private-artifact-' + [Guid]::NewGuid().ToString('N') + '.zip')
function Send-ArtifactRequest([uri]$Uri, [string]$Accept = 'application/vnd.github+json') {
    if ($Uri.Scheme -cne 'https' -or $Uri.Port -ne 443) { throw 'Artifact transport must use HTTPS.' }
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $Uri)
    try {
        [void]$request.Headers.TryAddWithoutValidation('User-Agent', 'SharpProof-private-promotion')
        if ($Uri.Host -ceq 'api.github.com' -and ($Uri.AbsolutePath -ceq '/repos/alexyorke/SharpProof-preview' -or $Uri.AbsolutePath.StartsWith('/repos/alexyorke/SharpProof-preview/', [StringComparison]::Ordinal))) {
            $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $env:GH_TOKEN)
            [void]$request.Headers.TryAddWithoutValidation('Accept', $Accept)
            [void]$request.Headers.TryAddWithoutValidation('X-GitHub-Api-Version', '2022-11-28')
        }
        elseif ($Uri.Host -cnotmatch '\A[a-z0-9.-]+\.(?:githubusercontent\.com|blob\.core\.windows\.net)\z') { throw 'Artifact redirect escaped the GitHub artifact service.' }
        return $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    }
    finally { $request.Dispose() }
}
function Read-ArtifactJson([string]$Path) {
    $response = Send-ArtifactRequest ("https://api.github.com/repos/alexyorke/SharpProof-preview/$Path".TrimEnd('/'))
    try {
        if (-not $response.IsSuccessStatusCode) { throw "Private artifact metadata failed with HTTP $([int]$response.StatusCode)." }
        $json = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ($json.Length -gt 4MB) { throw 'Private artifact metadata exceeds its bound.' }
        return ($json | ConvertFrom-Json)
    }
    finally { $response.Dispose() }
}
try {
    $repository = Read-ArtifactJson ''
    $artifact = Read-ArtifactJson "actions/artifacts/$ArtifactId"
    $run = Read-ArtifactJson "actions/runs/$($artifact.workflow_run.id)"
    $workflowPath = if ($Kind -ceq 'Candidate') { '.github/workflows/private-preview.yml' } else { '.github/workflows/private-preview-receipts.yml' }
    Assert-SharpProofPrivatePreviewArtifactOrigin -Repository $repository -Artifact $artifact -Run $run -ArtifactId $ArtifactId -Kind $Kind -ExpectedWorkflowCommit $ExpectedWorkflowCommit
    $uri = [uri]"https://api.github.com/repos/alexyorke/SharpProof-preview/actions/artifacts/$ArtifactId/zip"
    $response = Send-ArtifactRequest $uri 'application/octet-stream'
    try {
        for ($redirect = 0; [int]$response.StatusCode -cin @(301, 302, 303, 307, 308); $redirect++) {
            if ($redirect -ge 3 -or $null -eq $response.Headers.Location) { throw 'Invalid artifact redirect.' }
            $uri = [uri]::new($uri, $response.Headers.Location)
            $response.Dispose()
            $response = Send-ArtifactRequest $uri 'application/octet-stream'
        }
        if (-not $response.IsSuccessStatusCode) { throw "Private artifact download failed with HTTP $([int]$response.StatusCode)." }
        $destination = [IO.File]::Create($scratch)
        try { $response.Content.CopyToAsync($destination).GetAwaiter().GetResult() }
        finally { $destination.Dispose() }
    }
    finally { $response.Dispose() }
    $digest = (Get-FileHash -LiteralPath $scratch -Algorithm SHA256).Hash.ToLowerInvariant()
    if ("sha256:$digest" -cne $artifact.digest) { throw 'Private artifact archive differs from the authenticated Actions digest.' }
    $root = [IO.Path]::GetFullPath($OutputDirectory)
    [void][IO.Directory]::CreateDirectory($root)
    if (@(Get-ChildItem -LiteralPath $root -Force).Count -ne 0) { throw 'Artifact extraction requires an empty directory.' }
    $zip = [IO.Compression.ZipFile]::OpenRead($scratch)
    try {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.TrimEnd('/')
            if ($name -cnotmatch '\A[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*\z' -or @($name.Split('/') | Where-Object { $_ -cin @('.', '..') }).Count -ne 0 -or
                -not $seen.Add($name) -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Unsafe, linked, or duplicate artifact entry.' }
            $path = Join-Path $root $name
            if ($entry.FullName.EndsWith('/')) { [void][IO.Directory]::CreateDirectory($path); continue }
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
            $inputStream = $entry.Open()
            $outputStream = [IO.File]::Create($path)
            try { $inputStream.CopyTo($outputStream) }
            finally { $inputStream.Dispose(); $outputStream.Dispose() }
        }
    }
    finally { $zip.Dispose() }
    $manifestHash = if ($Kind -ceq 'Candidate') { (Get-FileHash -LiteralPath (Join-Path $root 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
    $receipt = [ordered]@{ schemaVersion = 1; releaseRepository = 'alexyorke/SharpProof-preview'; artifactId = $ArtifactId; artifactDigest = $digest; runId = [string]$run.id; workflowCommit = $ExpectedWorkflowCommit; workflowPath = $workflowPath; kind = $Kind; status = 'authenticated-extracted'; candidateManifestSha256 = $manifestHash }
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($ReceiptPath), ($receipt | ConvertTo-Json) + "`n", [Text.UTF8Encoding]::new($false))
    Write-Host "Authenticated immutable private $Kind artifact $ArtifactId and originating workflow."
}
finally {
    $client.Dispose(); $handler.Dispose()
    $resolved = [IO.Path]::GetFullPath($scratch)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd([IO.Path]::DirectorySeparatorChar) -cne [IO.Path]::GetTempPath().TrimEnd([IO.Path]::DirectorySeparatorChar) -or [IO.Path]::GetFileName($resolved) -cnotmatch '\Asharpproof-private-artifact-[a-f0-9]{32}\.zip\z') { throw 'Unsafe artifact scratch cleanup.' }
    Remove-Item -LiteralPath $resolved -Force -ErrorAction SilentlyContinue
}
