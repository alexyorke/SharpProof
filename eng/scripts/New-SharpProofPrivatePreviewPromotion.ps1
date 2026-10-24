[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$AcceptanceDirectory,
    [Parameter(Mandatory)][string]$CandidateAuthenticationPath,
    [Parameter(Mandatory)][string]$AcceptanceAuthenticationPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidateSet('stage-registry', 'finalize')][string]$Phase
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$manifestPath = Join-Path $candidate 'release-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewEvidence.ps1') -EvidenceDirectory $candidate -UnsignedCandidate -CandidateAuthenticationPath $CandidateAuthenticationPath -ExpectedSourceCommit $env:SHARPPROOF_SOURCE_COMMIT -ExpectedWorkflowCommit $env:SHARPPROOF_CANDIDATE_WORKFLOW_COMMIT -ExpectedImageDigest $manifest.consumerImage.Split('@')[1]
$authentication = Get-Content -LiteralPath $CandidateAuthenticationPath -Raw | ConvertFrom-Json
$acceptanceAuthentication = Get-Content -LiteralPath $AcceptanceAuthenticationPath -Raw | ConvertFrom-Json
if ($acceptanceAuthentication.kind -cne 'Acceptance' -or $acceptanceAuthentication.status -cne 'authenticated-extracted' -or
    $acceptanceAuthentication.releaseRepository -cne 'alexyorke/SharpProof-preview' -or
    $acceptanceAuthentication.workflowCommit -cne $env:SHARPPROOF_WORKFLOW_COMMIT -or
    $acceptanceAuthentication.workflowPath -cne '.github/workflows/private-preview-receipts.yml') { throw 'Host acceptance must originate from the approved immutable private receipt workflow artifact.' }
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PrivatePreviewPromotionReceipts.psm1') -Force
$receiptKind = if ($Phase -ceq 'finalize') { 'All' } else { 'Editor' }
Assert-SharpProofPrivatePreviewPromotionReceipts -EvidenceDirectory $AcceptanceDirectory -CandidateDirectory $candidate -Manifest $manifest -CandidateArtifactId $authentication.artifactId -CandidateArtifactDigest $authentication.artifactDigest -CandidateRunId $authentication.runId -ReceiptKind $receiptKind
$output = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw 'Promotion output must be empty.' }
$acceptedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
function Add-AcceptedAttachment($Value) {
    if ($Value -is [Collections.IDictionary]) {
        if ($Value.Contains('path') -and $Value.Contains('sha256')) { [void]$acceptedPaths.Add([string]$Value.path) }
        foreach ($entry in $Value.Values) { Add-AcceptedAttachment $entry }
    }
    elseif ($Value -is [Collections.IEnumerable] -and $Value -isnot [string]) {
        foreach ($entry in $Value) { Add-AcceptedAttachment $entry }
    }
}
$receiptNames = @('editor-acceptance.json')
if ($Phase -ceq 'finalize') { $receiptNames += 'access-acceptance.json' }
foreach ($name in $receiptNames) {
    [void]$acceptedPaths.Add($name)
    Add-AcceptedAttachment (Get-Content -LiteralPath (Join-Path $AcceptanceDirectory $name) -Raw | ConvertFrom-Json -AsHashtable -Depth 32)
}
foreach ($relative in $acceptedPaths) {
    $destination = Join-Path $output "acceptance/$relative"
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
    Copy-Item -LiteralPath (Join-Path $AcceptanceDirectory $relative) -Destination $destination
}
Copy-Item -LiteralPath $CandidateAuthenticationPath -Destination (Join-Path $output 'candidate-authentication.json')
Copy-Item -LiteralPath $AcceptanceAuthenticationPath -Destination (Join-Path $output 'acceptance-authentication.json')
$files = @(Get-ChildItem -LiteralPath $output -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
        if ($_.LinkType) { throw 'Linked promotion evidence is not permitted.' }
        [ordered]@{ fileName = [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
$promotion = [ordered]@{
    schemaVersion = 1; releaseRepository = 'alexyorke/SharpProof-preview'; sourceRepository = 'alexyorke/SharpProof'
    sourceCommit = $manifest.sourceCommit; candidateWorkflowCommit = $manifest.workflowCommit; promotionWorkflowCommit = $env:SHARPPROOF_WORKFLOW_COMMIT
    workflowPath = '.github/workflows/private-preview-promote.yml'; phase = $Phase; consumerImage = $manifest.consumerImage
    candidateManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    candidateArtifact = $authentication; acceptanceArtifact = $acceptanceAuthentication; files = $files
}
[IO.File]::WriteAllText((Join-Path $output 'promotion-manifest.json'), ($promotion | ConvertTo-Json -Depth 15) + "`n", [Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPromotion.ps1') -CandidateDirectory $candidate -PromotionDirectory $output -Phase $Phase
Write-Host 'Promotion binds the original authenticated candidate and actual host receipts; candidate archives and manifest were not repacked.'
