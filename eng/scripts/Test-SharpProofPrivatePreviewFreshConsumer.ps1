[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
# Host orchestration: the only mounted consumer inputs are freshly downloaded
# registry archives and an isolated generated project. No source checkout is mounted.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$candidate = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $candidate 'release-manifest.json') -Raw | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewEvidence.ps1') -EvidenceDirectory $candidate -ExpectedSourceCommit $env:SHARPPROOF_SOURCE_COMMIT -ExpectedWorkflowCommit $env:SHARPPROOF_CANDIDATE_WORKFLOW_COMMIT -ExpectedImageDigest $manifest.consumerImage.Split('@')[1]
if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN) -or [string]::IsNullOrWhiteSpace($env:GHCR_TOKEN) -or $env:GITHUB_ACTOR -cne 'alexyorke') { throw 'Fresh private consumer requires the authenticated fixed owner.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw 'Fresh consumer output must be empty.' }
$feed = Join-Path $output 'packages'
[void][IO.Directory]::CreateDirectory($feed)
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromMinutes(5)
function Send-ConsumerRequest([uri]$Uri) {
    if ($Uri.Scheme -cne 'https' -or $Uri.Port -ne 443) { throw 'Fresh consumer transport must use HTTPS.' }
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $Uri)
    try {
        [void]$request.Headers.TryAddWithoutValidation('User-Agent', 'SharpProof-fresh-private-consumer')
        if ($Uri.Host -ceq 'nuget.pkg.github.com' -and $Uri.AbsolutePath.StartsWith('/alexyorke/', [StringComparison]::Ordinal)) {
            $credential = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$($env:GITHUB_ACTOR):$($env:GH_TOKEN)"))
            $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Basic', $credential)
        }
        elseif ($Uri.Host -cnotmatch '\A[a-z0-9.-]+\.githubusercontent\.com\z') { throw 'Fresh feed request escaped the fixed private feed.' }
        return $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    }
    finally { $request.Dispose() }
}
function Invoke-ConsumerDocker([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $output "$Name.log"
    & docker @Arguments *> $log
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $log -Tail 25 | Write-Host
        throw "Fresh private consumer Docker phase failed: $Name"
    }
}
try {
    $indexResponse = Send-ConsumerRequest 'https://nuget.pkg.github.com/alexyorke/index.json'
    try {
        if (-not $indexResponse.IsSuccessStatusCode) { throw 'Cannot authenticate the real private package service index.' }
        $index = $indexResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    }
    finally { $indexResponse.Dispose() }
    $resources = @($index.resources | Where-Object { $_.'@type' -ceq 'PackageBaseAddress/3.0.0' })
    if ($resources.Count -ne 1) { throw 'Private service index is missing its exact archive download protocol.' }
    $base = [uri](([string]$resources[0].'@id').TrimEnd('/') + '/')
    if ($base.Scheme -cne 'https' -or $base.Host -cne 'nuget.pkg.github.com' -or -not $base.AbsolutePath.StartsWith('/alexyorke/', [StringComparison]::Ordinal)) { throw 'Private package download protocol escaped the owner feed.' }
    foreach ($package in $manifest.packages) {
        $lower = $package.id.ToLowerInvariant()
        $uri = [uri]::new($base, "$lower/1.0.0-preview.1/$lower.1.0.0-preview.1.nupkg")
        $response = Send-ConsumerRequest $uri
        try {
            for ($redirect = 0; [int]$response.StatusCode -cin @(301, 302, 303, 307, 308); $redirect++) {
                if ($redirect -ge 3 -or $null -eq $response.Headers.Location) { throw 'Invalid private feed archive redirect.' }
                $uri = [uri]::new($uri, $response.Headers.Location)
                $response.Dispose()
                $response = Send-ConsumerRequest $uri
            }
            if (-not $response.IsSuccessStatusCode) { throw "Fresh private archive download failed with HTTP $([int]$response.StatusCode)." }
            $stream = [IO.File]::Create((Join-Path $feed $package.fileName))
            try { $response.Content.CopyToAsync($stream).GetAwaiter().GetResult() }
            finally { $stream.Dispose() }
        }
        finally { $response.Dispose() }
        if ((Get-FileHash -LiteralPath (Join-Path $feed $package.fileName) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $package.sha256) { throw 'Freshly downloaded private package bytes differ from the signed candidate.' }
    }
    $env:GHCR_TOKEN | & docker login ghcr.io --username $env:GITHUB_ACTOR --password-stdin *> (Join-Path $output 'registry-login.log')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot authenticate the private consumer registry.' }
    Invoke-ConsumerDocker 'pull-exact-digest' @('pull', '--platform', 'linux/amd64', $manifest.consumerImage)
    $bootstrap = @'
set -euo pipefail
install -d -o sharpproof -g sharpproof /workspace/fresh/workspace-consumer
export HOME=/home/sharpproof
runuser --user sharpproof --preserve-environment -- dotnet new install /private-feed/SharpProof.Templates.1.0.0-preview.1.nupkg
runuser --user sharpproof --preserve-environment -- dotnet new sharpproof-workspace --name FreshPreview --output /workspace/fresh/workspace-consumer --verification container
'@
    $bootstrapPath = Join-Path $output 'bootstrap.sh'
    [IO.File]::WriteAllText($bootstrapPath, $bootstrap.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
    Invoke-ConsumerDocker 'generate-published-workspace' @('run', '--rm', '--platform', 'linux/amd64', '--entrypoint', '/bin/bash', '--user', '0:0',
        '--mount', "type=bind,src=$output,dst=/workspace/fresh", '--mount', "type=bind,src=$feed,dst=/private-feed,readonly",
        '--workdir', '/tmp', $manifest.consumerImage, '/workspace/fresh/bootstrap.sh')
    # This executes the actual digest-pinned generated Compose workflow. Its
    # isolated feed consists solely of the six authenticated registry downloads.
    & (Join-Path $PSScriptRoot 'Test-SharpProofTemplateCompose.ps1') -EvidenceDirectory $output -ConsumerDirectory workspace-consumer -ConsumerImage $manifest.consumerImage
    $composeFiles = @(Get-ChildItem -LiteralPath (Join-Path $output 'compose-acceptance') -Recurse -Filter compose-run.json -File)
    if ($composeFiles.Count -ne 1) { throw 'Fresh generated consumer must have exactly one actual Compose receipt.' }
    $compose = Get-Content -LiteralPath $composeFiles[0].FullName -Raw | ConvertFrom-Json
    if ($compose.status -cne 'passed' -or $compose.sourceCommit -cne $manifest.sourceCommit -or $compose.consumerImage -cne $manifest.consumerImage) { throw 'Fresh private registry consumer did not pass for the exact published identities.' }
    $receipt = [ordered]@{
        schemaVersion = 1; status = 'passed'; releaseRepository = 'alexyorke/SharpProof-preview'; sourceCommit = $manifest.sourceCommit
        candidateWorkflowCommit = $manifest.workflowCommit; promotionWorkflowCommit = $env:SHARPPROOF_WORKFLOW_COMMIT
        consumerImage = $manifest.consumerImage; packageFeed = 'https://nuget.pkg.github.com/alexyorke/index.json'; packageAcquisition = 'authenticated-registry-downloads'
        sourceCheckoutMounted = $false; packages = $manifest.packages
        candidateManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $candidate 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
        composeReceiptSha256    = (Get-FileHash -LiteralPath $composeFiles[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    [IO.File]::WriteAllText((Join-Path $output 'fresh-consumer.json'), ($receipt | ConvertTo-Json -Depth 10) + "`n", [Text.UTF8Encoding]::new($false))
    Write-Host 'Fresh private registry downloads and digest pull passed the actual generated strict workspace workflow without a mounted source checkout.'
}
finally {
    $client.Dispose(); $handler.Dispose()
    & docker logout ghcr.io *> (Join-Path $output 'registry-logout.log')
}
