[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$previousGh = Get-Item Function:\gh -ErrorAction SilentlyContinue
$fixture = [pscustomobject]@{ Mode = 'valid' }
$environmentNames = @('GITHUB_REPOSITORY', 'SHARPPROOF_PRIVATE_PREVIEW', 'SHARPPROOF_WORKFLOW_COMMIT', 'GITHUB_WORKFLOW_SHA', 'GITHUB_SHA', 'GITHUB_EVENT_NAME', 'GITHUB_WORKFLOW_REF', 'GITHUB_REF', 'GITHUB_ACTOR', 'GITHUB_TRIGGERING_ACTOR', 'GH_TOKEN')
$previousEnvironment = @{}
foreach ($name in $environmentNames) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
$source = '1' * 40
$workflow = '2' * 40
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-private-policy-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$controls = 0
function Assert-PolicyRejection([string]$Name, [scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw "Private policy control did not reject: $Name" }
    $script:controls++
}
$ghMock = {
    $endpoint = [string]$args[-1]
    $mode = $fixture.Mode
    $global:LASTEXITCODE = 0
    if ($endpoint -ceq 'repos/alexyorke/SharpProof-preview') {
        return ([ordered]@{ private = $mode -cne 'public-repository'; visibility = $(if ($mode -ceq 'public-repository') { 'public' } else { 'private' }) } | ConvertTo-Json -Compress)
    }
    if ($endpoint -ceq 'repos/alexyorke/SharpProof') { return '{"private":false,"default_branch":"master"}' }
    if ($endpoint.Contains('/compare/')) {
        return ([ordered]@{ status = 'ahead'; merge_base_commit = @{ sha = $(if ($mode -ceq 'non-master-source') { '4' * 40 } else { '1' * 40 }) } } | ConvertTo-Json -Compress)
    }
    if ($endpoint.Contains('/actions/workflows/')) {
        $nightly = $endpoint.Contains('/nightly.yml/')
        if ($nightly -and $mode -ceq 'missing-nightly') { return '{"workflow_runs":[]}' }
        $fixtureEvent = if (-not $nightly) { 'push' } elseif ($mode -ceq 'manual-nightly') { 'workflow_dispatch' } elseif ($mode -ceq 'pull-request-nightly') { 'pull_request' } else { 'schedule' }
        $runs = @(@{ id = 2; run_number = 2; head_sha = '1' * 40; head_branch = 'master'; event = $fixtureEvent; status = $(if ($mode -ceq 'pending-source-gate' -or ($nightly -and $mode -ceq 'pending-nightly')) { 'in_progress' } else { 'completed' }); conclusion = $(if ($mode -ceq 'failed-source-gate' -or ($nightly -and $mode -ceq 'failed-nightly')) { 'failure' } else { 'success' }); html_url = 'https://github.com/alexyorke/SharpProof/actions/runs/2' })
        if ($nightly -and $mode -ceq 'pending-nightly') { $runs += @{ id = 1; run_number = 1; head_sha = '1' * 40; head_branch = 'master'; event = 'schedule'; status = 'completed'; conclusion = 'success'; html_url = 'https://github.com/alexyorke/SharpProof/actions/runs/1' } }
        return ([ordered]@{ workflow_runs = $runs } | ConvertTo-Json -Depth 5 -Compress)
    }
    if ($endpoint.EndsWith('/deployment-branch-policies')) {
        return ([ordered]@{ branch_policies = @(@{ name = $(if ($mode -ceq 'wrong-branch') { '*' } else { 'main' }); type = 'branch' }) } | ConvertTo-Json -Depth 5 -Compress)
    }
    if ($endpoint.EndsWith('/environments/private-preview-release')) {
        $rules = @(if ($mode -cne 'missing-reviewer') { @{ type = 'required_reviewers'; reviewers = @(@{ type = 'User'; reviewer = @{ login = $(if ($mode -ceq 'wrong-reviewer') { 'other-user' } else { 'alexyorke' }) } }) } })
        if ($mode -ceq 'duplicate-reviewer-rule') { $rules += $rules[0] }
        if ($mode -ceq 'team-reviewer') { $rules[0].reviewers[0].type = 'Team' }
        if ($mode -ceq 'empty-reviewer-rule') { $rules[0].reviewers = @() }
        return ([ordered]@{ protection_rules = $rules; deployment_branch_policy = $(if ($mode -ceq 'missing-branch-policy') { $null } else { @{ custom_branch_policies = $true } }) } | ConvertTo-Json -Depth 8 -Compress)
    }
    throw 'Unexpected fixture API endpoint; no real GitHub command is permitted.'
}.GetNewClosure()
Set-Item Function:\global:gh -Value $ghMock
try {
    $env:GITHUB_REPOSITORY = 'alexyorke/SharpProof-preview'
    $env:SHARPPROOF_PRIVATE_PREVIEW = 'true'
    $env:SHARPPROOF_WORKFLOW_COMMIT = $workflow
    $env:GITHUB_WORKFLOW_SHA = $workflow
    $env:GITHUB_SHA = $workflow
    $env:GITHUB_EVENT_NAME = 'workflow_dispatch'
    $env:GITHUB_WORKFLOW_REF = 'alexyorke/SharpProof-preview/.github/workflows/private-preview-promote.yml@refs/heads/main'
    $env:GITHUB_REF = 'refs/heads/main'
    $env:GITHUB_ACTOR = 'alexyorke'
    $env:GITHUB_TRIGGERING_ACTOR = 'alexyorke'
    $env:GH_TOKEN = 'fixture-only-token-no-network'
    $fixture.Mode = 'valid'
    & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1')
    $controls++
    $env:GITHUB_WORKFLOW_REF = 'alexyorke/SharpProof-preview/.github/workflows/private-preview.yml@refs/heads/main'
    $approvedOutput = Join-Path $scratch 'approved-output.txt'
    & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewSource.ps1') -SourceCommit $source -WorkflowCommit $workflow -OutputPath (Join-Path $scratch 'source.json') -GitHubOutputPath $approvedOutput
    $controls++
    if ([IO.File]::ReadAllText($approvedOutput) -cne "source_commit=$source`nworkflow_commit=$workflow`n") { throw 'Preflight did not emit exactly the two qualified immutable commit identities.' }
    $controls++
    $env:GITHUB_WORKFLOW_REF = 'alexyorke/SharpProof-preview/.github/workflows/private-preview-promote.yml@refs/heads/main'
    $fixture.Mode = 'missing-reviewer'
    & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1')
    $controls++
    foreach ($mode in @('wrong-reviewer', 'duplicate-reviewer-rule', 'team-reviewer', 'empty-reviewer-rule', 'missing-branch-policy', 'wrong-branch', 'public-repository')) {
        $fixture.Mode = $mode
        Assert-PolicyRejection $mode { & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1') }
    }
    $fixture.Mode = 'manual-nightly'
    $env:GITHUB_WORKFLOW_REF = 'alexyorke/SharpProof-preview/.github/workflows/private-preview.yml@refs/heads/main'
    & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewSource.ps1') -SourceCommit $source -WorkflowCommit $workflow -OutputPath (Join-Path $scratch 'source.json')
    $controls++
    $rejectedOutput = Join-Path $scratch 'rejected-output.txt'
    foreach ($mode in @('non-master-source', 'pending-source-gate', 'failed-source-gate', 'missing-nightly', 'pending-nightly', 'failed-nightly', 'pull-request-nightly')) {
        $env:GITHUB_WORKFLOW_REF = 'alexyorke/SharpProof-preview/.github/workflows/private-preview.yml@refs/heads/main'
        $fixture.Mode = $mode
        Assert-PolicyRejection $mode { & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewSource.ps1') -SourceCommit $source -WorkflowCommit $workflow -OutputPath (Join-Path $scratch 'source.json') -GitHubOutputPath $rejectedOutput }
    }
    if (Test-Path -LiteralPath $rejectedOutput) { throw 'Rejected source qualification emitted a trusted checkout identity.' }
    $controls++
    $env:GITHUB_WORKFLOW_REF = 'alexyorke/SharpProof-preview/.github/workflows/private-preview-promote.yml@refs/heads/main'
    $fixture.Mode = 'valid'
    $env:SHARPPROOF_WORKFLOW_COMMIT = '4' * 40
    Assert-PolicyRejection 'workflow mismatch' { & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1') }
    $env:SHARPPROOF_WORKFLOW_COMMIT = $workflow
    $env:GITHUB_SHA = '4' * 40
    Assert-PolicyRejection 'caller code differs from approved workflow' { & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1') }
    $env:GITHUB_SHA = $workflow
    $env:GITHUB_EVENT_NAME = 'push'
    Assert-PolicyRejection 'non-manual publication' { & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1') }
    $env:GITHUB_EVENT_NAME = 'workflow_dispatch'
    foreach ($actor in @('', 'other-user')) {
        $env:GITHUB_ACTOR = $actor
        Assert-PolicyRejection 'missing or foreign original actor' { & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1') }
    }
    $env:GITHUB_ACTOR = 'alexyorke'
    foreach ($actor in @('', 'other-user')) {
        $env:GITHUB_TRIGGERING_ACTOR = $actor
        Assert-PolicyRejection 'missing or foreign rerun actor' { & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1') }
    }
    $env:GITHUB_TRIGGERING_ACTOR = 'alexyorke'
    $env:GITHUB_REF = 'refs/heads/other'
    Assert-PolicyRejection 'off-main publication' { & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1') }
    $env:GITHUB_REF = 'refs/heads/main'
    $env:GITHUB_WORKFLOW_REF = 'alexyorke/SharpProof-preview/.github/workflows/remote-caller.yml@refs/heads/main'
    Assert-PolicyRejection 'remote reusable caller identity' { & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1') }
    Assert-PolicyRejection 'source qualification remote reusable caller identity' { & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewSource.ps1') -SourceCommit $source -WorkflowCommit $workflow -OutputPath (Join-Path $scratch 'source.json') }
    $env:GITHUB_WORKFLOW_REF = 'alexyorke/SharpProof-preview/.github/workflows/private-preview-promote.yml@refs/heads/main'
    $env:GITHUB_REPOSITORY = 'alexyorke/SharpProof'
    Assert-PolicyRejection 'public caller' { & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1') }
    $env:GITHUB_REPOSITORY = 'other-user/SharpProof-preview'
    Assert-PolicyRejection 'fork caller' { & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPublicationPolicy.ps1') }
    Write-Host "Private preview policy regressions passed: $controls controls. Mocked read-only API only; no real access, signing, or publication."
}
finally {
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name]) }
    if ($null -ne $previousGh) { Set-Item Function:\global:gh -Value $previousGh.ScriptBlock } else { Remove-Item Function:\global:gh -ErrorAction SilentlyContinue }
    $resolved = [IO.Path]::GetFullPath($scratch)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd([IO.Path]::DirectorySeparatorChar) -cne [IO.Path]::GetTempPath().TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::GetFileName($resolved) -cnotmatch '\Asharpproof-private-policy-[a-f0-9]{32}\z') { throw 'Refusing unsafe private policy fixture cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
