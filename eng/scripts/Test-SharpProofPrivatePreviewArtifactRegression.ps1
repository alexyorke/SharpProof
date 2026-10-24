[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Private artifact regressions require the canonical container.'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PrivatePreviewArtifact.psm1') -Force
$workflow = '1' * 40
$repositoryJson = '{"id":101,"private":true,"full_name":"alexyorke/SharpProof-preview"}'
$artifactJson = (@{ id = 202; expired = $false; digest = 'sha256:' + ('2' * 64); name = 'private-preview-candidate-303-1'; workflow_run = @{ id = 303; repository_id = 101; head_repository_id = 101; head_branch = 'main'; head_sha = $workflow } } | ConvertTo-Json -Depth 5 -Compress)
$runJson = (@{ id = 303; repository = @{ id = 101 }; head_sha = $workflow; head_branch = 'main'; event = 'workflow_dispatch'; path = '.github/workflows/private-preview.yml'; status = 'completed'; conclusion = 'success'; actor = @{ login = 'alexyorke' }; triggering_actor = @{ login = 'alexyorke' }; run_attempt = 1 } | ConvertTo-Json -Depth 5 -Compress)
$controls = 0
function Reset-ArtifactFixture {
    $script:repository = $repositoryJson | ConvertFrom-Json
    $script:artifact = $artifactJson | ConvertFrom-Json
    $script:run = $runJson | ConvertFrom-Json
}
function Test-ArtifactFixture([string]$Kind = 'Candidate') {
    Assert-SharpProofPrivatePreviewArtifactOrigin -Repository $repository -Artifact $artifact -Run $run -ArtifactId 202 -Kind $Kind -ExpectedWorkflowCommit $workflow
}
function Assert-ArtifactRejection([string]$Name, [scriptblock]$Mutation) {
    Reset-ArtifactFixture
    & $Mutation
    $rejected = $false
    try { Test-ArtifactFixture } catch { $rejected = $true }
    if (-not $rejected) { throw "Private artifact provenance accepted an invalid fixture: $Name" }
    $script:controls++
}
Reset-ArtifactFixture
Test-ArtifactFixture
$controls++
$run.path = '.github/workflows/private-preview.yml@refs/heads/main'
Test-ArtifactFixture
$controls++
$run.path = '.github/workflows/private-preview.yml@main'
Test-ArtifactFixture
$controls++
$run.path = ".github/workflows/private-preview.yml@$workflow"
Test-ArtifactFixture
$controls++
$artifact.name = 'private-preview-acceptance-303-1'
$run.path = '.github/workflows/private-preview-receipts.yml'
Test-ArtifactFixture Acceptance
$controls++
Assert-ArtifactRejection 'public repository' { $repository.private = $false }
Assert-ArtifactRejection 'different repository' { $repository.full_name = 'alexyorke/SharpProof' }
Assert-ArtifactRejection 'different artifact' { $artifact.id = 204 }
Assert-ArtifactRejection 'expired artifact' { $artifact.expired = $true }
Assert-ArtifactRejection 'missing SHA256 archive digest' { $artifact.digest = 'unavailable' }
Assert-ArtifactRejection 'wrong run repository' { $run.repository.id = 102 }
Assert-ArtifactRejection 'wrong artifact repository' { $artifact.workflow_run.repository_id = 102 }
Assert-ArtifactRejection 'fork origin' { $artifact.workflow_run.head_repository_id = 102 }
Assert-ArtifactRejection 'wrong run identity' { $run.id = 304 }
Assert-ArtifactRejection 'wrong workflow SHA' { $run.head_sha = '3' * 40 }
Assert-ArtifactRejection 'wrong artifact workflow SHA' { $artifact.workflow_run.head_sha = '3' * 40 }
Assert-ArtifactRejection 'wrong run branch' { $run.head_branch = 'other' }
Assert-ArtifactRejection 'wrong artifact branch' { $artifact.workflow_run.head_branch = 'other' }
Assert-ArtifactRejection 'nonmanual run' { $run.event = 'push' }
Assert-ArtifactRejection 'different workflow path' { $run.path = '.github/workflows/other.yml' }
Assert-ArtifactRejection 'different workflow ref suffix' { $run.path = '.github/workflows/private-preview.yml@refs/heads/other' }
Assert-ArtifactRejection 'incomplete run' { $run.status = 'in_progress' }
Assert-ArtifactRejection 'failed qualification' { $run.conclusion = 'failure' }
Assert-ArtifactRejection 'nonowner actor' { $run.actor.login = 'other' }
Assert-ArtifactRejection 'nonowner rerun' { $run.triggering_actor.login = 'other' }
Assert-ArtifactRejection 'different artifact name' { $artifact.name = 'private-preview-candidate-304-1' }
Assert-ArtifactRejection 'different attempt' { $run.run_attempt = 2 }
Write-Host "Private artifact origin regressions passed: $controls controls. Synthetic metadata only; no artifact download or real API claim."
