[CmdletBinding()]
param(
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
$contractPath = Join-Path $repositoryRoot 'eng\release\environment-contract.json'
$workflowPath = Join-Path $repositoryRoot '.github\workflows\package-consumers.yml'
$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
$workflow = Get-Content -LiteralPath $workflowPath -Raw

if ([int]$contract.schemaVersion -ne 1 -or
    [string]::IsNullOrWhiteSpace([string]$contract.repository)) {
    throw 'The release-environment contract is invalid.'
}
if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI is required to inspect release configuration.'
}

function Invoke-GitHubJson {
    param(
        [Parameter(Mandatory = $true)][string]$Endpoint,
        [switch]$Paginate
    )

    $arguments = if ($Paginate) {
        @('api', '--paginate', '--slurp', $Endpoint)
    }
    else {
        @('api', $Endpoint)
    }
    $output = & gh @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub API request failed for '$Endpoint': $output"
    }
    $json = ($output -join "`n") | ConvertFrom-Json
    if (-not $Paginate) {
        return $json
    }

    foreach ($page in @($json)) {
        if ($page -is [Array]) {
            foreach ($item in $page) {
                $item
            }
        }
        else {
            $page
        }
    }
}

function Require-SetMembers {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Actual,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Owner
    )

    Require-ExactSet -Actual $Actual -Expected $Expected -Owner $Owner
}

function Require-ExactSet {
    param(
        [AllowEmptyCollection()][string[]]$Actual = @(),
        [AllowEmptyCollection()][object[]]$Expected = @(),
        [Parameter(Mandatory = $true)][string]$Owner
    )

    $expectedStrings = @($Expected | ForEach-Object { [string]$_ })
    $actualUnique = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $expectedUnique = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    if (@($Actual | Where-Object { -not $actualUnique.Add($_) }).Count -ne 0 -or
        @($expectedStrings | Where-Object {
                -not $expectedUnique.Add($_)
            }).Count -ne 0 -or
        $actualUnique.Count -ne $expectedUnique.Count -or
        @($actualUnique | Where-Object {
                -not $expectedUnique.Contains($_)
            }).Count -ne 0) {
        throw "$Owner must equal the exact contract set."
    }
}

function Get-BypassActorIdentity {
    param(
        [Parameter(Mandatory = $true)][object]$Actor,
        [Parameter(Mandatory = $true)][bool]$ContractShape
    )

    $idName = if ($ContractShape) { 'actorId' } else { 'actor_id' }
    $typeName = if ($ContractShape) { 'actorType' } else { 'actor_type' }
    $modeName = if ($ContractShape) { 'bypassMode' } else { 'bypass_mode' }
    $expectedProperties = @($idName, $typeName, $modeName)
    $actualProperties = @($Actor.PSObject.Properties.Name)
    Require-ExactSet `
        -Actual $actualProperties `
        -Expected $expectedProperties `
        -Owner 'A release-tag bypass actor shape'

    $actorId = $Actor.$idName
    $actorType = [string]$Actor.$typeName
    $bypassMode = [string]$Actor.$modeName
    if ($null -eq $actorId -or
        [string]::IsNullOrWhiteSpace($actorType) -or
        [string]::IsNullOrWhiteSpace($bypassMode)) {
        throw 'A release-tag bypass actor identity is incomplete.'
    }
    $actorIdIdentity = $actorId | ConvertTo-Json -Compress -Depth 4
    return "$actorType|$actorIdIdentity|$bypassMode"
}

function Get-CanonicalWorkflowJob {
    param(
        [Parameter(Mandatory = $true)][string]$Yaml,
        [Parameter(Mandatory = $true)][string]$JobId
    )

    if ($Yaml.Contains("`t", [StringComparison]::Ordinal)) {
        throw 'The release workflow cannot contain YAML tab indentation.'
    }
    $normalized = $Yaml.Replace("`r`n", "`n").Replace("`r", "`n")
    $jobsHeaders = [regex]::Matches($normalized, '(?m)^jobs:\s*(?:#.*)?$')
    if ($jobsHeaders.Count -ne 1) {
        throw 'The release workflow must contain exactly one jobs mapping.'
    }
    $jobsText = $normalized.Substring(
        $jobsHeaders[0].Index + $jobsHeaders[0].Length).TrimStart("`n")
    $jobHeadings = [regex]::Matches(
        $jobsText,
        '(?m)^  (?<id>[A-Za-z0-9_-]+):\s*(?:#.*)?$')
    $duplicateJobIds = @($jobHeadings | Group-Object {
            $_.Groups['id'].Value
        } | Where-Object Count -ne 1)
    if ($duplicateJobIds.Count -ne 0) {
        throw 'The release workflow contains duplicate job keys.'
    }
    $heading = @($jobHeadings | Where-Object {
            $_.Groups['id'].Value -ceq $JobId
        })
    if ($heading.Count -ne 1) {
        throw "The release workflow must contain exactly one '$JobId' job."
    }
    $start = $heading[0].Index
    $nextHeading = @($jobHeadings | Where-Object Index -gt $start |
        Select-Object -First 1)
    $length = if ($nextHeading.Count -eq 0) {
        $jobsText.Length - $start
    }
    else {
        $nextHeading[0].Index - $start
    }
    $block = $jobsText.Substring($start, $length).TrimEnd("`n")
    if ([regex]::IsMatch($block, '(?m)^\s*(?:<<:|[^#\n]+:\s*[&*][A-Za-z0-9_-]+)')) {
        throw "Release job '$JobId' cannot use YAML aliases or merge keys."
    }
    return $block
}

function Get-Sha256Text {
    param([Parameter(Mandatory = $true)][string]$Value)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

$head = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw 'The release-configuration commit could not be resolved.'
}

$repository = [string]$contract.repository
$expectedWorkflowJobIds = @('publish-private-preview', 'publish')
Require-ExactSet `
    -Actual @($contract.workflowJobs | ForEach-Object { [string]$_.id }) `
    -Expected $expectedWorkflowJobIds `
    -Owner 'The release workflow job authority'
$workflowEvidence = @($contract.workflowJobs | ForEach-Object {
        $jobId = [string]$_.id
        $expectedHash = [string]$_.canonicalSha256
        if ($expectedHash -notmatch '^[0-9a-f]{64}$') {
            throw "Release job '$jobId' has an invalid canonical hash."
        }
        $actualHash = Get-Sha256Text (
            Get-CanonicalWorkflowJob -Yaml $workflow -JobId $jobId)
        if (-not $actualHash.Equals($expectedHash, [StringComparison]::Ordinal)) {
            throw "Release job '$jobId' does not equal its canonical structural contract."
        }
        [pscustomobject]@{ id = $jobId; canonicalSha256 = $actualHash }
    })
$rulesets = @(Invoke-GitHubJson "repos/$repository/rulesets" -Paginate)
$activeTagRulesets = @($rulesets |
    Where-Object { $_.target -ceq 'tag' -and $_.enforcement -ceq 'active' })
if ($activeTagRulesets.Count -ne 1) {
    throw 'Exactly one active tag ruleset must own the release tag policy.'
}
$tagRuleset = Invoke-GitHubJson (
    "repos/$repository/rulesets/$($activeTagRulesets[0].id)")
$rulesetIncludes = @($tagRuleset.conditions.ref_name.include |
    ForEach-Object { [string]$_ })
$rulesetExcludes = @($tagRuleset.conditions.ref_name.exclude |
    ForEach-Object { [string]$_ })
Require-ExactSet `
    -Actual $rulesetIncludes `
    -Expected @($contract.tagRuleset.includes) `
    -Owner 'The release-tag ruleset include policy'
$expectedRulesetExcludes = @($contract.tagRuleset.excludes)
if ($expectedRulesetExcludes.Count -eq 0) {
    if ($rulesetExcludes.Count -ne 0) {
        throw 'The release-tag ruleset exclude policy must equal the exact contract set.'
    }
}
else {
    Require-ExactSet `
        -Actual $rulesetExcludes `
        -Expected $expectedRulesetExcludes `
        -Owner 'The release-tag ruleset exclude policy'
}
if (@($rulesetIncludes | Where-Object {
            $rulesetExcludes -ccontains $_
        }).Count -ne 0) {
    throw 'The release-tag ruleset cannot both include and exclude a ref.'
}
$actualRules = @($tagRuleset.rules | ForEach-Object {
        $ruleProperties = @($_.PSObject.Properties.Name)
        if ('type' -notin $ruleProperties -or
            [string]::IsNullOrWhiteSpace([string]$_.type)) {
            throw 'A release-tag rule has no type.'
        }
        if ('parameters' -in $ruleProperties -and $null -ne $_.parameters) {
            throw "Release-tag rule '$($_.type)' has unauthorized parameters."
        }
        [string]$_.type
    })
Require-ExactSet `
    -Actual $actualRules `
    -Expected @($contract.tagRuleset.rules) `
    -Owner 'The release-tag rules'

if ('bypass_actors' -notin @($tagRuleset.PSObject.Properties.Name)) {
    throw 'The release-tag ruleset bypass policy is missing.'
}
$actualBypassActors = @($tagRuleset.bypass_actors | ForEach-Object {
        Get-BypassActorIdentity -Actor $_ -ContractShape $false
    })
$expectedBypassActors = @($contract.tagRuleset.bypassActors |
    ForEach-Object {
        Get-BypassActorIdentity -Actor $_ -ContractShape $true
    })
if ($expectedBypassActors.Count -eq 0) {
    if ($actualBypassActors.Count -ne 0) {
        throw 'The release-tag bypass policy must equal the exact contract set.'
    }
}
else {
    Require-ExactSet `
        -Actual $actualBypassActors `
        -Expected $expectedBypassActors `
        -Owner 'The release-tag bypass policy'
}

$environmentEvidence = @()
foreach ($required in $contract.environments) {
    $name = [string]$required.name
    $escapedName = [Uri]::EscapeDataString($name)
    $environment = Invoke-GitHubJson "repos/$repository/environments/$escapedName"
    if ($null -eq $environment.deployment_branch_policy -or
        [bool]$environment.deployment_branch_policy.protected_branches -or
        -not [bool]$environment.deployment_branch_policy.custom_branch_policies) {
        throw "Environment '$name' must use explicit tag policies."
    }
    $policies = Invoke-GitHubJson (
        "repos/$repository/environments/$escapedName/deployment-branch-policies")
    $actualRefs = @($policies.branch_policies | ForEach-Object {
            ([string]$_.type) + ':' + ([string]$_.name)
        })
    $expectedRefs = @($required.tags | ForEach-Object {
            'tag:' + ([string]$_)
        })
    Require-ExactSet `
        -Actual $actualRefs `
        -Expected $expectedRefs `
        -Owner "Environment '$name' deployment ref policies"

    $variables = Invoke-GitHubJson "repos/$repository/environments/$escapedName/variables"
    $actualVariables = @($variables.variables | ForEach-Object { [string]$_.name })
    Require-SetMembers `
        -Actual $actualVariables `
        -Expected @($required.variables) `
        -Owner "Environment '$name' variables"

    $secrets = Invoke-GitHubJson "repos/$repository/environments/$escapedName/secrets"
    $actualSecrets = @($secrets.secrets | ForEach-Object { [string]$_.name })
    $requiredSecrets = @($required.secrets)
    Require-SetMembers `
        -Actual $actualSecrets `
        -Expected $requiredSecrets `
        -Owner "Environment '$name' secrets"

    $environmentEvidence += [pscustomobject]@{
        name = $name
        tags = @($required.tags)
        variables = @($required.variables)
        secrets = @($required.secrets)
    }
}

$evidence = [ordered]@{
    schemaVersion = 1
    repository = $repository
    commit = $head
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    tagRulesetId = [long]$tagRuleset.id
    tagRules = @($contract.tagRuleset.rules)
    tagRulesetBypassActors = @($contract.tagRuleset.bypassActors)
    workflowJobs = $workflowEvidence
    environments = $environmentEvidence
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = Resolve-SharpProofContainedPath `
        -Root $repositoryRoot -Path $OutputPath -ParameterName 'OutputPath'
    $directory = [IO.Path]::GetDirectoryName($resolvedOutput)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.File]::WriteAllText(
        $resolvedOutput,
        ($evidence | ConvertTo-Json -Depth 6) + "`n",
        [Text.UTF8Encoding]::new($false))
}

Write-Host "Validated release tags and $($environmentEvidence.Count) publishing environments for $head."
