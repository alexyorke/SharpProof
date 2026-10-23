[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$policy = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../release/private-preview/policy.json') -Raw | ConvertFrom-Json
if ($policy.schemaVersion -ne 1 -or $policy.releaseRepository -cne 'alexyorke/SharpProof-preview' -or
    $policy.environment -cne 'private-preview-release' -or $policy.approvalPolicy -cne 'owner-manual-main' -or
    $policy.requiredActor -cne 'alexyorke' -or $policy.requiredBranch -cne 'main') { throw 'The pinned private publication approval policy is not the supported owner-manual-main policy.' }
if ($env:GITHUB_REPOSITORY -cne 'alexyorke/SharpProof-preview' -or $env:SHARPPROOF_PRIVATE_PREVIEW -cne 'true' -or
    $env:GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or $env:GITHUB_REF -cne 'refs/heads/main' -or
    $env:GITHUB_ACTOR -cne $policy.requiredActor -or $env:GITHUB_TRIGGERING_ACTOR -cne $policy.requiredActor -or
    $env:GITHUB_WORKFLOW_REF -cne 'alexyorke/SharpProof-preview/.github/workflows/private-preview-promote.yml@refs/heads/main' -or
    $env:SHARPPROOF_WORKFLOW_COMMIT -cnotmatch '\A[a-f0-9]{40}\z' -or $env:GITHUB_SHA -cne $env:SHARPPROOF_WORKFLOW_COMMIT -or
    $env:SHARPPROOF_WORKFLOW_COMMIT -cne $env:GITHUB_WORKFLOW_SHA -or [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
    throw 'Private publication requires the fixed private repository, approved workflow identity, and authenticated policy verification.'
}
$repositoryJson = & gh api --method GET repos/alexyorke/SharpProof-preview
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify release repository visibility.' }
$repository = $repositoryJson | ConvertFrom-Json
if (-not $repository.private -or $repository.visibility -cne 'private') { throw 'Release repository must remain private.' }
$environmentJson = & gh api --method GET repos/alexyorke/SharpProof-preview/environments/private-preview-release
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify protected publication environment. Signing and publication remain blocked.' }
$environment = $environmentJson | ConvertFrom-Json
$reviewRules = @($environment.protection_rules | Where-Object type -CEQ 'required_reviewers')
if ($reviewRules.Count -gt 1 -or ($reviewRules.Count -eq 1 -and (@($reviewRules[0].reviewers).Count -ne 1 -or
            $reviewRules[0].reviewers[0].type -cne 'User' -or $reviewRules[0].reviewers[0].reviewer.login -cne $policy.requiredActor))) {
    throw 'Configured environment reviewers must contain exactly the pinned owner; unexpected or empty reviewer rules are rejected.'
}
if ($null -eq $environment.deployment_branch_policy -or -not $environment.deployment_branch_policy.custom_branch_policies) {
    throw 'Signing and publication require the private environment and its explicit main-only deployment branch policy.'
}
$branchesJson = & gh api --method GET repos/alexyorke/SharpProof-preview/environments/private-preview-release/deployment-branch-policies
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify private publication branch policy.' }
$branches = $branchesJson | ConvertFrom-Json
if (@($branches.branch_policies).Count -ne 1 -or $branches.branch_policies[0].name -cne 'main' -or $branches.branch_policies[0].type -cne 'branch' -or $env:GITHUB_REF -cne 'refs/heads/main') {
    throw 'Private publication is restricted to the protected main orchestration branch.'
}
Write-Host 'Explicit owner-manual-main approval, both owner actors, private environment, exact workflow, optional owner-only reviewer rule, and main-only deployment policy verified.'
