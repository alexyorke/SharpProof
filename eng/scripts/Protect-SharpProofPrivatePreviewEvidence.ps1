[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$PromotionDirectory,
    [Parameter(Mandatory)][ValidateSet('stage-registry', 'finalize')][string]$Phase
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
& (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1')
if ($env:GITHUB_REPOSITORY -cne 'alexyorke/SharpProof-preview' -or $env:SHARPPROOF_PRIVATE_PREVIEW -cne 'true' -or
    [string]::IsNullOrWhiteSpace($env:PRIVATE_PREVIEW_SIGNING_KEY)) { throw 'Protected private signing context is missing.' }
$root = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$promotionRoot = (Resolve-Path -LiteralPath $PromotionDirectory).Path
& (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPromotion.ps1') -CandidateDirectory $root -PromotionDirectory $promotionRoot -Phase $Phase
$manifest = Get-Content -LiteralPath (Join-Path $root 'release-manifest.json') -Raw | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewEvidence.ps1') -EvidenceDirectory $root -UnsignedCandidate -CandidateAuthenticationPath (Join-Path $promotionRoot 'candidate-authentication.json') -ExpectedSourceCommit $env:SHARPPROOF_SOURCE_COMMIT -ExpectedWorkflowCommit $env:SHARPPROOF_CANDIDATE_WORKFLOW_COMMIT -ExpectedImageDigest $manifest.consumerImage.Split('@')[1]
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem($env:PRIVATE_PREVIEW_SIGNING_KEY)
    $policy = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../release/private-preview/policy.json') -Raw | ConvertFrom-Json
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
    if ($rsa.KeySize -ne 3072 -or $fingerprint -cne $policy.publicKeySpkiSha256) { throw 'Protected signing key does not match the pinned verifier.' }
    $signature = $rsa.SignData([IO.File]::ReadAllBytes((Join-Path $root 'release-manifest.json')), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [IO.File]::WriteAllBytes((Join-Path $root 'release-manifest.sig'), $signature)
    $promotionSignature = $rsa.SignData([IO.File]::ReadAllBytes((Join-Path $promotionRoot 'promotion-manifest.json')), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [IO.File]::WriteAllBytes((Join-Path $promotionRoot 'promotion-manifest.sig'), $promotionSignature)
}
finally { $rsa.Dispose(); $env:PRIVATE_PREVIEW_SIGNING_KEY = $null }
& (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewEvidence.ps1') -EvidenceDirectory $root -ExpectedSourceCommit $env:SHARPPROOF_SOURCE_COMMIT -ExpectedWorkflowCommit $env:SHARPPROOF_CANDIDATE_WORKFLOW_COMMIT -ExpectedImageDigest $manifest.consumerImage.Split('@')[1]
& (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPromotion.ps1') -CandidateDirectory $root -PromotionDirectory $promotionRoot -Phase $Phase -RequireSignature
