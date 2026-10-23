Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-SharpProofPrivatePreviewArtifactOrigin {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Repository,
        [Parameter(Mandatory)]$Artifact,
        [Parameter(Mandatory)]$Run,
        [Parameter(Mandatory)][ValidatePattern('\A[1-9][0-9]*\z')][string]$ArtifactId,
        [Parameter(Mandatory)][ValidateSet('Candidate', 'Acceptance')][string]$Kind,
        [Parameter(Mandatory)][ValidatePattern('\A[a-f0-9]{40}\z')][string]$ExpectedWorkflowCommit
    )
    if ($Repository.private -isnot [bool] -or -not $Repository.private -or $Repository.full_name -cne 'alexyorke/SharpProof-preview') { throw 'Artifact repository is not the fixed private repository.' }
    if ([string]$Artifact.id -cne $ArtifactId -or $Artifact.expired -isnot [bool] -or $Artifact.expired -or $Artifact.digest -cnotmatch '\Asha256:[a-f0-9]{64}\z') { throw 'Artifact must have a live immutable SHA256 identity.' }
    $workflowPath = if ($Kind -ceq 'Candidate') { '.github/workflows/private-preview.yml' } else { '.github/workflows/private-preview-receipts.yml' }
    if ($Run.repository.id -ne $Repository.id -or $Artifact.workflow_run.repository_id -ne $Repository.id -or
        $Artifact.workflow_run.head_repository_id -ne $Repository.id -or $Run.id -ne $Artifact.workflow_run.id -or
        $Run.head_sha -cne $ExpectedWorkflowCommit -or $Artifact.workflow_run.head_sha -cne $ExpectedWorkflowCommit -or
        $Run.head_branch -cne 'main' -or $Artifact.workflow_run.head_branch -cne 'main' -or
        $Run.event -cne 'workflow_dispatch' -or $Run.path -cnotin @($workflowPath, "$workflowPath@main", "$workflowPath@refs/heads/main", "$workflowPath@$ExpectedWorkflowCommit") -or $Run.status -cne 'completed' -or
        $Run.conclusion -cne 'success' -or $Run.actor.login -cne 'alexyorke' -or $Run.triggering_actor.login -cne 'alexyorke') { throw 'Artifact origin is not an approved successful owner-dispatched private main workflow.' }
    $expectedName = if ($Kind -ceq 'Candidate') { "private-preview-candidate-$($Run.id)-$($Run.run_attempt)" } else { "private-preview-acceptance-$($Run.id)-$($Run.run_attempt)" }
    if ($Artifact.name -cne $expectedName) { throw 'Artifact name does not match its immutable originating run.' }
}

Export-ModuleMember -Function Assert-SharpProofPrivatePreviewArtifactOrigin
