[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$PromotionDirectory,
    [Parameter(Mandatory)][ValidateSet('stage-registry', 'finalize')][string]$Phase,
    [switch]$RequireSignature
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path -LiteralPath $PromotionDirectory).Path
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$manifestPath = Join-Path $root 'promotion-manifest.json'
$promotion = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest = Get-Content -LiteralPath (Join-Path $candidate 'release-manifest.json') -Raw | ConvertFrom-Json
if ($env:GITHUB_REPOSITORY -cne 'alexyorke/SharpProof-preview' -or $env:SHARPPROOF_PRIVATE_PREVIEW -cne 'true' -or
    $env:SHARPPROOF_WORKFLOW_COMMIT -cnotmatch '\A[a-f0-9]{40}\z' -or
    $promotion.schemaVersion -ne 1 -or $promotion.releaseRepository -cne 'alexyorke/SharpProof-preview' -or
    $promotion.sourceRepository -cne 'alexyorke/SharpProof' -or $promotion.sourceCommit -cne $env:SHARPPROOF_SOURCE_COMMIT -or
    $promotion.candidateWorkflowCommit -cne $env:SHARPPROOF_CANDIDATE_WORKFLOW_COMMIT -or
    $promotion.promotionWorkflowCommit -cne $env:SHARPPROOF_WORKFLOW_COMMIT -or
    $promotion.workflowPath -cne '.github/workflows/private-preview-promote.yml' -or $promotion.phase -cne $Phase -or
    $promotion.consumerImage -cne $manifest.consumerImage -or
    $promotion.candidateManifestSha256 -cne (Get-FileHash -LiteralPath (Join-Path $candidate 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Promotion does not bind the exact original candidate and approved promotion workflow.' }
if ($RequireSignature) {
    $rsa = [Security.Cryptography.RSA]::Create()
    try {
        $trust = Join-Path $PSScriptRoot '../release/private-preview'
        $policy = Get-Content -LiteralPath (Join-Path $trust 'policy.json') -Raw | ConvertFrom-Json
        $rsa.ImportFromPem([IO.File]::ReadAllText((Join-Path $trust 'signing-public-key.pem')))
        $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
        if ($rsa.KeySize -ne 3072 -or $fingerprint -cne $policy.publicKeySpkiSha256 -or
            -not $rsa.VerifyData([IO.File]::ReadAllBytes($manifestPath), [IO.File]::ReadAllBytes((Join-Path $root 'promotion-manifest.sig')), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw 'Pinned promotion signature is invalid.' }
    }
    finally { $rsa.Dispose() }
}
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $promotion.files) {
    if ($file.fileName -cnotmatch '\A[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*\z' -or '..' -cin $file.fileName.Split('/') -or
        -not $seen.Add($file.fileName) -or $file.sha256 -cnotmatch '\A[a-f0-9]{64}\z') { throw 'Unsafe or duplicate promotion evidence filename.' }
    $item = Get-Item -LiteralPath (Join-Path $root $file.fileName)
    if ($item.LinkType -or $item.Length -ne $file.bytes -or (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) { throw 'Promotion receipt evidence bytes changed.' }
}
$expectedFiles = @($seen) + 'promotion-manifest.json'
if ($RequireSignature) { $expectedFiles += 'promotion-manifest.sig' }
$actualFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') })
if ((($expectedFiles | Sort-Object) -join '|') -cne (($actualFiles | Sort-Object) -join '|')) { throw 'Promotion contains missing or unbound files.' }
$authentication = Get-Content -LiteralPath (Join-Path $root 'candidate-authentication.json') -Raw | ConvertFrom-Json
$acceptanceAuthentication = Get-Content -LiteralPath (Join-Path $root 'acceptance-authentication.json') -Raw | ConvertFrom-Json
if ($authentication.kind -cne 'Candidate' -or $authentication.workflowCommit -cne $env:SHARPPROOF_CANDIDATE_WORKFLOW_COMMIT -or
    $acceptanceAuthentication.kind -cne 'Acceptance' -or $acceptanceAuthentication.status -cne 'authenticated-extracted' -or
    $acceptanceAuthentication.releaseRepository -cne 'alexyorke/SharpProof-preview' -or
    $acceptanceAuthentication.workflowCommit -cne $env:SHARPPROOF_WORKFLOW_COMMIT -or
    $acceptanceAuthentication.workflowPath -cne '.github/workflows/private-preview-receipts.yml' -or
    $acceptanceAuthentication.artifactId -cnotmatch '\A[1-9][0-9]*\z' -or $acceptanceAuthentication.artifactDigest -cnotmatch '\A[a-f0-9]{64}\z') { throw 'Promotion artifact authentication does not bind both approved private workflow identities.' }
if (($promotion.candidateArtifact | ConvertTo-Json -Compress) -cne ($authentication | ConvertTo-Json -Compress) -or
    ($promotion.acceptanceArtifact | ConvertTo-Json -Compress) -cne ($acceptanceAuthentication | ConvertTo-Json -Compress)) { throw 'Promotion artifact identities differ from their authenticated download receipts.' }
$candidateArguments = @{ EvidenceDirectory = $candidate; ExpectedSourceCommit = $env:SHARPPROOF_SOURCE_COMMIT; ExpectedWorkflowCommit = $env:SHARPPROOF_CANDIDATE_WORKFLOW_COMMIT; ExpectedImageDigest = $manifest.consumerImage.Split('@')[1] }
if (-not $RequireSignature) {
    $candidateArguments.UnsignedCandidate = $true
    $candidateArguments.CandidateAuthenticationPath = Join-Path $root 'candidate-authentication.json'
}
& (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewEvidence.ps1') @candidateArguments
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PrivatePreviewPromotionReceipts.psm1') -Force
$receiptKind = if ($Phase -ceq 'finalize') { 'All' } else { 'Editor' }
Assert-SharpProofPrivatePreviewPromotionReceipts -EvidenceDirectory (Join-Path $root 'acceptance') -CandidateDirectory $candidate -Manifest $manifest -CandidateArtifactId $authentication.artifactId -CandidateArtifactDigest $authentication.artifactDigest -CandidateRunId $authentication.runId -ReceiptKind $receiptKind
Write-Host 'Verified exact candidate, promotion workflow, immutable artifact identities, and required actual host receipts.'
