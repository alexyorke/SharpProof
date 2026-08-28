[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$canonicalWorkflow = [IO.File]::ReadAllText((Join-Path $repositoryRoot (
        '.github/workflows/package-consumers.yml')))
$fixture = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-release-configuration-' + [Guid]::NewGuid().ToString('N'))
$mockBin = Join-Path $fixture 'mock-bin'
$apiRoot = Join-Path $fixture 'api'
New-Item -ItemType Directory -Path `
    (Join-Path $fixture 'scripts'), `
    (Join-Path $fixture 'eng/acceptance'), `
    (Join-Path $fixture 'eng/release'), `
    (Join-Path $fixture '.github/workflows'), `
    $mockBin, $apiRoot -Force | Out-Null

function Write-Json([string]$Name, [object]$Value) {
    $Value | ConvertTo-Json -Depth 12 | Set-Content `
        -LiteralPath (Join-Path $apiRoot ($Name + '.json')) `
        -Encoding utf8NoBOM
}

function New-State {
    [pscustomobject]@{
        Contract = Get-Content -LiteralPath (Join-Path $repositoryRoot (
                'eng/release/environment-contract.json')) -Raw |
            ConvertFrom-Json
        Rulesets = @([pscustomobject]@{
                id = 7; target = 'tag'; enforcement = 'active'
            })
        RulesetsFirstPage = @([pscustomobject]@{
                id = 7; target = 'tag'; enforcement = 'active'
            })
        Ruleset = [pscustomobject]@{
            id = 7
            bypass_actors = @()
            conditions = [pscustomobject]@{
                ref_name = [pscustomobject]@{
                    include = @('refs/tags/v1.0.0*', 'refs/tags/evidence/v*')
                    exclude = @()
                }
            }
            rules = @(
                [pscustomobject]@{ type = 'deletion' },
                [pscustomobject]@{ type = 'update' })
        }
        PrivatePolicies = @([pscustomobject]@{
                type = 'tag'; name = 'v1.0.0-preview.1'
            })
        PublicPolicies = @(
            [pscustomobject]@{ type = 'tag'; name = 'v1.0.0-preview.2' },
            [pscustomobject]@{ type = 'tag'; name = 'v1.0.0-rc.1' },
            [pscustomobject]@{ type = 'tag'; name = 'v1.0.0' })
        PrivateVariables = @('NUGET_PRIVATE_SOURCE')
        PublicVariables = @('NUGET_USER')
        PrivateSecrets = @('NUGET_PRIVATE_API_KEY')
        PublicSecrets = @()
        Workflow = $canonicalWorkflow
    }
}

function Invoke-Case {
    param(
        [string]$Name,
        [scriptblock]$Mutate,
        [bool]$ExpectedSuccess
    )

    $state = New-State
    & $Mutate $state
    $state.Contract | ConvertTo-Json -Depth 12 | Set-Content `
        -LiteralPath (Join-Path $fixture 'eng/release/environment-contract.json') `
        -Encoding utf8NoBOM
    Write-Json rulesets $state.Rulesets
    Write-Json rulesets-first-page $state.RulesetsFirstPage
    Write-Json ruleset $state.Ruleset
    Write-Json private-environment ([pscustomobject]@{
            deployment_branch_policy = [pscustomobject]@{
                protected_branches = $false
                custom_branch_policies = $true
            }
        })
    Write-Json public-environment ([pscustomobject]@{
            deployment_branch_policy = [pscustomobject]@{
                protected_branches = $false
                custom_branch_policies = $true
            }
        })
    Write-Json private-policies ([pscustomobject]@{
            branch_policies = @($state.PrivatePolicies)
        })
    Write-Json public-policies ([pscustomobject]@{
            branch_policies = @($state.PublicPolicies)
        })
    Write-Json private-variables ([pscustomobject]@{
            variables = @($state.PrivateVariables | ForEach-Object {
                    [pscustomobject]@{ name = $_ }
                })
        })
    Write-Json public-variables ([pscustomobject]@{
            variables = @($state.PublicVariables | ForEach-Object {
                    [pscustomobject]@{ name = $_ }
                })
        })
    Write-Json private-secrets ([pscustomobject]@{
            secrets = @($state.PrivateSecrets | ForEach-Object {
                    [pscustomobject]@{ name = $_ }
                })
        })
    Write-Json public-secrets ([pscustomobject]@{
            secrets = @($state.PublicSecrets | ForEach-Object {
                    [pscustomobject]@{ name = $_ }
                })
        })
    [IO.File]::WriteAllText(
        (Join-Path $fixture '.github/workflows/package-consumers.yml'),
        [string]$state.Workflow,
        [Text.UTF8Encoding]::new($false))

    $outputPath = Join-Path $fixture "artifacts/$Name.json"
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
    [IO.File]::WriteAllText(
        $outputPath,
        '{"status":"passed","commit":"stale"}',
        [Text.UTF8Encoding]::new($false))
    $output = & pwsh -NoLogo -NoProfile -File (
        Join-Path $fixture 'scripts/Test-SharpProofReleaseConfiguration.ps1') `
        -OutputPath "artifacts/$Name.json" 2>&1
    $success = $LASTEXITCODE -eq 0
    if ($success -ne $ExpectedSuccess) {
        throw "Release configuration fixture '$Name' expected success=${ExpectedSuccess}: $output"
    }
    if ($success -and -not (Test-Path -LiteralPath (
                Join-Path $fixture "artifacts/$Name.json") -PathType Leaf)) {
        throw "Release configuration fixture '$Name' wrote no evidence."
    }
    if (-not $success -and (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
        throw "Release configuration fixture '$Name' preserved stale evidence."
    }
}

function Invoke-ReceiptCase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$EvidencePath,
        [Parameter(Mandatory = $true)][bool]$ExpectedSuccess
    )

    $receiptDirectory = Join-Path $fixture 'artifacts/receipts'
    $output = & pwsh -NoLogo -NoProfile -File (
        Join-Path $fixture 'scripts/Write-SharpProofQualificationReceipt.ps1') `
        -Gate release-configuration `
        -EvidencePath $EvidencePath `
        -RepositoryRoot $fixture `
        -ReceiptDirectory $receiptDirectory 2>&1
    $success = $LASTEXITCODE -eq 0
    if ($success -ne $ExpectedSuccess) {
        throw "Receipt fixture '$Name' expected success=${ExpectedSuccess}: $output"
    }
    $receiptPath = Join-Path $receiptDirectory 'release-configuration.json'
    if ($ExpectedSuccess -and -not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
        throw "Receipt fixture '$Name' did not write a receipt."
    }
    if (-not $ExpectedSuccess -and (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
        throw "Receipt fixture '$Name' preserved a stale receipt."
    }
}

function Invoke-AcceptanceReceiptCase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Gate,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [Parameter(Mandatory = $true)][bool]$ExpectedSuccess
    )

    $evidencePath = Join-Path $fixture "artifacts/$Name.json"
    $commit = (& git -C $fixture rev-parse HEAD).Trim()
    $phases = @(
        (Get-Content -LiteralPath (Join-Path $fixture 'eng/acceptance/contract.json') -Raw |
            ConvertFrom-Json).automation.acceptanceTimingPhases |
            ForEach-Object {
                [pscustomobject]@{ name = [string]$_; status = 'passed' }
            })
    [ordered]@{
        schemaVersion = 1
        command = 'acceptance'
        configuration = $Configuration
        commit = $commit
        status = 'passed'
        phases = $phases
    } | ConvertTo-Json | Set-Content -LiteralPath $evidencePath -Encoding utf8NoBOM
    $receiptDirectory = Join-Path $fixture 'artifacts/acceptance-receipts'
    $output = & pwsh -NoLogo -NoProfile -File (
        Join-Path $fixture 'scripts/Write-SharpProofQualificationReceipt.ps1') `
        -Gate $Gate `
        -EvidencePath $evidencePath `
        -RepositoryRoot $fixture `
        -ReceiptDirectory $receiptDirectory 2>&1
    $success = $LASTEXITCODE -eq 0
    if ($success -ne $ExpectedSuccess) {
        throw "Acceptance receipt fixture '$Name' expected success=${ExpectedSuccess}: $output"
    }
    $receiptPath = Join-Path $receiptDirectory ($Gate + '.json')
    if ($ExpectedSuccess) {
        if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
            throw "Acceptance receipt fixture '$Name' did not write a receipt."
        }
        $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
        if ([string]$receipt.configuration -cne $Configuration) {
            throw "Acceptance receipt fixture '$Name' lost its configuration binding."
        }
    }
    elseif (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
        throw "Acceptance receipt fixture '$Name' preserved a stale receipt."
    }
}

function Invoke-QualificationTombstoneCase {
    $directory = Join-Path $fixture 'artifacts/release-qualification'
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $path = Join-Path $directory 'qualification.json'
    [IO.File]::WriteAllText(
        $path,
        '{"status":"passed","commit":"stale"}',
        [Text.UTF8Encoding]::new($false))
    $oldSha = $env:GITHUB_SHA
    $oldContainer = $env:SHARPPROOF_CONTAINER
    try {
        $env:GITHUB_SHA = $null
        $env:SHARPPROOF_CONTAINER = '1'
        $output = & pwsh -NoLogo -NoProfile -File (
            Join-Path $fixture 'scripts/Invoke-SharpProofReleaseContainer.ps1') `
            -Mode WriteQualificationEvidence -PackageSource nupkgs 2>&1
        if ($LASTEXITCODE -eq 0) {
            throw 'Qualification tombstone fixture unexpectedly succeeded.'
        }
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            throw "Qualification tombstone fixture preserved stale evidence: $output"
        }
    }
    finally {
        $env:GITHUB_SHA = $oldSha
        $env:SHARPPROOF_CONTAINER = $oldContainer
    }
}

function Invoke-PilotTombstoneCase {
    $reportDirectory = Join-Path $fixture 'artifacts/pilots'
    $receiptDirectory = Join-Path $fixture `
        'artifacts/release-qualification/qualification-receipts'
    [IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
    [IO.Directory]::CreateDirectory($receiptDirectory) | Out-Null
    $reportPath = Join-Path $reportDirectory 'report.json'
    $receiptPath = Join-Path $receiptDirectory 'pilots.json'
    [IO.File]::WriteAllText(
        $reportPath,
        '{"status":"passed","commit":"stale"}',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        $receiptPath,
        '{"status":"passed","commit":"stale"}',
        [Text.UTF8Encoding]::new($false))
    $oldContainer = $env:SHARPPROOF_CONTAINER
    try {
        $env:SHARPPROOF_CONTAINER = '1'
        $output = & pwsh -NoLogo -NoProfile -File (
            Join-Path $fixture 'scripts/Test-SharpProofPilots.ps1') `
            -PackageSource artifacts/missing 2>&1
        if ($LASTEXITCODE -eq 0) {
            throw 'Pilot tombstone fixture unexpectedly succeeded.'
        }
        if ((Test-Path -LiteralPath $reportPath -PathType Leaf) -or
            (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
            throw "Pilot tombstone fixture preserved stale evidence: $output"
        }
    }
    finally {
        $env:SHARPPROOF_CONTAINER = $oldContainer
    }
}

try {
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Test-SharpProofReleaseConfiguration.ps1') `
        -Destination (Join-Path $fixture 'scripts/Test-SharpProofReleaseConfiguration.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Resolve-SharpProofContainedPath.ps1') `
        -Destination (Join-Path $fixture 'scripts/Resolve-SharpProofContainedPath.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'eng/release/environment-contract.json') `
        -Destination (Join-Path $fixture 'eng/release/environment-contract.json')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot '.github/workflows/package-consumers.yml') `
        -Destination (Join-Path $fixture '.github/workflows/package-consumers.yml')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Write-SharpProofQualificationReceipt.ps1') `
        -Destination (Join-Path $fixture 'scripts/Write-SharpProofQualificationReceipt.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/SharpProof.AcceptanceEvidence.psm1') `
        -Destination (Join-Path $fixture 'scripts/SharpProof.AcceptanceEvidence.psm1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/SharpProof.MutationEvidence.psm1') `
        -Destination (Join-Path $fixture 'scripts/SharpProof.MutationEvidence.psm1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/SharpProof.ReleaseConfigurationEvidence.psm1') `
        -Destination (Join-Path $fixture 'scripts/SharpProof.ReleaseConfigurationEvidence.psm1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Invoke-SharpProofReleaseContainer.ps1') `
        -Destination (Join-Path $fixture 'scripts/Invoke-SharpProofReleaseContainer.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Get-SharpProofReleaseVersion.ps1') `
        -Destination (Join-Path $fixture 'scripts/Get-SharpProofReleaseVersion.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Test-SharpProofPilots.ps1') `
        -Destination (Join-Path $fixture 'scripts/Test-SharpProofPilots.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Get-SharpProofPilotPackageAuthority.ps1') `
        -Destination (Join-Path $fixture 'scripts/Get-SharpProofPilotPackageAuthority.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/SharpProof.ContainerExecution.psm1') `
        -Destination (Join-Path $fixture 'scripts/SharpProof.ContainerExecution.psm1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'SharpProof.Release.props') `
        -Destination (Join-Path $fixture 'SharpProof.Release.props')
    New-Item -ItemType Directory -Path (
        Join-Path $fixture 'eng/acceptance'), (
        Join-Path $fixture 'eng/pilots') -Force | Out-Null
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'eng/pilots/catalog.json') `
        -Destination (Join-Path $fixture 'eng/pilots/catalog.json')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'eng/acceptance/contract.json') `
        -Destination (Join-Path $fixture 'eng/acceptance/contract.json')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Test-SharpProofPilotReport.ps1') `
        -Destination (Join-Path $fixture 'scripts/Test-SharpProofPilotReport.ps1')
@'
#!/bin/sh
paginate=0
endpoint=""
for argument in "$@"; do
  case "$argument" in
    --paginate) paginate=1 ;;
    repos/*) endpoint="$argument" ;;
  esac
done
case "$endpoint" in
  repos/alexyorke/SharpProof/rulesets)
    if [ "$paginate" -eq 1 ]; then file="rulesets"; else file="rulesets-first-page"; fi ;;
  repos/alexyorke/SharpProof/rulesets/7) file="ruleset" ;;
  */environments/nuget.private-preview/deployment-branch-policies) file="private-policies" ;;
  */environments/nuget.org/deployment-branch-policies) file="public-policies" ;;
  */environments/nuget.private-preview/variables) file="private-variables" ;;
  */environments/nuget.org/variables) file="public-variables" ;;
  */environments/nuget.private-preview/secrets) file="private-secrets" ;;
  */environments/nuget.org/secrets) file="public-secrets" ;;
  */environments/nuget.private-preview) file="private-environment" ;;
  */environments/nuget.org) file="public-environment" ;;
  *) exit 3 ;;
esac
cat "$GH_FIXTURE_ROOT/$file.json"
'@ | Set-Content -LiteralPath (Join-Path $mockBin 'gh') -Encoding utf8NoBOM
    & chmod +x (Join-Path $mockBin 'gh')
    if ($LASTEXITCODE -ne 0) { throw 'Could not make the fixture gh executable.' }
    & git -C $fixture init --quiet
    & git -C $fixture config user.email fixture@sharpproof.test
    & git -C $fixture config user.name 'SharpProof Fixture'
    & git -C $fixture add -- .
    & git -C $fixture commit --quiet -m fixture
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize fixture repository.' }

    $oldPath = $env:PATH
    $oldFixtureRoot = $env:GH_FIXTURE_ROOT
    $env:PATH = $mockBin + [IO.Path]::PathSeparator + $oldPath
    $env:GH_FIXTURE_ROOT = $apiRoot
    try {
        Invoke-Case exact-contract { param($state) } $true
        Invoke-Case empty-tags { param($state)
            $state.PrivatePolicies = @()
            ($state.Contract.environments | Where-Object name -eq 'nuget.private-preview').tags = @()
        } $true
        Invoke-Case empty-variables { param($state)
            $state.PublicVariables = @()
            ($state.Contract.environments | Where-Object name -eq 'nuget.org').variables = @()
        } $true
        Invoke-Case multiple-variables { param($state)
            $state.PublicVariables += 'NUGET_AUDIT_USER'
            ($state.Contract.environments | Where-Object name -eq 'nuget.org').variables += 'NUGET_AUDIT_USER'
        } $true
        Invoke-Case multiple-secrets { param($state)
            $state.PrivateSecrets += 'NUGET_PRIVATE_AUDIT_KEY'
            ($state.Contract.environments | Where-Object name -eq 'nuget.private-preview').secrets += 'NUGET_PRIVATE_AUDIT_KEY'
        } $true
        Invoke-Case unexpected-public-secret { param($state)
            $state.PublicSecrets += 'UNEXPECTED_SECRET'
        } $false
        Invoke-Case missing-secret { param($state)
            $state.PrivateSecrets = @()
        } $false
        Invoke-Case missing-variable { param($state)
            $state.PrivateVariables = @()
        } $false
        Invoke-Case extra-variable { param($state)
            $state.PrivateVariables += 'UNEXPECTED_VARIABLE'
        } $false
        Invoke-Case duplicate-variable { param($state)
            $state.PrivateVariables += $state.PrivateVariables[0]
        } $false
        Invoke-Case variable-case { param($state)
            $state.PrivateVariables[0] = 'nuget_private_source'
        } $false
        Invoke-Case duplicate-secret { param($state)
            $state.PrivateSecrets += $state.PrivateSecrets[0]
        } $false
        Invoke-Case secret-case { param($state)
            $state.PrivateSecrets[0] = 'nuget_private_api_key'
        } $false
        Invoke-Case wildcard { param($state) $state.PrivatePolicies += [pscustomobject]@{ type = 'tag'; name = '*' } } $false
        Invoke-Case extra-exact-tag { param($state) $state.PrivatePolicies += [pscustomobject]@{ type = 'tag'; name = 'v1.0.0-preview.99' } } $false
        Invoke-Case branch-policy { param($state) $state.PrivatePolicies += [pscustomobject]@{ type = 'branch'; name = 'master' } } $false
        Invoke-Case missing-tag { param($state) $state.PublicPolicies = @($state.PublicPolicies | Select-Object -Skip 1) } $false
        Invoke-Case duplicate-tag { param($state) $state.PrivatePolicies += $state.PrivatePolicies[0] } $false
        Invoke-Case case-difference { param($state) $state.PrivatePolicies[0].name = 'V1.0.0-preview.1' } $false
        Invoke-Case extra-ruleset-include { param($state) $state.Ruleset.conditions.ref_name.include += 'refs/tags/*' } $false
        Invoke-Case missing-ruleset-include { param($state) $state.Ruleset.conditions.ref_name.include = @($state.Ruleset.conditions.ref_name.include | Select-Object -Skip 1) } $false
        Invoke-Case duplicate-ruleset-include { param($state) $state.Ruleset.conditions.ref_name.include += $state.Ruleset.conditions.ref_name.include[0] } $false
        Invoke-Case ruleset-case { param($state) $state.Ruleset.conditions.ref_name.include[0] = 'refs/tags/V1.0.0*' } $false
        Invoke-Case include-exclude-conflict { param($state) $state.Ruleset.conditions.ref_name.exclude = @($state.Ruleset.conditions.ref_name.include[0]) } $false
        Invoke-Case second-active-tag-ruleset { param($state)
            $state.Rulesets = @(
                $state.Rulesets
                1..29 | ForEach-Object {
                    [pscustomobject]@{
                        id = 100 + $_; target = 'branch'; enforcement = 'active'
                    }
                }
                [pscustomobject]@{ id = 8; target = 'tag'; enforcement = 'active' }
            )
            $state.RulesetsFirstPage = @($state.Rulesets | Select-Object -First 30)
        } $false
        Invoke-Case bypass-user-always { param($state) $state.Ruleset.bypass_actors = @([pscustomobject]@{ actor_id = 41; actor_type = 'User'; bypass_mode = 'always' }) } $false
        Invoke-Case bypass-team-pull-request { param($state) $state.Ruleset.bypass_actors = @([pscustomobject]@{ actor_id = 42; actor_type = 'Team'; bypass_mode = 'pull_request' }) } $false
        Invoke-Case bypass-app-always { param($state) $state.Ruleset.bypass_actors = @([pscustomobject]@{ actor_id = 49; actor_type = 'Integration'; bypass_mode = 'always' }) } $false
        Invoke-Case bypass-unknown-actor { param($state) $state.Ruleset.bypass_actors = @([pscustomobject]@{ actor_id = 43; actor_type = 'Unknown'; bypass_mode = 'always' }) } $false
        Invoke-Case bypass-extra-role { param($state) $state.Ruleset.bypass_actors = @([pscustomobject]@{ actor_id = 5; actor_type = 'RepositoryRole'; bypass_mode = 'always' }) } $false
        Invoke-Case bypass-missing-mode { param($state) $state.Ruleset.bypass_actors = @([pscustomobject]@{ actor_id = 44; actor_type = 'Integration' }) } $false
        Invoke-Case bypass-duplicate { param($state) $actor = [pscustomobject]@{ actor_id = 45; actor_type = 'Team'; bypass_mode = 'pull_request' }; $state.Ruleset.bypass_actors = @($actor, $actor) } $false
        Invoke-Case bypass-type-case { param($state) $state.Ruleset.bypass_actors = @([pscustomobject]@{ actor_id = 46; actor_type = 'user'; bypass_mode = 'always' }) } $false
        Invoke-Case bypass-id-mismatch { param($state) $state.Ruleset.bypass_actors = @([pscustomobject]@{ actor_id = '47'; actor_type = 'User'; bypass_mode = 'always' }) } $false
        Invoke-Case bypass-mode-case { param($state) $state.Ruleset.bypass_actors = @([pscustomobject]@{ actor_id = 48; actor_type = 'User'; bypass_mode = 'Always' }) } $false
        Invoke-Case bypass-unknown-mode { param($state) $state.Ruleset.bypass_actors = @([pscustomobject]@{ actor_id = 50; actor_type = 'User'; bypass_mode = 'sometimes' }) } $false
        Invoke-Case missing-bypass-policy { param($state) $state.Ruleset.PSObject.Properties.Remove('bypass_actors') } $false
        Invoke-Case extra-rule { param($state) $state.Ruleset.rules += [pscustomobject]@{ type = 'creation' } } $false
        Invoke-Case missing-rule { param($state) $state.Ruleset.rules = @($state.Ruleset.rules | Where-Object { $_.type -ne 'update' }) } $false
        Invoke-Case duplicate-rule { param($state) $state.Ruleset.rules += [pscustomobject]@{ type = 'deletion' } } $false
        Invoke-Case unexpected-rule-parameters { param($state) $state.Ruleset.rules[0] | Add-Member -NotePropertyName parameters -NotePropertyValue ([pscustomobject]@{ enabled = $true }) } $false
        Invoke-Case workflow-comment-decoy { param($state) $state.Workflow = $state.Workflow.Replace('    environment: nuget.private-preview', '    environment: unrelated-private # nuget.private-preview') } $false
        Invoke-Case workflow-dead-job-decoy { param($state) $state.Workflow = $state.Workflow.Replace('    environment: nuget.org', '    environment: unrelated-public') + "`n  dead-publish:`n    environment: nuget.org`n    steps: []`n" } $false
        Invoke-Case workflow-wrong-environment { param($state) $state.Workflow = $state.Workflow.Replace('    environment: nuget.private-preview', '    environment: unrelated-private') } $false
        Invoke-Case workflow-wrong-guard { param($state) $state.Workflow = $state.Workflow.Replace("github.ref_name == 'v1.0.0-preview.1'", "github.ref_name == 'v1.0.0-preview.99'") } $false
        Invoke-Case workflow-wrong-secret { param($state) $state.Workflow = $state.Workflow.Replace('secrets.NUGET_PRIVATE_API_KEY', 'secrets.UNRELATED_API_KEY') } $false
        Invoke-Case workflow-missing-oidc-permission { param($state) $state.Workflow = $state.Workflow.Replace('      id-token: write', '      id-token: read') } $false
        Invoke-Case workflow-wrong-needs { param($state) $state.Workflow = $state.Workflow.Replace('    needs: release-qualification', '    needs: package') } $false
        Invoke-Case workflow-reordered-steps { param($state) $pattern = [regex]::new('(?ms)(^      - name: Validate private-feed configuration\n.*?)(^      - name: Build the pinned toolchain\n        uses: ./\.github/actions/build-tooling\n)'); $state.Workflow = $pattern.Replace($state.Workflow, '$2$1', 1) } $false
        Invoke-Case workflow-missing-login { param($state) $pattern = [regex]::new('(?ms)^      - name: Exchange GitHub OIDC token for a temporary NuGet key\n.*?(?=^      - name: Build the pinned toolchain)'); $state.Workflow = $pattern.Replace($state.Workflow, '', 1) } $false
        Invoke-Case workflow-duplicate-key { param($state) $state.Workflow = $state.Workflow.Replace('    environment: nuget.private-preview', "    environment: nuget.private-preview`n    environment: nuget.private-preview") } $false
        Invoke-Case workflow-alias-step { param($state) $state.Workflow = $state.Workflow.Replace('      - name: Validate public-feed configuration', '      - &public-validation`n        name: Validate public-feed configuration') } $false
    }
    finally {
        $env:PATH = $oldPath
        $env:GH_FIXTURE_ROOT = $oldFixtureRoot
    }
    $exactEvidence = Join-Path $fixture 'artifacts/exact-contract.json'
    Invoke-ReceiptCase exact-contract $exactEvidence $true
    $staleEvidence = Join-Path $fixture 'artifacts/stale.json'
    $stale = Get-Content -LiteralPath $exactEvidence -Raw | ConvertFrom-Json
    $stale.checkedAtUtc = '2000-01-01T00:00:00.0000000+00:00'
    $stale | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $staleEvidence -Encoding utf8NoBOM
    Invoke-ReceiptCase stale-timestamp $staleEvidence $false
    $minimalEvidence = Join-Path $fixture 'artifacts/minimal.json'
    $fixtureCommit = (& git -C $fixture rev-parse HEAD).Trim()
    '{"schemaVersion":1,"commit":"' + $fixtureCommit + '"}' |
        Set-Content -LiteralPath $minimalEvidence -Encoding utf8NoBOM
    Invoke-ReceiptCase minimal-schema $minimalEvidence $false
    Invoke-AcceptanceReceiptCase acceptance-debug-debug acceptance-debug Debug $true
    Invoke-AcceptanceReceiptCase acceptance-release-release acceptance-release Release $true
    Invoke-AcceptanceReceiptCase acceptance-debug-release acceptance-debug Release $false
    Invoke-AcceptanceReceiptCase acceptance-release-debug acceptance-release Debug $false
    Invoke-QualificationTombstoneCase
    Invoke-PilotTombstoneCase
    Write-Host 'Release configuration exact-ref fixtures passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
