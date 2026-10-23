[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('\A[a-f0-9]{40}\z')][string]$SourceCommit,
    [Parameter(Mandatory)][ValidatePattern('\A[a-f0-9]{40}\z')][string]$WorkflowCommit,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_REPOSITORY -cne 'alexyorke/SharpProof-preview' -or
    $env:GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or $env:GITHUB_REF -cne 'refs/heads/main' -or
    $env:GITHUB_WORKFLOW_REF -cne 'alexyorke/SharpProof-preview/.github/workflows/private-preview.yml@refs/heads/main' -or
    $env:GITHUB_SHA -cne $WorkflowCommit -or $env:GITHUB_WORKFLOW_SHA -cne $WorkflowCommit -or [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
    throw 'Private preview orchestration requires its private repository, approved workflow SHA, and authenticated API access.'
}
function Read-GitHubJson([string]$Endpoint) {
    $json = & gh api --method GET $Endpoint
    if ($LASTEXITCODE -ne 0) { throw "GitHub source qualification request failed: $Endpoint" }
    return ($json | ConvertFrom-Json)
}
$privateRepository = Read-GitHubJson 'repos/alexyorke/SharpProof-preview'
if (-not $privateRepository.private -or $privateRepository.visibility -cne 'private') { throw 'Release repository must remain private.' }
$publicRepository = Read-GitHubJson 'repos/alexyorke/SharpProof'
if ($publicRepository.private -or $publicRepository.default_branch -cne 'master') { throw 'Expected the canonical public master source repository.' }
$comparison = Read-GitHubJson "repos/alexyorke/SharpProof/compare/$SourceCommit...master"
if ($comparison.status -cnotin @('ahead', 'identical') -or $comparison.merge_base_commit.sha -cne $SourceCommit) {
    throw 'Approved source must be an exact commit on canonical public master.'
}
$gates = [Collections.Generic.List[object]]::new()
foreach ($workflow in @('ci.yml', 'coverage.yml', 'toolchain-security.yml', 'package-consumers.yml', 'docs-maintenance.yml', 'nightly.yml')) {
    $allowedEvents = if ($workflow -ceq 'nightly.yml') { @('schedule', 'workflow_dispatch') } else { @('push') }
    $endpoint = "repos/alexyorke/SharpProof/actions/workflows/$workflow/runs?branch=master&head_sha=$SourceCommit&per_page=100"
    if ($workflow -cne 'nightly.yml') { $endpoint += '&event=push' }
    $runs = @(Read-GitHubJson $endpoint).workflow_runs
    $latest = @($runs | Where-Object { $_.head_sha -ceq $SourceCommit -and $_.head_branch -ceq 'master' -and $_.event -cin $allowedEvents } |
            Sort-Object run_number -Descending | Select-Object -First 1)
    if ($latest.Count -ne 1 -or $latest[0].status -cne 'completed' -or $latest[0].conclusion -cne 'success') {
        throw "Private preview rejected: latest exact public-master $workflow allowed-event run is absent, pending, or unsuccessful."
    }
    $gates.Add([ordered]@{ workflow = $workflow; event = $latest[0].event; runId = $latest[0].id; runNumber = $latest[0].run_number; conclusion = $latest[0].conclusion; url = $latest[0].html_url })
}
$receipt = [ordered]@{
    schemaVersion = 1; sourceRepository = 'alexyorke/SharpProof'; sourceCommit = $SourceCommit
    releaseRepository = 'alexyorke/SharpProof-preview'; workflowCommit = $WorkflowCommit
    sourceBranch = 'master'; gates = @($gates.ToArray())
}
$fullOutput = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput))
[IO.File]::WriteAllText($fullOutput, ($receipt | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
Write-Host 'Approved source is on public master and all six exact-source workflows, including the latest nightly, passed; release repository remains private.'
